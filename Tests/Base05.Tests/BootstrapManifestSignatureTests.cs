using System.Security.Cryptography;
using System.Text.Json;
using Shared.Security;
using Xunit;

namespace Base05.Tests;

public sealed class BootstrapManifestSignatureTests
{
    [Fact]
    public void 确定性二进制载荷不受资源包JSON顺序影响()
    {
        BootstrapSignedManifest first = CreateManifest(7, "resource-v7");
        BootstrapSignedManifest second = CreateManifest(7, "resource-v7");
        second.Packages.Reverse();

        Assert.Equal(
            BootstrapManifestSignaturePolicy.BuildCanonicalPayload(first),
            BootstrapManifestSignaturePolicy.BuildCanonicalPayload(second));
    }

    [Fact]
    public void 当前与下一密钥按序列窗口平滑轮换()
    {
        using ECDsa current = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using ECDsa next = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        IReadOnlyDictionary<string, BootstrapManifestTrustedKey> keys = new Dictionary<string, BootstrapManifestTrustedKey>
        {
            ["resource-2026-a"] = Trust("resource-2026-a", current, 1, 10),
            ["resource-2026-b"] = Trust("resource-2026-b", next, 10, 0),
        };

        BootstrapSignedManifest beforeRotation = Sign(CreateManifest(9, "resource-v9", "resource-2026-a"), current);
        Assert.True(Verify(beforeRotation, keys).IsValid);

        BootstrapSignedManifest duringRotation = Sign(CreateManifest(10, "resource-v10", "resource-2026-b"), next);
        Assert.True(Verify(duringRotation, keys, new BootstrapManifestAcceptedState
        {
            Sequence = 9,
            ResourceVersion = "resource-v9",
        }).IsValid);

        BootstrapSignedManifest expiredKey = Sign(CreateManifest(11, "resource-v11", "resource-2026-a"), current);
        Assert.Contains("轮换窗口", Verify(expiredKey, keys).Error);
    }

    [Fact]
    public void 哈希篡改未知密钥和错误签名均失败关闭()
    {
        using ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using ECDsa attacker = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var keys = new Dictionary<string, BootstrapManifestTrustedKey>
        {
            ["resource-main"] = Trust("resource-main", signer),
        };

        BootstrapSignedManifest tampered = Sign(CreateManifest(5, "resource-v5"), signer);
        tampered.Packages[0].Sha256 = new string('c', 64);
        Assert.Contains("签名验证失败", Verify(tampered, keys).Error);

        BootstrapSignedManifest unknown = Sign(CreateManifest(5, "resource-v5", "resource-unknown"), attacker);
        Assert.Contains("不受信任", Verify(unknown, keys).Error);

        BootstrapSignedManifest wrongSignature = Sign(CreateManifest(5, "resource-v5"), attacker);
        Assert.Contains("签名验证失败", Verify(wrongSignature, keys).Error);
    }

    [Fact]
    public void 低序列与同序列不同资源版本均拒绝降级()
    {
        using ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var keys = new Dictionary<string, BootstrapManifestTrustedKey>
        {
            ["resource-main"] = Trust("resource-main", signer),
        };
        var accepted = new BootstrapManifestAcceptedState { Sequence = 10, ResourceVersion = "resource-v10" };

        Assert.Contains("拒绝降级", Verify(Sign(CreateManifest(9, "resource-v9"), signer), keys, accepted).Error);
        Assert.Contains("资源版本不同", Verify(Sign(CreateManifest(10, "resource-v10-replaced"), signer), keys, accepted).Error);
        Assert.True(Verify(Sign(CreateManifest(10, "resource-v10"), signer), keys, accepted).IsValid);
    }

    [Fact]
    public void 最低兼容版本高于当前客户端时拒绝更新()
    {
        using ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var keys = new Dictionary<string, BootstrapManifestTrustedKey>
        {
            ["resource-main"] = Trust("resource-main", signer),
        };
        BootstrapSignedManifest manifest = CreateManifest(12, "resource-v12");
        manifest.MinimumClientVersion = "2.0.0";
        manifest = Sign(manifest, signer);

        BootstrapManifestVerificationResult result = BootstrapManifestSignaturePolicy.Verify(
            Serialize(manifest), keys, new Version(1, 9, 9));

        Assert.False(result.IsValid);
        Assert.Contains("最低版本", result.Error);

        manifest.MinimumClientVersion = "1.0.0.0";
        manifest = Sign(manifest, signer);
        Assert.True(BootstrapManifestSignaturePolicy.Verify(
            Serialize(manifest), keys, new Version(1, 0, 0)).IsValid);
    }

