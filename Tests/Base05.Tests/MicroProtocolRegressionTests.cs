using System.Net;
using System.Net.Sockets;
using Server;
using Server.Library;
using Server.Library.Utils;
using Server.MirEnvir;
using Server.Security;
using Xunit;

namespace Base05.Tests;

[Collection("SEC04环境")]
public sealed class MicroProtocolRegressionTests
{
    [Fact]
    public async Task 现有文件Range图库头单图与声音路由保持客户端协议响应()
    {
        string repositoryRoot = FindRepositoryRoot(AppContext.BaseDirectory);
        string resourceRoot = Path.Combine(repositoryRoot, "Client_MonoGame.Shared", "BootstrapAssets");
        string secretRoot = Path.Combine(Path.GetTempPath(), "LyoCrystalMicroProtocolSecrets", Guid.NewGuid().ToString("N"));
        int port = GetFreePort();
        string originalAddress = Settings.HTTPIPAddress;
        bool originalActive = Settings.MicroServerActive;
        string originalAuthor = Settings.MicroAuthor;
        string originalCode = Settings.MicroCode;
        string originalResourcePath = Settings.MicroResourcePath;
        IDisposable secretScope = ProtectedSecretStore.UseTestRoot(secretRoot);
        HttpServer? server = null;
        try
        {
            Settings.HTTPIPAddress = $"http://127.0.0.1:{port}/";
            Settings.MicroServerActive = true;
            Settings.MicroAuthor = "protocol-reader";
            Settings.MicroCode = "protocol-code";
            Settings.MicroResourcePath = resourceRoot;
            server = new HttpServer();
            server.Start();

            using var handler = new HttpClientHandler { UseProxy = false };
            using var client = new HttpClient(handler) { BaseAddress = new Uri(Settings.HTTPIPAddress) };
            await WaitUntilReady(client);

            using var rangeRequest = Authorized(HttpMethod.Get, "/api/file/Data/ChrSel.Lib");
            rangeRequest.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 31);
            using HttpResponseMessage range = await client.SendAsync(rangeRequest);
            Assert.Equal(HttpStatusCode.PartialContent, range.StatusCode);
            Assert.Equal("bytes 0-31/18921965", range.Content.Headers.ContentRange?.ToString());
            Assert.Equal(32, (await range.Content.ReadAsByteArrayAsync()).Length);

            using HttpResponseMessage header = await client.SendAsync(Authorized(HttpMethod.Get, "/api/libheader/Data/ChrSel.Lib"));
            byte[] headerBytes = await header.Content.ReadAsByteArrayAsync();
            Assert.Equal(HttpStatusCode.OK, header.StatusCode);
            Assert.NotEmpty(headerBytes);

            using HttpResponseMessage image = await client.SendAsync(Authorized(HttpMethod.Get, "/api/libimage/Data/ChrSel.Lib/0"));
            byte[] imageBytes = await image.Content.ReadAsByteArrayAsync();
            Assert.Equal(HttpStatusCode.OK, image.StatusCode);
            Assert.NotEmpty(imageBytes);

            using HttpResponseMessage sound = await client.SendAsync(Authorized(HttpMethod.Get, "/api/sound/100"));
            string soundJson = await sound.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, sound.StatusCode);
            Assert.Contains("\"Max\":1", soundJson, StringComparison.Ordinal);
            Assert.Contains("\"Current\":1", soundJson, StringComparison.Ordinal);
        }
        finally
        {
            server?.Stop();
            secretScope.Dispose();
            Settings.HTTPIPAddress = originalAddress;
            Settings.MicroServerActive = originalActive;
            Settings.MicroAuthor = originalAuthor;
            Settings.MicroCode = originalCode;
            Settings.MicroResourcePath = originalResourcePath;
            if (Directory.Exists(secretRoot)) Directory.Delete(secretRoot, recursive: true);
        }
    }

    private static HttpRequestMessage Authorized(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("User", "protocol-reader");
        request.Headers.Add("Code", "protocol-code");
        return request;
    }

    private static async Task WaitUntilReady(HttpClient client)
    {
        Exception? last = null;
        for (int attempt = 0; attempt < 40; attempt++)
        {
            try
            {
                using HttpResponseMessage response = await client.GetAsync("/api/health");
                if (response.StatusCode == HttpStatusCode.OK) return;
            }
            catch (HttpRequestException ex)
            {
                last = ex;
            }
            await Task.Delay(50);
        }
        throw new InvalidOperationException("微端协议测试服务器未就绪", last);
    }

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static string FindRepositoryRoot(string start)
    {
        DirectoryInfo? current = new(start);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "global.json"))) return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("无法定位仓库根目录");
    }
}
