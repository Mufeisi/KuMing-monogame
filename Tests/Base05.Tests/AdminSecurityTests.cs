using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using Server;
using Server.Library.Utils;
using Server.Security;
using Xunit;

namespace Base05.Tests;

[Collection("SEC04环境")]
public sealed class AdminSecurityTests
{
    [Fact]
    public void 管理监听仅允许回环明文或内网HTTPS()
    {
        AdminSecurityPolicy.ValidateListener("http://127.0.0.1:7777/");
        AdminSecurityPolicy.ValidateListener("http://localhost:7777/");
        AdminSecurityPolicy.ValidateListener("https://10.20.30.40:7777/");
        AdminSecurityPolicy.ValidateListener("https://192.168.1.20:7777/");
        AdminSecurityPolicy.ValidateListener("https://[fd00::20]:7777/");

        Assert.Throws<InvalidOperationException>(() =>
            AdminSecurityPolicy.ValidateListener("http://192.168.1.20:7777/"));
        Assert.Throws<InvalidOperationException>(() =>
            AdminSecurityPolicy.ValidateListener("http://0.0.0.0:7777/"));
        Assert.Throws<InvalidOperationException>(() =>
            AdminSecurityPolicy.ValidateListener("https://203.0.113.10:7777/"));
        Assert.Throws<InvalidOperationException>(() =>
            AdminSecurityPolicy.ValidateListener("http://+:7777/"));
    }

    [Fact]
    public void 独立令牌映射角色且操作员不能执行高权限操作()
    {
        const string administrator = "administrator-secret";
        const string operatorToken = "operator-secret";

        Assert.Equal(AdminAuthorizationStatus.Unconfigured,
            AdminSecurityPolicy.Authorize(null, "/", null, null).Status);
        Assert.Equal(AdminAuthorizationStatus.Unauthorized,
            AdminSecurityPolicy.Authorize(null, "/", administrator, operatorToken).Status);
        Assert.Equal(AdminAuthorizationStatus.Unauthorized,
            AdminSecurityPolicy.Authorize("Bearer wrong", "/", administrator, operatorToken).Status);

        var operatorBroadcast = AdminSecurityPolicy.Authorize(
            "Bearer " + operatorToken, "/broadcast", administrator, operatorToken);
        Assert.Equal(AdminAuthorizationStatus.Authorized, operatorBroadcast.Status);
        Assert.Equal(AdminRole.Operator, operatorBroadcast.Role);
        Assert.Equal(AdminAuthorizationStatus.Forbidden,
            AdminSecurityPolicy.Authorize(
                "Bearer " + operatorToken, "/newaccount", administrator, operatorToken).Status);
        Assert.Equal(AdminAuthorizationStatus.Unconfigured,
            AdminSecurityPolicy.Authorize(
                "Bearer " + operatorToken, "/", operatorToken, operatorToken).Status);

        var administratorProvisioning = AdminSecurityPolicy.Authorize(
            "Bearer " + administrator, "/newaccount", administrator, operatorToken);
        Assert.Equal(AdminAuthorizationStatus.Authorized, administratorProvisioning.Status);
        Assert.Equal(AdminRole.Administrator, administratorProvisioning.Role);
    }

    [Fact]
    public void 管理审计只记录动作角色来源与结果()
    {
        var authorization = AdminSecurityPolicy.Authorize(
            "Bearer administrator-secret", "/newaccount", "administrator-secret", "operator-secret");
        string audit = AdminSecurityPolicy.BuildAuditLine(
            DateTimeOffset.UnixEpoch, "127.0.0.1\r\nforged=true", "GET", authorization);

        Assert.Contains("ADMIN_AUDIT", audit);
        Assert.Contains("client_ref=", audit);
        Assert.Contains("action=new-account", audit);
        Assert.Contains("principal=Administrator", audit);
        Assert.Contains("result=Authorized", audit);
        Assert.DoesNotContain("administrator-secret", audit);
        Assert.DoesNotContain("Bearer", audit);
        Assert.DoesNotContain("127.0.0.1", audit);
        Assert.DoesNotContain("forged", audit);
        Assert.DoesNotContain('\r', audit);
        Assert.DoesNotContain('\n', audit);
    }

