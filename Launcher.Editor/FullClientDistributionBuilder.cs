using System.IO.Compression;

namespace LyoCrystal.LauncherEditor;

public static class FullClientDistributionBuilder
{
    public static void Create(EditorProject project, string playerEntryExe, string outputZip)
    {
        if (project.DeliveryMode != ClientDeliveryMode.FullClient) throw new InvalidOperationException("当前项目不是完整客户端交付模式");
        string root = Path.GetFullPath(project.ImportedClientDirectory);
        string entry = Path.GetFullPath(playerEntryExe);
        if (!Directory.Exists(root) || !File.Exists(Path.Combine(root, "Client.exe"))) throw new InvalidDataException("完整客户端目录无效");
        if (!File.Exists(entry) || !entry.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("玩家入口无效");
        RejectReparse(root);
        string target = Path.GetFullPath(outputZip);
        string temporary = target + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using var archive = ZipFile.Open(temporary, ZipArchiveMode.Create);
            int count = 0; long total = 0;
            foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                if (++count > 200_000) throw new InvalidDataException("完整客户端文件数量超过限制");
                RejectPathChain(root, file);
                long length = new FileInfo(file).Length;
                if ((total = checked(total + length)) > 32L * 1024 * 1024 * 1024) throw new InvalidDataException("完整客户端总量超过 32 GiB 限制");
                archive.CreateEntryFromFile(file, "Client/" + Path.GetRelativePath(root, file).Replace('\\', '/'), CompressionLevel.Optimal);
            }
            archive.CreateEntryFromFile(entry, Path.GetFileName(entry), CompressionLevel.Optimal);
            ZipArchiveEntry note = archive.CreateEntry("使用说明.txt", CompressionLevel.Optimal);
            using var writer = new StreamWriter(note.Open(), new System.Text.UTF8Encoding(false));
            writer.WriteLine("解压全部文件后，双击根目录中的玩家入口 EXE。玩家不能在启动器中切换交付模式。");
        }
        catch { if (File.Exists(temporary)) File.Delete(temporary); throw; }
        File.Move(temporary, target, overwrite: true);
    }

    private static void RejectReparse(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("完整客户端目录不得为重解析点");
    }

    private static void RejectPathChain(string root, string file)
    {
        string current = root;
        foreach (string part in Path.GetRelativePath(root, file).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            current = Path.Combine(current, part);
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("完整客户端不得包含重解析点");
        }
    }
}
