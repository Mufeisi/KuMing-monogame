using System.Security.Cryptography;
using System.Runtime.Versioning;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Buffers.Binary;
using Shared.Security;

internal static class Program
{
    private const string PrivateKeyEnvironment = "LYOCRYSTAL_RESOURCE_SIGNING_PRIVATE_KEY_BASE64";
    private const string AndroidRecoveryPassphraseEnvironment = "LYOCRYSTAL_ANDROID_RECOVERY_PASSPHRASE";
    private const int AndroidRecoveryIterations = 600_000;
    private static readonly Regex KeyIdPattern = new("^[A-Za-z0-9._-]{1,64}$", RegexOptions.CultureInvariant);
    private static readonly UTF8Encoding Utf8NoBom = new(false, true);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static int Main(string[] args)
    {
        try
        {
            if (args.Length == 4 && args[0] == "provision-resource-key")
            {
                if (!OperatingSystem.IsWindows())
                    throw new PlatformNotSupportedException("DPAPI 私钥存储仅支持 Windows");
                ProvisionResourceKey(args[1], args[2], args[3]);
                return 0;
            }

            if (args.Length == 7 && args[0] == "sign-resource-index")
            {
                SignResourceIndex(
                    args[1], args[2], args[3],
                    ParsePositiveSequence(args[4]),
                    ParseVersion(args[5]),
                    args[6]);
                return 0;
            }

            if (args.Length == 4 && args[0] == "protect-environment-secret")
            {
                if (!OperatingSystem.IsWindows())
                    throw new PlatformNotSupportedException("DPAPI 秘密存储仅支持 Windows");
                ProtectEnvironmentSecret(args[1], args[2], args[3]);
                return 0;
            }

            if (args.Length == 3 && args[0] == "verify-resource-index")
            {
                VerifyResourceIndex(args[1], ParseVersion(args[2]));
                return 0;
            }

            if (args.Length == 7 && args[0] == "publish-signed-android")
            {
                if (!OperatingSystem.IsWindows())
                    throw new PlatformNotSupportedException("本地 DPAPI APK 签名构建仅支持 Windows");
                PublishSignedAndroid(args[1], args[2], args[3], args[4], args[5], args[6]);
                return 0;
            }

            if (args.Length == 6 && args[0] == "export-android-recovery")
            {
                if (!OperatingSystem.IsWindows())
                    throw new PlatformNotSupportedException("Android 恢复包导出仅支持 Windows");
                ExportAndroidRecovery(args[1], args[2], args[3], args[4], args[5]);
                return 0;
            }

            if (args.Length == 6 && args[0] == "import-android-recovery")
            {
                if (!OperatingSystem.IsWindows())
                    throw new PlatformNotSupportedException("Android 恢复包导入仅支持 Windows");
                ImportAndroidRecovery(args[1], args[2], args[3], args[4], args[5]);
                return 0;
            }

            throw new ArgumentException(
                "用法：\n" +
                "  provision-resource-key <KeyId> <私钥.dpapi> <公钥.json>\n" +
                "  protect-environment-secret <用途> <环境变量名> <秘密.dpapi>\n" +
                "  sign-resource-index <未签名索引> <签名索引> <KeyId> <Sequence> <最低版本> <私钥.dpapi或->\n" +
                "  verify-resource-index <签名索引> <客户端版本>\n" +
                "  publish-signed-android <项目> <keystore> <口令.dpapi> <用途> <alias> <构建日志>\n" +
                "  export-android-recovery <keystore> <口令.dpapi> <用途> <alias> <恢复包>\n" +
                "  import-android-recovery <恢复包> <预期用途> <预期alias> <keystore输出> <口令.dpapi输出>\n" +
                "CI 使用 '-' 时必须在当前步骤提供 LYOCRYSTAL_RESOURCE_SIGNING_PRIVATE_KEY_BASE64。"
            );
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }
    }

