using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Launcher.ThemeRuntime;

namespace LyoCrystal.LauncherEditor;

public static class ThemeTemplatePackage
{
    private const int MaximumFiles = 256;
    private const long MaximumBytes = 64L * 1024 * 1024;

    public static void Export(EditorProject project, string projectRoot, string outputPath)
    {
        ArgumentNullException.ThrowIfNull(project);
        RejectReparseChain(projectRoot);
        string output = Path.GetFullPath(outputPath);
        RejectReparseChain(Path.GetDirectoryName(output)!);
        if (File.Exists(output)) throw new IOException("主题模板包已存在，拒绝覆盖");
        LauncherTheme theme = Clone(project.Snapshot.Theme);
        var assets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int index = 0;
        RewriteAssets(theme, relative =>
        {
            if (string.IsNullOrWhiteSpace(relative)) return string.Empty;
            string source = LauncherSnapshotValidator.ResolveAsset(projectRoot, relative);
            if (!File.Exists(source)) throw new FileNotFoundException("主题素材不存在", source);
            if (!assets.TryGetValue(source, out string? entry))
            {
                entry = $"assets/{++index:D3}{Path.GetExtension(source).ToLowerInvariant()}";
                assets.Add(source, entry);
            }
            return entry;
        });
        if (assets.Count > MaximumFiles || assets.Keys.Sum(path => new FileInfo(path).Length) > MaximumBytes) throw new InvalidDataException("主题模板素材超过限制");
        string temporary = output + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            using (ZipArchive archive = ZipFile.Open(temporary, ZipArchiveMode.Create))
            {
                ZipArchiveEntry descriptor = archive.CreateEntry("theme.json", CompressionLevel.Optimal);
                using (Stream stream = descriptor.Open()) JsonSerializer.Serialize(stream, theme, EditorProjectJsonContext.Default.LauncherTheme);
                foreach ((string source, string entry) in assets) archive.CreateEntryFromFile(source, entry, CompressionLevel.Optimal);
            }
            File.Move(temporary, output);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static void Import(EditorProject project, string projectRoot, string inputPath)
    {
        ArgumentNullException.ThrowIfNull(project);
        string input = Path.GetFullPath(inputPath);
        if (!File.Exists(input) || new FileInfo(input).Length > MaximumBytes) throw new InvalidDataException("主题模板包不存在或超过限制");
        RejectReparseChain(input);
        string root = Path.GetFullPath(projectRoot);
        RejectReparseChain(root);
        string staging = Path.Combine(root, ".theme-import-" + Guid.NewGuid().ToString("N"));
        string folderName = "Theme-" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(input)))[..12].ToLowerInvariant();
        string final = Path.Combine(root, "Assets", folderName);
        try
        {
            using ZipArchive archive = ZipFile.OpenRead(input);
            if (archive.Entries.Count is < 1 or > MaximumFiles + 1) throw new InvalidDataException("主题模板文件数量无效");
            ZipArchiveEntry descriptor = archive.GetEntry("theme.json") ?? throw new InvalidDataException("主题模板缺少 theme.json");
            if (descriptor.Length > 1024 * 1024) throw new InvalidDataException("主题描述超过限制");
            LauncherTheme theme;
            using (Stream stream = descriptor.Open()) theme = JsonSerializer.Deserialize(stream, EditorProjectJsonContext.Default.LauncherTheme) ?? throw new InvalidDataException("主题描述为空");
            RejectReparseChain(staging); Directory.CreateDirectory(staging); RejectReparseChain(staging);
            long total = 0;
            foreach (ZipArchiveEntry entry in archive.Entries.Where(item => item.FullName.StartsWith("assets/", StringComparison.Ordinal) && !string.IsNullOrEmpty(item.Name)))
            {
                total = checked(total + entry.Length); if (total > MaximumBytes) throw new InvalidDataException("主题素材展开大小超过限制");
                string relative = entry.FullName["assets/".Length..];
                if (relative.Contains('/') || relative.Contains('\\') || relative.Contains("..", StringComparison.Ordinal)) throw new InvalidDataException("主题素材路径无效");
                string target = Path.Combine(staging, relative);
                using (Stream source = entry.Open())
                using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None)) source.CopyTo(output);
                using Image decoded = Image.FromFile(target);
                if ((long)decoded.Width * decoded.Height > 64L * 1024 * 1024) throw new InvalidDataException("主题素材解码像素超过限制");
            }
            RewriteAssets(theme, relative =>
            {
                if (string.IsNullOrWhiteSpace(relative)) return string.Empty;
                if (!relative.StartsWith("assets/", StringComparison.Ordinal) || relative["assets/".Length..].Contains('/')) throw new InvalidDataException("主题描述引用了包外素材");
                string name = relative["assets/".Length..];
                if (!File.Exists(Path.Combine(staging, name))) throw new InvalidDataException("主题描述引用素材缺失");
                return $"Assets/{folderName}/{name}";
            });
            if (Directory.Exists(final)) throw new IOException("同一主题模板已经导入");
            string assetsRoot = Path.GetDirectoryName(final)!;
            RejectReparseChain(assetsRoot); Directory.CreateDirectory(assetsRoot); RejectReparseChain(assetsRoot);
            Directory.Move(staging, final);
            project.Snapshot.Theme = theme;
        }
        finally { if (Directory.Exists(staging)) { RejectReparseChain(staging); Directory.Delete(staging, true); } }
    }

    private static LauncherTheme Clone(LauncherTheme theme) =>
        JsonSerializer.Deserialize(JsonSerializer.SerializeToUtf8Bytes(theme, EditorProjectJsonContext.Default.LauncherTheme), EditorProjectJsonContext.Default.LauncherTheme)
        ?? throw new InvalidDataException("主题无法复制");

    private static void RewriteAssets(LauncherTheme theme, Func<string, string> rewrite)
    {
        theme.BackgroundImage = rewrite(theme.BackgroundImage);
        theme.LaunchButtonImage = rewrite(theme.LaunchButtonImage);
        theme.LaunchButtonHoverImage = rewrite(theme.LaunchButtonHoverImage);
        theme.LaunchButtonPressedImage = rewrite(theme.LaunchButtonPressedImage);
        theme.LaunchButtonDisabledImage = rewrite(theme.LaunchButtonDisabledImage);
        foreach (LauncherControlOverride control in theme.Controls) control.BackgroundImage = rewrite(control.BackgroundImage);
    }

    private static void RejectReparseChain(string path)
    {
        string full = Path.GetFullPath(path), current = Path.GetPathRoot(full) ?? string.Empty;
        foreach (string part in full[current.Length..].Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (part.Length == 0) continue; current = Path.Combine(current, part);
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("主题模板路径不得经过重解析点");
        }
    }
}
