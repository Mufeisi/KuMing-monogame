using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Launcher.PlayerShell;
using Shared.Security;
using Xunit;

namespace Launcher.PlayerShellIntegration.Windows;

public sealed class PlayerReplacementCoordinatorTests
{
    [Fact]
    public void 只有签名清单授权的新入口才会原子替换并保留旧版()
    {
        string root = CreateTemporaryRoot();
        try
        {
            string target = Path.Combine(root, "Player.exe");
            string staged = target + ".new";
            string journal = Path.Combine(root, "player-replacement.json");
            File.WriteAllText(target, "old-player", Encoding.UTF8);
            File.WriteAllText(staged, "new-player", Encoding.UTF8);
            using ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            IReadOnlyDictionary<string, BootstrapManifestTrustedKey> trust = CreateTrust(signer);
            File.WriteAllText(journal, CreateSignedJournal(staged, signer), Encoding.UTF8);

            PlayerReplacementResult result = PlayerReplacementCoordinator.ApplyPending(
                journal, target, trust, new Version(1, 0, 0));

            Assert.Equal(PlayerReplacementStatus.Applied, result.Status);
            Assert.Equal("new-player", File.ReadAllText(target, Encoding.UTF8));
            Assert.Equal("old-player", File.ReadAllText(target + ".previous", Encoding.UTF8));
            Assert.False(File.Exists(staged));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void 旧版路径缺失但恢复点完整时会继续完成新版切换()
    {
        string root = CreateTemporaryRoot();
        try
        {
            string target = Path.Combine(root, "Player.exe");
            string staged = target + ".new";
            string journal = Path.Combine(root, "player-replacement.json");
            File.WriteAllText(target, "old-player", Encoding.UTF8);
            File.WriteAllText(staged, "new-player", Encoding.UTF8);
            using ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            IReadOnlyDictionary<string, BootstrapManifestTrustedKey> trust = CreateTrust(signer);
            File.WriteAllText(journal, CreateSignedJournal(staged, signer), Encoding.UTF8);
            File.Move(target, target + ".previous");

            PlayerReplacementResult result = PlayerReplacementCoordinator.ApplyPending(journal, target, trust, new Version(1, 0, 0));

            Assert.Equal(PlayerReplacementStatus.Applied, result.Status);
            Assert.Equal("new-player", File.ReadAllText(target, Encoding.UTF8));
            Assert.Equal("old-player", File.ReadAllText(target + ".previous", Encoding.UTF8));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void 强停发生在新版切换后会复验新版并提交日志()
    {
        string root = CreateTemporaryRoot();
        try
        {
            string target = Path.Combine(root, "Player.exe");
            string staged = target + ".new";
            string journal = Path.Combine(root, "player-replacement.json");
            File.WriteAllText(target, "old-player", Encoding.UTF8);
            File.WriteAllText(staged, "new-player", Encoding.UTF8);
            using ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            IReadOnlyDictionary<string, BootstrapManifestTrustedKey> trust = CreateTrust(signer);
            File.WriteAllText(journal, CreateSignedJournal(staged, signer), Encoding.UTF8);
            File.Move(target, target + ".previous");
            File.Move(staged, target);

            PlayerReplacementResult result = PlayerReplacementCoordinator.ApplyPending(journal, target, trust, new Version(1, 0, 0));

            Assert.Equal(PlayerReplacementStatus.Applied, result.Status);
            Assert.Equal("new-player", File.ReadAllText(target, Encoding.UTF8));
            Assert.Equal("old-player", File.ReadAllText(target + ".previous", Encoding.UTF8));
            Assert.Contains("\"Status\": \"Committed\"", File.ReadAllText(journal, Encoding.UTF8));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void 待替换入口被篡改时拒绝替换且旧版保持完整()
    {
        string root = CreateTemporaryRoot();
        try
        {
            string target = Path.Combine(root, "Player.exe");
            string staged = target + ".new";
            string journal = Path.Combine(root, "player-replacement.json");
            File.WriteAllText(target, "old-player", Encoding.UTF8);
            File.WriteAllText(staged, "new-player", Encoding.UTF8);
            using ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            IReadOnlyDictionary<string, BootstrapManifestTrustedKey> trust = CreateTrust(signer);
            File.WriteAllText(journal, CreateSignedJournal(staged, signer), Encoding.UTF8);
            File.AppendAllText(staged, "tampered", Encoding.UTF8);

            Assert.Throws<InvalidDataException>(() => PlayerReplacementCoordinator.ApplyPending(
                journal, target, trust, new Version(1, 0, 0)));
            Assert.Equal("old-player", File.ReadAllText(target, Encoding.UTF8));
            Assert.False(File.Exists(target + ".previous"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateSignedJournal(string stagedPath, ECDsa signer)
    {
        byte[] bytes = File.ReadAllBytes(stagedPath);
        var manifest = new BootstrapSignedManifest
        {
            Format = BootstrapManifestSignaturePolicy.Format,
            Algorithm = BootstrapManifestSignaturePolicy.Algorithm,
            KeyId = "player-test",
            Sequence = 1,
            GeneratedAtUtc = "2026-08-10T00:00:00Z",
            ResourceVersion = "player-v1",
            MinimumClientVersion = "1.0.0",
            Packages =
            [
                new BootstrapSignedPackage
                {
                    Name = "player-entry",
                    Size = bytes.Length,
                    Sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
                },
            ],
        };
        manifest.Signature = Convert.ToBase64String(signer.SignData(
            BootstrapManifestSignaturePolicy.BuildCanonicalPayload(manifest),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
        return JsonSerializer.Serialize(new
        {
            Format = "lyocrystal-player-replacement-v1",
            PackageName = "player-entry",
            SignedManifestJson = JsonSerializer.Serialize(manifest),
        });
    }

    private static IReadOnlyDictionary<string, BootstrapManifestTrustedKey> CreateTrust(ECDsa signer) =>
        new Dictionary<string, BootstrapManifestTrustedKey>(StringComparer.Ordinal)
        {
            ["player-test"] = new()
            {
                KeyId = "player-test",
                SubjectPublicKeyInfo = Convert.ToBase64String(signer.ExportSubjectPublicKeyInfo()),
                NotBeforeSequence = 1,
            },
        };

    private static string CreateTemporaryRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "LyoCrystalReplacementTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
