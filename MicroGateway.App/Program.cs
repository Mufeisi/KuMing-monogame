namespace LyoCrystal.MicroGateway.App;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 1 && string.Equals(args[0], "--service", StringComparison.OrdinalIgnoreCase))
            return WindowsServiceHost.Run();
        if ((args.Length == 1 || (args.Length == 3 && args[1] == "--caller-sid")) && args[0] is "--configure-network" or "--rollback-network" or "--install-service" or "--uninstall-service")
        {
            try
            {
                GatewayProjectConfiguration operationProject = GatewayProjectConfiguration.TryLoad(AppContext.BaseDirectory) ?? throw new InvalidDataException("gateway-project.json 无效");
                if (args[0] == "--configure-network")
                {
                    string callerSid = args.Length == 3 ? args[2] : (System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value ?? throw new InvalidOperationException("无法读取当前用户 SID。"));
                    WindowsGatewayOperations.ConfigureNetwork(operationProject, callerSid);
                }
                else if (args[0] == "--rollback-network") WindowsGatewayOperations.RollbackNetwork(operationProject);
                else if (args[0] == "--install-service") WindowsGatewayOperations.InstallService(Environment.ProcessPath!, operationProject.ReadSecret(AppContext.BaseDirectory, false));
                else WindowsGatewayOperations.UninstallService();
                return 0;
            }
            catch (Exception error) { MessageBox.Show(error.Message, "管理员操作失败", MessageBoxButtons.OK, MessageBoxIcon.Error); return 1; }
        }
        if (args.Length == 2 && string.Equals(args[0], "--gateway-smoke", StringComparison.OrdinalIgnoreCase))
        {
            GatewayProjectConfiguration? smokeProject = GatewayProjectConfiguration.TryLoad(AppContext.BaseDirectory);
            smokeProject?.ImportSecretIfPresent(AppContext.BaseDirectory);
            string user = smokeProject?.User ?? "smoke-user";
            string code = smokeProject is null ? "smoke-code" : Shared.Security.ProtectedClientSecretStore.ReadMicroCode(smokeProject.ProjectId);
            return RunSmokeAsync(args[1], user, code).GetAwaiter().GetResult();
        }

        ApplicationConfiguration.Initialize();
        GatewayProjectConfiguration? project = GatewayProjectConfiguration.TryLoad(AppContext.BaseDirectory);
        project?.ImportSecretIfPresent(AppContext.BaseDirectory);
        Application.Run(new MainForm(project));
        return 0;
    }

    private static async Task<int> RunSmokeAsync(string resourceRoot, string user, string code)
    {
        int port;
        var probe = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        probe.Start();
        port = ((System.Net.IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        await using var host = new MicroHttpListenerHost();
        try
        {
            await host.StartAsync($"http://127.0.0.1:{port}/", new MicroGatewayOptions(resourceRoot, user, code, resourceRoot));
            using var client = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = new Uri($"http://127.0.0.1:{port}/") };
            using HttpResponseMessage health = await client.GetAsync("api/health");
            using var request = new HttpRequestMessage(HttpMethod.Get, "api/file/Data/ChrSel.Lib");
            request.Headers.Add("User", user);
            request.Headers.Add("Code", code);
            request.Headers.TryAddWithoutValidation("Range", "bytes=0-31");
            using HttpResponseMessage file = await client.SendAsync(request);
            byte[] bytes = await file.Content.ReadAsByteArrayAsync();
            return health.IsSuccessStatusCode && file.StatusCode == System.Net.HttpStatusCode.PartialContent && bytes.Length == 32 ? 0 : 2;
        }
        catch { return 1; }
    }
}
