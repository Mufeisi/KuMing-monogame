using System.Net;
using System.Net.Sockets;
using LyoCrystal.MicroGateway;
using Server;
using Server.Library.Utils;
using Server.Security;
using Xunit;

namespace Base05.Tests;

[Collection("SEC04环境")]
public sealed class MicroGatewayCoreTests
{
    [Fact]
    public async Task 资源索引只原子发布稳定文件且失败保留旧快照()
    {
        string root = Path.Combine(Path.GetTempPath(), "LyoCrystalIndexTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Data"));
        string stable = Path.Combine(root, "Data", "stable.bin");
        await File.WriteAllBytesAsync(stable, new byte[] { 1, 2, 3 });
        await using var index = new MicroResourceIndex(TimeSpan.FromMilliseconds(80), TimeSpan.FromHours(1), TimeSpan.Zero, TimeSpan.Zero);
        try
        {
            await index.StartAsync(root);
            Assert.True(index.TryGetFile(stable, out _));
            MicroResourceIndexSnapshot before = index.GetSnapshot();

            string uploading = Path.Combine(root, "Data", "uploading.bin");
            Task writer = Task.Run(async () =>
            {
                await using var output = new FileStream(uploading, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
                await output.WriteAsync(new byte[32]);
                await output.FlushAsync();
                await Task.Delay(40);
                await output.WriteAsync(new byte[32]);
            });
            await index.ReconcileAsync();
            await writer;
            Assert.False(index.TryGetFile(uploading, out _));
            Assert.True(index.TryGetFile(stable, out _));

            Directory.Delete(root, true);
            Assert.False(await index.ReconcileAsync());
            Assert.Equal(before.FileCount, index.GetSnapshot().FileCount);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task 新文件经过隔离稳定期后才进入索引()
    {
        string root = Path.Combine(Path.GetTempPath(), "LyoCrystalIndexQuarantine", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "new.bin");
        await using var index = new MicroResourceIndex(TimeSpan.FromMilliseconds(20), TimeSpan.FromHours(1), TimeSpan.Zero, TimeSpan.FromMilliseconds(150));
        try
        {
            await index.StartAsync(root);
            await File.WriteAllBytesAsync(path, new byte[] { 1 });
            await index.ReconcileAsync();
            Assert.False(index.TryGetFile(path, out _));
            DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
            while (!index.TryGetFile(path, out _) && DateTime.UtcNow < deadline)
            {
                await Task.Delay(80);
                await index.ReconcileAsync();
            }
            Assert.True(index.TryGetFile(path, out _));
            Assert.True(await index.ReconcileAsync());
            Assert.True(index.TryGetFile(path, out _));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void 两级缓存受限且损坏后重建而不修改资源库()
    {
        string root = Path.Combine(Path.GetTempPath(), "LyoCrystalCacheResources", Guid.NewGuid().ToString("N"));
        string cacheRoot = Path.Combine(Path.GetTempPath(), "LyoCrystalCacheData", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        byte[] resource = new byte[] { 9, 8, 7 };
        string resourcePath = Path.Combine(root, "source.bin");
        File.WriteAllBytes(resourcePath, resource);
        try
        {
            var cache = new MicroPayloadCache(root, cacheRoot, 1, 1);
            int builds = 0;
            byte[] first = cache.GetOrCreate("first", _ => { builds++; return Enumerable.Repeat((byte)1, 600_000).ToArray(); })!;
            cache.GetOrCreate("second", _ => Enumerable.Repeat((byte)2, 600_000).ToArray());
            Assert.True(Directory.EnumerateFiles(cacheRoot, "*.bin").Sum(path => new FileInfo(path).Length) <= 1024L * 1024L);

            string stored = Directory.EnumerateFiles(cacheRoot, "*.bin").Single();
            File.WriteAllBytes(stored, new byte[] { 1, 2, 3 });
            var restarted = new MicroPayloadCache(root, cacheRoot, 0, 1);
            int beforeRebuild = builds;
            restarted.GetOrCreate(Path.GetFileNameWithoutExtension(stored).Equals(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("second"))), StringComparison.Ordinal) ? "second" : "first",
                _ => { builds++; return first; });
            Assert.Equal(resource, File.ReadAllBytes(resourcePath));
            Assert.True(builds > beforeRebuild);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
            if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, true);
        }
    }

    [Fact]
    public async Task 一百个并发流式请求内容一致且全部收敛()
    {
        string repository = FindRepositoryRoot(AppContext.BaseDirectory);
        string resources = Path.Combine(repository, "Client_MonoGame.Shared", "BootstrapAssets");
        int port = GetFreePort();
        await using var host = new MicroHttpListenerHost();
        await host.StartAsync($"http://127.0.0.1:{port}/", new MicroGatewayOptions(resources, "reader", "code", NewFileQuarantineSeconds: 0));
        using HttpClient client = Client(port);
        long baseline = System.Diagnostics.Process.GetCurrentProcess().WorkingSet64;
        long peak = baseline;
        using var sampling = new CancellationTokenSource();
        Task sampler = Task.Run(async () =>
        {
            try
            {
                while (!sampling.IsCancellationRequested)
                {
                    long current = System.Diagnostics.Process.GetCurrentProcess().WorkingSet64;
                    long observed; while (current > (observed = Volatile.Read(ref peak))) Interlocked.CompareExchange(ref peak, current, observed);
                    await Task.Delay(5, sampling.Token);
                }
            }
            catch (OperationCanceledException) { }
        });
        Task<byte[]>[] requests = Enumerable.Range(0, 100).Select(async _ =>
        {
            using HttpRequestMessage request = Authorized("/api/file/Data/ChrSel.Lib");
            request.Headers.TryAddWithoutValidation("Range", "bytes=0-1023");
            using HttpResponseMessage response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
            return await response.Content.ReadAsByteArrayAsync();
        }).ToArray();
        byte[][] payloads = await Task.WhenAll(requests);
        sampling.Cancel();
        await sampler;
        Assert.All(payloads, payload => Assert.Equal(payloads[0], payload));
        Assert.True(peak - baseline < 256L * 1024 * 1024, $"100 并发期间工作集增长 {peak - baseline} 字节，超过 256 MiB 门限");
        Assert.Equal(0, host.GetSnapshot().ActiveRequestCount);
        await host.StopAsync();
    }

    [Fact]
    public void 同一缓存键并发只生成一次()
    {
        string resources = Path.Combine(Path.GetTempPath(), "LyoCrystalSingleFlightResources", Guid.NewGuid().ToString("N"));
        string cacheRoot = Path.Combine(Path.GetTempPath(), "LyoCrystalSingleFlightCache", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(resources);
        try
        {
            var cache = new MicroPayloadCache(resources, cacheRoot, 16, 128);
            int builds = 0;
            Parallel.For(0, 100, _ => Assert.Equal(new byte[] { 4, 2 }, cache.GetOrCreate("same", _ =>
            {
                Interlocked.Increment(ref builds);
                Thread.Sleep(30);
                return new byte[] { 4, 2 };
            })));
            Assert.Equal(1, builds);
        }
        finally
        {
            if (Directory.Exists(resources)) Directory.Delete(resources, true);
            if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, true);
        }
    }

    [Fact]
    public void 不同缓存键的生成并发和单项大小受硬限制()
    {
        string resources = Path.Combine(Path.GetTempPath(), "LyoCrystalBoundedFactoryResources", Guid.NewGuid().ToString("N"));
        string cacheRoot = Path.Combine(Path.GetTempPath(), "LyoCrystalBoundedFactoryCache", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(resources);
        try
        {
            var cache = new MicroPayloadCache(resources, cacheRoot, 1, 128);
            int active = 0, peak = 0;
            Parallel.For(0, 20, index => cache.GetOrCreate("key-" + index, limit =>
            {
                int current = Interlocked.Increment(ref active);
                int observed; while (current > (observed = Volatile.Read(ref peak))) Interlocked.CompareExchange(ref peak, current, observed);
                Thread.Sleep(10);
                Interlocked.Decrement(ref active);
                return new byte[Math.Min(128, limit)];
            }));
            Assert.Equal(1, peak);
            Assert.Null(cache.GetOrCreate("oversized", limit => new byte[limit + 1]));
            Assert.True(cache.GetSnapshot().MemoryBytes <= 1024L * 1024L);
        }
        finally
        {
            if (Directory.Exists(resources)) Directory.Delete(resources, true);
            if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, true);
        }
    }

    [Fact]
    public async Task 索引文件重新进入写入状态时拒绝流式响应()
    {
        string root = Path.Combine(Path.GetTempPath(), "LyoCrystalStreamLease", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Data"));
        string path = Path.Combine(root, "Data", "sample.bin");
        await File.WriteAllBytesAsync(path, new byte[128]);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow - TimeSpan.FromMinutes(2));
        var core = new MicroGatewayCore();
        try
        {
            await core.StartAsync(new MicroGatewayOptions(root, "reader", "code", NewFileQuarantineSeconds: 0));
            await using var writer = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
            var headers = new Dictionary<string, string?> { ["User"] = "reader", ["Code"] = "code" };
            await using MicroGatewayResponse response = await core.HandleAsync(new MicroGatewayRequest("GET", "/api/file/Data/sample.bin", headers));
            await using var output = new MemoryStream();
            await Assert.ThrowsAsync<IOException>(() => response.WriteBodyAsync!(output, CancellationToken.None));
        }
        finally { await core.StopAsync(); if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    [Fact]
    public async Task 独立网关与内置服务端四类协议响应一致且公开目录只读安全()
    {
        string repository = FindRepositoryRoot(AppContext.BaseDirectory);
        string resources = Path.Combine(repository, "Client_MonoGame.Shared", "BootstrapAssets");
        string launcherRoot = Path.Combine(Path.GetTempPath(), "LyoCrystalLauncherPublic", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(launcherRoot);
        await File.WriteAllTextAsync(Path.Combine(launcherRoot, "server-catalog.json"), "{\"sequence\":1}");
        int embeddedPort = GetFreePort(), standalonePort = GetFreePort();
        string originalAddress = Settings.HTTPIPAddress;
        bool originalActive = Settings.MicroServerActive;
        string originalAuthor = Settings.MicroAuthor, originalCode = Settings.MicroCode, originalRoot = Settings.MicroResourcePath;
        string secretRoot = Path.Combine(Path.GetTempPath(), "LyoCrystalMicroGatewayTests", Guid.NewGuid().ToString("N"));
        using IDisposable secrets = ProtectedSecretStore.UseTestRoot(secretRoot);
        HttpServer? embedded = null;
        await using var standalone = new MicroHttpListenerHost();
        try
        {
            Settings.HTTPIPAddress = $"http://127.0.0.1:{embeddedPort}/";
            Settings.MicroServerActive = true;
            Settings.MicroAuthor = "reader";
            Settings.MicroCode = "code";
            Settings.MicroResourcePath = resources;
            embedded = new HttpServer();
            embedded.Start();
            await standalone.StartAsync($"http://127.0.0.1:{standalonePort}/", new MicroGatewayOptions(resources, "reader", "code", launcherRoot, NewFileQuarantineSeconds: 0));
            using var first = Client(embeddedPort);
            using var second = Client(standalonePort);
            await WaitReady(first); await WaitReady(second);

            foreach (string path in new[] { "/api/file/Data/ChrSel.Lib", "/api/libheader/Data/ChrSel.Lib", "/api/libimage/Data/ChrSel.Lib/0", "/api/sound/100" })
            {
                using HttpRequestMessage leftRequest = Authorized(path);
                using HttpRequestMessage rightRequest = Authorized(path);
                if (path.Contains("/file/")) { leftRequest.Headers.TryAddWithoutValidation("Range", "bytes=0-31"); rightRequest.Headers.TryAddWithoutValidation("Range", "bytes=0-31"); }
                using HttpResponseMessage left = await first.SendAsync(leftRequest);
                using HttpResponseMessage right = await second.SendAsync(rightRequest);
                Assert.Equal(left.StatusCode, right.StatusCode);
                Assert.Equal(await left.Content.ReadAsByteArrayAsync(), await right.Content.ReadAsByteArrayAsync());
                Assert.Equal(left.Content.Headers.ContentType?.ToString(), right.Content.Headers.ContentType?.ToString());
                Assert.Equal(left.Headers.AcceptRanges.ToString(), right.Headers.AcceptRanges.ToString());
                Assert.Equal(left.Content.Headers.ContentRange?.ToString(), right.Content.Headers.ContentRange?.ToString());
            }

            using HttpResponseMessage launcher = await second.GetAsync("/launcher/server-catalog.json");
            Assert.Equal(HttpStatusCode.OK, launcher.StatusCode);
            using HttpResponseMessage traversal = await second.GetAsync("/launcher/%2e%2e/global.json");
            Assert.Equal(HttpStatusCode.NotFound, traversal.StatusCode);
            using HttpResponseMessage unauthorized = await second.GetAsync("/api/file/Data/ChrSel.Lib");
            Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

            Settings.MicroAuthor = "changed-reader";
            Settings.MicroCode = "changed-code";
            using var changedRequest = new HttpRequestMessage(HttpMethod.Get, "/api/file/Data/ChrSel.Lib");
            changedRequest.Headers.Add("User", "changed-reader");
            changedRequest.Headers.Add("Code", "changed-code");
            changedRequest.Headers.TryAddWithoutValidation("Range", "bytes=0-31");
            using HttpResponseMessage changed = await first.SendAsync(changedRequest);
            Assert.Equal(HttpStatusCode.PartialContent, changed.StatusCode);
            Settings.MicroAuthor = "reader";
            Settings.MicroCode = "code";

            foreach (string path in new[] { "/api/file/Data/Missing.Lib", "/api/file/Data/ChrSel.Lib" })
            {
                using HttpRequestMessage leftRequest = Authorized(path);
                using HttpRequestMessage rightRequest = Authorized(path);
                if (path.EndsWith("ChrSel.Lib", StringComparison.Ordinal)) { leftRequest.Headers.TryAddWithoutValidation("Range", "bytes=999999999-"); rightRequest.Headers.TryAddWithoutValidation("Range", "bytes=999999999-"); }
                using HttpResponseMessage left = await first.SendAsync(leftRequest);
                using HttpResponseMessage right = await second.SendAsync(rightRequest);
                Assert.Equal(left.StatusCode, right.StatusCode);
                Assert.Equal(left.Content.Headers.ContentRange?.ToString(), right.Content.Headers.ContentRange?.ToString());
                Assert.Equal(await left.Content.ReadAsByteArrayAsync(), await right.Content.ReadAsByteArrayAsync());
            }
        }
        finally
        {
            embedded?.Stop();
            await standalone.StopAsync();
            Settings.HTTPIPAddress = originalAddress; Settings.MicroServerActive = originalActive;
            Settings.MicroAuthor = originalAuthor; Settings.MicroCode = originalCode; Settings.MicroResourcePath = originalRoot;
            if (Directory.Exists(secretRoot)) Directory.Delete(secretRoot, true);
            if (Directory.Exists(launcherRoot)) Directory.Delete(launcherRoot, true);
        }
    }

    [Fact]
    public async Task 资源目录内重解析点不能读取根目录外文件()
    {
        string root = Path.Combine(Path.GetTempPath(), "LyoCrystalMicroRoot", Guid.NewGuid().ToString("N"));
        string outside = Path.Combine(Path.GetTempPath(), "LyoCrystalMicroOutside", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); Directory.CreateDirectory(outside);
        await File.WriteAllTextAsync(Path.Combine(outside, "secret.bin"), "secret");
        try
        {
            string commandProcessor = Environment.GetEnvironmentVariable("ComSpec") ?? Path.Combine(Environment.SystemDirectory, "cmd.exe");
            var junction = new System.Diagnostics.ProcessStartInfo(commandProcessor) { UseShellExecute = false, CreateNoWindow = true };
            junction.ArgumentList.Add("/d"); junction.ArgumentList.Add("/c"); junction.ArgumentList.Add("mklink"); junction.ArgumentList.Add("/J");
            junction.ArgumentList.Add(Path.Combine(root, "escape")); junction.ArgumentList.Add(outside);
            using System.Diagnostics.Process process = System.Diagnostics.Process.Start(junction)!;
            await process.WaitForExitAsync();
            Assert.Equal(0, process.ExitCode);
            var core = new MicroGatewayCore();
            await core.StartAsync(new MicroGatewayOptions(root, "reader", "code", root, NewFileQuarantineSeconds: 0));
            var headers = new Dictionary<string, string?> { ["User"] = "reader", ["Code"] = "code" };
            MicroGatewayResponse api = await core.HandleAsync(new MicroGatewayRequest("GET", "/api/file/escape/secret.bin", headers));
            MicroGatewayResponse launcher = await core.HandleAsync(new MicroGatewayRequest("GET", "/launcher/escape/secret.bin", new Dictionary<string, string?>()));
            Assert.Equal(404, api.StatusCode);
            Assert.Equal(404, launcher.StatusCode);
        }
        finally
        {
            string link = Path.Combine(root, "escape");
            if (Directory.Exists(link)) Directory.Delete(link);
            if (Directory.Exists(root)) Directory.Delete(root, true);
            if (Directory.Exists(outside)) Directory.Delete(outside, true);
        }
    }

    [Fact]
    public void 独立核心不引用游戏服务端或Envir程序集()
    {
        string[] references = typeof(MicroGatewayCore).Assembly.GetReferencedAssemblies().Select(item => item.Name ?? string.Empty).ToArray();
        Assert.DoesNotContain("Server.Library", references);
        Assert.DoesNotContain(references, name => name.Contains("Envir", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task 快照和停止覆盖响应体的完整写入周期()
    {
        string repository = FindRepositoryRoot(AppContext.BaseDirectory);
        string resources = Path.Combine(repository, "Client_MonoGame.Shared", "BootstrapAssets");
        var core = new MicroGatewayCore();
        await core.StartAsync(new MicroGatewayOptions(resources, "reader", "code", NewFileQuarantineSeconds: 0));
        var headers = new Dictionary<string, string?> { ["user"] = "reader", ["code"] = "code", ["range"] = "bytes=0-31" };
        MicroGatewayResponse response = await core.HandleAsync(new MicroGatewayRequest("GET", "/api/file/Data/ChrSel.Lib", headers));
        Assert.Equal(1, core.GetSnapshot().ActiveRequestCount);
        Task stopping = core.StopAsync();
        Assert.False(stopping.IsCompleted);
        await using var output = new MemoryStream();
        await response.WriteBodyAsync!(output, CancellationToken.None);
        await stopping;
        Assert.Equal(0, core.GetSnapshot().ActiveRequestCount);
        Assert.Equal(32, output.Length);

        await core.StartAsync(new MicroGatewayOptions(resources, "reader", "code", NewFileQuarantineSeconds: 0));
        MicroGatewayResponse abandoned = await core.HandleAsync(new MicroGatewayRequest("GET", "/api/file/Data/ChrSel.Lib", headers));
        Task secondStop = core.StopAsync();
        Assert.False(secondStop.IsCompleted);
        await using MicroGatewayResponse rejected = await core.HandleAsync(new MicroGatewayRequest("GET", "/api/health", new Dictionary<string, string?>()));
        Assert.Equal(503, rejected.StatusCode);
        Assert.Equal(1, core.GetSnapshot().ActiveRequestCount);
        await abandoned.DisposeAsync();
        await secondStop;
    }

    private static HttpClient Client(int port) => new(new HttpClientHandler { UseProxy = false }) { BaseAddress = new Uri($"http://127.0.0.1:{port}/") };
    private static HttpRequestMessage Authorized(string path) { var request = new HttpRequestMessage(HttpMethod.Get, path); request.Headers.Add("User", "reader"); request.Headers.Add("Code", "code"); return request; }
    private static async Task WaitReady(HttpClient client) { for (int i = 0; i < 40; i++) { try { using var response = await client.GetAsync("/api/health"); if (response.IsSuccessStatusCode) return; } catch (HttpRequestException) { } await Task.Delay(50); } throw new TimeoutException("网关未就绪"); }
    private static int GetFreePort() { var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); int port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop(); return port; }
    private static string FindRepositoryRoot(string start) { DirectoryInfo? current = new(start); while (current is not null) { if (File.Exists(Path.Combine(current.FullName, "global.json"))) return current.FullName; current = current.Parent; } throw new DirectoryNotFoundException(); }
}
