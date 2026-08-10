using System.Security.Cryptography;
using System.Diagnostics;
using System.Text;
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
        Assert.Contains("Validate and normalize release inputs without secrets", workflow, StringComparison.Ordinal);
        Assert.Contains("[Version]::TryParse($env:INPUT_MINIMUM_CLIENT_VERSION", workflow, StringComparison.Ordinal);
        Assert.Contains("$env:RELEASE_SEQUENCE", workflow, StringComparison.Ordinal);
        Assert.Contains("$env:RELEASE_MINIMUM_CLIENT_VERSION", workflow, StringComparison.Ordinal);
        Assert.Contains("FixedTimeEquals($apkHash, $aHash)", workflow, StringComparison.Ordinal);
        Assert.Contains("AndroidSigningStorePass=env:LYOCRYSTAL_APK_STORE_PASSWORD", workflow, StringComparison.Ordinal);
        Assert.Contains("Remove temporary signing material", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("PRIVATE KEY-----", workflow, StringComparison.Ordinal);

        string ignore = File.ReadAllText(Path.Combine(root, ".gitignore"));
        Assert.Contains("**/Configs/ReleaseSecrets/", ignore, StringComparison.Ordinal);
        Assert.Contains("*.keystore", ignore, StringComparison.Ordinal);
    }

    [Fact]
    public void Android签名恢复包可跨DPAPI文件往返且错误口令失败关闭()
    {
        if (!OperatingSystem.IsWindows()) return;
        string root = FindRepositoryRoot(AppContext.BaseDirectory);
        string directory = Path.Combine(Path.GetTempPath(), "lyocrystal-android-recovery-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        const string purpose = "android-test-purpose";
        const string alias = "android-test-alias";
        const string passwordText = "test-password-not-a-production-secret";
        string keyStore = Path.Combine(directory, "source.keystore");
        string passwordPath = Path.Combine(directory, "source-password.dpapi");
        string backup = Path.Combine(directory, "recovery.android-recovery.json");
        string corruptedPasswordPath = Path.Combine(directory, "corrupted-password.dpapi");
        string corruptedExport = Path.Combine(directory, "corrupted.android-recovery.json");
        string restoredKeyStore = Path.Combine(directory, "restored.keystore");
        string restoredPassword = Path.Combine(directory, "restored-password.dpapi");
        byte[] keyStoreBytes = RandomNumberGenerator.GetBytes(512);
        byte[] passwordBytes = Encoding.UTF8.GetBytes(passwordText);
        try
        {
            File.WriteAllBytes(keyStore, keyStoreBytes);
            File.WriteAllBytes(passwordPath, ProtectedData.Protect(
                passwordBytes,
                SHA256.HashData(Encoding.UTF8.GetBytes("LyoCrystal.Release.Secret.v1:" + purpose)),
                DataProtectionScope.CurrentUser));
            string passphrase = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            File.WriteAllBytes(corruptedPasswordPath, RandomNumberGenerator.GetBytes(64));
            InvalidOperationException corruptedDpapi = Assert.Throws<InvalidOperationException>(() => RunSigningTool(
                root, passphrase, "export-android-recovery", keyStore, corruptedPasswordPath, purpose, alias, corruptedExport));
            Assert.Contains("退出码 2", corruptedDpapi.Message, StringComparison.Ordinal);
            Assert.False(File.Exists(corruptedExport));

            RunSigningTool(root, passphrase, "export-android-recovery", keyStore, passwordPath, purpose, alias, backup);
            InvalidOperationException wrong = Assert.Throws<InvalidOperationException>(() => RunSigningTool(
                root, "wrong-passphrase-with-enough-length", "import-android-recovery", backup, purpose, alias,
                restoredKeyStore, restoredPassword));
            Assert.Contains("退出码 2", wrong.Message, StringComparison.Ordinal);
            Assert.False(File.Exists(restoredKeyStore));
            Assert.False(File.Exists(restoredPassword));

            string malformed = Path.Combine(directory, "malformed.android-recovery.json");
            File.WriteAllText(malformed, "{\"Format\":\"LyoCrystal.AndroidRecovery.v1\",\"Iterations\":600000,\"Salt\":\"%%%\"}");
            InvalidOperationException malformedFailure = Assert.Throws<InvalidOperationException>(() => RunSigningTool(
                root, passphrase, "import-android-recovery", malformed, purpose, alias,
                restoredKeyStore, restoredPassword));
            Assert.Contains("退出码 2", malformedFailure.Message, StringComparison.Ordinal);
            Assert.False(File.Exists(restoredKeyStore));
            Assert.False(File.Exists(restoredPassword));

            string blockedPasswordOutput = Path.Combine(directory, "blocked-password-output");
            Directory.CreateDirectory(blockedPasswordOutput);
            InvalidOperationException secondOutputFailure = Assert.Throws<InvalidOperationException>(() => RunSigningTool(
                root, passphrase, "import-android-recovery", backup, purpose, alias,
                restoredKeyStore, blockedPasswordOutput));
            Assert.Contains("退出码 2", secondOutputFailure.Message, StringComparison.Ordinal);
            Assert.False(File.Exists(restoredKeyStore));

            RunSigningTool(root, passphrase, "import-android-recovery", backup, purpose, alias, restoredKeyStore, restoredPassword);
            Assert.Equal(keyStoreBytes, File.ReadAllBytes(restoredKeyStore));
            byte[] restoredPlain = ProtectedData.Unprotect(
                File.ReadAllBytes(restoredPassword),
                SHA256.HashData(Encoding.UTF8.GetBytes("LyoCrystal.Release.Secret.v1:" + purpose)),
                DataProtectionScope.CurrentUser);
            try { Assert.Equal(passwordText, Encoding.UTF8.GetString(restoredPlain)); }
            finally { CryptographicOperations.ZeroMemory(restoredPlain); }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(keyStoreBytes);
            CryptographicOperations.ZeroMemory(passwordBytes);
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private static void RunSigningTool(string root, string passphrase, params string[] arguments)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("run");
        start.ArgumentList.Add("--project");
        start.ArgumentList.Add(Path.Combine(root, "Tools", "ReleaseSigningTool", "ReleaseSigningTool.csproj"));
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("Release");
        start.ArgumentList.Add("--no-build");
        start.ArgumentList.Add("--");
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        start.Environment["LYOCRYSTAL_ANDROID_RECOVERY_PASSPHRASE"] = passphrase;
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("无法启动发布签名工具");
        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"发布签名工具退出码 {process.ExitCode}：{stderr}{stdout}");
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
