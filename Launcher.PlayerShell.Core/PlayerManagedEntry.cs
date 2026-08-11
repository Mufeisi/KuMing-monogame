namespace Launcher.PlayerShell;

public static class PlayerManagedEntry
{
    public static string Ensure(string sourceExecutable, string projectId, string managedRoot, PlayerPayloadInfo expected)
    {
        if (projectId.Length is < 3 or > 64 || projectId.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_')) throw new InvalidDataException("玩家入口项目标识无效");
        string source = Path.GetFullPath(sourceExecutable), root = Path.GetFullPath(managedRoot);
        if (!File.Exists(source)) throw new FileNotFoundException("玩家入口不存在", source);
        RejectReparse(root); Directory.CreateDirectory(root); RejectReparse(root);
        string projectRoot = Path.Combine(root, projectId); RejectReparse(projectRoot); Directory.CreateDirectory(projectRoot); RejectReparse(projectRoot);
        string target = Path.Combine(projectRoot, "PlayerEntry.exe");
        if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase)) return target;
        if (File.Exists(target))
        {
            try { if (PlayerPayloadPackage.Verify(target).Sha256 == expected.Sha256) return target; }
            catch (InvalidDataException) { }
        }
        string temporary = target + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) input.CopyTo(output);
            PlayerPayloadInfo copied = PlayerPayloadPackage.Verify(temporary);
            if (copied.Sha256 != expected.Sha256) throw new InvalidDataException("受管理玩家入口复制校验失败");
            File.Move(temporary, target, true);
            return target;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static void RejectReparse(string path)
    {
        string full = Path.GetFullPath(path), current = Path.GetPathRoot(full) ?? string.Empty;
        foreach (string part in full[current.Length..].Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (part.Length == 0) continue; current = Path.Combine(current, part);
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("受管理玩家入口路径不得经过重解析点");
        }
    }
}
