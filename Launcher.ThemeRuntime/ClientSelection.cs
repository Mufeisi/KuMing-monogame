namespace Launcher.ThemeRuntime;

public static class ClientSelection
{
    private const string EntryFileName = "Client.exe";
    private const string CapabilityFileName = "launcher-capabilities.json";

    internal static ClientSelectionResult GetPreferred(string projectId, string embeddedDirectory, IReadOnlyCollection<LauncherCoreResource> resources)
    {
        string source = Environment.GetEnvironmentVariable("LYOCRYSTAL_PLAYER_SOURCE_DIRECTORY") ?? string.Empty;
        if (TryUseSourceDirectory(source, embeddedDirectory, resources, out ClientSelectionResult? sourceSelection)) return sourceSelection!;
        string persisted = ReadPersisted(GetStatePath(projectId));
        if (TryUseSourceDirectory(persisted, embeddedDirectory, resources, out ClientSelectionResult? persistedSelection)) return persistedSelection!;
        return new(Path.GetFullPath(embeddedDirectory), Path.GetFullPath(embeddedDirectory));
    }

    internal static ClientSelectionResult? Resolve(IWin32Window owner, string projectId, string embeddedDirectory, IReadOnlyCollection<LauncherCoreResource> resources)
    {
        string statePath = GetStatePath(projectId);
        string source = Environment.GetEnvironmentVariable("LYOCRYSTAL_PLAYER_SOURCE_DIRECTORY") ?? string.Empty;
        if (TryUseSourceDirectory(source, embeddedDirectory, resources, out ClientSelectionResult? sourceSelection))
        {
            Persist(statePath, source);
            return sourceSelection;
        }
        string persisted = ReadPersisted(statePath);
        if (TryUseSourceDirectory(persisted, embeddedDirectory, resources, out ClientSelectionResult? persistedSelection)) return persistedSelection;
        string[] driveRoots = DriveInfo.GetDrives().Where(drive => drive.IsReady && drive.DriveType is DriveType.Fixed or DriveType.Removable).Select(drive => drive.RootDirectory.FullName).ToArray();
        string[] roots = new[] { source, Environment.CurrentDirectory }
            .Concat(driveRoots.SelectMany(root => new[] { Path.Combine(root, "Games"), Path.Combine(root, "ChuanQi"), Path.Combine(root, "传奇"), Path.Combine(root, "Mir") }))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        string embeddedFull = Path.GetFullPath(embeddedDirectory);
        IReadOnlyList<string> candidates = ClientLocator.Find(
            EntryFileName,
            roots,
            maximumDepth: 4,
            candidateFilter: path => !string.Equals(Path.GetFullPath(path), embeddedFull, StringComparison.OrdinalIgnoreCase) && IsCompatible(path),
            timeBudget: TimeSpan.FromSeconds(2));
        if (candidates.Count == 1)
        {
            Persist(statePath, candidates[0]);
            return new(candidates[0], candidates[0]);
        }
        if (candidates.Count > 1)
        {
            using var dialog = new ClientChoiceDialog(candidates, persisted);
            if (dialog.ShowDialog(owner) != DialogResult.OK) return null;
            Persist(statePath, dialog.SelectedDirectory!);
            return new(dialog.SelectedDirectory!, dialog.SelectedDirectory!);
        }
        IReadOnlyList<string> resourceCandidates = ClientLocator.Find(
                "Title.Lib", roots, maximumDepth: 5,
                candidateFilter: dataDirectory => string.Equals(Path.GetFileName(dataDirectory), "Data", StringComparison.OrdinalIgnoreCase)
                    && IsTrustedResourceDirectory(Path.GetDirectoryName(dataDirectory) ?? string.Empty, resources),
                timeBudget: TimeSpan.FromSeconds(2))
            .Select(dataDirectory => Path.GetDirectoryName(dataDirectory)!)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (resourceCandidates.Count == 1)
        {
            Persist(statePath, resourceCandidates[0]);
            return new(Path.GetFullPath(embeddedDirectory), resourceCandidates[0]);
        }
        if (resourceCandidates.Count > 1)
        {
            using var dialog = new ClientChoiceDialog(resourceCandidates, persisted);
            if (dialog.ShowDialog(owner) != DialogResult.OK) return null;
            Persist(statePath, dialog.SelectedDirectory!);
            return new(Path.GetFullPath(embeddedDirectory), dialog.SelectedDirectory!);
        }
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("LYOCRYSTAL_PLAYER_SOURCE_DIRECTORY"))) return new(Path.GetFullPath(embeddedDirectory), Path.GetFullPath(embeddedDirectory));
        return InstallEmbeddedClient(owner, projectId, embeddedDirectory);
    }

    private static bool TryUseSourceDirectory(string source, string embeddedDirectory, IReadOnlyCollection<LauncherCoreResource> resources, out ClientSelectionResult? selection)
    {
        selection = null;
        if (!IsTrustedResourceDirectory(source, resources)) return false;
        string resourceRoot = Path.GetFullPath(source);
        selection = new(IsCompatible(source) ? resourceRoot : Path.GetFullPath(embeddedDirectory), resourceRoot);
        return true;
    }

    internal static ClientSelectionResult? SelectManually(IWin32Window owner, string projectId, string embeddedDirectory, IReadOnlyCollection<LauncherCoreResource> resources)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "请选择包含 Client.exe 的游戏客户端目录",
            ShowNewFolderButton = false,
            UseDescriptionForTitle = true,
        };
        if (dialog.ShowDialog(owner) != DialogResult.OK) return null;
        string selected = Path.GetFullPath(dialog.SelectedPath);
        if (!IsCompatible(selected) && !IsTrustedResourceDirectory(selected, resources))
        {
            MessageBox.Show(owner, "所选目录不是可用的客户端资源目录。请选择包含 Data 文件夹及 Title.Lib、ChrSel.Lib、Prguse.Lib 的完整客户端目录。", "选择客户端", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return null;
        }
        Persist(GetStatePath(projectId), selected);
        return IsCompatible(selected) ? new(selected, selected) : new(Path.GetFullPath(embeddedDirectory), selected);
    }

    private static ClientSelectionResult? InstallEmbeddedClient(IWin32Window owner, string projectId, string embeddedDirectory)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "请选择游戏客户端的安置位置（只需本次确认）",
            ShowNewFolderButton = true,
            UseDescriptionForTitle = true,
        };
        if (dialog.ShowDialog(owner) != DialogResult.OK) return null;
        string parent = Path.GetFullPath(dialog.SelectedPath);
        string target = Path.Combine(parent, "LyoCrystalClient-" + projectId);
        long bytes = Directory.EnumerateFiles(embeddedDirectory, "*", SearchOption.AllDirectories).Sum(file => new FileInfo(file).Length);
        string driveRoot = Path.GetPathRoot(target) ?? target;
        if (new DriveInfo(driveRoot).AvailableFreeSpace < bytes + 128L * 1024 * 1024)
        {
            MessageBox.Show(owner, "目标磁盘空间不足，至少需要客户端大小再预留 128 MiB。", "安置客户端", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return null;
        }
        if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any())
        {
            MessageBox.Show(owner, "目标客户端目录已存在且非空，请选择其他位置。", "安置客户端", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return null;
        }
        string size = bytes >= 1024 * 1024 ? $"{bytes / 1024d / 1024d:F1} MiB" : $"{bytes / 1024d:F1} KiB";
        if (MessageBox.Show(owner, $"客户端将安置到：\r\n{target}\r\n\r\n所需空间约 {size}。是否继续？", "确认安置客户端", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return null;
        string staging = target + ".installing-" + Guid.NewGuid().ToString("N");
        try
        {
            CopyDirectory(embeddedDirectory, staging);
            if (Directory.Exists(target)) Directory.Delete(target);
            Directory.Move(staging, target);
            Persist(GetStatePath(projectId), target);
            return new(target, target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try { if (Directory.Exists(staging)) Directory.Delete(staging, true); } catch { }
            MessageBox.Show(owner, "安置客户端失败：" + ex.Message, "安置客户端", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return null;
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        var pending = new Stack<string>();
        pending.Push(Path.GetFullPath(source));
        while (pending.Count > 0)
        {
            string current = pending.Pop();
            foreach (string file in Directory.EnumerateFiles(current))
                File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file)), overwrite: false);
            foreach (string directory in Directory.EnumerateDirectories(current))
            {
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("客户端载荷包含不允许的重解析点");
                Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
                pending.Push(directory);
            }
        }
    }

    public static bool IsCompatible(string directory)
    {
        return ClientCapabilityProbe.Detect(directory) != ClientLaunchCapability.Unsupported;
    }

    internal static bool IsResourceDirectory(string directory)
    {
        try
        {
            string root = Path.GetFullPath(directory);
            if (!Directory.Exists(root)) return false;
            RejectReparseChain(root, Path.Combine(root, "Data"));
            return new[] { "Title.Lib", "ChrSel.Lib", "Prguse.Lib" }.All(file => IsPlainNonEmptyFile(root, Path.Combine(root, "Data", file)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return false; }
    }

    public static bool IsTrustedResourceDirectory(string directory, IReadOnlyCollection<LauncherCoreResource> resources)
    {
        if (!IsResourceDirectory(directory) || resources.Count != 3) return false;
        try
        {
            string root = Path.GetFullPath(directory);
            return resources.All(resource =>
            {
                string path = Path.GetFullPath(Path.Combine(root, resource.Path.Replace('/', Path.DirectorySeparatorChar)));
                RejectReparseChain(root, path);
                if (!File.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 || new FileInfo(path).Length != resource.Size) return false;
                using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan);
                return string.Equals(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream)), resource.Sha256, StringComparison.OrdinalIgnoreCase);
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException) { return false; }
    }

    private static bool IsPlainNonEmptyFile(string root, string path)
    {
        RejectReparseChain(root, path);
        return File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0 && new FileInfo(path).Length > 0;
    }

    private static void RejectReparseChain(string root, string path)
    {
        string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        string full = Path.GetFullPath(path);
        if (!full.Equals(fullRoot, StringComparison.OrdinalIgnoreCase) && !full.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("客户端资源路径越界");
        string current = Path.GetPathRoot(full) ?? string.Empty;
        foreach (string part in full[current.Length..].Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (part.Length == 0) continue;
            current = Path.Combine(current, part);
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("客户端资源路径不得经过重解析点");
        }
    }


    private static string GetStatePath(string projectId)
    {
        LauncherSnapshotValidator.ValidateProjectId(projectId);
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LyoCrystal", "Launcher", "Clients", projectId + ".txt");
    }

    private static string ReadPersisted(string path)
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > 4096) return string.Empty;
            string value = Path.GetFullPath(File.ReadAllText(path).Trim());
            return IsCompatible(value) || IsResourceDirectory(value) ? value : string.Empty;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return string.Empty; }
    }

    private static void Persist(string path, string directory)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        File.WriteAllText(temporary, Path.GetFullPath(directory));
        File.Move(temporary, path, overwrite: true);
    }
}

