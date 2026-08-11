namespace Launcher.ThemeRuntime;

internal static class ClientSelection
{
    private const string EntryFileName = "Client.exe";
    private const string CapabilityFileName = "launcher-capabilities.json";

    public static string GetPreferred(string projectId, string embeddedDirectory)
    {
        string persisted = ReadPersisted(GetStatePath(projectId));
        if (IsCompatible(persisted)) return persisted;
        string source = Environment.GetEnvironmentVariable("LYOCRYSTAL_PLAYER_SOURCE_DIRECTORY") ?? string.Empty;
        if (IsCompatible(source)) return Path.GetFullPath(source);
        return embeddedDirectory;
    }

    public static string? Resolve(IWin32Window owner, string projectId, string embeddedDirectory)
    {
        string statePath = GetStatePath(projectId);
        string persisted = ReadPersisted(statePath);
        if (IsCompatible(persisted)) return persisted;
        string source = Environment.GetEnvironmentVariable("LYOCRYSTAL_PLAYER_SOURCE_DIRECTORY") ?? string.Empty;
        if (IsCompatible(source))
        {
            Persist(statePath, source);
            return Path.GetFullPath(source);
        }
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
            return candidates[0];
        }
        if (candidates.Count > 1)
        {
            using var dialog = new ClientChoiceDialog(candidates, persisted);
            if (dialog.ShowDialog(owner) != DialogResult.OK) return null;
            Persist(statePath, dialog.SelectedDirectory!);
            return dialog.SelectedDirectory;
        }
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("LYOCRYSTAL_PLAYER_SOURCE_DIRECTORY"))) return embeddedDirectory;
        return InstallEmbeddedClient(owner, projectId, embeddedDirectory);
    }

    public static string? SelectManually(IWin32Window owner, string projectId)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "请选择包含 Client.exe 的游戏客户端目录",
            ShowNewFolderButton = false,
            UseDescriptionForTitle = true,
        };
        if (dialog.ShowDialog(owner) != DialogResult.OK) return null;
        string selected = Path.GetFullPath(dialog.SelectedPath);
        if (!IsCompatible(selected))
        {
            MessageBox.Show(owner, "所选目录不是本启动器支持的客户端，或缺少能力标记。", "选择客户端", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return null;
        }
        Persist(GetStatePath(projectId), selected);
        return selected;
    }

    private static string? InstallEmbeddedClient(IWin32Window owner, string projectId, string embeddedDirectory)
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
            return target;
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

    internal static bool IsCompatible(string directory)
    {
        return ClientCapabilityProbe.Detect(directory) != ClientLaunchCapability.Unsupported;
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
            return IsCompatible(value) ? value : string.Empty;
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
