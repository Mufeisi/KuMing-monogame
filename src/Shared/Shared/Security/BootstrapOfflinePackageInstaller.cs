using System.IO.Compression;

namespace Shared.Security;

public sealed record BootstrapOfflineInstallResult(long Sequence, string VersionName, string VersionDirectory);

public static class BootstrapOfflinePackageInstaller
{
    public static BootstrapOfflineInstallResult Install(string packagePath, string publishRoot, IReadOnlyDictionary<string, BootstrapManifestTrustedKey> trustedKeys, Version clientVersion)
    {
        string zip = Path.GetFullPath(packagePath), root = Path.GetFullPath(publishRoot);
        if (!File.Exists(zip) || new FileInfo(zip).Length > 256L * 1024 * 1024) throw new InvalidDataException("离线发布包不存在或超过 256 MiB");
        RejectReparse(zip);
        RejectReparse(root); Directory.CreateDirectory(root); RejectReparse(root);
        using var mutex = new Mutex(false, "Local\\LyoCrystal.OfflineInstall." + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(root.ToUpperInvariant())))[..32]);
        bool acquired;
        try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(30)); }
        catch (AbandonedMutexException) { acquired = true; }
        if (!acquired) throw new TimeoutException("等待离线导入锁超时");
        string staging = Path.Combine(root, ".offline-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(staging);
            using ZipArchive archive = ZipFile.OpenRead(zip);
            if (archive.Entries.Count is < 3 or > 512) throw new InvalidDataException("离线发布包文件数量无效");
            ZipArchiveEntry pointer = archive.GetEntry("current.txt") ?? throw new InvalidDataException("离线发布包缺少版本指针");
            if (pointer.Length is < 1 or > 256) throw new InvalidDataException("离线发布包版本指针长度无效");
            string version; using (var reader = new StreamReader(pointer.Open(), System.Text.Encoding.UTF8, true, 256, false)) version = reader.ReadToEnd().Trim();
            ValidateVersion(version);
            string prefix = "versions/" + version + "/"; long total = 0; int count = 0;
            foreach (ZipArchiveEntry entry in archive.Entries.Where(item => item.FullName.StartsWith(prefix, StringComparison.Ordinal)))
            {
                string name = entry.FullName[prefix.Length..];
                if (string.IsNullOrWhiteSpace(name) || Path.GetFileName(name) != name || ++count > 510) throw new InvalidDataException("离线发布包路径或文件数无效");
                if ((total = checked(total + entry.Length)) > 192L * 1024 * 1024) throw new InvalidDataException("离线发布包展开后过大");
                using Stream input = entry.Open(); using var output = new FileStream(Path.Combine(staging, name), FileMode.CreateNew, FileAccess.Write, FileShare.None); input.CopyTo(output);
            }
            string manifestPath = Path.Combine(staging, "bootstrap-manifest.json");
            string json = ReadBoundedText(manifestPath, BootstrapManifestSignaturePolicy.MaximumJsonBytes);
            string acceptanceState = Path.Combine(Path.GetDirectoryName(root)!, "." + Path.GetFileName(root) + ".offline-bootstrap-state.json");
            RejectReparse(acceptanceState);
            EnsureCurrentAccepted(root, acceptanceState, trustedKeys, clientVersion);
            BootstrapSignedManifest accepted = BootstrapManifestAcceptanceStore.VerifyForAcceptance(json, acceptanceState, trustedKeys, clientVersion);
            var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "bootstrap-manifest.json" };
            foreach (BootstrapSignedPackage package in accepted.Packages)
            {
                if (Path.GetFileName(package.Name) != package.Name || !expected.Add(package.Name)) throw new InvalidDataException("离线发布签名文件名无效");
                string file = Path.Combine(staging, package.Name);
                if (!File.Exists(file) || new FileInfo(file).Length != package.Size) throw new InvalidDataException("离线发布文件不完整：" + package.Name);
                BootstrapSignedPackageHashPolicy.VerifyFile(file, package.Sha256);
            }
            if (Directory.EnumerateFiles(staging).Select(Path.GetFileName).Any(name => name is not null && !expected.Contains(name))) throw new InvalidDataException("离线发布包包含未签名文件");
            string destination = Path.Combine(root, "versions", version); Directory.CreateDirectory(Path.GetDirectoryName(destination)!); RejectReparse(Path.GetDirectoryName(destination)!);
            if (Directory.Exists(destination))
            {
                RejectReparse(destination);
                string existingJson = ReadBoundedText(Path.Combine(destination, "bootstrap-manifest.json"), BootstrapManifestSignaturePolicy.MaximumJsonBytes);
                BootstrapSignedManifest existing = BootstrapManifestAcceptanceStore.VerifyForAcceptance(existingJson, acceptanceState, trustedKeys, clientVersion);
                if (existing.Sequence != accepted.Sequence || !string.Equals(existing.ResourceVersion, accepted.ResourceVersion, StringComparison.Ordinal) || !string.Equals(existingJson, json, StringComparison.Ordinal)) throw new IOException("同名离线版本目录与待导入内容不同");
                var existingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "bootstrap-manifest.json" };
                foreach (BootstrapSignedPackage package in existing.Packages)
                {
                    existingNames.Add(package.Name); string file = Path.Combine(destination, package.Name);
                    if (!File.Exists(file) || new FileInfo(file).Length != package.Size) throw new InvalidDataException("已存在离线版本不完整：" + package.Name);
                    BootstrapSignedPackageHashPolicy.VerifyFile(file, package.Sha256);
                }
                EnsureExactTopLevelFiles(destination, existingNames, "已存在离线版本");
                Directory.Delete(staging, true);
            }
            else Directory.Move(staging, destination);
            BootstrapManifestAcceptanceStore.VerifyAndAccept(json, acceptanceState, trustedKeys, clientVersion);
            WritePointer(root, version);
            return new BootstrapOfflineInstallResult(accepted.Sequence, version, destination);
        }
        finally
        {
            if (Directory.Exists(staging)) { RejectReparse(staging); Directory.Delete(staging, true); }
            try { mutex.ReleaseMutex(); } catch (ApplicationException) { }
        }
    }

    private static void EnsureCurrentAccepted(string root, string statePath, IReadOnlyDictionary<string, BootstrapManifestTrustedKey> keys, Version clientVersion)
    {
        string pointer = Path.Combine(root, "current.txt"); if (!File.Exists(pointer) || File.Exists(statePath)) return;
        if ((File.GetAttributes(pointer) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("目标发布指针不得为重解析点");
        if (new FileInfo(pointer).Length is < 1 or > 256) throw new InvalidDataException("目标发布指针长度无效");
        string version = ReadBoundedText(pointer, 256).Trim(); ValidateVersion(version);
        string versionRoot = Path.GetFullPath(Path.Combine(root, "versions", version)); RejectReparse(versionRoot);
        string json = ReadBoundedText(Path.Combine(versionRoot, "bootstrap-manifest.json"), BootstrapManifestSignaturePolicy.MaximumJsonBytes);
        BootstrapManifestVerificationResult result = BootstrapManifestSignaturePolicy.Verify(json, keys, clientVersion);
        if (!result.IsValid) throw new InvalidDataException("目标当前发布签名无效：" + result.Error);
        var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "bootstrap-manifest.json" };
        foreach (BootstrapSignedPackage package in result.Manifest.Packages)
        {
            if (Path.GetFileName(package.Name) != package.Name || !expected.Add(package.Name)) throw new InvalidDataException("目标当前发布签名文件名无效");
            string file = Path.Combine(versionRoot, package.Name);
            RejectReparse(file);
            if (!File.Exists(file) || new FileInfo(file).Length != package.Size) throw new InvalidDataException("目标当前发布文件不完整：" + package.Name);
            BootstrapSignedPackageHashPolicy.VerifyFile(file, package.Sha256);
        }
        EnsureExactTopLevelFiles(versionRoot, expected, "目标当前发布");
        BootstrapManifestAcceptanceStore.VerifyAndAccept(json, statePath, keys, clientVersion);
    }

    private static void WritePointer(string root, string version)
    {
        string target = Path.Combine(root, "current.txt"), temporary = target + ".tmp-" + Guid.NewGuid().ToString("N");
        try { File.WriteAllText(temporary, version + "\n", new System.Text.UTF8Encoding(false)); File.Move(temporary, target, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static void ValidateVersion(string value)
    {
        if (value.Length is < 3 or > 96 || value.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_')) throw new InvalidDataException("离线版本名无效");
    }

    private static string ReadBoundedText(string path, int maximumBytes)
    {
        RejectReparse(path);
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length is < 1 || input.Length > maximumBytes) throw new InvalidDataException("签名发布文本超过大小限制");
        using var reader = new StreamReader(input, System.Text.Encoding.UTF8, true, 4096, false);
        return reader.ReadToEnd();
    }

    private static void EnsureExactTopLevelFiles(string directory, HashSet<string> expected, string description)
    {
        RejectReparse(directory);
        if (Directory.EnumerateDirectories(directory).Any()) throw new InvalidDataException(description + "包含未签名子目录");
        foreach (string file in Directory.EnumerateFiles(directory))
        {
            RejectReparse(file);
            string name = Path.GetFileName(file);
            if (!expected.Contains(name)) throw new InvalidDataException(description + "包含未签名文件：" + name);
        }
    }

    private static void RejectReparse(string path)
    {
        string full = Path.GetFullPath(path); string current = Path.GetPathRoot(full) ?? string.Empty;
        foreach (string part in full[current.Length..].Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (part.Length == 0) continue; current = Path.Combine(current, part);
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("离线导入路径不得经过重解析点");
        }
    }
}
