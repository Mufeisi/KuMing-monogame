using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Shared.CustomGui;
using Shared.Release;
using Shared.Security;

namespace LyoCrystal.LauncherEditor;

public static class TestResourceReleasePublisher
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    public static TestResourceReleaseResult Publish(EditorProject project, string projectRoot, string outputRoot)
    {
        ArgumentNullException.ThrowIfNull(project);
        ProjectReleaseKeyStore.EnsureProvisioned(project, projectRoot);
        string sourceRoot = Path.GetFullPath(FirstExistingDirectory(project.Gateway.ResourceDirectory, project.ImportedClientDirectory));
        if (!Directory.Exists(sourceRoot)) throw new DirectoryNotFoundException("资源目录不存在，无法生成测试资源发布。");
        string output = Path.GetFullPath(outputRoot);
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
            throw new IOException("测试资源发布目录必须为空，拒绝覆盖已有内容。");
        string parent = Path.GetDirectoryName(output) ?? throw new InvalidDataException("测试资源发布目录无效。");
        Directory.CreateDirectory(parent);
        string staging = Path.Combine(parent, ".test-resource-staging-" + Guid.NewGuid().ToString("N"));
        string packagesRoot = Path.Combine(staging, "Packages");
        Directory.CreateDirectory(packagesRoot);
        try
        {
            var specs = new[]
            {
                new TestPackageSpec("core-startup", "core", ["Data/Title.Lib", "Data/ChrSel.Lib", "Data/Prguse.Lib"]),
                new TestPackageSpec("fui-retro", "ui", ["Assets/UI/复古/UI_fui.bytes"]),
            };
            var resourceManifest = new BootstrapPackageManifestDocument
            {
                Packs = specs.Select(item => new BootstrapPackageManifestEntry
                {
                    Name = item.Name,
                    Kind = item.Kind,
                    Description = "作者工具测试资源",
                    AssetCount = item.Assets.Count,
                    TotalBytes = 0L,
                    ManifestPath = string.Empty,
                    InstallRootHint = $"Cache/Mobile/Packages/{item.Name}/",
                    Assets = item.Assets.ToList(),
                }).ToList(),
            };
            string packageManifest = JsonSerializer.Serialize(resourceManifest);
            var packages = new List<BootstrapSignedPackage>();
            foreach (TestPackageSpec spec in specs) AddPackage(sourceRoot, packagesRoot, spec.Name, spec.Assets, packageManifest, packages);
            CustomGuiRuntimeDocument guiDocument = project.GameGuiDocuments.FirstOrDefault()
                ?? throw new InvalidDataException("项目缺少游戏 GUI 文档");
            var guiBindings = new CustomGuiResourceBindingsDocument
            {
                Assets =
                [
                    new("event-banner", "core-startup", "Data/Title.Lib"),
                    new("starter-sword", "core-startup", "Data/Prguse.Lib"),
                ],
            };
            string guiPath = Path.Combine(packagesRoot, CustomGuiStaticPackagePublisher.PackageName + ".zip");
            CustomGuiStaticPackagePublisher.Publish(guiPath, guiDocument, guiBindings, resourceManifest);
            packages.Add(new BootstrapSignedPackage
            {
                Name = CustomGuiStaticPackagePublisher.PackageName,
                Sha256 = Hash(guiPath),
                Size = new FileInfo(guiPath).Length,
            });

            long sequence = Math.Max(1, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            string resourceVersion = $"{project.Snapshot.ProjectId}-test-{sequence}";
            var manifest = new BootstrapSignedManifest
            {
                Format = BootstrapManifestSignaturePolicy.Format, Algorithm = BootstrapManifestSignaturePolicy.Algorithm,
                KeyId = project.Release.CurrentKeyId, Sequence = sequence,
                GeneratedAtUtc = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'"),
                ResourceVersion = resourceVersion, MinimumClientVersion = "1.0.0.0",
                Packages = packages.OrderBy(item => item.Name, StringComparer.Ordinal).ToList(),
            };
            byte[] privateKey = ProjectReleaseKeyStore.LoadCurrentPrivateKey(project, projectRoot);
            try
            {
                using ECDsa signer = ECDsa.Create(); signer.ImportPkcs8PrivateKey(privateKey, out int read);
                if (read != privateKey.Length) throw new CryptographicException("项目签名私钥格式无效");
                manifest.Signature = Convert.ToBase64String(signer.SignData(BootstrapManifestSignaturePolicy.BuildCanonicalPayload(manifest), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
            }
            finally { CryptographicOperations.ZeroMemory(privateKey); }

            string json = JsonSerializer.Serialize(manifest, TestResourceReleaseJsonContext.Default.BootstrapSignedManifest);
            File.WriteAllText(Path.Combine(packagesRoot, "bootstrap-package-index.json"), json, Utf8NoBom);
            File.WriteAllText(Path.Combine(packagesRoot, "bootstrap-package-index.signed.json"), json, Utf8NoBom);
            var trusted = new Dictionary<string, BootstrapManifestTrustedKey>(StringComparer.Ordinal)
            {
                [project.Release.CurrentKeyId] = new BootstrapManifestTrustedKey { KeyId = project.Release.CurrentKeyId, SubjectPublicKeyInfo = project.Release.CurrentPublicKey, NotBeforeSequence = project.Release.CurrentKeyNotBeforeSequence },
            };
            BootstrapManifestVerificationResult verified = BootstrapManifestSignaturePolicy.Verify(json, trusted, new Version(2, 0, 0));
            if (!verified.IsValid) throw new InvalidDataException("测试资源发布签名自检失败：" + verified.Error);
            if (Directory.Exists(output)) Directory.Delete(output, recursive: false);
            Directory.Move(staging, output);
            return new TestResourceReleaseResult(output, resourceVersion, manifest.KeyId, sequence, packages.Count);
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); }
    }

    private static void AddPackage(string sourceRoot, string packagesRoot, string name, IReadOnlyList<string> assets, string packageManifest, ICollection<BootstrapSignedPackage> output)
    {
        string[] missing = assets.Where(relative => !File.Exists(ResolveContained(sourceRoot, relative))).ToArray();
        if (missing.Length > 0) throw new FileNotFoundException($"资源目录缺少测试包 {name} 所需文件：{string.Join("、", missing)}");
        string zipPath = Path.Combine(packagesRoot, name + ".zip");
        using (ZipArchive archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            ZipArchiveEntry manifestEntry = archive.CreateEntry("bootstrap-packages.json", CompressionLevel.Optimal);
            manifestEntry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
            using (var writer = new StreamWriter(manifestEntry.Open(), Utf8NoBom, leaveOpen: false)) writer.Write(packageManifest);
            foreach (string relative in assets.OrderBy(value => value, StringComparer.Ordinal))
            {
                ZipArchiveEntry entry = archive.CreateEntry("Packages/" + name + "/" + relative.Replace('\\', '/'), CompressionLevel.Optimal);
                entry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
                using Stream source = File.OpenRead(ResolveContained(sourceRoot, relative));
                using Stream target = entry.Open();
                source.CopyTo(target);
            }
        }
        output.Add(new BootstrapSignedPackage { Name = name, Sha256 = Hash(zipPath), Size = new FileInfo(zipPath).Length });
    }

    private static string FirstExistingDirectory(params string[] candidates) => candidates.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value) && Directory.Exists(value)) ?? candidates.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
    private static string ResolveContained(string root, string relative)
    {
        string normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string path = Path.GetFullPath(Path.Combine(normalizedRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("资源路径越出资源根目录。");
        return path;
    }
    private static string Hash(string path) { using FileStream stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(); }
    private sealed record TestPackageSpec(string Name, string Kind, IReadOnlyList<string> Assets);
}

public sealed record TestResourceReleaseResult(string OutputRoot, string ResourceVersion, string KeyId, long Sequence, int PackageCount);

[System.Text.Json.Serialization.JsonSourceGenerationOptions(WriteIndented = true, UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
[System.Text.Json.Serialization.JsonSerializable(typeof(BootstrapSignedManifest))]
internal sealed partial class TestResourceReleaseJsonContext : System.Text.Json.Serialization.JsonSerializerContext;
