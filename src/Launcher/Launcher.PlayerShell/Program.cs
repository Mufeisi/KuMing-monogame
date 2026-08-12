using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
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
            if (args.Length == 5 && string.Equals(args[0], "--apply-player-update", StringComparison.Ordinal))
                return ApplyPlayerUpdate(args[1], args[2], args[3], args[4]);

            string executablePath = Environment.ProcessPath
                ?? throw new InvalidOperationException("无法确定玩家入口路径");
            PlayerPayloadInfo payload = PlayerPayloadPackage.Verify(executablePath);
            string originalSourceExecutable = ResolveOriginalSource(executablePath, payload);
            string originalSourceDirectory = Path.GetDirectoryName(originalSourceExecutable)!;
            string installDirectory = EnsureExtracted(executablePath, payload);
            if (!args.Any(argument => string.Equals(argument, "--theme-render-smoke", StringComparison.OrdinalIgnoreCase)))
            {
                string projectId = LoadProjectId(installDirectory);
                string managed = PlayerManagedEntry.Ensure(executablePath, projectId, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LyoCrystal", "ManagedEntries"), payload);
                if (!string.Equals(managed, executablePath, StringComparison.OrdinalIgnoreCase))
                {
                    var forward = new ProcessStartInfo(managed) { WorkingDirectory = Path.GetDirectoryName(managed)!, UseShellExecute = false };
                    forward.Environment["LYOCRYSTAL_PLAYER_SOURCE_DIRECTORY"] = originalSourceDirectory;
                    forward.Environment["LYOCRYSTAL_PLAYER_SOURCE_EXECUTABLE"] = originalSourceExecutable;
                    foreach (string argument in args) forward.ArgumentList.Add(argument);
                    Process.Start(forward)?.Dispose();
                    return 0;
                }
            }
            if (TryStartPlayerUpdateHelper(executablePath, installDirectory)) return 0;
            string entryPoint = Path.GetFullPath(Path.Combine(installDirectory, payload.EntryPoint.Replace('/', Path.DirectorySeparatorChar)));
            if (!entryPoint.StartsWith(installDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(entryPoint))
                throw new InvalidDataException("玩家入口载荷入口点无效");

            var start = new ProcessStartInfo(entryPoint)
            {
                WorkingDirectory = installDirectory,
                UseShellExecute = false,
            };
            start.Environment["LYOCRYSTAL_PLAYER_SOURCE_DIRECTORY"] = originalSourceDirectory;
            start.Environment["LYOCRYSTAL_PLAYER_SOURCE_EXECUTABLE"] = originalSourceExecutable;
            foreach (string argument in args) start.ArgumentList.Add(argument);
            using Process? child = Process.Start(start);
            if (args.Any(argument => string.Equals(argument, "--theme-render-smoke", StringComparison.OrdinalIgnoreCase)))
            {
                if (child is null || !child.WaitForExit(30_000))
                {
                    try { child?.Kill(entireProcessTree: true); } catch { }
                    throw new TimeoutException("玩家入口主题验证超时");
                }
                return child.ExitCode;
            }
            return 0;
        }
        catch (Exception ex)
        {
            MessageBoxW(0, ex.Message, "玩家入口无法启动", 0x00000010);
            return 2;
        }
    }

    private static string ResolveOriginalSource(string executablePath, PlayerPayloadInfo verifiedPayload)
    {
        string inherited = Environment.GetEnvironmentVariable("LYOCRYSTAL_PLAYER_SOURCE_EXECUTABLE") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(inherited)) return executablePath;
        try
        {
            string candidate = Path.GetFullPath(inherited);
            if (!File.Exists(candidate) || (File.GetAttributes(candidate) & FileAttributes.ReparsePoint) != 0) return executablePath;
            PlayerPayloadInfo candidatePayload = PlayerPayloadPackage.Verify(candidate);
            return string.Equals(candidatePayload.Sha256, verifiedPayload.Sha256, StringComparison.OrdinalIgnoreCase)
                && string.Equals(candidatePayload.EntryPoint, verifiedPayload.EntryPoint, StringComparison.OrdinalIgnoreCase)
                && candidatePayload.FileCount == verifiedPayload.FileCount ? candidate : executablePath;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException) { return executablePath; }
    }

    private static bool TryStartPlayerUpdateHelper(string executablePath, string installDirectory)
    {
        string directory = Path.GetDirectoryName(executablePath)!;
        string helperDirectory = Path.Combine(Path.GetTempPath(), "LyoCrystal", "PlayerUpdateHelpers");
        if (Directory.Exists(helperDirectory)) CleanupUpdateHelpers(helperDirectory);
        string journalPath = Path.Combine(directory, "player-replacement.json");
        if (!File.Exists(journalPath)) return false;
        if (PlayerGameSessionMarker.IsGameRunning(executablePath)) return false;
        PlayerTrustContext trust = LoadTrustContext(installDirectory);

        try
        {
            bool requiresApply = PlayerReplacementCoordinator.ValidatePending(
                journalPath,
                executablePath,
                trust.TrustedKeys,
                BootstrapManifestTrustConfiguration.CurrentClientCompatibilityVersion,
                trust.AcceptedStatePath);
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
            start.ArgumentList.Add(Path.Combine(installDirectory, "Launcher", "BuiltIn", "launcher-snapshot.json"));
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

    private static int ApplyPlayerUpdate(string journalPath, string targetPath, string parentProcessId, string snapshotPath)
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

        Exception? failure = PlayerGameSessionMarker.IsGameRunning(targetPath)
            ? new InvalidOperationException("游戏仍在运行，玩家入口更新将在游戏退出后的下次启动处理")
            : null;
        try
        {
            if (failure is null)
            {
                PlayerTrustContext trust = LoadTrustContextFromSnapshot(snapshotPath);
                PlayerReplacementCoordinator.ApplyPending(
                    journalPath,
                    targetPath,
                    trust.TrustedKeys,
                    BootstrapManifestTrustConfiguration.CurrentClientCompatibilityVersion,
                    trust.AcceptedStatePath);
                ArchiveAppliedJournal(journalPath);
            }
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

    private static PlayerTrustContext LoadTrustContext(string installDirectory) =>
        LoadTrustContextFromSnapshot(Path.Combine(installDirectory, "Launcher", "BuiltIn", "launcher-snapshot.json"));

    private static string LoadProjectId(string installDirectory)
    {
        string snapshotPath = Path.Combine(installDirectory, "Launcher", "BuiltIn", "launcher-snapshot.json");
        if (!File.Exists(snapshotPath) || new FileInfo(snapshotPath).Length > BootstrapManifestSignaturePolicy.MaximumJsonBytes) throw new InvalidDataException("玩家入口内置项目快照不存在或过大");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(snapshotPath));
        string projectId = document.RootElement.GetProperty("ProjectId").GetString() ?? string.Empty;
        if (projectId.Length is < 1 or > 64 || projectId.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_' and not '.')) throw new InvalidDataException("玩家入口项目标识无效");
        return projectId;
    }

    private static PlayerTrustContext LoadTrustContextFromSnapshot(string snapshotPath)
    {
        string fullPath = Path.GetFullPath(snapshotPath);
        if (!File.Exists(fullPath) || new FileInfo(fullPath).Length > BootstrapManifestSignaturePolicy.MaximumJsonBytes)
            throw new InvalidDataException("玩家入口内置项目快照不存在或过大");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(fullPath));
        JsonElement root = document.RootElement;
        string projectId = root.GetProperty("ProjectId").GetString() ?? string.Empty;
        if (projectId.Length is < 1 or > 64 || projectId.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_' and not '.'))
            throw new InvalidDataException("玩家入口项目标识无效");
        var keys = new Dictionary<string, BootstrapManifestTrustedKey>(StringComparer.Ordinal);
        foreach (JsonElement element in root.GetProperty("TrustedReleaseKeys").EnumerateArray())
        {
            var key = new BootstrapManifestTrustedKey
            {
                KeyId = element.GetProperty("KeyId").GetString() ?? string.Empty,
                SubjectPublicKeyInfo = element.GetProperty("SubjectPublicKeyInfo").GetString() ?? string.Empty,
                NotBeforeSequence = element.GetProperty("NotBeforeSequence").GetInt64(),
                NotAfterSequence = element.TryGetProperty("NotAfterSequence", out JsonElement after) && after.ValueKind != JsonValueKind.Null ? after.GetInt64() : 0,
            };
            if (!keys.TryAdd(key.KeyId, key)) throw new InvalidDataException("玩家入口项目签名键重复");
        }
        if (keys.Count is < 1 or > 4) throw new InvalidDataException("玩家入口项目签名键数量无效");
        string localRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LyoCrystal", "Launcher", projectId);
        string statePath = Path.Combine(localRoot, "BootstrapManifestSecurityState.json");
        IReadOnlyDictionary<string, BootstrapManifestTrustedKey> resolved = BootstrapTrustChainStore.Resolve(Path.Combine(localRoot, "ReleaseTrustChain"), keys, BootstrapManifestTrustConfiguration.CurrentClientCompatibilityVersion);
        return new PlayerTrustContext(resolved, statePath);
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

    private sealed record PlayerTrustContext(IReadOnlyDictionary<string, BootstrapManifestTrustedKey> TrustedKeys, string AcceptedStatePath);
}
