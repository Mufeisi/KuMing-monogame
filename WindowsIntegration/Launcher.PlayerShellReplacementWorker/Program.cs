using Launcher.PlayerShell;
using Shared.Security;

namespace Launcher.PlayerShellReplacementWorker;

public sealed class ReplacementWorkerMarker;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length != 6)
                throw new ArgumentException("用法：<日志> <目标> <公钥Base64> <故障点|None> <到达标记> <结果标记>");
            string journal = Path.GetFullPath(args[0]);
            string target = Path.GetFullPath(args[1]);
            string publicKey = args[2];
            string pauseName = args[3];
            string reachedMarker = Path.GetFullPath(args[4]);
            string resultMarker = Path.GetFullPath(args[5]);
            var trust = new Dictionary<string, BootstrapManifestTrustedKey>(StringComparer.Ordinal)
            {
                ["player-gate-l0"] = new()
                {
                    KeyId = "player-gate-l0",
                    SubjectPublicKeyInfo = publicKey,
                    NotBeforeSequence = 1,
                },
            };

            PlayerReplacementResult result;
            if (string.Equals(pauseName, "None", StringComparison.Ordinal))
            {
                result = PlayerReplacementCoordinator.ApplyPending(journal, target, trust, new Version(1, 0, 0));
            }
            else
            {
                if (!Enum.TryParse(pauseName, ignoreCase: false, out PlayerReplacementInterruptionPoint requested))
                    throw new ArgumentException("未知强停故障点");
                result = PlayerReplacementCoordinator.ApplyPendingForInterruptionTest(
                    journal,
                    target,
                    trust,
                    new Version(1, 0, 0),
                    point =>
                    {
                        if (point != requested) return;
                        WriteMarker(reachedMarker, point.ToString());
                        Thread.Sleep(Timeout.Infinite);
                    });
            }
            WriteMarker(resultMarker, result.Status.ToString());
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 2;
        }
    }

    private static void WriteMarker(string path, string value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, value + Environment.NewLine);
    }
}
