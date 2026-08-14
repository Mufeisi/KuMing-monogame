using log4net;
using Server.Persistence.Sql;
using Server.Diagnostics;
using Server.MirEnvir;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Server.MirForms
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
                ServerCrashDiagnostics.Capture(eventArgs.ExceptionObject as Exception ??
                    new InvalidOperationException("服务器发生未知未处理异常"));

            if (args.Length > 0)
            {
                Environment.ExitCode = RunCommandLine(args);
                return;
            }

            // WinFormsComInterop 仅供 Native AOT；动态 .NET 运行时使用框架自带 COM marshalling。
            if (!RuntimeFeature.IsDynamicCodeSupported)
                ComWrappers.RegisterForMarshalling(WinFormsComInterop.WinFormsComWrappers.Instance);

            Packet.IsServer = true;

            //var logRepository = LogManager.GetRepository(Assembly.GetEntryAssembly());
            //log4net.Config.XmlConfigurator.Configure(logRepository, new FileInfo("log4net.config"));

            try
            {
                Settings.Load();

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new SMain());

                Settings.Save();
            }
            catch(Exception ex)
            {
                ServerCrashDiagnostics.Capture(ex);
            }
        }

        private static int RunCommandLine(string[] args)
        {
            if (string.Equals(args[0], "--headless-smoke-server", StringComparison.OrdinalIgnoreCase))
                return RunHeadlessSmokeServer(args);

            if (!string.Equals(args[0], "--restore-sqlite", StringComparison.OrdinalIgnoreCase) ||
                (args.Length != 2 && args.Length != 4) ||
                (args.Length == 4 && !string.Equals(args[2], "--target", StringComparison.OrdinalIgnoreCase)))
            {
                Console.Error.WriteLine("用法：Server.exe --restore-sqlite <备份路径> [--target <目标数据库路径>]");
                return 2;
            }

            try
            {
                string target;
                if (args.Length == 4)
                {
                    target = args[3];
                }
                else
                {
                    Settings.Load();
                    target = Settings.SqlitePath;
                }

                SqliteRestoreResult result = SqliteRestoreService.Restore(args[1], target);
                Console.WriteLine($"SQLite恢复成功：目标={result.TargetPath}；耗时={result.DurationMilliseconds}ms；回滚={result.RollbackPath}");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"SQLite恢复失败：{ex.GetType().Name}：{ex.Message}");
                return 1;
            }
        }

        private static int RunHeadlessSmokeServer(string[] args)
        {
            if (args.Length != 2 || !int.TryParse(args[1], out int durationSeconds) || durationSeconds is < 30 or > 600)
            {
                Console.Error.WriteLine("用法：Server.exe --headless-smoke-server <30..600秒>");
                return 2;
            }

            Packet.IsServer = true;
            Settings.Load();
            if (!Envir.Edit.LoadDB())
            {
                Console.Error.WriteLine("隐藏测试服加载数据库失败。");
                return 3;
            }

            try
            {
                Envir.Main.StartHeadlessTestServer();
                DateTime deadline = DateTime.UtcNow.AddSeconds(durationSeconds);
                while (DateTime.UtcNow < deadline && Envir.Main.Running)
                    Thread.Sleep(200);
                return Envir.Main.Running ? 0 : 4;
            }
            finally
            {
                if (Envir.Main.Running) Envir.Main.Stop();
            }
        }
    }
}
