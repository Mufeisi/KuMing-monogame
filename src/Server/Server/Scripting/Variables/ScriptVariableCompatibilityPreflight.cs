using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Server.Scripting.Variables
{
    public enum ScriptVariableCompatibilityMode
    {
        LegacyCurrent,
        Audit,
        LingFengCompatible
    }

    public enum ScriptVariablePreflightSeverity
    {
        Information,
        Warning,
        Error
    }

    public sealed record ScriptVariablePreflightDiagnostic(
        string Code,
        ScriptVariablePreflightSeverity Severity,
        string File,
        int Line,
        string Message);

    public sealed record ScriptVariablePrefixUsage(string Prefix, int Count);

    public sealed class ScriptVariablePreflightReport
    {
        internal ScriptVariablePreflightReport(
            string rootPath,
            int fileCount,
            string contentDigest,
            IReadOnlyList<ScriptVariablePrefixUsage> prefixUsages,
            IReadOnlyList<ScriptVariablePreflightDiagnostic> diagnostics)
        {
            RootPath = rootPath;
            FileCount = fileCount;
            ContentDigest = contentDigest;
            PrefixUsages = prefixUsages;
            Diagnostics = diagnostics;
        }

        public string RootPath { get; }
        public int FileCount { get; }
        public string ContentDigest { get; }
        public IReadOnlyList<ScriptVariablePrefixUsage> PrefixUsages { get; }
        public IReadOnlyList<ScriptVariablePreflightDiagnostic> Diagnostics { get; }
        public bool HasErrors => Diagnostics.Any(item => item.Severity == ScriptVariablePreflightSeverity.Error);
    }

    public readonly struct ScriptVariableCompatibilityActivationResult
    {
        internal ScriptVariableCompatibilityActivationResult(bool success, string diagnostic)
        {
            Success = success;
            Diagnostic = diagnostic ?? string.Empty;
        }

        public bool Success { get; }
        public string Diagnostic { get; }
    }

    /// <summary>只读扫描实际脚本目录，输出可复核摘要；不会修改或重编码任何文件。</summary>
    public static class ScriptVariableCompatibilityPreflight
    {
        private static readonly Regex FixedReference = new Regex(
            @"(?<![A-Za-z0-9_$])(?<prefix>P|D|M|N|S|I|U|T|J|Z|G|A)(?<index>\d{1,4})(?![A-Za-z0-9_])",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex DynamicReference = new Regex(
            @"(?:[NSLD]\$|(?:P|D|M|N|S|I|U|T|J|Z|G|A|HUMAN|GUILD|GLOBAL)\.)[^\s,\]\)]+<\$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex AbsoluteVariablePath = new Regex(
            @"\b(?:LOADVAR|SAVEVAR|LOADVALUE|SAVEVALUE)\b[^\r\n]*(?:[A-Z]:\\|\\\\)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex AWrite = new Regex(
            @"^\s*(?:MOV|INC|DEC|MUL|DIV|CALCVAR)\s+A(?:\d+|\.)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static ScriptVariablePreflightReport Scan(string rootPath)
        {
            var diagnostics = new List<ScriptVariablePreflightDiagnostic>();
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var hashes = new List<string>();
            string root;
            try
            {
                root = string.IsNullOrWhiteSpace(rootPath)
                    ? string.Empty
                    : Path.GetFullPath(rootPath);
            }
            catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
            {
                diagnostics.Add(new ScriptVariablePreflightDiagnostic(
                    "VAR07-ROOT-001", ScriptVariablePreflightSeverity.Error,
                    rootPath ?? string.Empty, 0, "脚本预检根目录无效：" + error.Message));
                return Create(rootPath ?? string.Empty, 0, hashes, counts, diagnostics);
            }
            if (root.Length == 0 || !Directory.Exists(root))
            {
                diagnostics.Add(new ScriptVariablePreflightDiagnostic(
                    "VAR07-ROOT-001", ScriptVariablePreflightSeverity.Error,
                    root, 0, "脚本预检根目录不存在。"));
                return Create(root, 0, hashes, counts, diagnostics);
            }

            string[] files;
            try
            {
                files = Directory.EnumerateFiles(root, "*", new EnumerationOptions
                    {
                        RecurseSubdirectories = true,
                        IgnoreInaccessible = false,
                        AttributesToSkip = FileAttributes.ReparsePoint
                    })
                    .Where(path => string.Equals(Path.GetExtension(path), ".txt", StringComparison.OrdinalIgnoreCase) ||
                                   string.Equals(Path.GetExtension(path), ".ini", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                diagnostics.Add(new ScriptVariablePreflightDiagnostic(
                    "VAR07-FILE-001", ScriptVariablePreflightSeverity.Error,
                    root, 0, "脚本目录无法完整枚举：" + error.Message));
                return Create(root, 0, hashes, counts, diagnostics);
            }
            foreach (string file in files)
            {
                byte[] bytes;
                try
                {
                    bytes = File.ReadAllBytes(file);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    diagnostics.Add(new ScriptVariablePreflightDiagnostic(
                        "VAR07-FILE-001", ScriptVariablePreflightSeverity.Error,
                        Relative(root, file), 0, "文件无法只读打开：" + error.Message));
                    continue;
                }

                string relative = Relative(root, file);
                hashes.Add(relative.Replace('\\', '/') + ":" + Convert.ToHexString(SHA256.HashData(bytes)));
                if (!TryDecode(bytes, out string text, out string encoding))
                {
                    diagnostics.Add(new ScriptVariablePreflightDiagnostic(
                        "VAR07-ENCODING-001", ScriptVariablePreflightSeverity.Error,
                        relative, 0, "文件不是有效 UTF-8 或 CP936 文本。"));
                    continue;
                }

                bool hasCrLf = text.Contains("\r\n", StringComparison.Ordinal);
                bool hasBareLf = Regex.IsMatch(text, @"(?<!\r)\n");
                if (hasCrLf && hasBareLf)
                    diagnostics.Add(new ScriptVariablePreflightDiagnostic(
                        "VAR07-NEWLINE-001", ScriptVariablePreflightSeverity.Warning,
                        relative, 0, $"{encoding} 文件混用了 CRLF 与 LF。"));

                string[] lines = Regex.Split(text, "\r\n|\n|\r");
                for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
                {
                    string line = lines[lineIndex];
                    int lineNumber = lineIndex + 1;
                    foreach (Match match in FixedReference.Matches(line))
                    {
                        string prefix = match.Groups["prefix"].Value.ToUpperInvariant();
                        counts[prefix] = counts.TryGetValue(prefix, out int count) ? count + 1 : 1;
                        int index = int.Parse(match.Groups["index"].Value, System.Globalization.CultureInfo.InvariantCulture);
                        int maximum = prefix is "U" or "T" or "J" or "Z" ? 499 : 999;
                        if (index > maximum)
                            diagnostics.Add(new ScriptVariablePreflightDiagnostic(
                                "VAR07-RANGE-001", ScriptVariablePreflightSeverity.Error,
                                relative, lineNumber, $"{prefix}{index} 超出允许范围 0-{maximum}。"));
                        if (prefix == "N" && index is 998 or 999)
                            diagnostics.Add(new ScriptVariablePreflightDiagnostic(
                                "VAR07-RESERVED-001", ScriptVariablePreflightSeverity.Warning,
                                relative, lineNumber, $"{prefix}{index} 是保留兼容槽位，迁移前必须确认用途。"));
                        if (prefix == "A")
                            diagnostics.Add(new ScriptVariablePreflightDiagnostic(
                                AWrite.IsMatch(line) ? "VAR07-A-WRITE" : "VAR07-A-READ",
                                ScriptVariablePreflightSeverity.Warning,
                                relative, lineNumber,
                                AWrite.IsMatch(line)
                                    ? "A 固定变量写入现在是全服共享且持久化，请确认原脚本意图。"
                                    : "A 固定变量读取现在来自全服持久存储，请确认原脚本意图。"));
                    }

                    if (DynamicReference.IsMatch(line))
                        diagnostics.Add(new ScriptVariablePreflightDiagnostic(
                            "VAR07-DYNAMIC-001", ScriptVariablePreflightSeverity.Error,
                            relative, lineNumber, "动态拼接变量名无法静态确认，必须人工改为显式引用或登记映射。"));
                    if (AbsoluteVariablePath.IsMatch(line))
                        diagnostics.Add(new ScriptVariablePreflightDiagnostic(
                            "VAR07-PATH-001", ScriptVariablePreflightSeverity.Warning,
                            relative, lineNumber, "变量存取使用绝对路径，部署前必须确认路径和备份边界。"));
                }
            }

            if (files.Length == 0)
                diagnostics.Add(new ScriptVariablePreflightDiagnostic(
                    "VAR07-CONTENT-001", ScriptVariablePreflightSeverity.Error,
                    root, 0, "目录中没有可预检的 TXT/INI 脚本。"));
            return Create(root, files.Length, hashes, counts, diagnostics);
        }

        public static ScriptVariableCompatibilityActivationResult ValidateActivation(
            ScriptVariableCompatibilityMode mode,
            ScriptVariablePreflightReport report,
            string acknowledgedDigest)
        {
            if (mode != ScriptVariableCompatibilityMode.LingFengCompatible)
                return new ScriptVariableCompatibilityActivationResult(true, string.Empty);
            if (report == null || report.FileCount == 0 || report.HasErrors)
                return new ScriptVariableCompatibilityActivationResult(
                    false, "LingFengCompatible 启动失败：真实脚本预检缺失或存在阻断错误。");
            if (string.IsNullOrWhiteSpace(acknowledgedDigest) ||
                !string.Equals(acknowledgedDigest.Trim(), report.ContentDigest, StringComparison.OrdinalIgnoreCase))
                return new ScriptVariableCompatibilityActivationResult(
                    false, $"LingFengCompatible 启动失败：请审核预检报告后配置确认摘要 {report.ContentDigest}。");
            return new ScriptVariableCompatibilityActivationResult(true, string.Empty);
        }

        private static bool TryDecode(byte[] bytes, out string text, out string encodingName)
        {
            text = string.Empty;
            encodingName = string.Empty;
            try
            {
                var utf8 = new UTF8Encoding(false, true);
                int offset = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
                text = utf8.GetString(bytes, offset, bytes.Length - offset);
                encodingName = offset == 3 ? "UTF-8 BOM" : "UTF-8";
                return true;
            }
            catch (DecoderFallbackException)
            {
                try
                {
                    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                    text = Encoding.GetEncoding(936,
                        EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback).GetString(bytes);
                    encodingName = "CP936";
                    return true;
                }
                catch (DecoderFallbackException)
                {
                    return false;
                }
            }
        }

        private static ScriptVariablePreflightReport Create(
            string root,
            int fileCount,
            IEnumerable<string> hashes,
            IReadOnlyDictionary<string, int> counts,
            IEnumerable<ScriptVariablePreflightDiagnostic> diagnostics)
        {
            string manifest = string.Join("\n", hashes);
            string digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(manifest)));
            return new ScriptVariablePreflightReport(
                root,
                fileCount,
                digest,
                counts.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(pair => new ScriptVariablePrefixUsage(pair.Key.ToUpperInvariant(), pair.Value)).ToArray(),
                diagnostics.OrderBy(item => item.File, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.Line).ThenBy(item => item.Code, StringComparer.Ordinal).ToArray());
        }

        private static string Relative(string root, string file) =>
            Path.GetRelativePath(root, file);
    }
}
