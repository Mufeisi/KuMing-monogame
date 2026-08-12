using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Shared.Security;

namespace Launcher.PlayerShell;

public enum PlayerReplacementStatus
{
    Applied,
    AlreadyApplied,
    RestoredPrevious,
}

public sealed record PlayerReplacementResult(PlayerReplacementStatus Status, string TargetPath, string PreviousPath);

internal enum PlayerReplacementInterruptionPoint
{
    BeforeApplying,
    AfterApplyingJournalPersisted,
    AfterAtomicReplace,
}

public static class PlayerReplacementCoordinator
{
    private const string JournalFormat = "lyocrystal-player-replacement-v1";
    private static readonly UTF8Encoding Utf8NoBom = new(false, true);

    public static bool ValidatePending(
        string journalPath,
        string targetPath,
        IReadOnlyDictionary<string, BootstrapManifestTrustedKey> trustedKeys,
        Version currentClientVersion,
        string? acceptedStatePath = null)
    {
        ReplacementAuthorization authorization = LoadAuthorization(journalPath, targetPath, trustedKeys, currentClientVersion, acceptedStatePath);
        if (authorization.Journal.Status == PlayerReplacementJournalStatus.Committed)
        {
            VerifyPackage(authorization.TargetPath, authorization.Package);
            return false;
        }
        if (File.Exists(authorization.StagedPath))
        {
            VerifyPackage(authorization.StagedPath, authorization.Package);
            return true;
        }
        if (File.Exists(authorization.TargetPath) && File.Exists(authorization.PreviousPath))
        {
            VerifyPackage(authorization.TargetPath, authorization.Package);
            return true;
        }
        if (!File.Exists(authorization.TargetPath) && File.Exists(authorization.PreviousPath)) return true;
        throw new InvalidDataException("玩家入口待替换文件不存在");
    }

    public static PlayerReplacementResult ApplyPending(
        string journalPath,
        string targetPath,
        IReadOnlyDictionary<string, BootstrapManifestTrustedKey> trustedKeys,
        Version currentClientVersion,
        string? acceptedStatePath = null) =>
        ApplyPendingCore(journalPath, targetPath, trustedKeys, currentClientVersion, acceptedStatePath, interruptionPoint: null);