    [Fact]
    public async Task 真实管理端点拒绝缺失错误及越权凭据()
    {
        int port = GetFreePort();
        string originalAddress = Settings.HTTPIPAddress;
        string originalTrustedAddress = Settings.HTTPTrustedIPAddress;
        string originalAdministrator = Environment.GetEnvironmentVariable(
            AdminSecurityPolicy.AdministratorTokenEnvironmentVariable);
        string originalOperator = Environment.GetEnvironmentVariable(
            AdminSecurityPolicy.OperatorTokenEnvironmentVariable);
        string auditDirectory = Path.Combine(Path.GetTempPath(), "LyoCrystalAdminAudit-" + Guid.NewGuid().ToString("N"));
        HttpServer server = null;
        try
        {
            Logger.Flush(TimeSpan.FromSeconds(2));
            Logger.Configure(new LoggerOptions { Directory = auditDirectory, MaxFileSizeMB = 1, RetentionDays = 1 });
            Settings.HTTPIPAddress = $"http://127.0.0.1:{port}/";
            Settings.HTTPTrustedIPAddress = "127.0.0.1";
            Environment.SetEnvironmentVariable(AdminSecurityPolicy.AdministratorTokenEnvironmentVariable, "administrator-secret-32-characters-minimum");
            Environment.SetEnvironmentVariable(AdminSecurityPolicy.OperatorTokenEnvironmentVariable, "operator-secret-32-characters-minimum");
            server = new HttpServer();
            server.Start();

            using var handler = new HttpClientHandler { UseProxy = false };
            using var client = new HttpClient(handler) { BaseAddress = new Uri(Settings.HTTPIPAddress) };
            HttpResponseMessage missing = await SendWhenReady(client, "/");
            Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);

            using var wrong = new HttpRequestMessage(HttpMethod.Get, "/");
            wrong.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "wrong-secret-32-characters-minimum");
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(wrong)).StatusCode);

            using var operatorStatus = new HttpRequestMessage(HttpMethod.Get, "/");
            operatorStatus.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "operator-secret-32-characters-minimum");
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(operatorStatus)).StatusCode);

            using var operatorProvision = new HttpRequestMessage(HttpMethod.Get, "/newaccount");
            operatorProvision.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "operator-secret-32-characters-minimum");
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(operatorProvision)).StatusCode);

            using var administratorUnknown = new HttpRequestMessage(HttpMethod.Get, "/unknown");
            administratorUnknown.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "administrator-secret-32-characters-minimum");
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(administratorUnknown)).StatusCode);

            using var postMissing = new HttpRequestMessage(HttpMethod.Post, "/broadcast");
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(postMissing)).StatusCode);
            using var postOperator = new HttpRequestMessage(HttpMethod.Post, "/broadcast");
            postOperator.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "operator-secret-32-characters-minimum");
            Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.SendAsync(postOperator)).StatusCode);

            Logger.Flush(TimeSpan.FromSeconds(2));
            string auditText = string.Join('\n', Directory.GetFiles(auditDirectory, "*.log", SearchOption.AllDirectories)
                .Select(File.ReadAllText));
            Assert.Contains("ADMIN_AUDIT", auditText);
            Assert.Contains("principal=Operator", auditText);
            Assert.DoesNotContain("operator-secret", auditText);
            Assert.DoesNotContain("127.0.0.1", auditText);
        }
        finally
        {
            server?.Stop();
            Settings.HTTPIPAddress = originalAddress;
            Settings.HTTPTrustedIPAddress = originalTrustedAddress;
            Environment.SetEnvironmentVariable(AdminSecurityPolicy.AdministratorTokenEnvironmentVariable, originalAdministrator);
            Environment.SetEnvironmentVariable(AdminSecurityPolicy.OperatorTokenEnvironmentVariable, originalOperator);
            Logger.Flush(TimeSpan.FromSeconds(2));
            Logger.Configure(new LoggerOptions
            {
                Directory = Settings.LogDirectory,
                MaxFileSizeMB = Settings.LogFileMaxSizeMB,
                RetentionDays = Settings.LogRetentionDays,
            });
            try { Directory.Delete(auditDirectory, true); } catch { }
        }
    }

    private static async Task<HttpResponseMessage> SendWhenReady(HttpClient client, string path)
    {
        Exception last = null;
        for (int attempt = 0; attempt < 30; attempt++)
        {
            try
            {
                return await client.GetAsync(path);
            }
            catch (HttpRequestException error)
            {
                last = error;
                await Task.Delay(50);
            }
        }
        throw new InvalidOperationException("管理 HTTP 测试服务未在期限内启动", last);
    }

    private static int GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}

[CollectionDefinition("SEC04环境", DisableParallelization = true)]
public sealed class AdminSecurityCollection
{
}
