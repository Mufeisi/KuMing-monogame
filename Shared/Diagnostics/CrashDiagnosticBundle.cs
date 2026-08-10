using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Shared.Diagnostics;

public sealed class CrashDiagnosticRequest
{
    public string OutputRoot { get; init; } = string.Empty;
    public string Component { get; init; } = string.Empty;
    public string ProductVersion { get; init; } = string.Empty;
    public string ResourceVersionPath { get; init; } = string.Empty;
    public string ResourceVersionFallbackPath { get; init; } = string.Empty;
    public string ResourceVersionFallbackContent { get; init; } = string.Empty;
    public Exception Exception { get; init; }
    public IReadOnlyList<string> LogPaths { get; init; } = Array.Empty<string>();
    public IReadOnlyDictionary<string, string> Configuration { get; init; } =
        new Dictionary<string, string>();
}

public sealed class CrashDiagnosticSummary
{
    public int FormatVersion { get; init; } = 1;
    public DateTimeOffset CapturedAtUtc { get; init; }
    public string Component { get; init; } = string.Empty;
    public string ProductVersion { get; init; } = string.Empty;
    public string ResourceVersion { get; init; } = string.Empty;
    public string ResourceStateSha256 { get; init; } = string.Empty;
    public string ConfigurationSha256 { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, string> Configuration { get; init; } =
        new Dictionary<string, string>();
    public IReadOnlyList<string> LogFiles { get; init; } = Array.Empty<string>();
    public string ExceptionType { get; init; } = string.Empty;
}

/// <summary>OPS-BASIC-02 离线崩溃诊断包：固定大小日志尾部、版本和白名单配置摘要。</summary>
public static class CrashDiagnosticBundle
{
    private const int MaximumTailBytes = 64 * 1024;
    private static readonly ConcurrentDictionary<string, byte> CapturedComponents =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly Regex AssignmentSecretPattern = new(
        @"(?i)\b(password|passwd|pwd|token|access[-_ ]?token|secret|client[-_ ]?secret|api[-_ ]?key|connection[-_ ]?string)\b[\""']?\s*[:=]\s*(?:[\""']([^\""'\r\n]*)[\""']|([^\s,;\]\}\r\n]+))",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex AuthorizationAssignmentPattern = new(
        @"(?i)\bauthorization\b[\""']?\s*[:=]\s*[\""']?[^\s,;\]\}\r\n]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex AuthorizationCredentialPattern = new(
        @"(?i)\b(bearer|basic)\s+[A-Za-z0-9._~+\-/]+=*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex EmailPattern = new(
        @"\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex Ipv4Pattern = new(
        @"(?<!\d)(?:(?:25[0-5]|2[0-4]\d|1?\d?\d)\.){3}(?:25[0-5]|2[0-4]\d|1?\d?\d)(?!\d)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex UserProfilePathPattern = new(
        @"(?i)(?<prefix>(?:[A-Z]:\\Users\\|/Users/|/home/))[^\\/\s]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool TryWriteOnce(CrashDiagnosticRequest request, out string bundlePath, out string error)
    {
        bundlePath = string.Empty;
        error = string.Empty;
        string component = NormalizeComponent(request?.Component);
        if (!CapturedComponents.TryAdd(component, 0))
        {
            error = "当前进程已为该组件生成崩溃诊断包";
            return false;
        }

        try
        {
            bundlePath = Write(request);
            return true;
        }
        catch (Exception exception)
        {
            CapturedComponents.TryRemove(component, out _);
            error = exception.Message;
            return false;
        }
    }

    public static string Write(CrashDiagnosticRequest request)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));
        if (string.IsNullOrWhiteSpace(request.OutputRoot))
            throw new ArgumentException("诊断包输出目录不能为空", nameof(request));

        string component = NormalizeComponent(request.Component);
        string root = Path.GetFullPath(request.OutputRoot);
        Directory.CreateDirectory(root);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string name = $"{now:yyyyMMdd-HHmmssfff}-{component}-{Guid.NewGuid():N}";
        string partial = Path.Combine(root, "." + name + ".partial");
        string published = Path.Combine(root, name);
        Directory.CreateDirectory(partial);

        try
        {
            string resourceVersion = ReadResourceVersion(request, out string resourceSha256);
            SortedDictionary<string, string> configuration = NormalizeConfiguration(request.Configuration);
            string configurationJson = JsonSerializer.Serialize(configuration);
            string configurationSha256 = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(configurationJson)));
            var copiedLogs = new List<string>();
            string logsDirectory = Path.Combine(partial, "logs");

            foreach (string logPath in (request.LogPaths ?? Array.Empty<string>())
                         .Where(path => !string.IsNullOrWhiteSpace(path))
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!File.Exists(logPath)) continue;
                Directory.CreateDirectory(logsDirectory);
                string fileName = SafeFileName(Path.GetFileName(logPath));
                string relative = Path.Combine("logs", copiedLogs.Count.ToString("D2") + "-" + fileName + ".tail.txt");
                WriteTextFlushed(Path.Combine(partial, relative),
                    LimitUtf8Tail(Redact(ReadTail(logPath)), MaximumTailBytes));
                copiedLogs.Add(relative.Replace('\\', '/'));
            }

            WriteTextFlushed(Path.Combine(partial, "exception.txt"),
                Redact(request.Exception?.ToString() ?? "unknown"));
            var summary = new CrashDiagnosticSummary
            {
                CapturedAtUtc = now,
                Component = component,
                ProductVersion = request.ProductVersion?.Trim() ?? string.Empty,
                ResourceVersion = resourceVersion,
                ResourceStateSha256 = resourceSha256,
                ConfigurationSha256 = configurationSha256,
                Configuration = configuration,
                LogFiles = copiedLogs,
                ExceptionType = request.Exception?.GetType().FullName ?? "unknown",
            };
            WriteTextFlushed(Path.Combine(partial, "summary.json"), JsonSerializer.Serialize(summary, JsonOptions));
            Directory.Move(partial, published);
            return published;
        }
        catch
        {
            try { Directory.Delete(partial, recursive: true); } catch { }
            throw;
        }
    }

    private static string ReadTail(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        long start = Math.Max(0, stream.Length - MaximumTailBytes);
        stream.Position = start;
        byte[] bytes = new byte[checked((int)(stream.Length - start))];
        int read = 0;
        while (read < bytes.Length)
        {
            int count = stream.Read(bytes, read, bytes.Length - read);
            if (count <= 0) break;
            read += count;
        }
        return Encoding.UTF8.GetString(bytes, 0, read);
    }

    private static string ReadResourceVersion(CrashDiagnosticRequest request, out string sha256)
    {
        sha256 = string.Empty;
        string[] candidates = { request.ResourceVersionPath, request.ResourceVersionFallbackPath };
        foreach (string path in candidates)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) continue;
            byte[] bytes = File.ReadAllBytes(path);
            string version = ReadResourceVersion(bytes);
            if (string.IsNullOrWhiteSpace(version)) continue;
            sha256 = Convert.ToHexString(SHA256.HashData(bytes));
            return version;
        }

        if (!string.IsNullOrWhiteSpace(request.ResourceVersionFallbackContent))
        {
            byte[] bytes = Encoding.UTF8.GetBytes(request.ResourceVersionFallbackContent);
            string version = ReadResourceVersion(bytes);
            if (!string.IsNullOrWhiteSpace(version))
            {
                sha256 = Convert.ToHexString(SHA256.HashData(bytes));
                return version;
            }
        }

        return "unavailable";
    }

    private static string ReadResourceVersion(byte[] bytes)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(bytes);
            return FindVersion(document.RootElement);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string FindVersion(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if ((property.NameEquals("ResourceVersion") || property.NameEquals("resourceVersion") ||
                     property.NameEquals("manifestVersion")) && property.Value.ValueKind == JsonValueKind.String)
                    return property.Value.GetString() ?? string.Empty;
                string nested = FindVersion(property.Value);
                if (!string.IsNullOrWhiteSpace(nested)) return nested;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
            {
                string nested = FindVersion(item);
                if (!string.IsNullOrWhiteSpace(nested)) return nested;
            }
        }
        return string.Empty;
    }

    private static SortedDictionary<string, string> NormalizeConfiguration(
        IReadOnlyDictionary<string, string> configuration)
    {
        var normalized = new SortedDictionary<string, string>(StringComparer.Ordinal);
        if (configuration == null) return normalized;
        foreach ((string key, string value) in configuration)
        {
            if (string.IsNullOrWhiteSpace(key)) continue;
            string safeKey = key.Trim();
            if (safeKey.Length > 128) safeKey = safeKey[..128];
            string safeValue = Redact(value?.Trim() ?? string.Empty);
            if (safeValue.Length > 512) safeValue = safeValue[..512];
            normalized[safeKey] = safeValue;
        }
        return normalized;
    }

    private static string Redact(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        string redacted = AuthorizationCredentialPattern.Replace(value, match => $"{match.Groups[1].Value} ***");
        redacted = AuthorizationAssignmentPattern.Replace(redacted, "authorization=***");
        redacted = AssignmentSecretPattern.Replace(redacted, match => $"{match.Groups[1].Value}=***");
        redacted = EmailPattern.Replace(redacted, "***@***");
        redacted = Ipv4Pattern.Replace(redacted, "***.***.***.***");
        return UserProfilePathPattern.Replace(redacted, "${prefix}***");
    }

    private static string LimitUtf8Tail(string value, int maximumBytes)
    {
        if (string.IsNullOrEmpty(value) || Encoding.UTF8.GetByteCount(value) <= maximumBytes)
            return value ?? string.Empty;

        int low = 0;
        int high = value.Length;
        while (low < high)
        {
            int middle = low + ((high - low) / 2);
            if (Encoding.UTF8.GetByteCount(value.AsSpan(middle)) > maximumBytes)
                low = middle + 1;
            else
                high = middle;
        }

        int start = low;
        if (start < value.Length && char.IsLowSurrogate(value[start])) start++;
        return value[start..];
    }

    private static string NormalizeComponent(string value)
    {
        string component = SafeFileName(value);
        return string.IsNullOrWhiteSpace(component) ? "unknown" : component;
    }

    private static string SafeFileName(string value)
    {
        string safe = string.IsNullOrWhiteSpace(value) ? "unknown" : value.Trim();
        foreach (char invalid in Path.GetInvalidFileNameChars()) safe = safe.Replace(invalid, '_');
        return safe.Length <= 80 ? safe : safe[..80];
    }

    private static void WriteTextFlushed(string path, string text)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 4096,
            FileOptions.WriteThrough);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, leaveOpen: true);
        writer.Write(text ?? string.Empty);
        writer.Flush();
        stream.Flush(true);
    }
}
