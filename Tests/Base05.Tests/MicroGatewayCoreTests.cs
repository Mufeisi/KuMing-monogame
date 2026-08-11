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
            await standalone.StartAsync($"http://127.0.0.1:{standalonePort}/", new MicroGatewayOptions(resources, "reader", "code", launcherRoot));
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
            await core.StartAsync(new MicroGatewayOptions(root, "reader", "code", root));
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
        await core.StartAsync(new MicroGatewayOptions(resources, "reader", "code"));
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

        await core.StartAsync(new MicroGatewayOptions(resources, "reader", "code"));
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