    public static void PreparePending(
        string journalPath,
        string targetPath,
        string signedManifestJson,
        string packageName,
        IReadOnlyDictionary<string, BootstrapManifestTrustedKey> trustedKeys,
        Version currentClientVersion,
        string acceptedStatePath)
    {
        journalPath = Path.GetFullPath(journalPath); targetPath = Path.GetFullPath(targetPath);
        if (!string.Equals(Path.GetDirectoryName(journalPath), Path.GetDirectoryName(targetPath), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("玩家入口替换日志必须与目标位于同一目录");
        BootstrapSignedManifest manifest = BootstrapManifestAcceptanceStore.VerifyAndAccept(signedManifestJson, acceptedStatePath, trustedKeys, currentClientVersion);
        BootstrapSignedPackage package = manifest.Packages.SingleOrDefault(item => string.Equals(item.Name, packageName, StringComparison.Ordinal)) ?? throw new InvalidDataException("签名清单未授权玩家入口更新包");
        string staged = targetPath + ".new"; VerifyPackage(staged, package);
        if (!BootstrapManifestAcceptanceStore.IsAuthorizedUpdateQueue(acceptedStatePath, manifest.ResourceVersion, new[] { new BootstrapManifestAuthorizedPackage { Name = package.Name, Sha256 = package.Sha256 } }, trustedKeys, currentClientVersion)) throw new InvalidDataException("玩家入口更新未绑定当前已接受签名状态");
        string previous = targetPath + ".previous"; if (File.Exists(previous)) File.Delete(previous);
        WriteJournalAtomic(journalPath, new PlayerReplacementJournal { Format = JournalFormat, PackageName = packageName, SignedManifestJson = signedManifestJson, Status = PlayerReplacementJournalStatus.Prepared });
    }

    internal static PlayerReplacementResult ApplyPendingForInterruptionTest(
        string journalPath,
        string targetPath,
        IReadOnlyDictionary<string, BootstrapManifestTrustedKey> trustedKeys,
        Version currentClientVersion,
        Action<PlayerReplacementInterruptionPoint> interruptionPoint) =>
        ApplyPendingCore(journalPath, targetPath, trustedKeys, currentClientVersion, acceptedStatePath: null, interruptionPoint);

    private static PlayerReplacementResult ApplyPendingCore(
        string journalPath,
        string targetPath,
        IReadOnlyDictionary<string, BootstrapManifestTrustedKey> trustedKeys,
        Version currentClientVersion,
        string? acceptedStatePath,
        Action<PlayerReplacementInterruptionPoint>? interruptionPoint)
    {
        ReplacementAuthorization authorization = LoadAuthorization(journalPath, targetPath, trustedKeys, currentClientVersion, acceptedStatePath);
        PlayerReplacementJournal journal = authorization.Journal;
        BootstrapSignedPackage package = authorization.Package;
        targetPath = authorization.TargetPath;
        string stagedPath = authorization.StagedPath;
        string previousPath = authorization.PreviousPath;
        if (journal.Status == PlayerReplacementJournalStatus.Committed)
        {
            VerifyPackage(targetPath, package);
            return new PlayerReplacementResult(PlayerReplacementStatus.AlreadyApplied, targetPath, previousPath);
        }

        if (File.Exists(stagedPath)) VerifyPackage(stagedPath, package);

        if (File.Exists(targetPath) && !File.Exists(stagedPath) && File.Exists(previousPath))
        {
            try
            {
                VerifyPackage(targetPath, package);
                journal.Status = PlayerReplacementJournalStatus.Committed;
                WriteJournalAtomic(authorization.JournalPath, journal);
                return new PlayerReplacementResult(PlayerReplacementStatus.Applied, targetPath, previousPath);
            }
            catch (InvalidDataException)
            {
                RestorePrevious(targetPath, previousPath);
                return new PlayerReplacementResult(PlayerReplacementStatus.RestoredPrevious, targetPath, previousPath);
            }
        }

        if (!File.Exists(stagedPath))
        {
            if (!File.Exists(targetPath) && File.Exists(previousPath))
            {
                File.Move(previousPath, targetPath);
                return new PlayerReplacementResult(PlayerReplacementStatus.RestoredPrevious, targetPath, previousPath);
            }
            throw new InvalidDataException("玩家入口待替换文件不存在");
        }

        interruptionPoint?.Invoke(PlayerReplacementInterruptionPoint.BeforeApplying);
        journal.Status = PlayerReplacementJournalStatus.Applying;
        WriteJournalAtomic(authorization.JournalPath, journal);
        interruptionPoint?.Invoke(PlayerReplacementInterruptionPoint.AfterApplyingJournalPersisted);
        if (File.Exists(targetPath))
        {
            if (File.Exists(previousPath)) throw new InvalidDataException("玩家入口上一版本已存在，拒绝覆盖恢复点");
            File.Replace(stagedPath, targetPath, previousPath, ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(stagedPath, targetPath);
        }
        interruptionPoint?.Invoke(PlayerReplacementInterruptionPoint.AfterAtomicReplace);
        try
        {
            VerifyPackage(targetPath, package);
        }
        catch
        {
            if (File.Exists(previousPath)) RestorePrevious(targetPath, previousPath);
            throw;
        }
        journal.Status = PlayerReplacementJournalStatus.Committed;
        WriteJournalAtomic(authorization.JournalPath, journal);
        return new PlayerReplacementResult(PlayerReplacementStatus.Applied, targetPath, previousPath);
    }

    private static ReplacementAuthorization LoadAuthorization(
        string journalPath,
        string targetPath,
        IReadOnlyDictionary<string, BootstrapManifestTrustedKey> trustedKeys,
        Version currentClientVersion,
        string? acceptedStatePath = null)
    {
        ArgumentNullException.ThrowIfNull(trustedKeys);
        ArgumentNullException.ThrowIfNull(currentClientVersion);
        journalPath = Path.GetFullPath(journalPath ?? throw new ArgumentNullException(nameof(journalPath)));
        targetPath = Path.GetFullPath(targetPath ?? throw new ArgumentNullException(nameof(targetPath)));
        if (!string.Equals(Path.GetDirectoryName(journalPath), Path.GetDirectoryName(targetPath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("玩家入口替换日志必须与目标位于同一目录");
        if (!File.Exists(journalPath)) throw new FileNotFoundException("玩家入口替换日志不存在", journalPath);
        if (new FileInfo(journalPath).Length > BootstrapManifestSignaturePolicy.MaximumJsonBytes)
            throw new InvalidDataException("玩家入口替换日志超过 8 MiB 上限");

        PlayerReplacementJournal journal;
        try
        {
            journal = JsonSerializer.Deserialize(
                File.ReadAllText(journalPath, Utf8NoBom),
                PlayerPayloadJsonContext.Default.PlayerReplacementJournal)
                ?? throw new InvalidDataException("玩家入口替换日志为空");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("玩家入口替换日志 JSON 无效", ex);
        }
        if (!string.Equals(journal.Format, JournalFormat, StringComparison.Ordinal))
            throw new InvalidDataException("玩家入口替换日志格式不受支持");
        BootstrapManifestVerificationResult verification = BootstrapManifestSignaturePolicy.Verify(
            journal.SignedManifestJson, trustedKeys, currentClientVersion);
        if (!verification.IsValid) throw new InvalidDataException("玩家入口替换授权无效：" + verification.Error);
        BootstrapSignedPackage package = verification.Manifest.Packages.SingleOrDefault(
            item => string.Equals(item.Name, journal.PackageName, StringComparison.Ordinal))
            ?? throw new InvalidDataException("签名清单未授权玩家入口替换包");
        if (!string.IsNullOrWhiteSpace(acceptedStatePath) && !BootstrapManifestAcceptanceStore.IsAuthorizedUpdateQueue(acceptedStatePath, verification.Manifest.ResourceVersion, new[] { new BootstrapManifestAuthorizedPackage { Name = package.Name, Sha256 = package.Sha256 } }, trustedKeys, currentClientVersion)) throw new InvalidDataException("玩家入口替换日志不是当前已接受签名版本");
        return new ReplacementAuthorization(journalPath, targetPath, targetPath + ".new", targetPath + ".previous", journal, package);
    }

    private static void VerifyPackage(string path, BootstrapSignedPackage package)
    {
        if (!File.Exists(path)) throw new InvalidDataException("签名玩家入口文件不存在");
        if (new FileInfo(path).Length != package.Size) throw new InvalidDataException("签名玩家入口文件大小不一致");
        BootstrapSignedPackageHashPolicy.VerifyFile(path, package.Sha256);
    }

    private static void RestorePrevious(string targetPath, string previousPath)
    {
        if (File.Exists(targetPath))
        {
            string rejected = targetPath + ".rejected-" + Guid.NewGuid().ToString("N");
            File.Replace(previousPath, targetPath, rejected, ignoreMetadataErrors: true);
            return;
        }
        File.Move(previousPath, targetPath);
    }

    private static void WriteJournalAtomic(string path, PlayerReplacementJournal journal)
    {
        string temporaryPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(
                journal,
                PlayerPayloadJsonContext.Default.PlayerReplacementJournal);
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.WriteByte((byte)'\n');
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private sealed record ReplacementAuthorization(
        string JournalPath,
        string TargetPath,
        string StagedPath,
        string PreviousPath,
        PlayerReplacementJournal Journal,
        BootstrapSignedPackage Package);
}

internal enum PlayerReplacementJournalStatus
{
    Prepared,
    Applying,
    Committed,
}

internal sealed class PlayerReplacementJournal
{
    public string Format { get; set; } = string.Empty;
    public string PackageName { get; set; } = string.Empty;
    public string SignedManifestJson { get; set; } = string.Empty;
    [JsonConverter(typeof(JsonStringEnumConverter<PlayerReplacementJournalStatus>))]
    public PlayerReplacementJournalStatus Status { get; set; } = PlayerReplacementJournalStatus.Prepared;
}
