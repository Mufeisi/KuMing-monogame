using System.Text.Json;
using Shared.Diagnostics;
using Xunit;

namespace Base05.Tests;

public sealed class CrashDiagnosticBundleTests
{
    [Fact]
    public void 原子诊断包包含日志尾部版本资源版本和配置摘要()
    {
        string root = Path.Combine(Path.GetTempPath(), "base05-ops02-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            string log = Path.Combine(root, "runtime.log");
            File.WriteAllText(log, string.Concat(Enumerable.Repeat("1.1.1.1 ", 9_000)) +
                " {\"token\":\"jsonsecret\"} token=supersecret user@example.com 192.168.1.10 " +
                @"Authorization: Bearer signed.payload Authorization: Basic dXNlcjpwYXNz " +
                @"ConnectionString='Server=db;Password=database-secret' C:\Users\private-user\client.log tail-proof");
            string resource = Path.Combine(root, "resource.json");
            File.WriteAllText(resource, "{\"Manifest\":{\"ResourceVersion\":\"content-42\"}}");

            string bundle = CrashDiagnosticBundle.Write(new CrashDiagnosticRequest
            {
                OutputRoot = Path.Combine(root, "crashes"),
                Component = "pc-client",
                ProductVersion = "2.3.4",
                ResourceVersionPath = resource,
                Exception = new InvalidOperationException("crash-proof password=hunter2"),
                LogPaths = new[] { log, Path.Combine(root, "missing.log") },
                Configuration = new Dictionary<string, string>
                {
                    ["TlsEnabled"] = "true",
                    ["Profile"] = "Classic",
                },
            });

            Assert.True(Directory.Exists(bundle));
            Assert.Empty(Directory.GetDirectories(Path.Combine(root, "crashes"), "*.partial"));
            using JsonDocument summary = JsonDocument.Parse(File.ReadAllText(Path.Combine(bundle, "summary.json")));
            Assert.Equal("pc-client", summary.RootElement.GetProperty("Component").GetString());
            Assert.Equal("2.3.4", summary.RootElement.GetProperty("ProductVersion").GetString());
            Assert.Equal("content-42", summary.RootElement.GetProperty("ResourceVersion").GetString());
            Assert.Equal(64, summary.RootElement.GetProperty("ResourceStateSha256").GetString()!.Length);
            Assert.Equal(64, summary.RootElement.GetProperty("ConfigurationSha256").GetString()!.Length);
            string copied = summary.RootElement.GetProperty("LogFiles")[0].GetString()!;
            string tail = File.ReadAllText(Path.Combine(bundle, copied.Replace('/', Path.DirectorySeparatorChar)));
            Assert.Contains("tail-proof", tail);
            Assert.DoesNotContain("supersecret", tail);
            Assert.DoesNotContain("user@example.com", tail);
            Assert.DoesNotContain("192.168.1.10", tail);
            Assert.DoesNotContain("signed.payload", tail);
            Assert.DoesNotContain("dXNlcjpwYXNz", tail);
            Assert.DoesNotContain("jsonsecret", tail);
            Assert.DoesNotContain("database-secret", tail);
            Assert.DoesNotContain("private-user", tail);
            Assert.Contains("***", tail);
            Assert.True(new FileInfo(Path.Combine(bundle, copied.Replace('/', Path.DirectorySeparatorChar))).Length <= 65_536);
            string exception = File.ReadAllText(Path.Combine(bundle, "exception.txt"));
            Assert.Contains("crash-proof", exception);
            Assert.DoesNotContain("hunter2", exception);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public void 已接受状态缺失时使用随包基线资源身份()
    {
        string root = Path.Combine(Path.GetTempPath(), "base05-ops02-fallback-" + Guid.NewGuid().ToString("N"));
        try
        {
            string bundle = CrashDiagnosticBundle.Write(new CrashDiagnosticRequest
            {
                OutputRoot = Path.Combine(root, "crashes"),
                Component = "first-start",
                ProductVersion = "1.0.0",
                ResourceVersionPath = Path.Combine(root, "missing-state.json"),
                ResourceVersionFallbackContent = "{\"ResourceVersion\":\"bundled-content-7\"}",
                Exception = new InvalidOperationException("first-start"),
            });

            using JsonDocument summary = JsonDocument.Parse(File.ReadAllText(Path.Combine(bundle, "summary.json")));
            Assert.Equal("bundled-content-7", summary.RootElement.GetProperty("ResourceVersion").GetString());
            Assert.Equal(64, summary.RootElement.GetProperty("ResourceStateSha256").GetString()!.Length);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public void 失败发布会清理半成品目录()
    {
        string root = Path.Combine(Path.GetTempPath(), "base05-ops02-fail-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            string lockedLog = Path.Combine(root, "locked.log");
            File.WriteAllText(lockedLog, "locked");
            using var lockStream = new FileStream(lockedLog, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            Assert.ThrowsAny<IOException>(() => CrashDiagnosticBundle.Write(new CrashDiagnosticRequest
            {
                OutputRoot = Path.Combine(root, "crashes"),
                Component = "server",
                LogPaths = new[] { lockedLog },
            }));
            Assert.Empty(Directory.GetDirectories(Path.Combine(root, "crashes"), "*.partial"));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }
}
