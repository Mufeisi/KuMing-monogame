using System.Security.Cryptography;
using System.Text.Json;
using Shared.Security;
using Xunit;

namespace Base05.Tests;

public sealed class ProductionReleaseSigningTests
{
    [Fact]
    public void 生产资源公钥与发布记录一致且轮换窗口有效()
    {
        string root = FindRepositoryRoot(AppContext.BaseDirectory);
        IReadOnlyDictionary<string, BootstrapManifestTrustedKey> trust = BootstrapManifestTrustConfiguration.TrustedKeys;
        Assert.Equal(2, trust.Count);

        BootstrapManifestTrustedKey current = trust["resource-2026-a"];
        BootstrapManifestTrustedKey next = trust["resource-2026-b"];
        Assert.Equal(1, current.NotBeforeSequence);
        Assert.Equal(999_999, current.NotAfterSequence);
        Assert.Equal(900_000, next.NotBeforeSequence);
        Assert.Equal(0, next.NotAfterSequence);
        Assert.NotEqual(current.SubjectPublicKeyInfo, next.SubjectPublicKeyInfo);

        AssertPublicRecord(root, current);
        AssertPublicRecord(root, next);
        using ECDsa currentKey = ECDsa.Create();
        using ECDsa nextKey = ECDsa.Create();
        currentKey.ImportSubjectPublicKeyInfo(Convert.FromBase64String(current.SubjectPublicKeyInfo), out _);
        nextKey.ImportSubjectPublicKeyInfo(Convert.FromBase64String(next.SubjectPublicKeyInfo), out _);
        Assert.Equal(256, currentKey.KeySize);
        Assert.Equal(256, nextKey.KeySize);
    }

    [Fact]
    public void 正式签名索引覆盖全部随包资源并兼容PC与Android()
    {
        string root = FindRepositoryRoot(AppContext.BaseDirectory);
        string signedJson = File.ReadAllText(Path.Combine(root, "Docs", "ReleaseKeys", "bootstrap-package-index.signed.json"));
        BootstrapManifestVerificationResult pc = BootstrapManifestSignaturePolicy.Verify(
            signedJson, BootstrapManifestTrustConfiguration.TrustedKeys, new Version(1, 0, 0));
        BootstrapManifestVerificationResult android = BootstrapManifestSignaturePolicy.Verify(
            signedJson, BootstrapManifestTrustConfiguration.TrustedKeys, new Version(2, 0, 0));
        Assert.True(pc.IsValid, pc.Error);
        Assert.True(android.IsValid, android.Error);
        Assert.Equal("resource-2026-a", pc.Manifest.KeyId);
        Assert.Equal(1, pc.Manifest.Sequence);
        Assert.Equal(261, pc.Manifest.Packages.Count);

        using JsonDocument unsigned = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            root, "Client_MonoGame.Shared", "BootstrapAssets", "bootstrap-package-index.json")));
        Dictionary<string, (string Hash, long Size)> expected = unsigned.RootElement.GetProperty("Packages")
            .EnumerateArray()
            .ToDictionary(
                package => package.GetProperty("Name").GetString()!,
                package => (package.GetProperty("Sha256").GetString()!, package.GetProperty("Size").GetInt64()),
                StringComparer.OrdinalIgnoreCase);
        Dictionary<string, (string Hash, long Size)> actual = pc.Manifest.Packages.ToDictionary(
            package => package.Name,
            package => (package.Sha256, package.Size),
            StringComparer.OrdinalIgnoreCase);
        Assert.Equal(expected.Count, actual.Count);
        foreach ((string name, (string hash, long size)) in expected)
        {
            Assert.True(actual.TryGetValue(name, out (string Hash, long Size) package), $"签名索引缺少资源包：{name}");
            Assert.Equal(hash, package.Hash);
            Assert.Equal(size, package.Size);
        }
    }

    [Fact]
    public void CI只在签名步骤取用独立秘密并清理临时文件()
    {
        string root = FindRepositoryRoot(AppContext.BaseDirectory);
        string workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "release-01-signing.yml"));
        Assert.Contains("environment: production-signing", workflow, StringComparison.Ordinal);
        Assert.Contains("LYOCRYSTAL_ANDROID_KEYSTORE_BASE64", workflow, StringComparison.Ordinal);
        Assert.Contains("LYOCRYSTAL_RESOURCE_KEY_A_PKCS8_BASE64", workflow, StringComparison.Ordinal);
        Assert.Contains("LYOCRYSTAL_RESOURCE_KEY_B_PKCS8_BASE64", workflow, StringComparison.Ordinal);
        Assert.Contains("IsNullOrWhiteSpace($env:APK_KEYSTORE_BASE64)", workflow, StringComparison.Ordinal);
        Assert.Contains("production-signing 环境缺少 APK 或资源签名秘密", workflow, StringComparison.Ordinal);
        Assert.Contains("FixedTimeEquals($apkHash, $aHash)", workflow, StringComparison.Ordinal);
        Assert.Contains("AndroidSigningStorePass=env:LYOCRYSTAL_APK_STORE_PASSWORD", workflow, StringComparison.Ordinal);
        Assert.Contains("Remove temporary signing material", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("PRIVATE KEY-----", workflow, StringComparison.Ordinal);

        string ignore = File.ReadAllText(Path.Combine(root, ".gitignore"));
        Assert.Contains("**/Configs/ReleaseSecrets/", ignore, StringComparison.Ordinal);
        Assert.Contains("*.keystore", ignore, StringComparison.Ordinal);
    }

    private static void AssertPublicRecord(string root, BootstrapManifestTrustedKey trusted)
    {
        using JsonDocument record = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            root, "Docs", "ReleaseKeys", trusted.KeyId + ".public.json")));
        Assert.Equal(trusted.KeyId, record.RootElement.GetProperty("KeyId").GetString());
        Assert.Equal(BootstrapManifestSignaturePolicy.Algorithm, record.RootElement.GetProperty("Algorithm").GetString());
        Assert.Equal(trusted.SubjectPublicKeyInfo, record.RootElement.GetProperty("SubjectPublicKeyInfo").GetString());
    }

    private static string FindRepositoryRoot(string start)
    {
        DirectoryInfo? current = new(start);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "global.json"))) return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("无法定位仓库根目录。");
    }
}
