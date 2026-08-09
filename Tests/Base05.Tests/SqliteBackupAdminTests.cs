using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Server;
using Server.Library.Utils;
using Server.Persistence.Sql;
using Server.Security;
using Xunit;

namespace Base05.Tests;

[Collection("SEC04环境")]
public sealed class SqliteBackupAdminTests
{
    [Fact]
    public async Task 管理员可一键触发备份且操作员只能查询持久状态()
    {
        string root = Path.Combine(Path.GetTempPath(), "base05-db03-admin-" + Guid.NewGuid().ToString("N"));
        string sourcePath = Path.Combine(root, "source", "server.db");
        string localDirectory = Path.Combine(root, "local");
        string offsiteDirectory = Path.Combine(root, "offsite");
        string secretDirectory = Path.Combine(root, "secrets");
        int port = GetFreePort();
        string originalAddress = Settings.HTTPIPAddress;
        string originalTrustedAddress = Settings.HTTPTrustedIPAddress;
        IDisposable secretScope = ProtectedSecretStore.UseTestRoot(secretDirectory);
        HttpServer server = null;
        SqliteBackupService service = null;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
            using (var connection = new SqliteConnection($"Data Source={sourcePath};Pooling=False"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "CREATE TABLE proof(value TEXT NOT NULL); INSERT INTO proof(value) VALUES ('db03');";
                command.ExecuteNonQuery();
            }

            service = new SqliteBackupService(new SqliteBackupOptions
            {
                SourcePath = sourcePath,
                BackupDirectory = localDirectory,
                OffsiteDirectory = offsiteDirectory,
                RetentionCount = 2,
                Interval = TimeSpan.FromHours(1),
            });
            Settings.HTTPIPAddress = $"http://127.0.0.1:{port}/";
            Settings.HTTPTrustedIPAddress = "127.0.0.1";
            ProtectedSecretStore.Write(ProtectedSecretStore.AdministratorToken, "administrator-secret-32-characters-minimum");
            ProtectedSecretStore.Write(ProtectedSecretStore.OperatorToken, "operator-secret-32-characters-minimum");
            server = new HttpServer(service);
            server.Start();

            using var handler = new HttpClientHandler { UseProxy = false };
            using var client = new HttpClient(handler) { BaseAddress = new Uri(Settings.HTTPIPAddress) };

            using (HttpResponseMessage response = await SendWhenReady(
                       client, HttpMethod.Get, "/backup/status", "operator-secret-32-characters-minimum"))
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            using (var forbiddenRun = Authorized(HttpMethod.Post, "/backup/run", "operator-secret-32-characters-minimum"))
                Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(forbiddenRun)).StatusCode);

            using (var run = Authorized(HttpMethod.Post, "/backup/run", "administrator-secret-32-characters-minimum"))
                Assert.Equal(HttpStatusCode.Accepted, (await client.SendAsync(run)).StatusCode);

            SqliteBackupStatus status = null;
            for (int attempt = 0; attempt < 60; attempt++)
            {
                using var request = Authorized(HttpMethod.Get, "/backup/status", "operator-secret-32-characters-minimum");
                using HttpResponseMessage response = await client.SendAsync(request);
                status = JsonSerializer.Deserialize<SqliteBackupStatus>(await response.Content.ReadAsStringAsync());
                if (status?.State == SqliteBackupState.Succeeded) break;
                await Task.Delay(50);
            }

            Assert.NotNull(status);
            Assert.Equal(SqliteBackupState.Succeeded, status.State);
            Assert.True(File.Exists(status.LastLocalPath));
            Assert.True(File.Exists(status.LastOffsitePath));
        }
        finally
        {
            server?.Stop();
            service?.Dispose();
            secretScope.Dispose();
            Settings.HTTPIPAddress = originalAddress;
            Settings.HTTPTrustedIPAddress = originalTrustedAddress;
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static HttpRequestMessage Authorized(HttpMethod method, string path, string token)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static async Task<HttpResponseMessage> SendWhenReady(
        HttpClient client,
        HttpMethod method,
        string path,
        string token)
    {
        Exception last = null;
        for (int attempt = 0; attempt < 30; attempt++)
        {
            try
            {
                using var request = Authorized(method, path, token);
                return await client.SendAsync(request);
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
