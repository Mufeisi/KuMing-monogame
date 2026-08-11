namespace LyoCrystal.MicroGateway.App;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 2 && string.Equals(args[0], "--gateway-smoke", StringComparison.OrdinalIgnoreCase))
            return RunSmokeAsync(args[1]).GetAwaiter().GetResult();

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
        return 0;
    }

    private static async Task<int> RunSmokeAsync(string resourceRoot)
    {
        int port;
        var probe = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        probe.Start();
        port = ((System.Net.IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        await using var host = new MicroHttpListenerHost();
        try
        {
            await host.StartAsync($"http://127.0.0.1:{port}/", new MicroGatewayOptions(resourceRoot, "smoke-user", "smoke-code", resourceRoot));
            using var client = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = new Uri($"http://127.0.0.1:{port}/") };
            using HttpResponseMessage health = await client.GetAsync("api/health");
            using var request = new HttpRequestMessage(HttpMethod.Get, "api/file/Data/ChrSel.Lib");
            request.Headers.Add("User", "smoke-user");
            request.Headers.Add("Code", "smoke-code");
            request.Headers.TryAddWithoutValidation("Range", "bytes=0-31");
            using HttpResponseMessage file = await client.SendAsync(request);
            byte[] bytes = await file.Content.ReadAsByteArrayAsync();
            return health.IsSuccessStatusCode && file.StatusCode == System.Net.HttpStatusCode.PartialContent && bytes.Length == 32 ? 0 : 2;
        }
        catch { return 1; }
    }
}
