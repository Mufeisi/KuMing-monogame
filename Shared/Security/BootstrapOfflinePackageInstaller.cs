using System.IO.Compression;

namespace Shared.Security;

public sealed record BootstrapOfflineInstallResult(long Sequence, string VersionName, string VersionDirectory);

public static class BootstrapOfflinePackageInstaller
{
    public static BootstrapOfflineInstallResult Install(string packagePath, string publishRoot, IReadOnlyDictionary<string, BootstrapManifestTrustedKey> trustedKeys, Version clientVersion)
    {
        string zip = Path.GetFullPath(packagePath), root = Path.GetFullPath(publishRoot);
        if (!File.Exists(zip) || new FileInfo(zip).Length > 256L * 1024 * 1024) throw new InvalidDataException("离线发布包不存在或超过 256 MiB");
        RejectReparse(root); Directory.CreateDirectory(root); RejectReparse(root);
        using var mutex = new Mutex(false, "Local\\LyoCrystal.OfflineInstall." + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(root.ToUpperInvariant())))[..32]);
        if (!mutex.WaitOne(TimeSpan.FromSeconds(30))) throw new TimeoutException("等待离线导入锁超时");
        string staging = Path.Combine(root, ".offline-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(staging);
            using ZipArchive archive = ZipFile.OpenRead(zip);
            if (archive.Entries.Count is < 3 or > 512) throw new InvalidDataException("离线发布包文件数量无效");
            ZipArchiveEntry pointer = archive.GetEntry("current.txt") ?? throw new InvalidDataException("离线发布包缺少版本指针");
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
            string json = File.ReadAllText(manifestPath);
            BootstrapManifestVerificationResult verified = BootstrapManifestSignaturePolicy.Verify(json, trustedKeys, clientVersion);
            if (!verified.IsValid) throw new InvalidDataException("离线发布签名无效：" + verified.Error);
            var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "bootstrap-manifest.json" };
            foreach (BootstrapSignedPackage package in verified.Manifest.Packages)
            {
                if (Path.GetFileName(package.Name) != package.Name || !expected.Add(package.Name)) throw new InvalidDataException("离线发布签名文件名无效");
                string file = Path.Combine(staging, package.Name);
                if (!File.Exists(file) || new FileInfo(file).Length != package.Size) throw new InvalidDataException("离线发布文件不完整：" + package.Name);
                BootstrapSignedPackageHashPolicy.VerifyFile(file, package.Sha256);
            }
            if (Directory.EnumerateFiles(staging).Select(Path.GetFileName).Any(name => name is not null && !expected.Contains(name))) throw new InvalidDataException("离线发布包包含未签名文件");
            long floor = ReadCurrentSequence(root, trustedKeys, clientVersion);
            if (verified.Manifest.Sequence <= floor) throw new InvalidDataException("离线发布序列必须严格高于目标当前序列");
            string destination = Path.Combine(root, "versions", version); Directory.CreateDirectory(Path.GetDirectoryName(destination)!); RejectReparse(Path.GetDirectoryName(destination)!);
            if (Directory.Exists(destination)) throw new IOException("离线版本目录已存在");
            Directory.Move(staging, destination);
            WritePointer(root, version);
            return new BootstrapOfflineInstallResult(verified.Manifest.Sequence, version, destination);
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
            try { mutex.ReleaseMutex(); } catch (ApplicationException) { }
        }
    }

    private static long ReadCurrentSequence(string root, IReadOnlyDictionary<string, BootstrapManifestTrustedKey> keys, Version clientVersion)
    {
        string pointer = Path.Combine(root, "current.txt"); if (!File.Exists(pointer)) return 0;
        string version = File.ReadAllText(pointer).Trim(); ValidateVersion(version);
        string versionRoot = Path.GetFullPath(Path.Combine(root, "versions", version)); RejectReparse(versionRoot);
        BootstrapManifestVerificationResult result = BootstrapManifestSignaturePolicy.Verify(File.ReadAllText(Path.Combine(versionRoot, "bootstrap-manifest.json")), keys, clientVersion);
        if (!result.IsValid) throw new InvalidDataException("目标当前发布签名无效：" + result.Error);
        return result.Manifest.Sequence;
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
