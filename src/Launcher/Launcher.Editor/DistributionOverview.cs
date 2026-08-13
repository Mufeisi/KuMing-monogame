using Launcher.ThemeRuntime;

namespace LyoCrystal.LauncherEditor;

internal enum DistributionFixTarget { ResourceDirectory, DefaultEndpoint, ServerOverrides, Signing, Preflight }

internal sealed record DistributionIssue(string Message, DistributionFixTarget Target);

internal sealed record DistributionOverviewSnapshot(
    string DeliveryMode,
    string ClientCore,
    string ResourcePackage,
    string ResourceVersion,
    string SigningIdentity,
    string DefaultEndpoint,
    string BackupEndpoint,
    string ServerOverrides,
    long CoreBytes,
    long TotalBytes,
    int FileCount,
    IReadOnlyList<string> MissingCoreFiles,
    IReadOnlyList<string> DuplicateCoreFiles,
    IReadOnlyList<DistributionIssue> Issues);

internal static class DistributionOverview
{
    private static readonly string[] CoreFiles = ["Title.Lib", "ChrSel.Lib", "Prguse.Lib"];

    internal static DistributionOverviewSnapshot Inspect(EditorProject project, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        MicroEndpoint endpoint = project.Snapshot.DefaultMicro;
        string resourceRoot = FirstExistingDirectory(project.Gateway.ResourceDirectory, project.ImportedClientDirectory);
        var missing = new List<string>();
        var duplicate = new List<string>();
        var issues = new List<DistributionIssue>();
        long coreBytes = 0, totalBytes = 0;
        int fileCount = 0;

        if (string.IsNullOrWhiteSpace(resourceRoot))
        {
            missing.AddRange(CoreFiles);
            issues.Add(new("尚未选择可读取的客户端资源目录。", DistributionFixTarget.ResourceDirectory));
        }
        else
        {
            try
            {
                var matches = CoreFiles.ToDictionary(name => name, _ => new List<(string Path, long Length)>(), StringComparer.OrdinalIgnoreCase);
                foreach (string path in EnumerateFilesSafe(resourceRoot))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (++fileCount > 200_000) throw new InvalidDataException("资源文件数量超过 200000 个扫描上限");
                    long length = new FileInfo(path).Length;
                    totalBytes = checked(totalBytes + length);
                    string name = Path.GetFileName(path);
                    if (matches.TryGetValue(name, out List<(string Path, long Length)>? paths)) paths.Add((path, length));
                }
                foreach ((string name, List<(string Path, long Length)> paths) in matches)
                {
                    string expected = Path.GetFullPath(Path.Combine(resourceRoot, "Data", name));
                    (string Path, long Length) canonical = paths.FirstOrDefault(item => string.Equals(Path.GetFullPath(item.Path), expected, StringComparison.OrdinalIgnoreCase));
                    bool canonicalFound = !string.IsNullOrEmpty(canonical.Path);
                    if (!canonicalFound) missing.Add(name);
                    else coreBytes = checked(coreBytes + canonical.Length);
                    if (paths.Count > (canonicalFound ? 1 : 0)) duplicate.Add(name);
                }
                if (missing.Count > 0) issues.Add(new("客户端核心缺少：" + string.Join("、", missing) + "。", DistributionFixTarget.ResourceDirectory));
                if (duplicate.Count > 0) issues.Add(new("客户端核心存在重复文件：" + string.Join("、", duplicate) + "，请只保留 Data 目录中的正式文件。", DistributionFixTarget.ResourceDirectory));
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                issues.Add(new("资源目录扫描失败：" + error.Message, DistributionFixTarget.ResourceDirectory));
            }
        }

        if (endpoint.Enabled && string.IsNullOrWhiteSpace(endpoint.Address)) issues.Add(new("默认微端主入口缺少地址。", DistributionFixTarget.DefaultEndpoint));
        if (endpoint.Enabled && (string.IsNullOrWhiteSpace(endpoint.ResourceVersion) || string.IsNullOrWhiteSpace(endpoint.SigningIdentity)))
            issues.Add(new("默认微端缺少资源版本或签名身份。", DistributionFixTarget.Signing));
        int overrideCount = project.Snapshot.Servers.Count(server => server.MicroOverride?.Enabled == true);
        int inconsistent = project.Snapshot.Servers.Count(server => server.MicroOverride?.Enabled == true &&
            (!string.Equals(server.MicroOverride!.ResourceVersion, endpoint.ResourceVersion, StringComparison.Ordinal) ||
             !string.Equals(server.MicroOverride.SigningIdentity, endpoint.SigningIdentity, StringComparison.Ordinal)));
        if (inconsistent > 0) issues.Add(new($"{inconsistent} 个区服覆盖的资源版本或签名身份与项目默认值不一致。", DistributionFixTarget.ServerOverrides));

        string resourcePackage = string.IsNullOrWhiteSpace(resourceRoot)
            ? "未选择"
            : $"{resourceRoot}（{fileCount:N0} 个文件，{FormatBytes(totalBytes)}）";
        return new DistributionOverviewSnapshot(
            project.DeliveryMode == ClientDeliveryMode.MicroOnDemand ? "微端按需下载" : "完整客户端",
            missing.Count == 0 ? $"3/3 就绪（{FormatBytes(coreBytes)}）" : $"{3 - missing.Count}/3 就绪",
            resourcePackage,
            Display(endpoint.ResourceVersion), Display(endpoint.SigningIdentity),
            endpoint.Enabled ? $"{endpoint.Address}:{endpoint.Port}" : "已停用",
            string.IsNullOrWhiteSpace(endpoint.BackupAddress) ? "未配置" : $"{endpoint.BackupAddress}:{endpoint.BackupPort}",
            overrideCount == 0 ? "全部区服继承默认入口" : $"{overrideCount} 个区服使用覆盖（{inconsistent} 个不一致）",
            coreBytes, totalBytes, fileCount, missing, duplicate, issues);
    }

    private static string FirstExistingDirectory(params string[] candidates)
        => candidates.Select(value => string.IsNullOrWhiteSpace(value) ? string.Empty : Path.GetFullPath(value)).FirstOrDefault(Directory.Exists) ?? string.Empty;

    private static IEnumerable<string> EnumerateFilesSafe(string root)
    {
        var pending = new Stack<string>(); pending.Push(Path.GetFullPath(root));
        while (pending.Count > 0)
        {
            string directory = pending.Pop();
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("资源目录不得包含重解析点");
            foreach (string file in Directory.EnumerateFiles(directory))
            {
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("资源文件不得为重解析点");
                yield return file;
            }
            foreach (string child in Directory.EnumerateDirectories(directory)) pending.Push(child);
        }
    }

    internal static string FormatBytes(long value)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double size = value; int unit = 0;
        while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
        return unit == 0 ? $"{size:N0} {units[unit]}" : $"{size:N2} {units[unit]}";
    }

    private static string Display(string value) => string.IsNullOrWhiteSpace(value) ? "未配置" : value;
}
