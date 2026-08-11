using System.Drawing.Imaging;

namespace LyoCrystal.LauncherEditor;

public static class ThemeAssetImporter
{
    private const long MaximumSourceBytes = 64L * 1024 * 1024;

    public static string Import(string projectRoot, string sourceFile)
    {
        string source = Path.GetFullPath(sourceFile);
        if (!File.Exists(source) || new FileInfo(source).Length > MaximumSourceBytes)
            throw new InvalidDataException("主题图片不存在或超过 64 MiB");
        string extension = Path.GetExtension(source);
        if (!new[] { ".png", ".bmp", ".jpg", ".jpeg" }.Contains(extension, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException("主题图片仅支持 PNG、BMP、JPG");

        string root = Path.GetFullPath(projectRoot);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("项目目录不存在");
        RejectReparse(root);
        string assets = Path.Combine(root, "Assets");
        if (Directory.Exists(assets)) RejectReparse(assets); else Directory.CreateDirectory(assets);
        RejectReparse(assets);
        string name = extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase)
            ? Path.GetFileNameWithoutExtension(source) + ".png"
            : Path.GetFileName(source);
        string target = Path.Combine(assets, name);
        if (File.Exists(target) && (File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("项目素材文件不得为重解析点");
        using Image image = Image.FromFile(source);
        if ((long)image.Width * image.Height > 64L * 1024 * 1024) throw new InvalidDataException("主题图片解码像素超过限制");
        if (extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase))
        {
            image.Save(target, ImageFormat.Png);
        }
        else File.Copy(source, target, overwrite: true);
        return "Assets/" + name;
    }

    private static void RejectReparse(string path)
    {
        string full = Path.GetFullPath(path);
        string? current = Path.GetPathRoot(full);
        foreach (string segment in full[(current?.Length ?? 0)..].Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (segment.Length == 0) continue;
            current = Path.Combine(current ?? string.Empty, segment);
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("项目素材路径不得经过重解析点");
        }
    }
}