    [SupportedOSPlatform("windows")]
    private static void ExportAndroidRecovery(
        string keyStorePath,
        string protectedPasswordPath,
        string purpose,
        string alias,
        string outputPath)
    {
        ValidateAndroidSigningIdentity(purpose, alias);
        keyStorePath = Path.GetFullPath(keyStorePath);
        outputPath = Path.GetFullPath(outputPath);
        if (!File.Exists(keyStorePath)) throw new FileNotFoundException("Android keystore 不存在", keyStorePath);
        if (File.Exists(outputPath)) throw new IOException("恢复包已存在，拒绝覆盖");

        byte[] passphrase = ReadAndClearRecoveryPassphrase();
        byte[] password = LoadProtectedSecret(purpose, protectedPasswordPath);
        byte[] keyStore = File.ReadAllBytes(keyStorePath);
        byte[] plain = BuildAndroidRecoveryPayload(purpose, alias, keyStore, password);
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        byte[] nonce = RandomNumberGenerator.GetBytes(12);
        byte[] key = Rfc2898DeriveBytes.Pbkdf2(passphrase, salt, AndroidRecoveryIterations, HashAlgorithmName.SHA256, 32);
        byte[] cipher = new byte[plain.Length];
        byte[] tag = new byte[16];
        try
        {
            using var aes = new AesGcm(key, tag.Length);
            aes.Encrypt(nonce, plain, cipher, tag, Utf8NoBom.GetBytes("LyoCrystal.AndroidRecovery.v1"));
            var envelope = new AndroidRecoveryEnvelope
            {
                Format = "LyoCrystal.AndroidRecovery.v1",
                Iterations = AndroidRecoveryIterations,
                Salt = Convert.ToBase64String(salt),
                Nonce = Convert.ToBase64String(nonce),
                Ciphertext = Convert.ToBase64String(cipher),
                Tag = Convert.ToBase64String(tag),
            };
            WriteAtomic(outputPath, JsonSerializer.SerializeToUtf8Bytes(envelope, JsonOptions));
            Console.WriteLine($"Android 签名恢复包已加密导出：Purpose={purpose}；Alias={alias}。");
        }
        finally
        {
            Environment.SetEnvironmentVariable(AndroidRecoveryPassphraseEnvironment, null);
            CryptographicOperations.ZeroMemory(passphrase);
            CryptographicOperations.ZeroMemory(password);
            CryptographicOperations.ZeroMemory(keyStore);
            CryptographicOperations.ZeroMemory(plain);
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(cipher);
        }
    }

