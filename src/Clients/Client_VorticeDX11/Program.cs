using Client.Resolution;
using Client.Bootstrap;
using Client.Diagnostics;
using Launcher;
using Launcher.Remote;
using Launcher.ThemeRuntime;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Drawing.Imaging;

namespace Client
{
    internal static class Program
    {
        public static CMain Form;
        public static AMain PForm;

        public static bool Restart;
        public static bool Launch;

        [STAThread]
        private static void Main(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
                PcCrashDiagnostics.Capture(eventArgs.ExceptionObject as Exception ??
                    new InvalidOperationException("PC 客户端发生未知未处理异常"));
            bool runPreLoginUpdateCli = args.Any(item => string.Equals(item, "--prelogin-update-cli", StringComparison.OrdinalIgnoreCase));
            bool gameInstance = GameLaunchArguments.TryParse(args, out GameLaunchOptions launchOptions);
            bool gameInstanceRequested = args.Any(item => string.Equals(item, "--game-instance", StringComparison.OrdinalIgnoreCase));
            bool legacyLauncher = args.Any(item => string.Equals(item, "--legacy-launcher", StringComparison.OrdinalIgnoreCase));
            int renderIndex = Array.FindIndex(args, item => string.Equals(item, "--theme-render-smoke", StringComparison.OrdinalIgnoreCase));
            if (renderIndex >= 0 && renderIndex + 1 < args.Length)
            {
                Environment.Exit(RenderThemeEvidence(args[renderIndex + 1]));
                return;
            }
            int classicRenderIndex = Array.FindIndex(args, item => string.Equals(item, "--native-classic-render-smoke", StringComparison.OrdinalIgnoreCase));
            if (classicRenderIndex >= 0 && classicRenderIndex + 1 < args.Length)
            {
                Environment.Exit(RenderNativeClassicEvidence(args[classicRenderIndex + 1]));
                return;
            }

            if (args.Length > 0)
            {
                foreach (var arg in args)
                {
                    if (arg.ToLower() == "-tc") Settings.UseTestConfig = true;
                }
            }

#if DEBUG
            Settings.UseTestConfig = true;
#endif

            if (runPreLoginUpdateCli)
            {
                int exitCode = RunPreLoginUpdateCli(args);
                Environment.Exit(exitCode);
                return;
            }

            try
            {
                if (gameInstanceRequested && !gameInstance)
                    throw new ArgumentException("游戏子进程启动参数不完整或无效");

                Client.Utils.ResolutionTrace.StartSession("Client Startup");
                System.Windows.Forms.Application.SetHighDpiMode(System.Windows.Forms.HighDpiMode.PerMonitorV2);

                if (!gameInstance && UpdatePatcher()) return;

                if (RuntimePolicyHelper.LegacyV2RuntimeEnabledSuccessfully == true) { }

                Packet.IsServer = false;
                Settings.Load();
                if (gameInstance) PcSmokeTestAutomation.ApplyEnvironmentOverrides();
                if (gameInstance)
                {
                    string microBaseUrl = launchOptions.MicroEnabled
                        ? new UriBuilder(Uri.UriSchemeHttp, launchOptions.MicroAddress, launchOptions.MicroPort, "api/").Uri.AbsoluteUri
                        : string.Empty;
                    string microBackupBaseUrl = launchOptions.MicroEnabled && !string.IsNullOrWhiteSpace(launchOptions.MicroBackupAddress)
                        ? new UriBuilder(Uri.UriSchemeHttp, launchOptions.MicroBackupAddress, launchOptions.MicroBackupPort, "api/").Uri.AbsoluteUri
                        : string.Empty;
                    Settings.ApplyGameEndpointOverride(launchOptions.ServerAddress, launchOptions.ServerPort, microBaseUrl, microBackupBaseUrl);
                }
                Client.Utils.ResolutionTrace.LogClientState("Program.Main", "After Settings.Load");

                if (!gameInstance && !legacyLauncher)
                {
                    RunThemeLauncher();
                    Settings.Save();
                    return;
                }

                System.Windows.Forms.Application.EnableVisualStyles();
                System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);

                CheckResolutionSetting();
                Client.Utils.ResolutionTrace.LogClientState("Program.Main", "After CheckResolutionSetting");

                Launch = false;
                if (gameInstance)
                    Launch = true;
                else if (Settings.P_Patcher)
                    System.Windows.Forms.Application.Run(PForm = new AMain());
                else
                    Launch = true;

                if (Launch)
                {
                    // 玩家入口载荷已经包含启动核心；游戏实例不得被普通资源包更新阻断。
                    if (!gameInstance) TryRunPcPreLoginUpdate();
                    System.Windows.Forms.Application.Run(Form = new CMain());
                }

                if (gameInstance)
                {
                    Settings.ClearGameEndpointOverride();
                }
                if (!PcSmokeTestAutomation.Active) Settings.Save();

                if (Restart)
                {
                    System.Windows.Forms.Application.Restart();
                }
            }
            catch (Exception ex)
            {
                CMain.SaveError(ex.ToString());
                PcCrashDiagnostics.Capture(ex);
            }
        }

