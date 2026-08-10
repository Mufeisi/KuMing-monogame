using System.Diagnostics;
using System.Text.Json;
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
    }

    [Fact]
    public void 达到错误阈值自动切回上一可运行版本()
    {
        using var fixture = new PipelineFixture();
        fixture.WriteState("release-new", "release-old");
        fixture.WriteMetrics("release-new", updateAttempts: 100, updateFailures: 3, launches: 100, crashes: 0, fatal: 0);

        ProcessResult result = fixture.Run("Evaluate");

        Assert.Equal(0, result.ExitCode);
        using JsonDocument state = JsonDocument.Parse(File.ReadAllText(fixture.StatePath));
        Assert.Equal("release-old", state.RootElement.GetProperty("CurrentReleaseId").GetString());
        Assert.Equal("release-new", state.RootElement.GetProperty("PreviousReleaseId").GetString());
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
            Directory.CreateDirectory(Path.Combine(_root, "releases", "release-old"));
            Directory.CreateDirectory(Path.Combine(_root, "releases", "release-new"));
        }

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

        public ProcessResult Run(string action)
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
            using Process process = Process.Start(start) ?? throw new InvalidOperationException("无法启动 RELEASE-02 测试入口。");
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            Assert.True(process.ExitCode == 0, output + Environment.NewLine + error);
            return new ProcessResult(process.ExitCode, output, error);
        }

        public void Dispose()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
    }

    private sealed record ProcessResult(int ExitCode, string Output, string Error);
}