    [SupportedOSPlatform("windows")]
    private static void ImportAndroidRecovery(
        string inputPath,
        string expectedPurpose,
        string expectedAlias,
        string keyStoreOutputPath,
        string protectedPasswordOutputPath)
    {
        ValidateAndroidSigningIdentity(expectedPurpose, expectedAlias);
        inputPath = Path.GetFullPath(inputPath);
        keyStoreOutputPath = Path.GetFullPath(keyStoreOutputPath);
        protectedPasswordOutputPath = Path.GetFullPath(protectedPasswordOutputPath);
        if (File.Exists(keyStoreOutputPath) || File.Exists(protectedPasswordOutputPath))
            throw new IOException("恢复目标已存在，拒绝覆盖");

        AndroidRecoveryEnvelope envelope = JsonSerializer.Deserialize<AndroidRecoveryEnvelope>(
            File.ReadAllBytes(inputPath), JsonOptions) ?? throw new InvalidDataException("Android 恢复包为空");
        if (envelope.Format != "LyoCrystal.AndroidRecovery.v1" || envelope.Iterations != AndroidRecoveryIterations)
            throw new InvalidDataException("Android 恢复包格式或派生参数不受支持");

        byte[] passphrase = ReadAndClearRecoveryPassphrase();
        byte[] salt = Convert.FromBase64String(envelope.Salt);
        byte[] nonce = Convert.FromBase64String(envelope.Nonce);
        byte[] cipher = Convert.FromBase64String(envelope.Ciphertext);
        byte[] tag = Convert.FromBase64String(envelope.Tag);
        byte[] key = Rfc2898DeriveBytes.Pbkdf2(passphrase, salt, envelope.Iterations, HashAlgorithmName.SHA256, 32);
        byte[] plain = new byte[cipher.Length];
        byte[] keyStore = Array.Empty<byte>();
        byte[] password = Array.Empty<byte>();
        byte[] protectedPassword = Array.Empty<byte>();
        try
        {
            using var aes = new AesGcm(key, tag.Length);
            aes.Decrypt(nonce, cipher, tag, plain, Utf8NoBom.GetBytes("LyoCrystal.AndroidRecovery.v1"));
            var payload = ParseAndroidRecoveryPayload(plain);
            keyStore = payload.KeyStore;
            password = payload.Password;
            if (!string.Equals(payload.Purpose, expectedPurpose, StringComparison.Ordinal) ||
                !string.Equals(payload.Alias, expectedAlias, StringComparison.Ordinal))
                throw new InvalidDataException("Android 恢复载荷用途或 alias 不匹配");
            if (keyStore.Length == 0 || password.Length == 0) throw new InvalidDataException("Android 恢复载荷缺少密钥材料");
            protectedPassword = ProtectedData.Protect(
                password,
                SHA256.HashData(Utf8NoBom.GetBytes("LyoCrystal.Release.Secret.v1:" + expectedPurpose)),
                DataProtectionScope.CurrentUser);
            try
            {
                WriteAtomic(keyStoreOutputPath, keyStore);
                WriteAtomic(protectedPasswordOutputPath, protectedPassword);
            }
            catch
            {
                if (File.Exists(keyStoreOutputPath)) File.Delete(keyStoreOutputPath);
                if (File.Exists(protectedPasswordOutputPath)) File.Delete(protectedPasswordOutputPath);
                throw;
            }
            Console.WriteLine($"Android 签名材料已恢复并重新受当前 Windows 用户保护：Purpose={expectedPurpose}；Alias={expectedAlias}。");
        }
        finally
        {
            Environment.SetEnvironmentVariable(AndroidRecoveryPassphraseEnvironment, null);
            CryptographicOperations.ZeroMemory(passphrase);
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plain);
            if (keyStore.Length > 0) CryptographicOperations.ZeroMemory(keyStore);
            if (password.Length > 0) CryptographicOperations.ZeroMemory(password);
            if (protectedPassword.Length > 0) CryptographicOperations.ZeroMemory(protectedPassword);
        }
    }

    private static byte[] ReadAndClearRecoveryPassphrase()
    {
        string passphrase = Environment.GetEnvironmentVariable(AndroidRecoveryPassphraseEnvironment)
            ?? throw new InvalidOperationException($"当前步骤未提供 {AndroidRecoveryPassphraseEnvironment}");
        try
        {
            if (passphrase.Length < 16) throw new InvalidOperationException("Android 恢复口令至少需要 16 个字符");
            return Utf8NoBom.GetBytes(passphrase);
        }
        finally
        {
            Environment.SetEnvironmentVariable(AndroidRecoveryPassphraseEnvironment, null);
            passphrase = string.Empty;
        }
    }

    private static byte[] BuildAndroidRecoveryPayload(string purpose, string alias, byte[] keyStore, byte[] password)
    {
        byte[] purposeBytes = Utf8NoBom.GetBytes(purpose);
        byte[] aliasBytes = Utf8NoBom.GetBytes(alias);
        byte[] magic = Utf8NoBom.GetBytes("LyoCrystal.AndroidRecoveryPayload.v1");
        byte[] payload = new byte[checked(20 + magic.Length + purposeBytes.Length + aliasBytes.Length + keyStore.Length + password.Length)];
        int offset = 0;
        WriteField(payload, ref offset, magic);
        WriteField(payload, ref offset, purposeBytes);
        WriteField(payload, ref offset, aliasBytes);
        WriteField(payload, ref offset, keyStore);
        WriteField(payload, ref offset, password);
        return payload;
    }

    private static (string Purpose, string Alias, byte[] KeyStore, byte[] Password) ParseAndroidRecoveryPayload(byte[] payload)
    {
        int offset = 0;
        byte[] magic = ReadField(payload, ref offset, 128);
        byte[] purpose = ReadField(payload, ref offset, 128);
        byte[] alias = ReadField(payload, ref offset, 128);
        byte[] keyStore = ReadField(payload, ref offset, 16 * 1024 * 1024);
        byte[] password = ReadField(payload, ref offset, 64 * 1024);
        if (offset != payload.Length || Utf8NoBom.GetString(magic) != "LyoCrystal.AndroidRecoveryPayload.v1")
            throw new InvalidDataException("Android 恢复载荷格式不受支持");
        return (Utf8NoBom.GetString(purpose), Utf8NoBom.GetString(alias), keyStore, password);
    }

    private static void WriteField(byte[] target, ref int offset, byte[] value)
    {
        BinaryPrimitives.WriteInt32LittleEndian(target.AsSpan(offset, 4), value.Length);
        offset += 4;
        value.CopyTo(target, offset);
        offset += value.Length;
    }

    private static byte[] ReadField(byte[] source, ref int offset, int maximumLength)
    {
        if (offset > source.Length - 4) throw new InvalidDataException("Android 恢复载荷字段不完整");
        int length = BinaryPrimitives.ReadInt32LittleEndian(source.AsSpan(offset, 4));
        offset += 4;
        if (length < 0 || length > maximumLength || offset > source.Length - length)
            throw new InvalidDataException("Android 恢复载荷字段长度无效");
        byte[] value = source.AsSpan(offset, length).ToArray();
        offset += length;
        return value;
    }

    private static void ValidateAndroidSigningIdentity(string purpose, string alias)
    {
        if (!KeyIdPattern.IsMatch(purpose ?? string.Empty) || !KeyIdPattern.IsMatch(alias ?? string.Empty))
            throw new ArgumentException("APK 签名用途或 alias 无效");
    }

    [SupportedOSPlatform("windows")]
    private static void PublishSignedAndroid(
        string projectPath,
        string keyStorePath,
        string protectedPasswordPath,
        string purpose,
        string alias,
        string logPath)
    {
        projectPath = Path.GetFullPath(projectPath);
        keyStorePath = Path.GetFullPath(keyStorePath);
        logPath = Path.GetFullPath(logPath);
        if (!File.Exists(projectPath) || !File.Exists(keyStorePath))
            throw new FileNotFoundException("Android 项目或签名 keystore 不存在");
        if (!KeyIdPattern.IsMatch(purpose ?? string.Empty) || !KeyIdPattern.IsMatch(alias ?? string.Empty))
            throw new ArgumentException("APK 签名用途或 alias 无效");

        byte[] passwordBytes = LoadProtectedSecret(purpose!, protectedPasswordPath);
        string password = Utf8NoBom.GetString(passwordBytes);
        try
        {
            var start = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = FindRepositoryRoot(Path.GetDirectoryName(projectPath)!),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            string[] arguments =
            [
                "publish", projectPath,
                "-f", "net10.0-android", "-c", "Release", "-r", "android-arm64", "--no-restore",
                "-p:MobileBootstrapAssetMode=Micro", "-p:AndroidPackageFormat=apk", "-p:ArchiveOnBuild=false",
                "-p:RunAOTCompilation=true", "-p:PublishTrimmed=true", "-p:AndroidKeyStore=true",
                "-p:AndroidSigningKeyStore=" + keyStorePath,
                "-p:AndroidSigningKeyAlias=" + alias,
                "-p:AndroidSigningStorePass=env:LYOCRYSTAL_APK_SIGNING_PASSWORD",
                "-p:AndroidSigningKeyPass=env:LYOCRYSTAL_APK_SIGNING_PASSWORD",
                "--verbosity", "minimal",
            ];
            foreach (string argument in arguments) start.ArgumentList.Add(argument);
            start.Environment["LYOCRYSTAL_APK_SIGNING_PASSWORD"] = password;
            using Process process = Process.Start(start) ?? throw new InvalidOperationException("无法启动 Android 签名构建");
            Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
            Task<string> stderrTask = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            Task.WaitAll(stdoutTask, stderrTask);
            string log = stdoutTask.Result + stderrTask.Result;
            WriteAtomic(logPath, Utf8NoBom.GetBytes(log), overwrite: true);
            if (process.ExitCode != 0)
                throw new InvalidOperationException($"Android 签名构建失败，退出码 {process.ExitCode}；详见不含口令的构建日志");
            Console.WriteLine("Android 独立 keystore Release 签名构建完成；口令未写入命令或日志。");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
            password = string.Empty;
        }
    }

    [SupportedOSPlatform("windows")]
    private static byte[] LoadProtectedSecret(string purpose, string path)
    {
        byte[] protectedBytes = File.ReadAllBytes(Path.GetFullPath(path));
        try
        {
            return ProtectedData.Unprotect(
                protectedBytes,
                SHA256.HashData(Utf8NoBom.GetBytes("LyoCrystal.Release.Secret.v1:" + purpose)),
                DataProtectionScope.CurrentUser);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(protectedBytes);
        }
    }

    private static string FindRepositoryRoot(string start)
    {
        DirectoryInfo? current = new(start);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "global.json"))) return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("无法定位仓库根目录");
    }

    [SupportedOSPlatform("windows")]
    private static void ProtectEnvironmentSecret(string purpose, string environmentVariable, string outputPath)
    {
        if (!KeyIdPattern.IsMatch(purpose ?? string.Empty)) throw new ArgumentException("秘密用途无效");
        if (string.IsNullOrWhiteSpace(environmentVariable)) throw new ArgumentException("环境变量名为空");
        outputPath = Path.GetFullPath(outputPath);
        if (File.Exists(outputPath)) throw new IOException("受保护秘密已存在，拒绝覆盖");
        string value = Environment.GetEnvironmentVariable(environmentVariable)
            ?? throw new InvalidOperationException("当前步骤未提供待保护秘密");
        byte[] plain = Utf8NoBom.GetBytes(value);
        byte[] protectedBytes = Array.Empty<byte>();
        try
        {
            protectedBytes = ProtectedData.Protect(
                plain,
                SHA256.HashData(Utf8NoBom.GetBytes("LyoCrystal.Release.Secret.v1:" + purpose)),
                DataProtectionScope.CurrentUser);
            WriteAtomic(outputPath, protectedBytes);
            Console.WriteLine($"发布秘密已写入 Windows DPAPI CurrentUser：Purpose={purpose}。");
        }
        finally
        {
            Environment.SetEnvironmentVariable(environmentVariable, null);
            CryptographicOperations.ZeroMemory(plain);
            if (protectedBytes.Length > 0) CryptographicOperations.ZeroMemory(protectedBytes);
        }
    }

    [SupportedOSPlatform("windows")]
    private static void ProvisionResourceKey(string keyId, string protectedPrivateKeyPath, string publicKeyPath)
    {
        ValidateKeyId(keyId);
        EnsureWindowsDpapi();
        protectedPrivateKeyPath = Path.GetFullPath(protectedPrivateKeyPath);
        publicKeyPath = Path.GetFullPath(publicKeyPath);
        if (File.Exists(protectedPrivateKeyPath) || File.Exists(publicKeyPath))
            throw new IOException("密钥输出已存在；为避免覆盖生产密钥，已拒绝重新生成");

        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        byte[] privateKey = key.ExportPkcs8PrivateKey();
        byte[] protectedKey = Array.Empty<byte>();
        try
        {
            protectedKey = ProtectedData.Protect(privateKey, Entropy(keyId), DataProtectionScope.CurrentUser);
            WriteAtomic(protectedPrivateKeyPath, protectedKey);
            var publicRecord = new ResourceSigningPublicKey
            {
                KeyId = keyId,
                Algorithm = BootstrapManifestSignaturePolicy.Algorithm,
                SubjectPublicKeyInfo = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()),
            };
            WriteAtomic(publicKeyPath, Utf8NoBom.GetBytes(JsonSerializer.Serialize(publicRecord, JsonOptions) + "\n"));
            Console.WriteLine($"资源签名密钥已生成：KeyId={keyId}；私钥仅以 Windows DPAPI CurrentUser 形式落盘。");
        }
        catch
        {
            if (File.Exists(protectedPrivateKeyPath)) File.Delete(protectedPrivateKeyPath);
            if (File.Exists(publicKeyPath)) File.Delete(publicKeyPath);
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(privateKey);
            if (protectedKey.Length > 0) CryptographicOperations.ZeroMemory(protectedKey);
        }
    }

    private static void SignResourceIndex(
        string unsignedIndexPath,
        string signedIndexPath,
        string keyId,
        long sequence,
        Version minimumClientVersion,
        string protectedPrivateKeyPath)
    {
        ValidateKeyId(keyId);
        unsignedIndexPath = Path.GetFullPath(unsignedIndexPath);
        signedIndexPath = Path.GetFullPath(signedIndexPath);
        if (string.Equals(unsignedIndexPath, signedIndexPath, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("签名输出不得覆盖未签名输入");

        UnsignedPackageIndex source = JsonSerializer.Deserialize<UnsignedPackageIndex>(
            File.ReadAllText(unsignedIndexPath, Utf8NoBom), JsonOptions)
            ?? throw new InvalidDataException("未签名资源索引为空");
        if (string.IsNullOrWhiteSpace(source.ResourceVersion) || source.Packages is not { Count: > 0 })
            throw new InvalidDataException("未签名资源索引缺少资源版本或资源包");

        byte[] privateKey = LoadPrivateKey(keyId, protectedPrivateKeyPath);
        try
        {
            using ECDsa signer = ECDsa.Create();
            signer.ImportPkcs8PrivateKey(privateKey, out int bytesRead);
            if (bytesRead != privateKey.Length || signer.KeySize != 256)
                throw new CryptographicException("资源签名私钥必须是完整的 P-256 PKCS#8");

            var manifest = new BootstrapSignedManifest
            {
                Format = BootstrapManifestSignaturePolicy.Format,
                Algorithm = BootstrapManifestSignaturePolicy.Algorithm,
                KeyId = keyId,
                Sequence = sequence,
                GeneratedAtUtc = NormalizeGeneratedAt(source.GeneratedAtUtc),
                ResourceVersion = source.ResourceVersion,
                MinimumClientVersion = minimumClientVersion.ToString(),
                Packages = source.Packages.Select(package => new BootstrapSignedPackage
                {
                    Name = package.Name,
                    Sha256 = package.Sha256,
                    Size = package.Size,
                }).ToList(),
            };
            byte[] payload = BootstrapManifestSignaturePolicy.BuildCanonicalPayload(manifest);
            manifest.Signature = Convert.ToBase64String(signer.SignData(
                payload,
                HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation));

            var trust = new Dictionary<string, BootstrapManifestTrustedKey>(StringComparer.Ordinal)
            {
                [keyId] = new()
                {
                    KeyId = keyId,
                    SubjectPublicKeyInfo = Convert.ToBase64String(signer.ExportSubjectPublicKeyInfo()),
                    NotBeforeSequence = 1,
                },
            };
            string json = JsonSerializer.Serialize(manifest, JsonOptions) + "\n";
            BootstrapManifestVerificationResult verified = BootstrapManifestSignaturePolicy.Verify(
                json, trust, minimumClientVersion);
            if (!verified.IsValid) throw new InvalidDataException("签名后自检失败：" + verified.Error);

            WriteAtomic(signedIndexPath, Utf8NoBom.GetBytes(json));
            Console.WriteLine($"资源索引签名完成：KeyId={keyId}；Sequence={sequence}；Packages={manifest.Packages.Count}。");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(privateKey);
        }
    }

    private static void VerifyResourceIndex(string signedIndexPath, Version currentClientVersion)
    {
        BootstrapManifestVerificationResult result = BootstrapManifestSignaturePolicy.Verify(
            File.ReadAllText(Path.GetFullPath(signedIndexPath), Utf8NoBom),
            BootstrapManifestTrustConfiguration.TrustedKeys,
            currentClientVersion);
        if (!result.IsValid) throw new InvalidDataException(result.Error);
        Console.WriteLine($"资源索引正式信任表验签通过：KeyId={result.Manifest.KeyId}；Sequence={result.Manifest.Sequence}；Packages={result.Manifest.Packages.Count}。");
    }

    private static byte[] LoadPrivateKey(string keyId, string protectedPrivateKeyPath)
    {
        string? environmentValue = Environment.GetEnvironmentVariable(PrivateKeyEnvironment);
        if (protectedPrivateKeyPath == "-")
        {
            if (string.IsNullOrWhiteSpace(environmentValue))
                throw new InvalidOperationException($"缺少当前签名步骤的 {PrivateKeyEnvironment}");
            try
            {
                return Convert.FromBase64String(environmentValue);
            }
            finally
            {
                Environment.SetEnvironmentVariable(PrivateKeyEnvironment, null);
            }
        }

        if (!string.IsNullOrEmpty(environmentValue))
            throw new InvalidOperationException("同时提供环境私钥与 DPAPI 私钥路径，已失败关闭");
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("DPAPI 私钥存储仅支持 Windows");
        return LoadProtectedPrivateKey(keyId, protectedPrivateKeyPath);
    }

    [SupportedOSPlatform("windows")]
    private static byte[] LoadProtectedPrivateKey(string keyId, string protectedPrivateKeyPath)
    {
        EnsureWindowsDpapi();
        byte[] protectedBytes = File.ReadAllBytes(Path.GetFullPath(protectedPrivateKeyPath));
        try
        {
            return ProtectedData.Unprotect(protectedBytes, Entropy(keyId), DataProtectionScope.CurrentUser);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(protectedBytes);
        }
    }

    private static void WriteAtomic(string path, byte[] bytes, bool overwrite = false)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        string temporaryPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, path, overwrite);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static byte[] Entropy(string keyId) =>
        SHA256.HashData(Utf8NoBom.GetBytes("LyoCrystal.Release.ResourceSigning.v1:" + keyId));

    private static void ValidateKeyId(string keyId)
    {
        if (!KeyIdPattern.IsMatch(keyId ?? string.Empty)) throw new ArgumentException("资源签名 Key ID 无效");
    }

    private static void EnsureWindowsDpapi()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("DPAPI 私钥存储仅支持 Windows");
    }

    private static long ParsePositiveSequence(string value) =>
        long.TryParse(value, out long sequence) && sequence > 0
            ? sequence
            : throw new ArgumentException("Sequence 必须是正整数");

    private static Version ParseVersion(string value) =>
        Version.TryParse(value, out Version? version)
            ? version
            : throw new ArgumentException("客户端版本无效");

    private static string NormalizeGeneratedAt(string? value)
    {
        if (DateTimeOffset.TryParse(value, out DateTimeOffset timestamp) && timestamp.Year >= 2020)
            return timestamp.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'");
        return DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'");
    }

    private sealed class UnsignedPackageIndex
    {
        public string? GeneratedAtUtc { get; set; }
        public string? ResourceVersion { get; set; }
        public List<UnsignedPackage> Packages { get; set; } = new();
    }

    private sealed class UnsignedPackage
    {
        public string? Name { get; set; }
        public string? Sha256 { get; set; }
        public long Size { get; set; }
    }

    private sealed class ResourceSigningPublicKey
    {
        public string KeyId { get; set; } = string.Empty;
        public string Algorithm { get; set; } = string.Empty;
        public string SubjectPublicKeyInfo { get; set; } = string.Empty;
    }

    private sealed class AndroidRecoveryEnvelope
    {
        public string Format { get; set; } = string.Empty;
        public int Iterations { get; set; }
        public string Salt { get; set; } = string.Empty;
        public string Nonce { get; set; } = string.Empty;
        public string Ciphertext { get; set; } = string.Empty;
        public string Tag { get; set; } = string.Empty;
    }

}