        private static int RenderThemeEvidence(string outputDirectory)
        {
            string root = Path.GetFullPath(outputDirectory);
            Directory.CreateDirectory(root);
            foreach (LauncherTemplateKind kind in Enum.GetValues<LauncherTemplateKind>())
            foreach (int dpi in new[] { 100, 125, 150, 200 })
            {
                using Bitmap bitmap = LauncherRuntimeHost.RenderTemplateForEvidence(LauncherTemplateCatalog.Create(kind), root, dpi / 100f);
                bitmap.Save(Path.Combine(root, $"{kind.ToString().ToLowerInvariant()}-{dpi}.png"), ImageFormat.Png);
            }
            return Directory.EnumerateFiles(root, "*.png").Count() == 12 ? 0 : 2;
        }

        private static void RunThemeLauncher()
        {
            string clientDirectory = AppContext.BaseDirectory;
            LauncherRuntimeHost.Run(clientDirectory, (executableDirectory, resourceDirectory, server, micro, _) =>
            {
                string selectedClient = Path.Combine(executableDirectory, "Client.exe");
                var start = new ProcessStartInfo(selectedClient) { WorkingDirectory = resourceDirectory, UseShellExecute = false };
                foreach (string argument in GameProcessLaunchArguments.Create(server, micro, ClientCapabilityProbe.Detect(executableDirectory))) start.ArgumentList.Add(argument);
                using Process? game = Process.Start(start);
                string sourcePlayer = Environment.GetEnvironmentVariable("LYOCRYSTAL_PLAYER_SOURCE_EXECUTABLE") ?? string.Empty;
                if (game is not null && !string.IsNullOrWhiteSpace(sourcePlayer))
                {
                    try { Launcher.PlayerShell.PlayerGameSessionMarker.Record(sourcePlayer, game); }
                    catch { try { game.Kill(entireProcessTree: true); } catch { } throw; }
                }
            }, RunOriginalClassicLauncher);
        }

        private static int RenderNativeClassicEvidence(string outputPath)
        {
            System.Windows.Forms.Application.SetHighDpiMode(System.Windows.Forms.HighDpiMode.PerMonitorV2);
            System.Windows.Forms.Application.EnableVisualStyles();
            LauncherSnapshot snapshot = LauncherTemplateCatalog.Create(LauncherTemplateKind.Classic);
            snapshot.ProjectName = "酷明传奇";
            snapshot.TaskbarName = "酷明传奇";
            using var form = new AMain(snapshot, AppContext.BaseDirectory, AppContext.BaseDirectory);
            using var bitmap = new Bitmap(form.Width, form.Height, PixelFormat.Format32bppArgb);
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
            bitmap.Save(outputPath, ImageFormat.Png);
            return form.ClientSize == new Size(801, 554) && form.Text == "酷明传奇" ? 0 : 2;
        }

        private static void RunOriginalClassicLauncher(LoadedLauncherSnapshot loaded, string executableDirectory, string resourceDirectory)
        {
            Directory.SetCurrentDirectory(resourceDirectory);
            Settings.P_Patcher = false;
            Settings.P_ServerListUrl = string.Empty;
            Settings.P_BrowserAddress = string.Empty;
            Settings.P_ServerName = loaded.Snapshot.ProjectName;
            LauncherServer first = loaded.Snapshot.Servers[0];
            Settings.IPAddress = first.Address;
            Settings.Port = first.Port;
            MicroEndpoint micro = first.MicroOverride ?? loaded.Snapshot.DefaultMicro;
            Settings.MicroBaseUrl = micro.Enabled
                ? new UriBuilder(Uri.UriSchemeHttp, micro.Address, micro.Port, "api/").Uri.AbsoluteUri
                : string.Empty;
            System.Windows.Forms.Application.EnableVisualStyles();
            System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
            using var form = new AMain(loaded.Snapshot, executableDirectory, resourceDirectory);
            Program.PForm = form;
            System.Windows.Forms.Application.Run(form);
        }

