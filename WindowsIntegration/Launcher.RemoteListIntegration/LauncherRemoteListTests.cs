using Launcher.Remote;
using Client;
using System.Net;
using System.Text;
using Xunit;

namespace Launcher.RemoteListIntegration.Windows;

public sealed class LauncherRemoteListTests
{
    [Fact]
    public void 有效清单解析为规范化的区服快照()
    {
        const string json = """
            {
              "version": 1,
              "maxInstances": 3,
              "patchUrl": "http://patch.example.com/root",
              "servers": [
                {
                  "name": " 一区 ",
                  "serverAddress": "game.example.com",
                  "serverPort": 7001,
                  "microEnabled": true,
                  "microAddress": "2001:db8::10",
                  "microPort": 7777
                }
              ]
            }
            """;

        RemoteLaunchManifest manifest = RemoteLaunchManifest.ParseAndValidate(json);

        Assert.Equal(1, manifest.Version);
        Assert.Equal(3, manifest.MaxInstances);
        Assert.Equal("http://patch.example.com/root/", manifest.PatchUrl);
        ServerEntry server = Assert.Single(manifest.Servers);
        Assert.Equal("一区", server.Name);
        Assert.Equal("game.example.com", server.ServerAddress);
        Assert.Equal(7001, server.ServerPort);
        Assert.Equal("http://[2001:db8::10]:7777/api/", server.BuildMicroBaseUrl());
    }

