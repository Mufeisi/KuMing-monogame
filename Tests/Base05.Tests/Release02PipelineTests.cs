using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Net;
using System.Net.Sockets;
using Xunit;

namespace Base05.Tests;

public sealed class Release02PipelineTests
{
    [Fact]
    public void 一键入口固定构建冒烟导出签名与五百分比灰度()
    {
        string script = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Tools", "Invoke-Release02.ps1"));

        Assert.Contains("dotnet' @('test'", script, StringComparison.Ordinal);
        Assert.Contains("dotnet' @('publish', 'Client_VorticeDX11/Client_VorticeDX11.csproj'", script, StringComparison.Ordinal);
        Assert.Contains("dotnet' @('publish', 'Server.MirForms/Server.csproj'", script, StringComparison.Ordinal);
        Assert.Contains("Mobile-BootstrapPackageRepoExport.ps1", script, StringComparison.Ordinal);
        Assert.Contains("sign-resource-index", script, StringComparison.Ordinal);
        Assert.Contains("verify-resource-index", script, StringComparison.Ordinal);
        Assert.Contains("publish-signed-android", script, StringComparison.Ordinal);
        Assert.Contains("apksigner", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("^apksigner\\.(bat|cmd|exe)$", script, StringComparison.Ordinal);
        Assert.Contains("JDK 17 或更高版本", script, StringComparison.Ordinal);
        Assert.Contains("release-channel.lock", script, StringComparison.Ordinal);
        Assert.Contains("release-run-transcript.txt", script, StringComparison.Ordinal);
        Assert.Contains("RolloutPercent = 5", script, StringComparison.Ordinal);
        Assert.Contains("[IO.Directory]::Move($partial, $releaseDirectory)", script, StringComparison.Ordinal);
        Assert.Contains("Start-ReleaseGateway", script, StringComparison.Ordinal);
        Assert.Contains("/release/select", script, StringComparison.Ordinal);
        Assert.Contains("/release/events", script, StringComparison.Ordinal);

        string pc = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Client_VorticeDX11", "Bootstrap", "PcBootstrapPreLoginUpdateService.cs"));
        string mobile = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Client_MonoGame.Shared", "ClientResourceLayout.cs"));
        string mobileRuntime = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Client_MonoGame.Shared", "BootstrapPackageRuntime.cs"));
        Assert.Contains("InstallExtractedPackagesToClient(preparedPackages, stateEntries)", pc, StringComparison.Ordinal);
        Assert.DoesNotContain("InstallExtractedPackageToClient(stagingRoot, packageName)", pc, StringComparison.Ordinal);
        Assert.Contains("updatePackages.All(signedBundles.ContainsKey)", mobile, StringComparison.Ordinal);
        Assert.Contains("TryApplyPackageBundleSetTransactionally", mobile, StringComparison.Ordinal);
        Assert.Contains("TryGetUpdateDesiredSha256(meta.PackageName", mobile, StringComparison.Ordinal);
        Assert.DoesNotContain("TryOnBundleApplied(directory, releaseResult)", mobile, StringComparison.Ordinal);
        Assert.Contains("BuildBundleDeclaredPackages(sourceDirectory, installManifestBundle)", mobileRuntime, StringComparison.Ordinal);
        Assert.Contains("BuildReleaseCommitEntries(required, stateStagingDirectory)", mobileRuntime, StringComparison.Ordinal);
        Assert.Contains("不能覆盖已保存服务器/仓库设置", mobileRuntime, StringComparison.Ordinal);
        Assert.Contains("事务资源版本缺少清单声明文件", mobileRuntime, StringComparison.Ordinal);
        int callbackStart = mobileRuntime.IndexOf("verifyAfterPublish: () =>", StringComparison.Ordinal);
        int callbackEnd = mobileRuntime.IndexOf("ClientResourceLayout.ReloadBootstrapMetadata();", callbackStart, StringComparison.Ordinal);
        Assert.DoesNotContain("TryApplyPackageBundleFromDirectory", mobileRuntime[callbackStart..callbackEnd], StringComparison.Ordinal);

        string mobileUpdate = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Client_MonoGame.Shared", "BootstrapPackageUpdateService.cs"));
        Assert.Contains("BootstrapPackageUpdateRuntime.SignedPackageIndexFileName", mobileUpdate, StringComparison.Ordinal);
        Assert.Contains("签名更新是整版事务", mobile, StringComparison.Ordinal);
    }

    [Fact]
    public void 达到错误阈值自动切回上一可运行版本()
    {
        using var fixture = new PipelineFixture();
        fixture.WriteState("release-new", "release-old");
        Assert.DoesNotContain(fixture.CollectorToken, Encoding.UTF8.GetString(File.ReadAllBytes(fixture.CollectorTokenPath)), StringComparison.Ordinal);
        fixture.WriteMetrics("release-new", updateAttempts: 100, updateFailures: 3, launches: 100, crashes: 0, fatal: 0);

        ProcessResult result = fixture.Run("Evaluate");

        Assert.Equal(0, result.ExitCode);
        using JsonDocument state = JsonDocument.Parse(File.ReadAllText(fixture.StatePath));
        Assert.Equal("release-old", state.RootElement.GetProperty("CurrentReleaseId").GetString());
        Assert.Equal(string.Empty, state.RootElement.GetProperty("PreviousReleaseId").GetString());
        Assert.Equal("release-new", state.RootElement.GetProperty("FailedReleaseId").GetString());
        Assert.Equal("RolledBack", state.RootElement.GetProperty("Status").GetString());
        Assert.Contains("更新失败率", state.RootElement.GetProperty("Reason").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void 样本不足时保持灰度且不会伪报全量健康()
    {
        using var fixture = new PipelineFixture();
        fixture.WriteState("release-new", "release-old");
        fixture.WriteMetrics("release-new", updateAttempts: 20, updateFailures: 1, launches: 20, crashes: 1, fatal: 0);

        ProcessResult result = fixture.Run("Evaluate");

        Assert.Equal(0, result.ExitCode);
        using JsonDocument state = JsonDocument.Parse(File.ReadAllText(fixture.StatePath));
        Assert.Equal("release-new", state.RootElement.GetProperty("CurrentReleaseId").GetString());
        Assert.Equal("CanaryObserving", state.RootElement.GetProperty("Status").GetString());
        Assert.Equal(5, state.RootElement.GetProperty("RolloutPercent").GetInt32());
        Assert.Contains("样本不足", state.RootElement.GetProperty("Reason").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void 启动崩溃率越线时自动回滚()
    {
        using var fixture = new PipelineFixture();
        fixture.WriteState("release-new", "release-old");
        fixture.WriteMetrics("release-new", updateAttempts: 100, updateFailures: 0, launches: 100, crashes: 2, fatal: 0);

        fixture.Run("Evaluate");

        using JsonDocument state = JsonDocument.Parse(File.ReadAllText(fixture.StatePath));
        Assert.Equal("release-old", state.RootElement.GetProperty("CurrentReleaseId").GetString());
        Assert.Contains("启动崩溃率", state.RootElement.GetProperty("Reason").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void 连续致命崩溃不等待样本量直接回滚()
    {
        using var fixture = new PipelineFixture();
        fixture.WriteState("release-new", "release-old");
        fixture.WriteMetrics("release-new", updateAttempts: 1, updateFailures: 0, launches: 3, crashes: 3, fatal: 3);

        fixture.Run("Evaluate");

        using JsonDocument state = JsonDocument.Parse(File.ReadAllText(fixture.StatePath));
        Assert.Equal("release-old", state.RootElement.GetProperty("CurrentReleaseId").GetString());
        Assert.Contains("连续致命崩溃", state.RootElement.GetProperty("Reason").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void 重复回滚不会把失败版本重新切回当前()
    {
        using var fixture = new PipelineFixture();
        fixture.WriteState("release-new", "release-old");
        fixture.WriteMetrics("release-new", 100, 3, 100, 0, 0);
        fixture.Run("Evaluate");

        ProcessResult second = fixture.RunExpectFailure("Rollback");

        Assert.NotEqual(0, second.ExitCode);
        using JsonDocument state = JsonDocument.Parse(File.ReadAllText(fixture.StatePath));
        Assert.Equal("release-old", state.RootElement.GetProperty("CurrentReleaseId").GetString());
        Assert.Equal("release-new", state.RootElement.GetProperty("FailedReleaseId").GetString());
    }

    [Fact]
    public void 损坏的上一版本拒绝回滚()
    {
        using var fixture = new PipelineFixture();
        fixture.WriteState("release-new", "release-old");
        File.AppendAllText(Path.Combine(fixture.Root, "releases", "release-old", "resources", "Packages", "bootstrap-package-index.signed.json"), "tampered");

        ProcessResult result = fixture.RunExpectFailure("Rollback");

        Assert.NotEqual(0, result.ExitCode);
        using JsonDocument state = JsonDocument.Parse(File.ReadAllText(fixture.StatePath));
        Assert.Equal("release-new", state.RootElement.GetProperty("CurrentReleaseId").GetString());
    }

    [Fact]
    public void 稳定客户端标识真实选择百分之五灰度或健康版本()
    {
        using var fixture = new PipelineFixture();
        fixture.WriteState("release-new", "release-old");
        string canaryId = Enumerable.Range(0, 10000).Select(i => "client-" + i).First(id => Bucket(id) < 5);
        string stableId = Enumerable.Range(0, 10000).Select(i => "client-" + i).First(id => Bucket(id) >= 5);

        Assert.Contains("\"ReleaseId\":\"release-new\"", fixture.Run("Select", "-ClientId", canaryId).Output, StringComparison.Ordinal);
        Assert.Contains("\"ReleaseId\":\"release-old\"", fixture.Run("Select", "-ClientId", stableId).Output, StringComparison.Ordinal);
    }

    [Fact]
    public void 发布观测事件写入即自动触发回滚且事件幂等()
    {
        using var fixture = new PipelineFixture();
        fixture.WriteState("release-new", "release-old");
        string canaryId = Enumerable.Range(0, 10000).Select(i => "record-client-" + i).First(id => Bucket(id) < 5);
        fixture.Run("Record", "-EventReleaseId", "release-new", "-ClientId", canaryId, "-EventType", "FatalCrash", "-EventId", "fatal-1");
        fixture.Run("Record", "-EventReleaseId", "release-new", "-ClientId", canaryId, "-EventType", "FatalCrash", "-EventId", "fatal-1");
        fixture.Run("Record", "-EventReleaseId", "release-new", "-ClientId", canaryId, "-EventType", "FatalCrash", "-EventId", "fatal-2");
        fixture.Run("Record", "-EventReleaseId", "release-new", "-ClientId", canaryId, "-EventType", "FatalCrash", "-EventId", "fatal-3");

        using JsonDocument state = JsonDocument.Parse(File.ReadAllText(fixture.StatePath));
        Assert.Equal("release-old", state.RootElement.GetProperty("CurrentReleaseId").GetString());
    }

    [Fact]
    public async Task 本地发布网关真实消费灰度选择并在事件到达时自动回滚()
    {
        using var fixture = new PipelineFixture();
        fixture.WriteState("release-new", "release-old");
        int port;
        var reservation = new TcpListener(IPAddress.Loopback, 0);
        reservation.Start();
        port = ((IPEndPoint)reservation.LocalEndpoint).Port;
        reservation.Stop();
        string prefix = $"http://127.0.0.1:{port}/";
        using Process gateway = fixture.StartGateway(prefix);
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            for (int i = 0; i < 30; i++)
            {
                try { if ((await http.GetAsync(prefix + "health")).IsSuccessStatusCode) break; } catch { }
                await Task.Delay(100);
            }
            string canaryId = Enumerable.Range(0, 10000).Select(i => "gateway-client-" + i).First(id => Bucket(id) < 5);
            string stableId = Enumerable.Range(0, 10000).Select(i => "gateway-stable-" + i).First(id => Bucket(id) >= 5);
            string selection = await http.GetStringAsync(prefix + "release/select?clientId=" + Uri.EscapeDataString(canaryId));
            Assert.Contains("\"ReleaseId\":\"release-new\"", selection, StringComparison.Ordinal);
            using JsonDocument selected = JsonDocument.Parse(selection);
            string resourcePath = selected.RootElement.GetProperty("ResourceRepositoryPath").GetString()!;
            string signedIndex = await http.GetStringAsync(prefix.TrimEnd('/') + resourcePath + "Packages/bootstrap-package-index.signed.json");
            Assert.Equal("{\"signed\":true}", signedIndex);
            using (var rangeRequest = new HttpRequestMessage(HttpMethod.Get, prefix.TrimEnd('/') + resourcePath + "Packages/bootstrap-package-index.signed.json"))
            {
                rangeRequest.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(5, null);
                using HttpResponseMessage rangeResponse = await http.SendAsync(rangeRequest);
                Assert.Equal(HttpStatusCode.PartialContent, rangeResponse.StatusCode);
                Assert.Equal("bytes 5-14/15", rangeResponse.Content.Headers.ContentRange?.ToString());
                Assert.Equal("ned\":true}", await rangeResponse.Content.ReadAsStringAsync());
            }

            using (var unauthorizedContent = EventContent("release-new", canaryId, "FatalCrash", "unauthorized"))
            using (HttpResponseMessage unauthorized = await http.PostAsync(prefix + "release/events", unauthorizedContent))
                Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

            using (var stableContent = EventContent("release-new", stableId, "FatalCrash", "stable-cohort"))
            using (var stableRequest = new HttpRequestMessage(HttpMethod.Post, prefix + "release/events") { Content = stableContent })
            {
                stableRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", fixture.CollectorToken);
                using HttpResponseMessage stable = await http.SendAsync(stableRequest);
                Assert.Equal(HttpStatusCode.BadRequest, stable.StatusCode);
            }

            using (var oversizedContent = new StringContent(new string('x', 5000), Encoding.UTF8, "application/json"))
            using (var oversizedRequest = new HttpRequestMessage(HttpMethod.Post, prefix + "release/events") { Content = oversizedContent })
            {
                oversizedRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", fixture.CollectorToken);
                using HttpResponseMessage oversized = await http.SendAsync(oversizedRequest);
                Assert.Equal(HttpStatusCode.RequestEntityTooLarge, oversized.StatusCode);
            }

            for (int i = 1; i <= 3; i++)
            {
                using var content = EventContent("release-new", canaryId, "FatalCrash", "gateway-fatal-" + i);
                using var request = new HttpRequestMessage(HttpMethod.Post, prefix + "release/events") { Content = content };
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", fixture.CollectorToken);
                using HttpResponseMessage response = await http.SendAsync(request);
                Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            }
            using JsonDocument state = JsonDocument.Parse(File.ReadAllText(fixture.StatePath));
            Assert.Equal("release-old", state.RootElement.GetProperty("CurrentReleaseId").GetString());

            using var lateContent = EventContent("release-new", canaryId, "FatalCrash", "late-after-rollback");
            using var lateRequest = new HttpRequestMessage(HttpMethod.Post, prefix + "release/events") { Content = lateContent };
            lateRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", fixture.CollectorToken);
            using HttpResponseMessage late = await http.SendAsync(lateRequest);
            Assert.Equal(HttpStatusCode.BadRequest, late.StatusCode);
        }
        finally
        {
            if (!gateway.HasExited) gateway.Kill(entireProcessTree: true);
        }
    }

    private static StringContent EventContent(string releaseId, string clientId, string eventType, string eventId) =>
        new(JsonSerializer.Serialize(new
        {
            Format = "lyocrystal-release-event-v1",
            ReleaseId = releaseId,
            ClientId = clientId,
            EventType = eventType,
            EventId = eventId,
        }), Encoding.UTF8, "application/json");

    private static int Bucket(string id)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(id));
        return (((int)hash[0] << 8) | hash[1]) % 100;
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "global.json"))) return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("无法定位仓库根目录。");
    }

    private sealed class PipelineFixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "lyocrystal-release02-pipeline-" + Guid.NewGuid().ToString("N"));

        public PipelineFixture()
        {
            WriteRelease("release-old");
            WriteRelease("release-new");
            byte[] plain = Encoding.UTF8.GetBytes(CollectorToken);
            byte[] protectedToken = ProtectedData.Protect(
                plain,
                Encoding.UTF8.GetBytes("LyoCrystal.Release02.CollectorToken.v1"),
                DataProtectionScope.CurrentUser);
            File.WriteAllBytes(Path.Combine(_root, "release-events-token.dpapi"), protectedToken);
            CryptographicOperations.ZeroMemory(plain);
        }

        public string Root => _root;
        public string CollectorToken { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        public string CollectorTokenPath => Path.Combine(_root, "release-events-token.dpapi");
        public string StatePath => Path.Combine(_root, "channel-state.json");
        private string MetricsPath => Path.Combine(_root, "metrics.json");

        public void WriteState(string current, string previous)
        {
            File.WriteAllText(StatePath, JsonSerializer.Serialize(new
            {
                Format = "lyocrystal-release-channel-v1",
                CurrentReleaseId = current,
                PreviousReleaseId = previous,
                RolloutPercent = 5,
                Status = "Canary",
                Reason = "test",
                UpdatedUtc = DateTime.UtcNow.ToString("O"),
            }));
        }

        public void WriteMetrics(string releaseId, long updateAttempts, long updateFailures, long launches, long crashes, long fatal)
        {
            File.WriteAllText(MetricsPath, JsonSerializer.Serialize(new
            {
                Format = "lyocrystal-release-metrics-v1",
                ReleaseId = releaseId,
                UpdateAttempts = updateAttempts,
                UpdateFailures = updateFailures,
                Launches = launches,
                Crashes = crashes,
                ConsecutiveFatalCrashes = fatal,
            }));
        }

        public ProcessResult Run(string action, params string[] extraArguments)
        {
            string script = Path.Combine(FindRepositoryRoot(), "Tools", "Invoke-Release02.ps1");
            var start = new ProcessStartInfo("powershell.exe")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            foreach (string argument in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script, "-Action", action, "-ChannelRoot", _root, "-MetricsPath", MetricsPath })
                start.ArgumentList.Add(argument);
            foreach (string argument in extraArguments) start.ArgumentList.Add(argument);
            using Process process = Process.Start(start) ?? throw new InvalidOperationException("无法启动 RELEASE-02 测试入口。");
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            Assert.True(process.ExitCode == 0, output + Environment.NewLine + error);
            return new ProcessResult(process.ExitCode, output, error);
        }

        public ProcessResult RunExpectFailure(string action, params string[] extraArguments)
        {
            string script = Path.Combine(FindRepositoryRoot(), "Tools", "Invoke-Release02.ps1");
            var start = new ProcessStartInfo("powershell.exe") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (string argument in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script, "-Action", action, "-ChannelRoot", _root }) start.ArgumentList.Add(argument);
            foreach (string argument in extraArguments) start.ArgumentList.Add(argument);
            using Process process = Process.Start(start) ?? throw new InvalidOperationException("无法启动 RELEASE-02 测试入口。");
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            return new ProcessResult(process.ExitCode, output, error);
        }

        public Process StartGateway(string prefix)
        {
            string script = Path.Combine(FindRepositoryRoot(), "Tools", "Invoke-Release02.ps1");
            var start = new ProcessStartInfo("powershell.exe") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (string argument in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script, "-Action", "Serve", "-ChannelRoot", _root, "-GatewayPrefix", prefix }) start.ArgumentList.Add(argument);
            return Process.Start(start) ?? throw new InvalidOperationException("无法启动发布网关。");
        }

        private void WriteRelease(string releaseId)
        {
            string root = Path.Combine(_root, "releases", releaseId);
            string signed = Path.Combine(root, "resources", "Packages", "bootstrap-package-index.signed.json");
            Directory.CreateDirectory(Path.GetDirectoryName(signed)!);
            File.WriteAllText(signed, "{\"signed\":true}");
            byte[] bytes = File.ReadAllBytes(signed);
            File.WriteAllText(Path.Combine(root, "release-manifest.json"), JsonSerializer.Serialize(new
            {
                Format = "lyocrystal-release-artifact-v1",
                ReleaseId = releaseId,
                Files = new[] { new { Path = "resources/Packages/bootstrap-package-index.signed.json", Size = bytes.LongLength, Sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() } },
            }));
        }

        public void Dispose()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
    }

    private sealed record ProcessResult(int ExitCode, string Output, string Error);
}
