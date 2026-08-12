using System.IO.Compression;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Launcher.ThemeRuntime;
using Shared.Security;

namespace LyoCrystal.LauncherEditor;

public sealed record ProjectReleaseResult(long Sequence, string VersionName, string VersionDirectory, string ManifestSha256);
public sealed record ProjectReleaseDiff(IReadOnlyList<string> Added, IReadOnlyList<string> Removed, IReadOnlyList<string> Changed)
{
    public string Summary => $"新增 {Added.Count}，删除 {Removed.Count}，变更 {Changed.Count}";
}

public static class ProjectReleasePublisher
{
    public static ProjectReleaseDiff CompareVersions(EditorProject project, string publishRoot, string fromVersionName, string toVersionName)
    {
        string versions = Path.Combine(Path.GetFullPath(publishRoot), "versions");
        BootstrapSignedManifest Load(string version)
        {
            string root = Path.GetFullPath(Path.Combine(versions, version));
            if (!root.StartsWith(versions + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(root)) throw new InvalidDataException("差异版本路径无效");
            return VerifyImportedVersion(project, root);
        }
        Dictionary<string, BootstrapSignedPackage> from = Load(fromVersionName).Packages.ToDictionary(item => item.Name, StringComparer.OrdinalIgnoreCase);
        Dictionary<string, BootstrapSignedPackage> to = Load(toVersionName).Packages.ToDictionary(item => item.Name, StringComparer.OrdinalIgnoreCase);
        string[] added = to.Keys.Except(from.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
        string[] removed = from.Keys.Except(to.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
        string[] changed = from.Keys.Intersect(to.Keys, StringComparer.OrdinalIgnoreCase).Where(name => from[name].Sha256 != to[name].Sha256 || from[name].Size != to[name].Size).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
        return new ProjectReleaseDiff(added, removed, changed);
    }
    public static ProjectReleaseResult Publish(EditorProject project, string projectRoot, string publishRoot, string note)
    {
        ProjectReleaseKeyStore.EnsureProvisioned(project, projectRoot);
        LauncherSnapshot snapshot = CloneSnapshot(project.Snapshot);
        snapshot.LoginCoreResources = PlayerArtifactBuilder.BuildLoginCoreManifest(project.ImportedClientDirectory);
        return PublishCore(project, projectRoot, publishRoot, note, snapshot, null, null);
    }

    public static ProjectReleaseResult Rollback(EditorProject project, string projectRoot, string publishRoot, string sourceVersionName, string note)
    {
        ProjectReleaseKeyStore.EnsureProvisioned(project, projectRoot);
        string versions = Path.Combine(Path.GetFullPath(publishRoot), "versions");
        string source = Path.GetFullPath(Path.Combine(versions, sourceVersionName));
        if (!source.StartsWith(versions + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(source)) throw new InvalidDataException("回滚源版本不存在或路径无效");
        RejectReparsePath(source);
        ProjectReleaseHistoryItem history = project.Release.History.SingleOrDefault(item => item.VersionName == sourceVersionName) ?? throw new InvalidDataException("回滚源版本不属于当前项目历史");
        BootstrapSignedManifest historicalManifest = VerifyHistoricalVersion(project, source, history);
        string snapshotPath = Path.Combine(source, "launcher-snapshot.json");
        LauncherSnapshot snapshot = JsonSerializer.Deserialize(File.ReadAllBytes(snapshotPath), LauncherSnapshotJsonContext.Default.LauncherSnapshot) ?? throw new InvalidDataException("回滚源快照为空");
        LauncherSnapshotValidator.Validate(snapshot);
        snapshot.LoginCoreResources = PlayerArtifactBuilder.BuildLoginCoreManifest(project.ImportedClientDirectory);
        return PublishCore(project, projectRoot, publishRoot, note, snapshot, source, history.Sequence, historicalManifest);
    }

    public static void CreateOfflineDeploymentPackage(string publishRoot, string outputZip)
    {
        string root = Path.GetFullPath(publishRoot), pointer = Path.Combine(root, "current.txt");
        if (!File.Exists(pointer) || new FileInfo(pointer).Length > 256) throw new InvalidDataException("发布源缺少当前版本指针");
        string version = File.ReadAllText(pointer).Trim();
        string source = Path.GetFullPath(Path.Combine(root, "versions", version));
        if (!source.StartsWith(Path.Combine(root, "versions") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(source)) throw new InvalidDataException("当前发布版本无效");
        RejectReparsePath(source);
        string output = Path.GetFullPath(outputZip); if (File.Exists(output)) throw new IOException("离线发布包已存在，拒绝覆盖");
        string temp = output + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var archive = ZipFile.Open(temp, ZipArchiveMode.Create))
            {
                WriteEntry(archive, "current.txt", Encoding.UTF8.GetBytes(version + "\n"));
                foreach (string file in Directory.EnumerateFiles(source)) archive.CreateEntryFromFile(file, "versions/" + version + "/" + Path.GetFileName(file), CompressionLevel.Optimal);
            }
            File.Move(temp, output);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    public static ProjectReleaseResult ImportOfflineDeploymentPackage(EditorProject project, string inputZip, string publishRoot)
    {
        string zipPath = Path.GetFullPath(inputZip);
        if (!File.Exists(zipPath) || new FileInfo(zipPath).Length > 256L * 1024 * 1024) throw new InvalidDataException("离线发布包不存在或超过 256 兆字节");
        string root = Path.GetFullPath(publishRoot);
        RejectReparsePath(root); if (!Directory.Exists(root)) Directory.CreateDirectory(root); RejectReparsePath(root);
        using IDisposable publishLock = AcquirePublishLock(root);
        string staging = Path.Combine(root, ".offline-import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            using ZipArchive archive = ZipFile.OpenRead(zipPath);
            if (archive.Entries.Count is < 3 or > 512) throw new InvalidDataException("离线发布包文件数量无效");
            ZipArchiveEntry pointerEntry = archive.GetEntry("current.txt") ?? throw new InvalidDataException("离线发布包缺少当前版本指针");
            string version; using (var reader = new StreamReader(pointerEntry.Open(), Encoding.UTF8, true, 256, leaveOpen: false)) version = reader.ReadToEnd().Trim();
            if (version.Length is < 3 or > 96 || version.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_')) throw new InvalidDataException("离线发布包版本名无效");
            string prefix = "versions/" + version + "/"; long total = 0;
            foreach (ZipArchiveEntry entry in archive.Entries.Where(entry => entry.FullName.StartsWith(prefix, StringComparison.Ordinal)))
            {
                string name = entry.FullName[prefix.Length..];
                if (Path.GetFileName(name) != name || string.IsNullOrWhiteSpace(name)) throw new InvalidDataException("离线发布包包含越界路径");
                total = checked(total + entry.Length); if (total > 192L * 1024 * 1024) throw new InvalidDataException("离线发布包展开后过大");
                using Stream input = entry.Open(); using var output = new FileStream(Path.Combine(staging, name), FileMode.CreateNew, FileAccess.Write, FileShare.None); input.CopyTo(output);
            }
            BootstrapSignedManifest manifest = VerifyImportedVersion(project, staging);
            string importedManifestJson = File.ReadAllText(Path.Combine(staging, "bootstrap-manifest.json"));
            string importedSha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(importedManifestJson))).ToLowerInvariant();
            ProjectReleaseHistoryItem? sameHistory = project.Release.History.SingleOrDefault(item => item.Sequence == manifest.Sequence);
            if (sameHistory is not null && !string.Equals(sameHistory.ContentSha256, importedSha, StringComparison.Ordinal)) throw new InvalidDataException("离线发布包与项目同序列历史内容不同");
            if (manifest.Sequence < Math.Max(0, project.Release.NextSequence - 1)) throw new InvalidDataException("离线发布包序列低于项目已发布版本");
            string? currentSource = TryResolveCurrentVersionRoot(root);
            if (currentSource is not null)
            {
                BootstrapSignedManifest currentManifest = VerifyImportedVersion(project, currentSource);
                string currentJson = File.ReadAllText(Path.Combine(currentSource, "bootstrap-manifest.json"));
                string currentSha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(currentJson))).ToLowerInvariant();
                if (manifest.Sequence < currentManifest.Sequence || manifest.Sequence == currentManifest.Sequence && !string.Equals(importedSha, currentSha, StringComparison.Ordinal)) throw new InvalidDataException("离线发布包不能降低或分叉目标发布序列");
                if (manifest.Sequence == currentManifest.Sequence) return new ProjectReleaseResult(currentManifest.Sequence, Path.GetFileName(currentSource), currentSource, currentSha);
            }
            string destination = Path.Combine(root, "versions", version); Directory.CreateDirectory(Path.GetDirectoryName(destination)!); RejectReparsePath(Path.GetDirectoryName(destination)!);
            if (Directory.Exists(destination)) throw new IOException("离线发布版本已存在，拒绝覆盖不可变目录");
            Directory.Move(staging, destination);
            WritePointerAtomic(root, version);
            string manifestJson = File.ReadAllText(Path.Combine(destination, "bootstrap-manifest.json"));
            string sha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(manifestJson))).ToLowerInvariant();
            if (!project.Release.History.Any(item => item.Sequence == manifest.Sequence)) project.Release.History.Add(new ProjectReleaseHistoryItem { Sequence = manifest.Sequence, VersionName = version, CreatedAtUtc = manifest.GeneratedAtUtc, Note = "离线导入", ContentSha256 = sha });
            project.Release.NextSequence = Math.Max(project.Release.NextSequence, checked(manifest.Sequence + 1));
            project.Release.LastPublishRoot = root;
            return new ProjectReleaseResult(manifest.Sequence, version, destination, sha);
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
    }

    private static string? TryResolveCurrentVersionRoot(string root)
    {
        string pointer = Path.Combine(root, "current.txt");
        if (!File.Exists(pointer)) return null;
        if ((File.GetAttributes(pointer) & FileAttributes.ReparsePoint) != 0 || new FileInfo(pointer).Length > 256) throw new InvalidDataException("目标发布指针无效");
        string version = File.ReadAllText(pointer).Trim();
        if (version.Length is < 3 or > 96 || version.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_')) throw new InvalidDataException("目标发布指针格式无效");
        string source = Path.GetFullPath(Path.Combine(root, "versions", version));
        if (!source.StartsWith(Path.Combine(root, "versions") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(source)) throw new InvalidDataException("目标发布版本无效");
        RejectReparsePath(source); return source;
    }

    public static string ValidateCurrentPublishedVersion(EditorProject project, string publishRoot)
    {
        string root = Path.GetFullPath(publishRoot), pointer = Path.Combine(root, "current.txt");
        RejectReparsePath(root);
        if (!File.Exists(pointer) || (File.GetAttributes(pointer) & FileAttributes.ReparsePoint) != 0 || new FileInfo(pointer).Length > 256) throw new InvalidDataException("当前项目发布源无有效版本");
        string version = File.ReadAllText(pointer).Trim();
        if (version.Length is < 3 or > 96 || version.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_')) throw new InvalidDataException("当前项目发布指针无效");
        string source = Path.GetFullPath(Path.Combine(root, "versions", version));
        if (!source.StartsWith(Path.Combine(root, "versions") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(source)) throw new InvalidDataException("当前项目发布版本路径无效");
        RejectReparsePath(source); _ = VerifyImportedVersion(project, source); return source;
    }

    private static ProjectReleaseResult PublishCore(EditorProject project, string projectRoot, string publishRoot, string note, LauncherSnapshot snapshot, string? sourceVersion, long? rollbackSequence, BootstrapSignedManifest? historicalManifest = null)
    {
        string root = Path.GetFullPath(publishRoot), versions = Path.Combine(root, "versions");
        RejectReparsePath(root);
        if (!Directory.Exists(root)) Directory.CreateDirectory(root);
        RejectReparsePath(root);
        if (!Directory.Exists(versions)) Directory.CreateDirectory(versions);
        RejectReparsePath(versions);
        using IDisposable publishLock = AcquirePublishLock(root);
        long sequence = DetermineNextSequence(project, root);
        string staging = Path.Combine(versions, ".partial-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(staging);
        try
        {
            snapshot.TrustedReleaseKeys = project.Release.RetiredPublicKeys.TakeLast(2).Concat(new[]
            {
                new BootstrapManifestTrustedKey { KeyId = project.Release.CurrentKeyId, SubjectPublicKeyInfo = project.Release.CurrentPublicKey, NotBeforeSequence = project.Release.CurrentKeyNotBeforeSequence },
                new BootstrapManifestTrustedKey { KeyId = project.Release.NextKeyId, SubjectPublicKeyInfo = project.Release.NextPublicKey, NotBeforeSequence = project.Release.NextKeyNotBeforeSequence },
            }).ToList();
            if (sourceVersion is null) CopyAndRewriteAssets(snapshot, projectRoot, staging);
            else CopyRollbackFiles(sourceVersion, staging, snapshot);
            File.WriteAllBytes(Path.Combine(staging, "launcher-snapshot.json"), JsonSerializer.SerializeToUtf8Bytes(snapshot, LauncherSnapshotJsonContext.Default.LauncherSnapshot));
            if (sourceVersion is null) AddPlayerUpdate(project, staging);
            else CopyRollbackPlayerUpdate(sourceVersion, staging);
            if (historicalManifest is not null) VerifyCopiedHistoricalFiles(staging, historicalManifest);

            var files = Directory.EnumerateFiles(staging)
                .Where(path => Path.GetFileName(path) is not ("player-entry.exe" or "player-update.json"))
                .Select(path => new LauncherReleaseFile { Name = Path.GetFileName(path), Sha256 = Hash(path) }).OrderBy(item => item.Name, StringComparer.Ordinal).ToList();
            string resourceVersion = $"{project.Snapshot.ProjectId}-r{sequence}";
            var descriptor = new LauncherReleaseDescriptor { ResourceVersion = resourceVersion, Files = files };
            string descriptorPath = Path.Combine(staging, "launcher-release.json");
            File.WriteAllBytes(descriptorPath, JsonSerializer.SerializeToUtf8Bytes(descriptor, LauncherSnapshotJsonContext.Default.LauncherReleaseDescriptor));

            var packages = Directory.EnumerateFiles(staging).Select(path => new BootstrapSignedPackage { Name = Path.GetFileName(path), Sha256 = Hash(path), Size = new FileInfo(path).Length }).OrderBy(item => item.Name, StringComparer.Ordinal).ToList();
            var manifest = new BootstrapSignedManifest
            {
                Format = BootstrapManifestSignaturePolicy.Format, Algorithm = BootstrapManifestSignaturePolicy.Algorithm,
                KeyId = project.Release.CurrentKeyId, Sequence = sequence, GeneratedAtUtc = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'"),
                ResourceVersion = resourceVersion, MinimumClientVersion = "1.0.0.0", Packages = packages,
            };
            byte[] privateKey = ProjectReleaseKeyStore.LoadCurrentPrivateKey(project, projectRoot);
            try
            {
                using ECDsa signer = ECDsa.Create(); signer.ImportPkcs8PrivateKey(privateKey, out int read); if (read != privateKey.Length) throw new CryptographicException("项目签名私钥格式无效");
                manifest.Signature = Convert.ToBase64String(signer.SignData(BootstrapManifestSignaturePolicy.BuildCanonicalPayload(manifest), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
            }
            finally { CryptographicOperations.ZeroMemory(privateKey); }
            string manifestJson = JsonSerializer.Serialize(manifest, ProjectReleaseJsonContext.Default.BootstrapSignedManifest);
            File.WriteAllText(Path.Combine(staging, "bootstrap-manifest.json"), manifestJson, new UTF8Encoding(false));
            VerifyVersion(staging, manifestJson, project);
            string manifestSha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(manifestJson))).ToLowerInvariant();
            string versionName = $"r{sequence}-{manifestSha[..12]}";
            string destination = Path.Combine(versions, versionName); if (Directory.Exists(destination)) throw new IOException("不可变发布版本已存在");
            Directory.Move(staging, destination);
            WritePointerAtomic(root, versionName);
            project.Release.History.Add(new ProjectReleaseHistoryItem { Sequence = sequence, VersionName = versionName, CreatedAtUtc = manifest.GeneratedAtUtc, Note = note?.Trim() ?? string.Empty, ContentSha256 = manifestSha, RolledBackFromSequence = rollbackSequence });
            project.Release.NextSequence = checked(sequence + 1);
            return new ProjectReleaseResult(sequence, versionName, destination, manifestSha);
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
    }

    private static void CopyAndRewriteAssets(LauncherSnapshot snapshot, string projectRoot, string staging)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string Rewrite(string relative)
        {
            if (string.IsNullOrWhiteSpace(relative)) return string.Empty;
            if (map.TryGetValue(relative, out string? mapped)) return mapped;
            string source = LauncherSnapshotValidator.ResolveAsset(projectRoot, relative); if (!File.Exists(source)) throw new FileNotFoundException("发布素材不存在", source);
            string hash = Hash(source); string name = "asset-" + hash[..20] + Path.GetExtension(source).ToLowerInvariant();
            File.Copy(source, Path.Combine(staging, name), overwrite: false); map[relative] = name; return name;
        }
        snapshot.Theme.BackgroundImage = Rewrite(snapshot.Theme.BackgroundImage); snapshot.Theme.LaunchButtonImage = Rewrite(snapshot.Theme.LaunchButtonImage);
        snapshot.Theme.LaunchButtonHoverImage = Rewrite(snapshot.Theme.LaunchButtonHoverImage); snapshot.Theme.LaunchButtonPressedImage = Rewrite(snapshot.Theme.LaunchButtonPressedImage); snapshot.Theme.LaunchButtonDisabledImage = Rewrite(snapshot.Theme.LaunchButtonDisabledImage);
        foreach (LauncherControlOverride control in snapshot.Theme.Controls) control.BackgroundImage = Rewrite(control.BackgroundImage);
        foreach (LauncherAnnouncement announcement in snapshot.Announcements) announcement.Image = Rewrite(announcement.Image);
    }

    private static void CopyRollbackFiles(string source, string staging, LauncherSnapshot snapshot)
    {
        foreach (string relative in new[] { snapshot.Theme.BackgroundImage, snapshot.Theme.LaunchButtonImage, snapshot.Theme.LaunchButtonHoverImage, snapshot.Theme.LaunchButtonPressedImage, snapshot.Theme.LaunchButtonDisabledImage }.Concat(snapshot.Theme.Controls.Select(control => control.BackgroundImage)).Concat(snapshot.Announcements.Select(item => item.Image)).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (Path.GetFileName(relative) != relative) throw new InvalidDataException("回滚版本素材路径无效");
            File.Copy(Path.Combine(source, relative), Path.Combine(staging, relative), false);
        }
    }

    private static void AddPlayerUpdate(EditorProject project, string staging)
    {
        if (project.Release.PlayerUpdateMode == PlayerUpdateMode.None) return;
        string source = Path.GetFullPath(project.Release.PlayerUpdateFile);
        if (!File.Exists(source) || !source.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || new FileInfo(source).Length > Launcher.PlayerShell.PlayerPayloadPackage.MaximumPlayerExecutableBytes) throw new InvalidDataException("新版玩家入口不存在、格式无效或超过 80 兆字节");
        if (!Version.TryParse(project.Release.PlayerUpdateVersion, out Version? configuredVersion)) throw new InvalidDataException("新版玩家入口版本无效");
        string? actualValue = FileVersionInfo.GetVersionInfo(source).FileVersion;
        if (!Version.TryParse(actualValue, out Version? actualVersion) || actualVersion != configuredVersion) throw new InvalidDataException("新版玩家入口文件版本与发布设置不一致");
        File.Copy(source, Path.Combine(staging, "player-entry.exe"), false);
        var descriptor = new PlayerUpdateDescriptor { Version = project.Release.PlayerUpdateVersion, Required = project.Release.PlayerUpdateMode == PlayerUpdateMode.Required };
        File.WriteAllBytes(Path.Combine(staging, "player-update.json"), JsonSerializer.SerializeToUtf8Bytes(descriptor, LauncherSnapshotJsonContext.Default.PlayerUpdateDescriptor));
    }

    private static void CopyRollbackPlayerUpdate(string source, string staging)
    {
        foreach (string name in new[] { "player-update.json", "player-entry.exe" }) if (File.Exists(Path.Combine(source, name))) File.Copy(Path.Combine(source, name), Path.Combine(staging, name), false);
    }

    private static void VerifyVersion(string staging, string manifestJson, EditorProject project)
    {
        IReadOnlyDictionary<string, BootstrapManifestTrustedKey> keys = BuildEditorTrust(project);
        BootstrapManifestVerificationResult verification = BootstrapManifestSignaturePolicy.Verify(manifestJson, keys, new Version(1, 0, 0, 0)); if (!verification.IsValid) throw new InvalidDataException("发布版本签名自检失败：" + verification.Error);
        foreach (BootstrapSignedPackage package in verification.Manifest.Packages) { string path = Path.Combine(staging, package.Name); if (!File.Exists(path) || new FileInfo(path).Length != package.Size) throw new InvalidDataException("发布版本文件不完整：" + package.Name); BootstrapSignedPackageHashPolicy.VerifyFile(path, package.Sha256); }
    }

    private static LauncherSnapshot CloneSnapshot(LauncherSnapshot snapshot) => JsonSerializer.Deserialize(JsonSerializer.SerializeToUtf8Bytes(snapshot, LauncherSnapshotJsonContext.Default.LauncherSnapshot), LauncherSnapshotJsonContext.Default.LauncherSnapshot) ?? throw new InvalidDataException("启动器快照克隆失败");
    private static string Hash(string path) { using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.SequentialScan); return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(); }
    private static void WritePointerAtomic(string root, string version) { Directory.CreateDirectory(root); string path = Path.Combine(root, "current.txt"), temp = path + ".tmp-" + Guid.NewGuid().ToString("N"); try { using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough)) { byte[] bytes = Encoding.UTF8.GetBytes(version + "\n"); stream.Write(bytes); stream.Flush(true); } if (File.Exists(path)) File.Replace(temp, path, null, true); else File.Move(temp, path); } finally { if (File.Exists(temp)) File.Delete(temp); } }
    private static void WriteEntry(ZipArchive archive, string name, byte[] bytes) { using Stream output = archive.CreateEntry(name, CompressionLevel.NoCompression).Open(); output.Write(bytes); }

    private static long DetermineNextSequence(EditorProject project, string root)
    {
        long next = Math.Max(1, project.Release.NextSequence);
        string pointer = Path.Combine(root, "current.txt");
        if (!File.Exists(pointer)) return next;
        if ((File.GetAttributes(pointer) & FileAttributes.ReparsePoint) != 0 || new FileInfo(pointer).Length > 256) throw new InvalidDataException("现有发布指针无效");
        string version = File.ReadAllText(pointer).Trim();
        if (version.Length is < 3 or > 96 || version.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_')) throw new InvalidDataException("现有发布指针格式无效");
        string versionRoot = Path.GetFullPath(Path.Combine(root, "versions", version));
        RejectReparsePath(versionRoot);
        string manifestPath = Path.Combine(versionRoot, "bootstrap-manifest.json");
        if (!File.Exists(manifestPath) || new FileInfo(manifestPath).Length > BootstrapManifestSignaturePolicy.MaximumJsonBytes) throw new InvalidDataException("现有发布版本签名索引缺失");
        IReadOnlyDictionary<string, BootstrapManifestTrustedKey> keys = BuildEditorTrust(project);
        BootstrapManifestVerificationResult verification = BootstrapManifestSignaturePolicy.Verify(File.ReadAllText(manifestPath), keys, new Version(1, 0, 0, 0));
        if (!verification.IsValid) throw new InvalidDataException("现有发布版本签名无效：" + verification.Error);
        return Math.Max(next, checked(verification.Manifest.Sequence + 1));
    }

    private static void RejectReparsePath(string path)
    {
        string full = Path.GetFullPath(path); string? current = Path.GetPathRoot(full);
        foreach (string part in full[(current?.Length ?? 0)..].Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (part.Length == 0) continue;
            current = Path.Combine(current ?? string.Empty, part);
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("发布路径不得经过重解析点");
        }
    }

    private static BootstrapSignedManifest VerifyHistoricalVersion(EditorProject project, string source, ProjectReleaseHistoryItem history)
    {
        RejectReparsePath(source);
        string manifestPath = Path.Combine(source, "bootstrap-manifest.json");
        if (!File.Exists(manifestPath) || new FileInfo(manifestPath).Length > BootstrapManifestSignaturePolicy.MaximumJsonBytes) throw new InvalidDataException("回滚源签名索引缺失");
        string manifestJson = File.ReadAllText(manifestPath);
        string manifestSha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(manifestJson))).ToLowerInvariant();
        if (!string.Equals(manifestSha, history.ContentSha256, StringComparison.Ordinal)) throw new InvalidDataException("回滚源签名索引与项目历史不一致");
        BootstrapManifestVerificationResult verification = BootstrapManifestSignaturePolicy.Verify(manifestJson, BuildEditorTrust(project), new Version(1, 0, 0, 0));
        if (!verification.IsValid || verification.Manifest.Sequence != history.Sequence) throw new InvalidDataException("回滚源签名验证失败：" + verification.Error);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (BootstrapSignedPackage package in verification.Manifest.Packages)
        {
            if (Path.GetFileName(package.Name) != package.Name || !names.Add(package.Name)) throw new InvalidDataException("回滚源签名文件名无效");
            string path = Path.Combine(source, package.Name);
            if (!File.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 || new FileInfo(path).Length != package.Size) throw new InvalidDataException("回滚源文件缺失或被替换：" + package.Name);
            BootstrapSignedPackageHashPolicy.VerifyFile(path, package.Sha256);
        }
        if (!names.Contains("launcher-snapshot.json")) throw new InvalidDataException("回滚源未签名启动器快照");
        return verification.Manifest;
    }

    private static BootstrapSignedManifest VerifyImportedVersion(EditorProject project, string source)
    {
        string manifestPath = Path.Combine(source, "bootstrap-manifest.json");
        if (!File.Exists(manifestPath) || new FileInfo(manifestPath).Length > BootstrapManifestSignaturePolicy.MaximumJsonBytes) throw new InvalidDataException("离线发布包签名索引缺失");
        string manifestJson = File.ReadAllText(manifestPath);
        BootstrapManifestVerificationResult verification = BootstrapManifestSignaturePolicy.Verify(manifestJson, BuildEditorTrust(project), new Version(1, 0, 0, 0));
        if (!verification.IsValid) throw new InvalidDataException("离线发布包签名无效：" + verification.Error);
        var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "bootstrap-manifest.json" };
        foreach (BootstrapSignedPackage package in verification.Manifest.Packages)
        {
            if (Path.GetFileName(package.Name) != package.Name || !expected.Add(package.Name)) throw new InvalidDataException("离线发布包签名文件名无效");
            string path = Path.Combine(source, package.Name);
            if (!File.Exists(path) || new FileInfo(path).Length != package.Size) throw new InvalidDataException("离线发布包不完整：" + package.Name);
            BootstrapSignedPackageHashPolicy.VerifyFile(path, package.Sha256);
        }
        string[] actual = Directory.EnumerateFiles(source).Select(Path.GetFileName).OfType<string>().ToArray();
        if (actual.Any(name => !expected.Contains(name))) throw new InvalidDataException("离线发布包包含未签名文件");
        return verification.Manifest;
    }

    private static void VerifyCopiedHistoricalFiles(string staging, BootstrapSignedManifest manifest)
    {
        Dictionary<string, BootstrapSignedPackage> packages = manifest.Packages.ToDictionary(package => package.Name, StringComparer.OrdinalIgnoreCase);
        foreach (string path in Directory.EnumerateFiles(staging))
        {
            string name = Path.GetFileName(path);
            if (name.StartsWith("asset-", StringComparison.OrdinalIgnoreCase) || name is "player-entry.exe" or "player-update.json")
            {
                if (!packages.TryGetValue(name, out BootstrapSignedPackage? package) || new FileInfo(path).Length != package.Size) throw new InvalidDataException("回滚复制文件不在历史签名索引中：" + name);
                BootstrapSignedPackageHashPolicy.VerifyFile(path, package.Sha256);
            }
        }
    }

    private static IReadOnlyDictionary<string, BootstrapManifestTrustedKey> BuildEditorTrust(EditorProject project)
    {
        var keys = new Dictionary<string, BootstrapManifestTrustedKey>(StringComparer.Ordinal);
        foreach (BootstrapManifestTrustedKey key in project.Release.RetiredPublicKeys.Concat(new[]
        {
            new BootstrapManifestTrustedKey { KeyId = project.Release.CurrentKeyId, SubjectPublicKeyInfo = project.Release.CurrentPublicKey, NotBeforeSequence = project.Release.CurrentKeyNotBeforeSequence },
            new BootstrapManifestTrustedKey { KeyId = project.Release.NextKeyId, SubjectPublicKeyInfo = project.Release.NextPublicKey, NotBeforeSequence = project.Release.NextKeyNotBeforeSequence },
        }))
            if (!keys.TryAdd(key.KeyId, key) && keys[key.KeyId].SubjectPublicKeyInfo != key.SubjectPublicKeyInfo) throw new InvalidDataException("项目发布公钥标识冲突");
        return keys;
    }

    private static IDisposable AcquirePublishLock(string root)
    {
        string name = "Local\\LyoCrystal.LauncherPublish." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(root.ToUpperInvariant())))[..32];
        var mutex = new Mutex(false, name); bool acquired;
        try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(30)); }
        catch (AbandonedMutexException) { acquired = true; }
        if (!acquired) { mutex.Dispose(); throw new TimeoutException("另一个编辑器正在发布同一目录"); }
        return new MutexLease(mutex);
    }

    private sealed class MutexLease(Mutex mutex) : IDisposable
    {
        public void Dispose() { try { mutex.ReleaseMutex(); } finally { mutex.Dispose(); } }
    }
}

[System.Text.Json.Serialization.JsonSerializable(typeof(BootstrapSignedManifest))]
internal sealed partial class ProjectReleaseJsonContext : System.Text.Json.Serialization.JsonSerializerContext;
