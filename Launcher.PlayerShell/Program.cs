using System.Diagnostics;
using System.Runtime.InteropServices;
using Launcher.PlayerShell;
using Shared.Security;

namespace Launcher.PlayerShell.Host;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 1 && string.Equals(args[0], "--shell-smoke", StringComparison.Ordinal))
            {
                PlayerPayloadPackage.Verify(Environment.ProcessPath
                    ?? throw new InvalidOperationException("无法确定玩家入口路径"));
                return 0;
            }
            if (args.Length == 4 && string.Equals(args[0], "--apply-player-update", StringComparison.Ordinal))
                return ApplyPlayerUpdate(args[1], args[2], args[3]);

            string executablePath = Environment.ProcessPath
                ?? throw new InvalidOperationException("无法确定玩家入口路径");
            if (TryStartPlayerUpdateHelper(executablePath)) return 0;
            PlayerPayloadInfo payload = PlayerPayloadPackage.Verify(executablePath);
            string installDirectory = EnsureExtracted(executablePath, payload);
            string entryPoint = Path.GetFullPath(Path.Combine(installDirectory, payload.EntryPoint.Replace('/', Path.DirectorySeparatorChar)));
            if (!entryPoint.StartsWith(installDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(entryPoint))
                throw new InvalidDataException("玩家入口载荷入口点无效");

            var start = new ProcessStartInfo(entryPoint)
            {
                WorkingDirectory = installDirectory,
                UseShellExecute = false,
            };
            start.Environment["LYOCRYSTAL_PLAYER_SOURCE_DIRECTORY"] = Path.GetDirectoryName(executablePath)!;
            foreach (string argument in args) start.ArgumentList.Add(argument);
            Process.Start(start)?.Dispose();
            return 0;
        }
        catch (Exception ex)
        {
            MessageBoxW(0, ex.Message, "玩家入口无法启动", 0x00000010);
            return 2;
        }
    }

    private static bool TryStartPlayerUpdateHelper(string executablePath)
    {
        string directory = Path.GetDirectoryName(executablePath)!;
        string helperDirectory = Path.Combine(Path.GetTempPath(), "LyoCrystal", "PlayerUpdateHelpers");
        if (Directory.Exists(helperDirectory)) CleanupUpdateHelpers(helperDirectory);
        string journalPath = Path.Combine(directory, "player-replacement.json");
        if (!File.Exists(journalPath)) return false;

        try
        {
            bool requiresApply = PlayerReplacementCoordinator.ValidatePending(
                journalPath,
                executablePath,
                BootstrapManifestTrustConfiguration.TrustedKeys,
                BootstrapManifestTrustConfiguration.CurrentClientCompatibilityVersion);
            if (!requiresApply)
            {
                ArchiveAppliedJournal(journalPath);
                return false;
            }
        }
        catch
        {
            // 普通更新无效时继续运行当前完整旧版。
            return false;
        }

        string? helperPath = null;
        try
        {
            Directory.CreateDirectory(helperDirectory);
            helperPath = Path.Combine(helperDirectory, "PlayerUpdateHelper-" + Guid.NewGuid().ToString("N") + ".exe");
            File.Copy(executablePath, helperPath, overwrite: false);
            var start = new ProcessStartInfo(helperPath)
            {
                WorkingDirectory = directory,
                UseShellExecute = false,
            };
            start.ArgumentList.Add("--apply-player-update");
            start.ArgumentList.Add(journalPath);
            start.ArgumentList.Add(executablePath);
            start.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Process.Start(start)?.Dispose();
            return true;
        }
        catch
        {
            if (helperPath != null)
            {
                try { File.Delete(helperPath); } catch { }
            }
            return false;
        }
    }

    private static int ApplyPlayerUpdate(string journalPath, string targetPath, string parentProcessId)
    {
        if (!int.TryParse(parentProcessId, out int processId) || processId <= 0)
            throw new InvalidDataException("玩家入口更新父进程 ID 无效");
        try
        {
            using Process parent = Process.GetProcessById(processId);
            if (!parent.WaitForExit(30_000)) throw new TimeoutException("等待旧玩家入口退出超时");
        }
        catch (ArgumentException)
        {
            // 父进程已退出，继续应用替换。
        }

        Exception? failure = null;
        try
        {
            PlayerReplacementCoordinator.ApplyPending(
                journalPath,
                targetPath,
                BootstrapManifestTrustConfiguration.TrustedKeys,
                BootstrapManifestTrustConfiguration.CurrentClientCompatibilityVersion);
            ArchiveAppliedJournal(journalPath);
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        if (File.Exists(targetPath))
        {
            Process.Start(new ProcessStartInfo(targetPath)
            {
                WorkingDirectory = Path.GetDirectoryName(targetPath)!,
                UseShellExecute = false,
            })?.Dispose();
        }
        if (failure == null) return 0;
        MessageBoxW(0, "玩家入口更新失败，已继续使用完整旧版。\n" + failure.Message, "玩家入口更新", 0x00000030);
        return 2;
    }

    private static void ArchiveAppliedJournal(string journalPath)
    {
        string archivedJournal = journalPath + ".applied-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
        File.Move(journalPath, archivedJournal);
    }

    private static void CleanupUpdateHelpers(string helperDirectory)
    {
        foreach (string path in Directory.EnumerateFiles(helperDirectory, "PlayerUpdateHelper-*.exe"))
        {
            try
            {
                File.Delete(path);
            }
            catch
            {
                // 正在运行或被安全软件检查的助手留待下次清理。
            }
        }
    }

    private static string EnsureExtracted(string executablePath, PlayerPayloadInfo payload)
    {
        string root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LyoCrystal", "PlayerPayloads");
        string finalDirectory = Path.Combine(root, payload.Sha256[..24]);
        string markerPath = Path.Combine(finalDirectory, ".payload-sha256");
        if (File.Exists(markerPath) && string.Equals(File.ReadAllText(markerPath).Trim(), payload.Sha256, StringComparison.Ordinal))
        {
            try
            {
                PlayerPayloadPackage.VerifyExtracted(executablePath, finalDirectory);
                return finalDirectory;
            }
            catch (InvalidDataException)
            {
                // 已解包缓存损坏时隔离整套目录并从内嵌载荷重建。
            }
        }

        Directory.CreateDirectory(root);
        string temporaryDirectory = Path.Combine(root, ".extracting-" + Guid.NewGuid().ToString("N"));
        try
        {
            PlayerPayloadPackage.ExtractVerified(executablePath, temporaryDirectory);
            File.WriteAllText(Path.Combine(temporaryDirectory, ".payload-sha256"), payload.Sha256 + Environment.NewLine);
            if (Directory.Exists(finalDirectory))
            {
                string quarantine = finalDirectory + ".invalid-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
                Directory.Move(finalDirectory, quarantine);
            }
            Directory.Move(temporaryDirectory, finalDirectory);
            return finalDirectory;
        }
        finally
        {
            if (Directory.Exists(temporaryDirectory)) Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(nint hWnd, string text, string caption, uint type);
}