    [Fact]
    public async Task 远程清单有效时优先使用并更新缓存()
    {
        string root = Path.Combine(Path.GetTempPath(), "lyocrystal-launcher-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            const string remoteJson = """
                {
                  "version": 1,
                  "maxInstances": 2,
                  "patchUrl": "",
                  "servers": [
                    {
                      "name": "远程一区",
                      "serverAddress": "127.0.0.1",
                      "serverPort": 7000,
                      "microEnabled": false,
                      "microAddress": "",
                      "microPort": 0
                    }
                  ]
                }
                """;
            using var httpClient = new HttpClient(new StaticResponseHandler(remoteJson));
            var loader = new RemoteLaunchManifestLoader(httpClient, root, TimeSpan.FromSeconds(1));
            RemoteLaunchManifest fallback = RemoteLaunchManifest.ParseAndValidate(remoteJson.Replace("远程一区", "本地配置"));

            LaunchManifestLoadResult result = await loader.LoadAsync("http://list.example.com/launcher.txt", fallback);

            Assert.Equal(LaunchManifestSource.Remote, result.Source);
            Assert.Equal("远程一区", Assert.Single(result.Manifest.Servers).Name);
            Assert.Equal("http://list.example.com/launcher.txt", result.ListUrl);

            using var failedHttpClient = new HttpClient(new FailingResponseHandler());
            var cachedLoader = new RemoteLaunchManifestLoader(failedHttpClient, root, TimeSpan.FromSeconds(1));
            LaunchManifestLoadResult cachedResult = await cachedLoader.LoadAsync("http://list.example.com/launcher.txt", fallback);
            Assert.Equal(LaunchManifestSource.Cache, cachedResult.Source);
            Assert.Equal("远程一区", Assert.Single(cachedResult.Manifest.Servers).Name);
            Assert.Equal("http://list.example.com/launcher.txt", cachedResult.ListUrl);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task 远程有效时不会提前创建无效的本地保底()
    {
        string root = CreateTemporaryRoot();
        try
        {
            const string remoteJson = """
                {"version":1,"maxInstances":1,"patchUrl":"","servers":[{"name":"远程一区","serverAddress":"127.0.0.1","serverPort":7000,"microEnabled":false,"microAddress":"","microPort":0}]}
                """;
            using var httpClient = new HttpClient(new StaticResponseHandler(remoteJson));
            var loader = new RemoteLaunchManifestLoader(httpClient, root, TimeSpan.FromSeconds(1));

            LaunchManifestLoadResult result = await loader.LoadAsync(
                "https://list.example.com/launcher.txt",
                () => throw new InvalidLaunchManifestException("本地配置故意无效"));

            Assert.Equal(LaunchManifestSource.Remote, result.Source);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task 远程失败时使用重新校验过的有效缓存()
    {
        string root = CreateTemporaryRoot();
        try
        {
            const string cachedJson = """
                {
                  "version": 1,
                  "maxInstances": 2,
                  "patchUrl": "",
                  "servers": [
                    {
                      "name": "缓存一区",
                      "serverAddress": "127.0.0.1",
                      "serverPort": 7000,
                      "microEnabled": false,
                      "microAddress": "",
                      "microPort": 0
                    }
                  ]
                }
                """;
            await File.WriteAllTextAsync(Path.Combine(root, "RemoteLaunchManifest.json"), cachedJson);
            using var httpClient = new HttpClient(new FailingResponseHandler());
            var loader = new RemoteLaunchManifestLoader(httpClient, root, TimeSpan.FromSeconds(1));
            RemoteLaunchManifest fallback = RemoteLaunchManifest.ParseAndValidate(cachedJson.Replace("缓存一区", "本地配置"));

            LaunchManifestLoadResult result = await loader.LoadAsync("http://list.example.com/launcher.json", fallback);

            Assert.Equal(LaunchManifestSource.Cache, result.Source);
            Assert.Equal("缓存一区", Assert.Single(result.Manifest.Servers).Name);
            Assert.Contains("已使用上次区服列表", result.Warning);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task 远程和缓存均失败时使用本地保底()
    {
        string root = CreateTemporaryRoot();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "RemoteLaunchManifest.json"), "{损坏缓存");
            using var httpClient = new HttpClient(new FailingResponseHandler());
            var loader = new RemoteLaunchManifestLoader(httpClient, root, TimeSpan.FromSeconds(1));
            const string fallbackJson = """
                {
                  "version": 1,
                  "maxInstances": 1,
                  "patchUrl": "",
                  "servers": [
                    {
                      "name": "本地配置",
                      "serverAddress": "127.0.0.1",
                      "serverPort": 7000,
                      "microEnabled": false,
                      "microAddress": "",
                      "microPort": 0
                    }
                  ]
                }
                """;
            RemoteLaunchManifest fallback = RemoteLaunchManifest.ParseAndValidate(fallbackJson);

            LaunchManifestLoadResult result = await loader.LoadAsync("http://list.example.com/launcher.json", fallback);

            Assert.Equal(LaunchManifestSource.Local, result.Source);
            Assert.Same(fallback, result.Manifest);
            Assert.Contains("已使用本地配置", result.Warning);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task 超出整数范围的缓存版本会安全回退本地配置()
    {
        string root = CreateTemporaryRoot();
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(root, "RemoteLaunchManifest.json"),
                "{\"cacheVersion\":999999999999999999999,\"listUrl\":\"https://list.example.com/list.txt\",\"manifest\":{}}");
            using var httpClient = new HttpClient(new FailingResponseHandler());
            var loader = new RemoteLaunchManifestLoader(httpClient, root, TimeSpan.FromSeconds(1));

            LaunchManifestLoadResult result = await loader.LoadAsync("https://list.example.com/list.txt", CreateFallback());

            Assert.Equal(LaunchManifestSource.Local, result.Source);
            Assert.Contains("已使用本地配置", result.Warning);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("\"maxInstances\": 0", "\"maxInstances\": 1")]
    [InlineData("\"maxInstances\": 11", "\"maxInstances\": 1")]
    [InlineData("\"name\": \"一区\", \"unexpected\": true", "\"name\": \"一区\"")]
    [InlineData("\"microEnabled\": false, \"microEnabled\": true", "\"microEnabled\": false")]
    public void 非法字段使整份清单失效(string replacement, string source)
    {
        const string valid = """
            {
              "version": 1,
              "maxInstances": 1,
              "patchUrl": "",
              "servers": [
                {
                  "name": "一区",
                  "serverAddress": "127.0.0.1",
                  "serverPort": 7000,
                  "microEnabled": false,
                  "microAddress": "",
                  "microPort": 0
                }
              ]
            }
            """;

        Assert.Throws<InvalidLaunchManifestException>(() => RemoteLaunchManifest.ParseAndValidate(valid.Replace(source, replacement)));
    }

    [Fact]
    public async Task 超过一兆的远程响应被拒绝并回退本地配置()
    {
        string root = CreateTemporaryRoot();
        try
        {
            byte[] oversized = new byte[RemoteLaunchManifestLoader.MaximumResponseBytes + 1];
            using var httpClient = new HttpClient(new ByteResponseHandler(oversized));
            var loader = new RemoteLaunchManifestLoader(httpClient, root, TimeSpan.FromSeconds(1));
            RemoteLaunchManifest fallback = CreateFallback();

            LaunchManifestLoadResult result = await loader.LoadAsync("https://list.example.com/list.any", fallback);

            Assert.Equal(LaunchManifestSource.Local, result.Source);
            Assert.Contains("超过 1 MiB", result.Warning);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(LaunchManifestSource.Remote, "http://list.example.com/list.txt", "https://patch.example.com/", "")]
    [InlineData(LaunchManifestSource.Remote, "https://list.example.com/list.txt", "http://patch.example.com/", "")]
    [InlineData(LaunchManifestSource.Remote, "https://list.example.com/list.txt", "https://patch.example.com/", "https://patch.example.com/")]
    [InlineData(LaunchManifestSource.Cache, "https://list.example.com/list.txt", "https://patch.example.com/", "https://patch.example.com/")]
    [InlineData(LaunchManifestSource.Local, "", "http://127.0.0.1/patch/", "http://127.0.0.1/patch/")]
    public void 远程补丁只有在清单和补丁地址均为HTTPS时启用(
        LaunchManifestSource source,
        string listUrl,
        string patchUrl,
        string expected)
    {
        Assert.Equal(expected, RemotePatchPolicy.ResolvePatchUrl(source, listUrl, patchUrl));
    }

    [Fact]
    public async Task HTTP来源缓存不能被后来配置的HTTPS地址授予补丁权限()
    {
        string root = CreateTemporaryRoot();
        try
        {
            const string remoteJson = """
                {"version":1,"maxInstances":1,"patchUrl":"https://evil.example.com/patch/","servers":[{"name":"一区","serverAddress":"127.0.0.1","serverPort":7000,"microEnabled":false,"microAddress":"","microPort":0}]}
                """;
            using (var httpClient = new HttpClient(new StaticResponseHandler(remoteJson)))
            {
                var loader = new RemoteLaunchManifestLoader(httpClient, root, TimeSpan.FromSeconds(1));
                await loader.LoadAsync("http://list.example.com/launcher.txt", CreateFallback());
            }

            using var failedHttpClient = new HttpClient(new FailingResponseHandler());
            var cachedLoader = new RemoteLaunchManifestLoader(failedHttpClient, root, TimeSpan.FromSeconds(1));
            LaunchManifestLoadResult cached = await cachedLoader.LoadAsync("https://list.example.com/launcher.txt", CreateFallback());

            Assert.Equal(LaunchManifestSource.Cache, cached.Source);
            Assert.Equal("http://list.example.com/launcher.txt", cached.ListUrl);
            Assert.Equal(string.Empty, RemotePatchPolicy.ResolvePatchUrl(cached.Source, cached.ListUrl, cached.Manifest.PatchUrl));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task 启动器状态独立保存并恢复上次区服名()
    {
        string root = CreateTemporaryRoot();
        try
        {
            var store = new LauncherStateStore(root);

            await store.SaveLastServerNameAsync(" 一区 ");

            Assert.Equal("一区", await store.LoadLastServerNameAsync());
            Assert.False(File.Exists(Path.Combine(root, "Mir2Config.ini")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task 并发保存区服选择会串行完成且不遗留临时文件()
    {
        string root = CreateTemporaryRoot();
        try
        {
            var store = new LauncherStateStore(root);
            Task first = store.SaveLastServerNameAsync("一区");
            Task second = store.SaveLastServerNameAsync("二区");

            await Task.WhenAll(first, second);

            Assert.Equal("二区", await store.LoadLastServerNameAsync());
            Assert.False(File.Exists(Path.Combine(root, "LauncherState.json.tmp")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void 游戏子进程参数可以无损往返并拒绝缺项()
    {
        var server = new ServerEntry("一区", "game.example.com", 7001, true, "micro.example.com", 8080, "backup.example.com", 8081);

        string[] arguments = GameLaunchArguments.Create(server);
        Assert.True(GameLaunchArguments.TryParse(arguments, out GameLaunchOptions options));
        Assert.Equal("game.example.com", options.ServerAddress);
        Assert.Equal(7001, options.ServerPort);
        Assert.True(options.MicroEnabled);
        Assert.Equal("micro.example.com", options.MicroAddress);
        Assert.Equal(8080, options.MicroPort);
        Assert.Equal("backup.example.com", options.MicroBackupAddress);
        Assert.Equal(8081, options.MicroBackupPort);
        Assert.False(GameLaunchArguments.TryParse(arguments[..^2], out _));
        Assert.True(GameLaunchArguments.TryParse(arguments.Append("-tc").ToArray(), out _));

        string[] invalidHost = (string[])arguments.Clone();
        invalidHost[2] = "https://game.example.com/path";
        Assert.False(GameLaunchArguments.TryParse(invalidHost, out _));
    }

    [Fact]
    public void 多开限制只按启动器追踪的存活子进程计数()
    {
        var limit = new GameInstanceLimit(2);

        Assert.True(limit.TryAcquire());
        Assert.True(limit.TryAcquire());
        Assert.False(limit.TryAcquire());
        limit.Release();
        Assert.True(limit.TryAcquire());
        Assert.Equal(2, limit.ActiveCount);
    }

    [Fact]
    public void 游戏运行期间保存配置不会落盘临时选区()
    {
        string root = CreateTemporaryRoot();
        string originalDirectory = Environment.CurrentDirectory;
        try
        {
            Environment.CurrentDirectory = root;
            Settings.UseTestConfig = true;
            Settings.UseConfig = true;
            Settings.UseTlsV2 = true;
            Settings.IPAddress = "configured.example.com";
            Settings.Port = 7000;
            Settings.TlsPort = 7001;
            Settings.MicroBaseUrl = "http://configured.example.com:8000/api/";

            Settings.ApplyGameEndpointOverride("selected.example.com", 9001, "http://selected.example.com:9002/api/");
            Settings.Save();

            var reader = new InIReader(Path.Combine(root, "Mir2Test.ini"));
            Assert.Equal(7001, reader.ReadInt32("Network", "TlsPort", 0));
            Assert.Equal("http://configured.example.com:8000/api/", reader.ReadString("Micro", "BaseUrl", string.Empty));
            Assert.Equal("selected.example.com", Settings.IPAddress);
            Assert.Equal(9001, Settings.TlsPort);
        }
        finally
        {
            Settings.ClearGameEndpointOverride();
            Environment.CurrentDirectory = originalDirectory;
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTemporaryRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "lyocrystal-launcher-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static RemoteLaunchManifest CreateFallback() => RemoteLaunchManifest.ParseAndValidate("""
        {
          "version": 1,
          "maxInstances": 1,
          "patchUrl": "",
          "servers": [
            {
              "name": "本地配置",
              "serverAddress": "127.0.0.1",
              "serverPort": 7000,
              "microEnabled": false,
              "microAddress": "",
              "microPort": 0
            }
          ]
        }
        """);

    private sealed class StaticResponseHandler(string content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json"),
            };
            return Task.FromResult(response);
        }
    }

    private sealed class FailingResponseHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("测试故障");
    }

    private sealed class ByteResponseHandler(byte[] content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(content) });
    }
}
