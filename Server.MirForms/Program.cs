using log4net;
using Server.Persistence.Sql;
using Server.Diagnostics;
using System.Reflection;
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

            // winform aot
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
    }
}