internal sealed record ClientSelectionResult(string ExecutableDirectory, string ResourceDirectory);

internal sealed class ClientChoiceDialog : Form
{
    private readonly ListBox _list = new() { Dock = DockStyle.Fill };
    public string? SelectedDirectory => _list.SelectedItem as string;

    public ClientChoiceDialog(IReadOnlyList<string> candidates, string preferred)
    {
        Text = "选择游戏客户端";
        ClientSize = new Size(620, 300);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = MaximizeBox = false;
        _list.Items.AddRange(candidates.Cast<object>().ToArray());
        int preferredIndex = candidates.ToList().FindIndex(path => string.Equals(path, preferred, StringComparison.OrdinalIgnoreCase));
        _list.SelectedIndex = preferredIndex >= 0 ? preferredIndex : 0;
        var prompt = new Label { Text = "检测到多个客户端，请选择本次使用的目录：", Dock = DockStyle.Top, Height = 34, Padding = new Padding(8) };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        var ok = new Button { Text = "使用此客户端", DialogResult = DialogResult.OK, Width = 110 };
        var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Width = 80 };
        buttons.Controls.AddRange(new Control[] { ok, cancel });
        Controls.AddRange(new Control[] { _list, prompt, buttons });
        AcceptButton = ok;
        CancelButton = cancel;
    }
}