    [Fact]
    public void 重复包非规范摘要和未知字段均拒绝()
    {
        using ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var keys = new Dictionary<string, BootstrapManifestTrustedKey>
        {
            ["resource-main"] = Trust("resource-main", signer),
        };

        BootstrapSignedManifest duplicate = CreateManifest(3, "resource-v3");
        duplicate.Packages.Add(new BootstrapSignedPackage
        {
            Name = duplicate.Packages[0].Name.ToUpperInvariant(),
            Sha256 = new string('d', 64),
            Size = 1,
        });
        Assert.Contains("重复", Verify(duplicate, keys).Error);

        BootstrapSignedManifest upperHash = CreateManifest(3, "resource-v3");
        upperHash.Packages[0].Sha256 = new string('A', 64);
        Assert.Contains("小写", Verify(upperHash, keys).Error);

        BootstrapSignedManifest valid = Sign(CreateManifest(3, "resource-v3"), signer);
        string withUnknownField = Serialize(valid).Replace("{", "{\"Unexpected\":true,", StringComparison.Ordinal);
        Assert.Contains("JSON 无效", BootstrapManifestSignaturePolicy.Verify(withUnknownField, keys, new Version(1, 0, 0)).Error);

        string validJson = Serialize(valid);
        string duplicateField = validJson.Replace("\"Format\":", "\"Format\":\"duplicate\",\"Format\":", StringComparison.Ordinal);
        Assert.Contains("重复字段", BootstrapManifestSignaturePolicy.Verify(duplicateField, keys, new Version(1, 0, 0)).Error);
    }

    [Fact]
    public void 正式接受存储原子记录最高序列并在重启后拒绝降级()
    {
        using ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var keys = new Dictionary<string, BootstrapManifestTrustedKey>
        {
            ["resource-main"] = Trust("resource-main", signer),
        };
        string root = Path.Combine(Path.GetTempPath(), "LyoCrystalManifestState-" + Guid.NewGuid().ToString("N"));
        string statePath = Path.Combine(root, "BootstrapManifestSecurityState.json");
        try
        {
            BootstrapSignedManifest accepted = Sign(CreateManifest(20, "resource-v20"), signer);
            BootstrapManifestAcceptanceStore.VerifyAndAccept(
                Serialize(accepted), statePath, keys, new Version(1, 0, 0));

            Assert.True(File.Exists(statePath));
            Assert.Contains("\"Sequence\": 20", File.ReadAllText(statePath));
            Assert.True(BootstrapManifestAcceptanceStore.IsAcceptedResourceVersion(statePath, "resource-v20"));
            Assert.False(BootstrapManifestAcceptanceStore.IsAcceptedResourceVersion(statePath, "resource-v19"));
            Assert.Throws<InvalidDataException>(() => BootstrapManifestAcceptanceStore.VerifyAndAccept(
                Serialize(Sign(CreateManifest(19, "resource-v19"), signer)),
                statePath,
                keys,
                new Version(1, 0, 0)));

            File.WriteAllText(statePath, "{}");
            Assert.False(BootstrapManifestAcceptanceStore.IsAcceptedResourceVersion(statePath, "resource-v20"));
            Assert.Throws<InvalidDataException>(() => BootstrapManifestAcceptanceStore.VerifyAndAccept(
                Serialize(accepted), statePath, keys, new Version(1, 0, 0)));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static BootstrapManifestVerificationResult Verify(
        BootstrapSignedManifest manifest,
        IReadOnlyDictionary<string, BootstrapManifestTrustedKey> keys,
        BootstrapManifestAcceptedState state = null) =>
        BootstrapManifestSignaturePolicy.Verify(Serialize(manifest), keys, new Version(1, 0, 0), state);

    private static BootstrapSignedManifest CreateManifest(long sequence, string resourceVersion, string keyId = "resource-main") => new()
    {
        Format = BootstrapManifestSignaturePolicy.Format,
        Algorithm = BootstrapManifestSignaturePolicy.Algorithm,
        KeyId = keyId,
        Sequence = sequence,
        GeneratedAtUtc = "2026-08-10T12:00:00Z",
        ResourceVersion = resourceVersion,
        MinimumClientVersion = "1.0.0",
        Packages = new List<BootstrapSignedPackage>
        {
            new() { Name = "core-startup", Sha256 = new string('a', 64), Size = 1024 },
            new() { Name = "data-items", Sha256 = new string('b', 64), Size = 2048 },
        },
        Signature = string.Empty,
    };

    private static BootstrapSignedManifest Sign(BootstrapSignedManifest manifest, ECDsa signer)
    {
        byte[] payload = BootstrapManifestSignaturePolicy.BuildCanonicalPayload(manifest);
        manifest.Signature = Convert.ToBase64String(signer.SignData(
            payload,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
        return manifest;
    }

    private static BootstrapManifestTrustedKey Trust(string keyId, ECDsa key, long notBefore = 1, long notAfter = 0) => new()
    {
        KeyId = keyId,
        SubjectPublicKeyInfo = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()),
        NotBeforeSequence = notBefore,
        NotAfterSequence = notAfter,
    };

    private static string Serialize(BootstrapSignedManifest manifest) => JsonSerializer.Serialize(manifest);
}
