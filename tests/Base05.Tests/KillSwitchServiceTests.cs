using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Server;
using Server.Library.Utils;
using Server.MirDatabase;
using Server.MirEnvir;
using Server.MirObjects;
using Server.Operations;
using Server.Security;
using Xunit;

namespace Base05.Tests;

[Collection("SEC04环境")]
public sealed class KillSwitchServiceTests
{
    [Fact]
    public void 状态先原子持久化再发布且重启保持关闭()
    {
        string root = NewRoot();
        string path = Path.Combine(root, "kill-switches.json");
        var audits = new List<string>();
        try
        {
            var service = new KillSwitchService(path, () => DateTimeOffset.UnixEpoch, audits.Add);
            Assert.True(service.IsEnabled(KillSwitchFeature.GameShop));
            Assert.True(service.IsEnabled(KillSwitchFeature.ResourceUpdate));

            KillSwitchSnapshot changed = service.Set(
                KillSwitchFeature.GameShop, enabled: false, "商城异常紧急止损", "Administrator");
            var reloaded = new KillSwitchService(path, auditSink: audits.Add);

            Assert.Equal(1, changed.Revision);
            Assert.False(reloaded.IsEnabled(KillSwitchFeature.GameShop));
            Assert.True(reloaded.IsEnabled(KillSwitchFeature.ResourceUpdate));
            Assert.Single(reloaded.GetSnapshot().AuditTrail);
            Assert.Equal("商城异常紧急止损", reloaded.GetSnapshot().AuditTrail[0].Reason);
            Assert.DoesNotContain(Directory.GetFiles(root), file => file.Contains(".partial-", StringComparison.Ordinal));
            Assert.Contains(audits, line => line.Contains("OPS_KILL_SWITCH") && line.Contains("feature=GameShop"));
            Assert.DoesNotContain(audits, line => line.Contains("商城异常紧急止损", StringComparison.Ordinal));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void 运行日志失败不伪报变更失败且原子状态保留完整审计()
    {
        string root = NewRoot();
        string path = Path.Combine(root, "kill-switches.json");
        try
        {
            var service = new KillSwitchService(path, auditSink: _ => throw new IOException("日志目录不可写"));
            KillSwitchSnapshot changed = service.Set(
                KillSwitchFeature.HighRiskOperations, false, "账户异常紧急止损", "Administrator");
            var reloaded = new KillSwitchService(path, auditSink: _ => { });

            Assert.False(changed.HighRiskOperationsEnabled);
            Assert.False(reloaded.IsEnabled(KillSwitchFeature.HighRiskOperations));
            KillSwitchAuditEntry audit = Assert.Single(reloaded.GetSnapshot().AuditTrail);
            Assert.Equal(1, audit.Revision);
            Assert.Equal(KillSwitchFeature.HighRiskOperations, audit.Feature);
            Assert.False(audit.Enabled);
            Assert.Equal("账户异常紧急止损", audit.Reason);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void 当前开关与连续审计重放结果不一致时失败关闭()
    {
        string root = NewRoot();
        string path = Path.Combine(root, "kill-switches.json");
        try
        {
            var service = new KillSwitchService(path, auditSink: _ => { });
            service.Set(KillSwitchFeature.GameShop, false, "关闭商城入口", "Administrator");
            string state = File.ReadAllText(path, Encoding.UTF8);
            File.WriteAllText(path, state.Replace(
                "\"GameShopEnabled\": false", "\"GameShopEnabled\": true", StringComparison.Ordinal),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => new KillSwitchService(path));
            Assert.Contains("拒绝启动", error.Message);
            Assert.Contains("审计重放结果", error.InnerException?.Message);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"FormatVersion\":2}")]
    [InlineData("not-json")]
    public void 损坏缺字段或未知版本状态均失败关闭(string payload)
    {
        string root = NewRoot();
        string path = Path.Combine(root, "kill-switches.json");
        try
        {
            File.WriteAllText(path, payload, Encoding.UTF8);
            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => new KillSwitchService(path));
            Assert.Contains("拒绝启动", error.Message);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void 商城活动与高风险入口读取同一服务端开关()
    {
        string root = NewRoot();
        string path = Path.Combine(root, "kill-switches.json");
        var service = new KillSwitchService(path, auditSink: _ => { });
        object original = GetKillSwitchProperty().GetValue(Envir.Main);
        try
        {
            service.Set(KillSwitchFeature.GameShop, false, "关闭商城入口", "test");
            service.Set(KillSwitchFeature.Activities, false, "关闭活动进度", "test");
            service.Set(KillSwitchFeature.HighRiskOperations, false, "关闭高风险入口", "test");
            GetKillSwitchProperty().SetValue(Envir.Main, service);

            var player = new PlayerObject();
            Envir.Main.GameShopList.Add(new GameShopItem { Stock = 1 });
            player.GetGameShop();
            player.MarketPanelType = MarketPanelType.GameShop;
            player.MarketPage(0);
            player.GameShopStock(new GameShopItem { Stock = 1 });

            var dragonInfo = new DragonInfo { Experience = 10 };
            var dragon = new Dragon(dragonInfo);
            dragon.GainExp(25);

            Assert.Equal(10, dragonInfo.Experience);
            Assert.Equal(0, Envir.Main.HTTPNewAccount(new ClientPackets.NewAccount(), "127.0.0.1"));
        }
        finally
        {
            Envir.Main.GameShopList.Clear();
            GetKillSwitchProperty().SetValue(Envir.Main, original);
            TryDelete(root);
        }
    }

    [Fact]
    public async Task 操作员可查询管理员可修改且微端资源下载立即关闭()
    {
        string root = NewRoot();
        string statePath = Path.Combine(root, "kill-switches.json");
        string secretPath = Path.Combine(root, "secrets");
        int port = GetFreePort();
        string originalAddress = Settings.HTTPIPAddress;
        string originalTrustedAddress = Settings.HTTPTrustedIPAddress;
        bool originalMicroActive = Settings.MicroServerActive;
        string originalMicroAuthor = Settings.MicroAuthor;
        string originalMicroCode = Settings.MicroCode;
        IDisposable secretScope = ProtectedSecretStore.UseTestRoot(secretPath);
        HttpServer server = null;
        try
        {
            Settings.HTTPIPAddress = $"http://127.0.0.1:{port}/";
            Settings.HTTPTrustedIPAddress = "127.0.0.1";
            Settings.MicroServerActive = true;
            Settings.MicroAuthor = "resource-reader";
            Settings.MicroCode = "resource-code";
            ProtectedSecretStore.Write(ProtectedSecretStore.AdministratorToken, "administrator-secret-32-characters-minimum");
            ProtectedSecretStore.Write(ProtectedSecretStore.OperatorToken, "operator-secret-32-characters-minimum");
            var service = new KillSwitchService(statePath, auditSink: _ => { });
            server = new HttpServer(killSwitches: service);
            server.Start();

            using var handler = new HttpClientHandler { UseProxy = false };
            using var client = new HttpClient(handler) { BaseAddress = new Uri(Settings.HTTPIPAddress) };

            using HttpResponseMessage query = await SendWhenReady(
                client, HttpMethod.Get, "/operations/kill-switches", "operator-secret-32-characters-minimum");
            Assert.Equal(HttpStatusCode.OK, query.StatusCode);

            using HttpResponseMessage forbidden = await SendWhenReady(
                client, HttpMethod.Post, "/operations/kill-switches/set", "operator-secret-32-characters-minimum",
                "{\"feature\":\"resource-update\",\"enabled\":false,\"reason\":\"更新异常紧急止损\"}");
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

            using HttpResponseMessage changed = await SendWhenReady(
                client, HttpMethod.Post, "/operations/kill-switches/set", "administrator-secret-32-characters-minimum",
                "{\"feature\":\"resource-update\",\"enabled\":false,\"reason\":\"更新异常紧急止损\"}");
            Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
            using JsonDocument changedJson = JsonDocument.Parse(await changed.Content.ReadAsStringAsync());
            Assert.False(changedJson.RootElement.GetProperty("ResourceUpdateEnabled").GetBoolean());

            using var download = new HttpRequestMessage(HttpMethod.Get, "/api/file/package/update.zip");
            download.Headers.Add("User", "resource-reader");
            download.Headers.Add("Code", "resource-code");
            using HttpResponseMessage disabled = await client.SendAsync(download);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, disabled.StatusCode);

            using HttpResponseMessage health = await client.GetAsync("/api/health");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, health.StatusCode);
        }
        finally
        {
            server?.Stop();
            secretScope.Dispose();
            Settings.HTTPIPAddress = originalAddress;
            Settings.HTTPTrustedIPAddress = originalTrustedAddress;
            Settings.MicroServerActive = originalMicroActive;
            Settings.MicroAuthor = originalMicroAuthor;
            Settings.MicroCode = originalMicroCode;
            TryDelete(root);
        }
    }

    private static PropertyInfo GetKillSwitchProperty() =>
        typeof(Envir).GetProperty("KillSwitches", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("未找到 Kill Switch 正式接缝");

    private static async Task<HttpResponseMessage> SendWhenReady(
        HttpClient client, HttpMethod method, string path, string bearerToken, string? body = null)
    {
        Exception last = null;
        for (int attempt = 0; attempt < 30; attempt++)
        {
            try
            {
                var request = new HttpRequestMessage(method, path);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
                if (body != null)
                    request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                return await client.SendAsync(request);
            }
            catch (HttpRequestException error)
            {
                last = error;
                await Task.Delay(50);
            }
        }
        throw new InvalidOperationException("Kill Switch HTTP 测试服务未在期限内启动", last);
    }

    private static string NewRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "base05-kill-switch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void TryDelete(string path)
    {
        try { Directory.Delete(path, recursive: true); } catch { }
    }

    private static int GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
