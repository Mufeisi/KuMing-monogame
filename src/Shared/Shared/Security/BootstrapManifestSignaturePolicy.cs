using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Shared.Security;

public static class BootstrapManifestSignaturePolicy
{
    public const string Format = "lyocrystal-bootstrap-index-v1";
    public const string Algorithm = "ECDSA_P256_SHA256_P1363";
    public const int MaximumJsonBytes = 8 * 1024 * 1024;
    public const int MaximumPackages = 4096;

    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("LyoCrystalBootstrapIndex\0");
    private static readonly Regex KeyIdPattern = new("^[A-Za-z0-9._-]{1,64}$", RegexOptions.CultureInvariant);
    private static readonly Regex ResourceVersionPattern = new("^[A-Za-z0-9._-]{1,128}$", RegexOptions.CultureInvariant);
    private static readonly Regex PackageNamePattern = new("^[A-Za-z0-9._-]{1,128}$", RegexOptions.CultureInvariant);
    private static readonly Regex Sha256Pattern = new("^[0-9a-f]{64}$", RegexOptions.CultureInvariant);
    public static BootstrapManifestVerificationResult Verify(
        string json,
        IReadOnlyDictionary<string, BootstrapManifestTrustedKey> trustedKeys,
        Version currentClientVersion,
        BootstrapManifestAcceptedState acceptedState = null)
    {
        if (string.IsNullOrWhiteSpace(json))
            return BootstrapManifestVerificationResult.Reject("签名资源索引为空");
        if (Encoding.UTF8.GetByteCount(json) > MaximumJsonBytes)
            return BootstrapManifestVerificationResult.Reject("签名资源索引超过 8 MiB 上限");
        if (trustedKeys == null || trustedKeys.Count == 0)
            return BootstrapManifestVerificationResult.Reject("客户端未配置可信资源签名公钥");
        if (currentClientVersion == null)
            return BootstrapManifestVerificationResult.Reject("客户端兼容版本为空");

        BootstrapSignedManifest manifest;
        try
        {
            using JsonDocument document = JsonDocument.Parse(json.TrimStart('\uFEFF'));
            if (ContainsDuplicateProperty(document.RootElement))
                return BootstrapManifestVerificationResult.Reject("签名资源索引 JSON 包含重复字段");
            manifest = document.RootElement.Deserialize(BootstrapManifestJsonContext.Default.BootstrapSignedManifest);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return BootstrapManifestVerificationResult.Reject("签名资源索引 JSON 无效");
        }

        string validationError = ValidateManifest(manifest, currentClientVersion, acceptedState);
        if (validationError != null)
            return BootstrapManifestVerificationResult.Reject(validationError);

        if (!trustedKeys.TryGetValue(manifest.KeyId, out BootstrapManifestTrustedKey trustedKey) || trustedKey == null)
            return BootstrapManifestVerificationResult.Reject("资源签名 Key ID 不受信任");
        if (!string.Equals(trustedKey.KeyId, manifest.KeyId, StringComparison.Ordinal))
            return BootstrapManifestVerificationResult.Reject("可信密钥表的 Key ID 不一致");
        if (trustedKey.NotBeforeSequence <= 0 ||
            trustedKey.NotAfterSequence > 0 && trustedKey.NotAfterSequence < trustedKey.NotBeforeSequence)
            return BootstrapManifestVerificationResult.Reject("可信资源签名密钥的序列窗口无效");
        if (manifest.Sequence < trustedKey.NotBeforeSequence ||
            trustedKey.NotAfterSequence > 0 && manifest.Sequence > trustedKey.NotAfterSequence)
            return BootstrapManifestVerificationResult.Reject("资源签名密钥不在当前序列的有效轮换窗口内");

        byte[] signature;
        byte[] publicKey;
        try
        {
            signature = Convert.FromBase64String(manifest.Signature);
            publicKey = Convert.FromBase64String(trustedKey.SubjectPublicKeyInfo);
        }
        catch (FormatException)
        {
            return BootstrapManifestVerificationResult.Reject("资源签名或可信公钥不是有效 Base64");
        }

        if (signature.Length != 64)
            return BootstrapManifestVerificationResult.Reject("ECDSA P-256 P1363 签名必须为 64 字节");

        byte[] payload;
        try
        {
            payload = BuildCanonicalPayload(manifest);
            string payloadSha256 = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
            if (acceptedState != null &&
                manifest.Sequence == acceptedState.Sequence &&
                !string.Equals(payloadSha256, acceptedState.CanonicalPayloadSha256, StringComparison.Ordinal))
                return BootstrapManifestVerificationResult.Reject("资源索引复用了已接受序列但签名载荷不同");
            using ECDsa ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(publicKey, out int bytesRead);
            if (bytesRead != publicKey.Length || ecdsa.KeySize != 256)
                return BootstrapManifestVerificationResult.Reject("可信资源签名公钥不是完整的 P-256 SPKI");
            if (!ecdsa.VerifyData(payload, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
                return BootstrapManifestVerificationResult.Reject("资源索引签名验证失败");
        }
        catch (CryptographicException)
        {
            return BootstrapManifestVerificationResult.Reject("可信资源签名公钥无效");
        }

        return BootstrapManifestVerificationResult.Accept(manifest, payload);
    }

    public static byte[] BuildCanonicalPayload(BootstrapSignedManifest manifest)
    {
        if (manifest == null) throw new ArgumentNullException(nameof(manifest));

        using var stream = new MemoryStream();
        stream.Write(Magic);
        WriteInt32(stream, 1);
        WriteInt64(stream, manifest.Sequence);
        WriteString(stream, manifest.Format);
        WriteString(stream, manifest.Algorithm);
        WriteString(stream, manifest.KeyId);
        WriteString(stream, manifest.ResourceVersion);
        WriteString(stream, NormalizeTimestamp(manifest.GeneratedAtUtc));
        WriteString(stream, manifest.MinimumClientVersion);

        BootstrapSignedPackage[] packages = (manifest.Packages ?? new List<BootstrapSignedPackage>())
            .OrderBy(item => item.Name, StringComparer.Ordinal)
            .ToArray();
        WriteInt32(stream, packages.Length);
        foreach (BootstrapSignedPackage package in packages)
        {
            WriteString(stream, package.Name);
            WriteString(stream, package.Sha256.ToLowerInvariant());
            WriteInt64(stream, package.Size);
        }

        return stream.ToArray();
    }

    private static string ValidateManifest(
        BootstrapSignedManifest manifest,
        Version currentClientVersion,
        BootstrapManifestAcceptedState acceptedState)
    {
        if (manifest == null) return "签名资源索引内容为空";
        if (!string.Equals(manifest.Format, Format, StringComparison.Ordinal)) return "资源索引签名格式不受支持";
        if (!string.Equals(manifest.Algorithm, Algorithm, StringComparison.Ordinal)) return "资源索引签名算法不受支持";
        if (!KeyIdPattern.IsMatch(manifest.KeyId ?? string.Empty)) return "资源签名 Key ID 无效";
        if (manifest.Sequence <= 0) return "资源索引单调序列必须大于零";
        if (!ResourceVersionPattern.IsMatch(manifest.ResourceVersion ?? string.Empty))
            return "资源版本无效";
        if (!TryNormalizeTimestamp(manifest.GeneratedAtUtc, out _)) return "资源索引生成时间必须是 UTC RFC3339 时间";
        if (!Version.TryParse(manifest.MinimumClientVersion, out Version minimumClientVersion))
            return "最低兼容客户端版本无效";
        if (CompareVersions(currentClientVersion, minimumClientVersion) < 0)
            return $"客户端版本低于资源索引要求的最低版本 {minimumClientVersion}";
        if (manifest.Packages == null || manifest.Packages.Count == 0 || manifest.Packages.Count > MaximumPackages)
            return "资源包数量超出允许范围";

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (BootstrapSignedPackage package in manifest.Packages)
        {
            if (package == null || !PackageNamePattern.IsMatch(package.Name ?? string.Empty)) return "资源包名称无效";
            if (!names.Add(package.Name)) return "资源索引包含重复资源包名称";
            if (!Sha256Pattern.IsMatch(package.Sha256 ?? string.Empty)) return "资源包 SHA-256 必须为 64 位小写十六进制";
            if (package.Size < 0) return "资源包大小不得为负数";
        }

        if (string.IsNullOrWhiteSpace(manifest.Signature) || manifest.Signature.Length > 128)
            return "资源索引签名缺失或过长";

        if (acceptedState != null && acceptedState.Sequence > 0)
        {
            if (manifest.Sequence < acceptedState.Sequence) return "资源索引序列低于已接受版本，拒绝降级";
            if (manifest.Sequence == acceptedState.Sequence &&
                !string.Equals(manifest.ResourceVersion, acceptedState.ResourceVersion, StringComparison.Ordinal))
                return "资源索引复用了已接受序列但资源版本不同";
        }

        return null;
    }

    private static string NormalizeTimestamp(string value)
    {
        if (!TryNormalizeTimestamp(value, out string normalized))
            throw new ArgumentException("生成时间无效", nameof(value));
        return normalized;
    }

    private static int CompareVersions(Version left, Version right)
    {
        int comparison = left.Major.CompareTo(right.Major);
        if (comparison != 0) return comparison;
        comparison = left.Minor.CompareTo(right.Minor);
        if (comparison != 0) return comparison;
        comparison = Math.Max(0, left.Build).CompareTo(Math.Max(0, right.Build));
        if (comparison != 0) return comparison;
        return Math.Max(0, left.Revision).CompareTo(Math.Max(0, right.Revision));
    }

    internal static bool ContainsDuplicateProperty(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (!names.Add(property.Name) || ContainsDuplicateProperty(property.Value)) return true;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
            {
                if (ContainsDuplicateProperty(item)) return true;
            }
        }
        return false;
    }

    private static bool TryNormalizeTimestamp(string value, out string normalized)
    {
        normalized = string.Empty;
        if (!DateTimeOffset.TryParseExact(
                value,
                new[] { "yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'" },
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                out DateTimeOffset timestamp))
            return false;
        normalized = timestamp.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", System.Globalization.CultureInfo.InvariantCulture);
        return true;
    }

    private static void WriteString(Stream stream, string value)
    {
        byte[] bytes = new UTF8Encoding(false, true).GetBytes(value ?? string.Empty);
        WriteInt32(stream, bytes.Length);
        stream.Write(bytes);
    }

    private static void WriteInt32(Stream stream, int value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void WriteInt64(Stream stream, long value)
    {
        Span<byte> bytes = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(bytes, value);
        stream.Write(bytes);
    }
}

public static class BootstrapManifestTrustConfiguration
{
    public static Version CurrentClientCompatibilityVersion { get; } = new(1, 0, 0);

    // RELEASE-01 生产信任表：私钥不在仓库；当前与下一把公钥用重叠序列窗口完成轮换。
    public static IReadOnlyDictionary<string, BootstrapManifestTrustedKey> TrustedKeys { get; } =
        new Dictionary<string, BootstrapManifestTrustedKey>(StringComparer.Ordinal)
        {
            ["resource-2026-a"] = new()
            {
                KeyId = "resource-2026-a",
                SubjectPublicKeyInfo = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEmZm1E/xwt7JUVS670s+x0OhqJz382Usxf52x1gJXFuJsM6AWC615Eu0hp9zWt5DvQ3X0g/tMxoACDSY8Vu6kpg==",
                NotBeforeSequence = 1,
                NotAfterSequence = 999_999,
            },
            ["resource-2026-b"] = new()
            {
                KeyId = "resource-2026-b",
                SubjectPublicKeyInfo = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEAFr2FvbF+GNBaHsk57c9O3UQr8IX/rbLJPoUj5yySHT5m1VDCV91wC7W5kfCdKPckOiy6JMUxgHfskNmIV+JXw==",
                NotBeforeSequence = 900_000,
                NotAfterSequence = 0,
            },
        };
}

public static partial class BootstrapManifestAcceptanceStore
{
    private static readonly object Gate = new();
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    public static BootstrapSignedManifest VerifyForAcceptance(
        string json,
        string statePath,
        IReadOnlyDictionary<string, BootstrapManifestTrustedKey> trustedKeys = null,
        Version currentClientVersion = null)
    {
        if (string.IsNullOrWhiteSpace(statePath)) throw new ArgumentException("防降级状态路径不能为空", nameof(statePath));
        lock (Gate)
        {
            IReadOnlyDictionary<string, BootstrapManifestTrustedKey> resolvedKeys = trustedKeys ?? BootstrapManifestTrustConfiguration.TrustedKeys;
            Version resolvedVersion = currentClientVersion ?? BootstrapManifestTrustConfiguration.CurrentClientCompatibilityVersion;
            BootstrapManifestSecurityState state = LoadState(statePath, resolvedKeys, resolvedVersion);
            BootstrapManifestAcceptedState acceptedState = state.Sequence > 0 ? new BootstrapManifestAcceptedState
            {
                Sequence = state.Sequence,
                ResourceVersion = state.ResourceVersion,
                CanonicalPayloadSha256 = state.CanonicalPayloadSha256,
            } : null;
            BootstrapManifestVerificationResult result = BootstrapManifestSignaturePolicy.Verify(json, resolvedKeys, resolvedVersion, acceptedState);
            if (!result.IsValid) throw new InvalidDataException(result.Error);
            return result.Manifest;
        }
    }

    public static string ReadAcceptedManifestJson(
        string statePath,
        IReadOnlyDictionary<string, BootstrapManifestTrustedKey> trustedKeys = null,
        Version currentClientVersion = null)
    {
        if (string.IsNullOrWhiteSpace(statePath)) throw new ArgumentException("防降级状态路径不能为空", nameof(statePath));
        lock (Gate)
        {
            BootstrapManifestSecurityState state = LoadState(
                statePath,
                trustedKeys ?? BootstrapManifestTrustConfiguration.TrustedKeys,
                currentClientVersion ?? BootstrapManifestTrustConfiguration.CurrentClientCompatibilityVersion);
            if (state.Sequence <= 0 || string.IsNullOrWhiteSpace(state.ManifestJson))
                throw new InvalidDataException("尚无已接受的签名资源清单");
            return state.ManifestJson;
        }
    }

    public static BootstrapSignedManifest VerifyAndAccept(
        string json,
        string statePath,
        IReadOnlyDictionary<string, BootstrapManifestTrustedKey> trustedKeys = null,
        Version currentClientVersion = null)
    {
        if (string.IsNullOrWhiteSpace(statePath)) throw new ArgumentException("防降级状态路径不能为空", nameof(statePath));
        lock (Gate)
        {
            IReadOnlyDictionary<string, BootstrapManifestTrustedKey> resolvedKeys =
                trustedKeys ?? BootstrapManifestTrustConfiguration.TrustedKeys;
            Version resolvedVersion = currentClientVersion ?? BootstrapManifestTrustConfiguration.CurrentClientCompatibilityVersion;
            BootstrapManifestSecurityState state = LoadState(statePath, resolvedKeys, resolvedVersion);
            BootstrapManifestAcceptedState acceptedState = state.Sequence > 0
                ? new BootstrapManifestAcceptedState
                {
                    Sequence = state.Sequence,
                    ResourceVersion = state.ResourceVersion,
                    CanonicalPayloadSha256 = state.CanonicalPayloadSha256,
                }
                : null;

            BootstrapManifestVerificationResult result = BootstrapManifestSignaturePolicy.Verify(
                json,
                resolvedKeys,
                resolvedVersion,
                acceptedState);
            if (!result.IsValid)
                throw new InvalidDataException(result.Error);

            var nextState = new BootstrapManifestSecurityState
            {
                Sequence = result.Manifest.Sequence,
                ResourceVersion = result.Manifest.ResourceVersion,
                KeyId = result.Manifest.KeyId,
                CanonicalPayloadSha256 = HashPayload(result.CanonicalPayload),
                ManifestJson = json.TrimStart('\uFEFF'),
                AcceptedAtUtc = DateTime.UtcNow.ToString("o"),
            };
            WriteState(statePath, nextState);
            EnsureMarker(statePath, nextState);
            return result.Manifest;
        }
    }

    public static bool IsAcceptedResourceVersion(
        string statePath,
        string resourceVersion,
        IReadOnlyDictionary<string, BootstrapManifestTrustedKey> trustedKeys = null,
        Version currentClientVersion = null)
    {
        if (string.IsNullOrWhiteSpace(statePath) || string.IsNullOrWhiteSpace(resourceVersion)) return false;
        lock (Gate)
        {
            try
            {
                BootstrapManifestSecurityState state = LoadState(
                    statePath,
                    trustedKeys ?? BootstrapManifestTrustConfiguration.TrustedKeys,
                    currentClientVersion ?? BootstrapManifestTrustConfiguration.CurrentClientCompatibilityVersion);
                return state.Sequence > 0 && string.Equals(state.ResourceVersion, resourceVersion, StringComparison.Ordinal);
            }
            catch (InvalidDataException)
            {
                return false;
            }
        }
    }

    public static bool IsAcceptedManifest(
        string statePath,
        string manifestJson,
        IReadOnlyDictionary<string, BootstrapManifestTrustedKey> trustedKeys = null,
        Version currentClientVersion = null)
    {
        if (string.IsNullOrWhiteSpace(statePath) || string.IsNullOrWhiteSpace(manifestJson)) return false;
        lock (Gate)
        {
            try
            {
                IReadOnlyDictionary<string, BootstrapManifestTrustedKey> resolvedKeys =
                    trustedKeys ?? BootstrapManifestTrustConfiguration.TrustedKeys;
                Version resolvedVersion = currentClientVersion ?? BootstrapManifestTrustConfiguration.CurrentClientCompatibilityVersion;
                BootstrapManifestSecurityState state = LoadState(statePath, resolvedKeys, resolvedVersion);
                if (state.Sequence <= 0) return false;

                var acceptedState = new BootstrapManifestAcceptedState
                {
                    Sequence = state.Sequence,
                    ResourceVersion = state.ResourceVersion,
                    CanonicalPayloadSha256 = state.CanonicalPayloadSha256,
                };
                BootstrapManifestVerificationResult result = BootstrapManifestSignaturePolicy.Verify(
                    manifestJson,
                    resolvedKeys,
                    resolvedVersion,
                    acceptedState);
                return result.IsValid &&
                       result.Manifest.Sequence == state.Sequence &&
                       string.Equals(result.Manifest.ResourceVersion, state.ResourceVersion, StringComparison.Ordinal) &&
                       string.Equals(HashPayload(result.CanonicalPayload), state.CanonicalPayloadSha256, StringComparison.OrdinalIgnoreCase);
            }
            catch (InvalidDataException)
            {
                return false;
            }
        }
    }

    public static bool IsAuthorizedUpdateQueue(
        string statePath,
        string resourceVersion,
        IEnumerable<BootstrapManifestAuthorizedPackage> packages,
        IReadOnlyDictionary<string, BootstrapManifestTrustedKey> trustedKeys = null,
        Version currentClientVersion = null)
    {
        if (string.IsNullOrWhiteSpace(statePath) || string.IsNullOrWhiteSpace(resourceVersion) || packages == null) return false;
        lock (Gate)
        {
            try
            {
                BootstrapManifestSecurityState state = LoadState(
                    statePath,
                    trustedKeys ?? BootstrapManifestTrustConfiguration.TrustedKeys,
                    currentClientVersion ?? BootstrapManifestTrustConfiguration.CurrentClientCompatibilityVersion);
                if (state.VerifiedManifest == null ||
                    !string.Equals(state.ResourceVersion, resourceVersion, StringComparison.Ordinal)) return false;

                var authorized = state.VerifiedManifest.Packages.ToDictionary(
                    item => item.Name,
                    item => item.Sha256,
                    StringComparer.OrdinalIgnoreCase);
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (BootstrapManifestAuthorizedPackage package in packages)
                {
                    if (package == null || string.IsNullOrWhiteSpace(package.Name) || !names.Add(package.Name)) return false;
                    if (!authorized.TryGetValue(package.Name, out string sha256) ||
                        !string.Equals(sha256, package.Sha256, StringComparison.Ordinal)) return false;
                }
                return true;
            }
            catch (InvalidDataException)
            {
                return false;
            }
        }
    }

    private static BootstrapManifestSecurityState LoadState(
        string statePath,
        IReadOnlyDictionary<string, BootstrapManifestTrustedKey> trustedKeys,
        Version currentClientVersion)
    {
        string markerPath = GetMarkerPath(statePath);
        if (!File.Exists(statePath))
        {
            if (File.Exists(markerPath))
                throw new InvalidDataException("资源签名防降级状态在当前安装中丢失");
            return new BootstrapManifestSecurityState();
        }
        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(statePath));
            if (BootstrapManifestSignaturePolicy.ContainsDuplicateProperty(document.RootElement))
                throw new InvalidDataException("资源签名防降级状态包含重复字段");
            BootstrapManifestSecurityState state = document.RootElement.Deserialize(AcceptanceStateJsonContext.Default.BootstrapManifestSecurityState)
                ?? throw new InvalidDataException("资源签名防降级状态为空");
            if (state.Sequence <= 0 ||
                !Regex.IsMatch(state.ResourceVersion ?? string.Empty, "^[A-Za-z0-9._-]{1,128}$", RegexOptions.CultureInvariant) ||
                !Regex.IsMatch(state.KeyId ?? string.Empty, "^[A-Za-z0-9._-]{1,64}$", RegexOptions.CultureInvariant) ||
                !Regex.IsMatch(state.CanonicalPayloadSha256 ?? string.Empty, "^[0-9a-f]{64}$", RegexOptions.CultureInvariant) ||
                string.IsNullOrWhiteSpace(state.ManifestJson))
                throw new InvalidDataException("资源签名防降级状态内容无效");

            BootstrapManifestVerificationResult stored = BootstrapManifestSignaturePolicy.Verify(
                state.ManifestJson,
                trustedKeys,
                currentClientVersion);
            if (!stored.IsValid ||
                stored.Manifest.Sequence != state.Sequence ||
                !string.Equals(stored.Manifest.ResourceVersion, state.ResourceVersion, StringComparison.Ordinal) ||
                !string.Equals(stored.Manifest.KeyId, state.KeyId, StringComparison.Ordinal) ||
                !string.Equals(HashPayload(stored.CanonicalPayload), state.CanonicalPayloadSha256, StringComparison.Ordinal))
                throw new InvalidDataException("资源签名防降级状态与已验签清单不一致");
            state.VerifiedManifest = stored.Manifest;
            EnsureMarker(statePath, state);
            return state;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new InvalidDataException("资源签名防降级状态无法读取", ex);
        }
    }

