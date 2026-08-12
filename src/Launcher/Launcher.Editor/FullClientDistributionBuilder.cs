using System.IO.Compression;
using System.Text.Json;
using Launcher.PlayerShell;
using Launcher.ThemeRuntime;

namespace LyoCrystal.LauncherEditor;

public static class FullClientDistributionBuilder
{
    public static void Create(EditorProject project, string playerEntryExe, string outputZip, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (project.DeliveryMode != ClientDeliveryMode.FullClient) throw new InvalidOperationException("当前项目不是完整客户端交付模式");
        string root = Path.GetFullPath(project.ImportedClientDirectory);
        string entry = Path.GetFullPath(playerEntryExe);
        if (!Directory.Exists(root) || ClientCapabilityProbe.Detect(root) != ClientLaunchCapability.Current15Arguments) throw new InvalidDataException("完整客户端目录缺少当前启动协议能力标记");
        if (!File.Exists(entry) || !entry.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("玩家入口无效");
        RejectReparseChain(root); RejectReparseChain(entry);
        string target = Path.GetFullPath(outputZip);
        RejectReparseChain(Path.GetDirectoryName(target)!);
        string temporary = target + ".tmp-" + Guid.NewGuid().ToString("N");
        string entryProbe = target + ".entry-probe-" + Guid.NewGuid().ToString("N");
        try
        {
            PlayerPayloadPackage.Verify(entry);
            PlayerPayloadPackage.ExtractVerified(entry, entryProbe);
            string snapshotPath = Path.Combine(entryProbe, "Launcher", "BuiltIn", "launcher-snapshot.json");
            LauncherSnapshot snapshot = JsonSerializer.Deserialize(File.ReadAllBytes(snapshotPath), LauncherSnapshotJsonContext.Default.LauncherSnapshot) ?? throw new InvalidDataException("玩家入口内置快照为空");
            if (!string.Equals(snapshot.ProjectId, project.Snapshot.ProjectId, StringComparison.Ordinal)) throw new InvalidDataException("玩家入口不属于当前项目");
            if (string.IsNullOrWhiteSpace(project.Release.CurrentKeyId) || !snapshot.TrustedReleaseKeys.Any(key => string.Equals(key.KeyId, project.Release.CurrentKeyId, StringComparison.Ordinal) && string.Equals(key.SubjectPublicKeyInfo, project.Release.CurrentPublicKey, StringComparison.Ordinal))) throw new InvalidDataException("玩家入口签名身份与当前项目不一致");
            using var archive = ZipFile.Open(temporary, ZipArchiveMode.Create);
            int count = 0; long total = 0;
            foreach (string file in EnumerateFilesSafe(root))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++count > 200_000) throw new InvalidDataException("完整客户端文件数量超过限制");
                long length = new FileInfo(file).Length;
                if ((total = checked(total + length)) > 32L * 1024 * 1024 * 1024) throw new InvalidDataException("完整客户端总量超过 32 吉字节限制");
                AddFile(archive, file, "Client/" + Path.GetRelativePath(root, file).Replace('\\', '/'), cancellationToken);
            }
            AddFile(archive, entry, Path.GetFileName(entry), cancellationToken);
            ZipArchiveEntry note = archive.CreateEntry("使用说明.txt", CompressionLevel.Optimal);
            using var writer = new StreamWriter(note.Open(), new System.Text.UTF8Encoding(false));
            writer.WriteLine("解压全部文件后，双击根目录中的玩家启动器。玩家不能自行切换下载方式。");
        }
        catch { if (File.Exists(temporary)) File.Delete(temporary); throw; }
        finally { if (Directory.Exists(entryProbe)) { RejectReparseChain(entryProbe); Directory.Delete(entryProbe, true); } }
        File.Move(temporary, target, overwrite: true);
    }

    private static void AddFile(ZipArchive archive, string source, string name, CancellationToken cancellationToken)
    {
        ZipArchiveEntry entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using Stream input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        using Stream output = entry.Open();
        byte[] buffer = new byte[1024 * 1024];
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0) { cancellationToken.ThrowIfCancellationRequested(); output.Write(buffer, 0, read); }
    }

    private static IEnumerable<string> EnumerateFilesSafe(string root)
    {
        var pending = new Stack<string>(); pending.Push(root);
        while (pending.Count > 0)
        {
            string directory = pending.Pop(); RejectReparseChain(directory);
            foreach (string file in Directory.EnumerateFiles(directory)) { RejectReparseChain(file); yield return file; }
            foreach (string child in Directory.EnumerateDirectories(directory)) { RejectReparseChain(child); pending.Push(child); }
        }
    }

    private static void RejectReparseChain(string path)
    {
        string full = Path.GetFullPath(path), current = Path.GetPathRoot(full) ?? string.Empty;
        foreach (string part in full[current.Length..].Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (part.Length == 0) continue; current = Path.Combine(current, part);
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("完整客户端路径不得经过重解析点");
        }
    }
}