        private static int RunPreLoginUpdateCli(string[] args)
        {
            try
            {
                string overrideRepo = string.Empty;
                string clientRoot = string.Empty;
                int timeoutSeconds = 300;
                bool? overrideVerifySha256 = null;
                bool? overrideAutoDownload = null;

                for (int i = 0; i < args.Length; i++)
                {
                    string arg = args[i] ?? string.Empty;

                    if (string.Equals(arg, "--repo", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                    {
                        overrideRepo = args[++i];
                        continue;
                    }

                    if (string.Equals(arg, "--clientRoot", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                    {
                        clientRoot = args[++i];
                        continue;
                    }

                    if (string.Equals(arg, "--timeoutSeconds", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length
                        && int.TryParse(args[++i], out int timeout))
                    {
                        timeoutSeconds = Math.Clamp(timeout, 10, 24 * 3600);
                        continue;
                    }

                    if (string.Equals(arg, "--verifySha256", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length
                        && bool.TryParse(args[++i], out bool verifySha))
                    {
                        overrideVerifySha256 = verifySha;
                        continue;
                    }

                    if (string.Equals(arg, "--autoDownload", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length
                        && bool.TryParse(args[++i], out bool autoDownload))
                    {
                        overrideAutoDownload = autoDownload;
                        continue;
                    }
                }

                if (!string.IsNullOrWhiteSpace(clientRoot))
                {
                    string full = Path.GetFullPath(clientRoot);
                    Directory.CreateDirectory(full);

                    // 同时覆盖更新/安装目标与配置文件读取目录（Settings 默认读取 .\\Mir2Config.ini）。
                    Environment.SetEnvironmentVariable("LOMMIR_PC_CLIENT_ROOT", full);
                    Environment.CurrentDirectory = full;
                }

                Packet.IsServer = false;
                Settings.Load();

                if (!string.IsNullOrWhiteSpace(overrideRepo))
                    Settings.BootstrapPackageRepo = overrideRepo;

                if (overrideVerifySha256.HasValue)
                    Settings.BootstrapVerifySha256 = overrideVerifySha256.Value;

                if (overrideAutoDownload.HasValue)
                    Settings.BootstrapAutoDownload = overrideAutoDownload.Value;

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
                Console.CancelKeyPress += (_, e) =>
                {
                    e.Cancel = true;
                    cts.Cancel();
                };

                var progressGate = new object();
                IProgress<PcBootstrapProgress> progress = new Progress<PcBootstrapProgress>(p =>
                {
                    if (p == null)
                        return;

                    lock (progressGate)
                    {
                        string pack = string.IsNullOrWhiteSpace(p.PackageName) ? "-" : p.PackageName;
                        string total = p.TotalBytes > 0 ? p.TotalBytes.ToString() : "?";
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {p.Stage,-10} {pack,-30} {p.ReceivedBytes}/{total} {p.Message}");
                    }
                });

                PcBootstrapApplyResultView result = PcBootstrapPreLoginUpdateService
                    .TryRunPreLoginUpdateAsync(progress, cts.Token)
                    .GetAwaiter()
                    .GetResult();

                if (result == null)
                {
                    Console.Error.WriteLine("PreLoginUpdate 未返回结果。");
                    return 2;
                }

                Console.WriteLine($"Result | Completed={result.Completed} | Skipped={result.Skipped} | Failed={result.Failed} | Updated={result.UpdatedPackageCount} | ResourceVersion={result.ResourceVersion} | Message={result.Message}");

                if (result.Failed)
                    return 2;

                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return 2;
            }
        }

        private static void TryRunPcPreLoginUpdate()
        {
            try
            {
                if (!Settings.BootstrapPreLoginUpdate)
                    return;

                if (!Settings.BootstrapAutoDownload)
                    return;

                string repositoryRoot = PcBootstrapHttp.ResolveRepositoryRoot(out _);
                if (string.IsNullOrWhiteSpace(repositoryRoot))
                    return;

                using var form = new PcPreLoginUpdateForm();
                form.ShowDialog();
            }
            catch (Exception ex)
            {
                CMain.SaveError($"PC PreLoginUpdate 启动失败：{ex.Message}");
            }
        }

        private static bool UpdatePatcher()
        {
            try
            {
                const string fromName = @".\AutoPatcher.gz", toName = @".\AutoPatcher.exe";
                if (!File.Exists(fromName)) return false;

                Process[] processes = Process.GetProcessesByName("AutoPatcher");

                if (processes.Length > 0)
                {
                    string patcherPath = System.Windows.Forms.Application.StartupPath + @"\AutoPatcher.exe";

                    for (int i = 0; i < processes.Length; i++)
                        if (processes[i].MainModule.FileName == patcherPath)
                            processes[i].Kill();

                    Stopwatch stopwatch = Stopwatch.StartNew();
                    bool wait = true;
                    processes = Process.GetProcessesByName("AutoPatcher");

                    while (wait)
                    {
                        wait = false;
                        for (int i = 0; i < processes.Length; i++)
                            if (processes[i].MainModule.FileName == patcherPath)
                            {
                                wait = true;
                            }

                        if (stopwatch.ElapsedMilliseconds <= 3000) continue;
                        MessageBox.Show("更新期间无法关闭自动修补程序");
                        return true;
                    }
                }

                if (File.Exists(toName)) File.Delete(toName);
                File.Move(fromName, toName);
                Process.Start(toName, "Auto");

                return true;
            }
            catch (Exception ex)
            {
                CMain.SaveError(ex.ToString());

                throw;
            }
        }

        public static class RuntimePolicyHelper
        {
            public static bool LegacyV2RuntimeEnabledSuccessfully { get; private set; }

            static RuntimePolicyHelper()
            {
                //ICLRRuntimeInfo clrRuntimeInfo =
                //    (ICLRRuntimeInfo)RuntimeEnvironment.GetRuntimeInterfaceAsObject(
                //        Guid.Empty,
                //        typeof(ICLRRuntimeInfo).GUID);

                //try
                //{
                //    clrRuntimeInfo.BindAsLegacyV2Runtime();
                //    LegacyV2RuntimeEnabledSuccessfully = true;
                //}
                //catch (COMException)
                //{
                //    // This occurs with an HRESULT meaning 
                //    // "A different runtime was already bound to the legacy CLR version 2 activation policy."
                //    LegacyV2RuntimeEnabledSuccessfully = false;
                //}
            }

            [ComImport]
            [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
            [Guid("BD39D1D2-BA2F-486A-89B0-B4B0CB466891")]
            private interface ICLRRuntimeInfo
            {
                void xGetVersionString();
                void xGetRuntimeDirectory();
                void xIsLoaded();
                void xIsLoadable();
                void xLoadErrorString();
                void xLoadLibrary();
                void xGetProcAddress();
                void xGetInterface();
                void xSetDefaultStartupFlags();
                void xGetDefaultStartupFlags();

                [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
                void BindAsLegacyV2Runtime();
            }
        }

        public static void CheckResolutionSetting()
        {
            var parsedOK = DisplayResolutions.GetDisplayResolutions();
            Client.Utils.ResolutionTrace.Log("Program.CheckResolutionSetting", $"ParsedOK={parsedOK}, Supported=[{string.Join(",", DisplayResolutions.DisplaySupportedResolutions)}], ConfigResolution={Settings.Resolution}");
            if (!parsedOK)
            {
                MessageBox.Show("无法获取显示分辨率", "获取显示分辨率问题", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Environment.Exit(0);
            }

            if (!DisplayResolutions.IsSupported(Settings.Resolution))
            {
                MessageBox.Show($"客户端不支持 {Settings.Resolution} 将设置成默认分辨率 1024x768",
                                "无效的客户端分辨率",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Error);

                Settings.Resolution = (int)eSupportedResolution.w1024h768;
                Settings.Save();
            }

            Settings.ApplyResolutionSetting();
            Client.Utils.ResolutionTrace.LogClientState("Program.CheckResolutionSetting", "After ApplyResolutionSetting");
        }

    }
}