    private static string HashPayload(byte[] payload) =>
        Convert.ToHexString(SHA256.HashData(payload ?? Array.Empty<byte>())).ToLowerInvariant();

    private static string GetMarkerPath(string statePath) => statePath + ".initialized";

    private static void EnsureMarker(string statePath, BootstrapManifestSecurityState state)
    {
        string markerPath = GetMarkerPath(statePath);
        if (File.Exists(markerPath))
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(markerPath));
            if (BootstrapManifestSignaturePolicy.ContainsDuplicateProperty(document.RootElement))
                throw new InvalidDataException("资源签名防降级安装标记包含重复字段");
            BootstrapManifestInstallMarker marker = document.RootElement.Deserialize(AcceptanceStateJsonContext.Default.BootstrapManifestInstallMarker)
                ?? throw new InvalidDataException("资源签名防降级安装标记为空");
            if (marker.Sequence <= 0 ||
                !Regex.IsMatch(marker.CanonicalPayloadSha256 ?? string.Empty, "^[0-9a-f]{64}$", RegexOptions.CultureInvariant))
                throw new InvalidDataException("资源签名防降级安装标记无效");
            if (marker.Sequence > state.Sequence)
                throw new InvalidDataException("资源签名防降级状态低于当前安装版本地板");
            if (marker.Sequence == state.Sequence)
            {
                if (!string.Equals(marker.CanonicalPayloadSha256, state.CanonicalPayloadSha256, StringComparison.Ordinal))
                    throw new InvalidDataException("资源签名防降级状态与安装标记摘要不一致");
                return;
            }
        }

        WriteMarker(markerPath, state);
    }

    private static void WriteMarker(string markerPath, BootstrapManifestSecurityState state)
    {
        string temporaryPath = markerPath + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(new BootstrapManifestInstallMarker
            {
                Sequence = state.Sequence,
                CanonicalPayloadSha256 = state.CanonicalPayloadSha256,
            }, AcceptanceStateJsonContext.Default.BootstrapManifestInstallMarker), Utf8NoBom);
            File.Move(temporaryPath, markerPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static void WriteState(string statePath, BootstrapManifestSecurityState state)
    {
        string directory = Path.GetDirectoryName(statePath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        string temporaryPath = statePath + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(state, AcceptanceStateJsonContext.Default.BootstrapManifestSecurityState), Utf8NoBom);
            File.Move(temporaryPath, statePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private sealed class BootstrapManifestSecurityState
    {
        public long Sequence { get; set; }
        public string ResourceVersion { get; set; }
        public string KeyId { get; set; }
        public string CanonicalPayloadSha256 { get; set; }
        public string ManifestJson { get; set; }
        public string AcceptedAtUtc { get; set; }
        [System.Text.Json.Serialization.JsonIgnore]
        public BootstrapSignedManifest VerifiedManifest { get; set; }
    }

    private sealed class BootstrapManifestInstallMarker
    {
        public long Sequence { get; set; }
        public string CanonicalPayloadSha256 { get; set; }
    }

    [System.Text.Json.Serialization.JsonSourceGenerationOptions(
        PropertyNameCaseInsensitive = false,
        WriteIndented = true,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
    [System.Text.Json.Serialization.JsonSerializable(typeof(BootstrapManifestSecurityState))]
    [System.Text.Json.Serialization.JsonSerializable(typeof(BootstrapManifestInstallMarker))]
    private sealed partial class AcceptanceStateJsonContext : System.Text.Json.Serialization.JsonSerializerContext;
}

public sealed class BootstrapManifestAuthorizedPackage
{
    public string Name { get; init; }
    public string Sha256 { get; init; }
}

public static class BootstrapSignedPackageHashPolicy
{
    private static readonly Regex Sha256Pattern = new("^[0-9a-f]{64}$", RegexOptions.CultureInvariant);

    public static string VerifyFile(string filePath, string expectedSha256)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            throw new FileNotFoundException("待校验资源包不存在", filePath);
        if (!Sha256Pattern.IsMatch(expectedSha256 ?? string.Empty))
            throw new InvalidDataException("已签名资源包 SHA-256 无效");

        using FileStream stream = File.OpenRead(filePath);
        string actualSha256 = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        if (!string.Equals(actualSha256, expectedSha256, StringComparison.Ordinal))
            throw new InvalidDataException("资源包 SHA-256 与已签名清单不一致");
        return actualSha256;
    }
}

public sealed class BootstrapSignedManifest
{
    public string Format { get; set; }
    public string Algorithm { get; set; }
    public string KeyId { get; set; }
    public long Sequence { get; set; }
    public string GeneratedAtUtc { get; set; }
    public string ResourceVersion { get; set; }
    public string MinimumClientVersion { get; set; }
    public List<BootstrapSignedPackage> Packages { get; set; } = new();
    public string Signature { get; set; }
}

public sealed class BootstrapSignedPackage
{
    public string Name { get; set; }
    public string Sha256 { get; set; }
    public long Size { get; set; }
}

public sealed class BootstrapManifestTrustedKey
{
    public string KeyId { get; init; }
    public string SubjectPublicKeyInfo { get; init; }
    public long NotBeforeSequence { get; init; } = 1;
    public long NotAfterSequence { get; init; }
}

public sealed class BootstrapManifestAcceptedState
{
    public long Sequence { get; init; }
    public string ResourceVersion { get; init; }
    public string CanonicalPayloadSha256 { get; init; }
}

public sealed class BootstrapManifestVerificationResult
{
    public bool IsValid { get; private init; }
    public string Error { get; private init; }
    public BootstrapSignedManifest Manifest { get; private init; }
    public byte[] CanonicalPayload { get; private init; }

    internal static BootstrapManifestVerificationResult Reject(string error) => new()
    {
        IsValid = false,
        Error = error ?? "资源索引签名验证失败",
    };

    internal static BootstrapManifestVerificationResult Accept(BootstrapSignedManifest manifest, byte[] payload) => new()
    {
        IsValid = true,
        Manifest = manifest,
        CanonicalPayload = payload,
    };
}
