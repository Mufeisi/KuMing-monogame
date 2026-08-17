using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Security.Cryptography;

namespace Server.Scripting;

internal sealed class LingFengRuntimeTextListStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);
    private readonly object _gate = new();

    public bool TryAdd(string sourcePath, string value)
    {
        if (!TryGetPath(sourcePath, out string key, out string path) ||
            string.IsNullOrWhiteSpace(value))
            return false;

        lock (_gate)
        {
            if (!TryRead(path, out List<string> values)) return false;
            if (!values.Contains(value, StringComparer.Ordinal)) values.Add(value);
            return TryWrite(key, path, values);
        }
    }

    public bool TrySetLine(string sourcePath, string value, int line)
    {
        if (line is < 0 or > 65_535 || value == null ||
            !TryGetPath(sourcePath, out string key, out string path)) return false;
        lock (_gate)
        {
            if (!TryRead(path, out List<string> values)) return false;
            while (values.Count <= line) values.Add(string.Empty);
            values[line] = value;
            return TryWrite(key, path, values);
        }
    }

    public IReadOnlyList<string> GetValues(string sourcePath)
    {
        if (!TryGetPath(sourcePath, out _, out string path))
            return Array.Empty<string>();
        lock (_gate)
        {
            return TryRead(path, out List<string> values)
                ? values.ToArray()
                : Array.Empty<string>();
        }
    }

    private static bool TryRead(string path, out List<string> values)
    {
        values = new List<string>();
        try
        {
            if (!File.Exists(path)) return true;
            string[] persisted = JsonSerializer.Deserialize<string[]>(
                File.ReadAllText(path, Encoding.UTF8), JsonOptions) ?? Array.Empty<string>();
            values.AddRange(persisted);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryWrite(string key, string path, IReadOnlyList<string> values)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temporaryPath = path + ".tmp";
            File.WriteAllText(temporaryPath,
                JsonSerializer.Serialize(values, JsonOptions), Utf8NoBom);
            File.Move(temporaryPath, path, true);
            return true;
        }
        catch
        {
            MessageQueue.Instance.Enqueue($"[TxtScripts] 文本列表写入失败：key={key}");
            return false;
        }
    }

    private static bool TryGetPath(string sourcePath, out string key, out string path)
    {
        key = string.Empty;
        path = string.Empty;
        if (string.IsNullOrWhiteSpace(sourcePath)) return false;
        if (!LingFengScriptReferenceResolver.TryResolveCandidateTextKey(sourcePath, out key))
            key = "external/" + Convert.ToHexString(
                SHA256.HashData(Utf8NoBom.GetBytes(sourcePath ?? string.Empty))).ToLowerInvariant();

        string root = Path.GetFullPath(Path.Combine(Settings.ConfigPath,
            "LingFengRuntime", "TextLists"));
        string relative = key.Replace('/', Path.DirectorySeparatorChar) + ".json";
        string candidate = Path.GetFullPath(Path.Combine(root, relative));
        string boundary = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                          Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(boundary, StringComparison.OrdinalIgnoreCase)) return false;
        path = candidate;
        return true;
    }
}
