using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using Launcher.ThemeRuntime;

namespace LyoCrystal.LauncherEditor;

public sealed class EditorProjectStore
{
    public string WorkspaceRoot { get; }

    public EditorProjectStore(string workspaceRoot)
    {
        WorkspaceRoot = Path.GetFullPath(workspaceRoot);
        Directory.CreateDirectory(WorkspaceRoot);
        RejectReparsePath(WorkspaceRoot);
    }

    public IReadOnlyList<string> ListProjectIds() => Directory.EnumerateDirectories(WorkspaceRoot)
        .Where(path => (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0 && File.Exists(Path.Combine(path, "project.json")))
        .Select(Path.GetFileName).Where(name => !string.IsNullOrWhiteSpace(name)).Cast<string>()
        .OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();

    public EditorProject Create(string projectId, string projectName, LauncherTemplateKind template)
        => Create(new EditorProjectCreationOptions { ProjectId = projectId, ProjectName = projectName, Template = template });

    public EditorProject Create(EditorProjectCreationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        string projectId = options.ProjectId;
        string projectName = options.ProjectName;
        LauncherTemplateKind template = options.Template;
        LauncherSnapshotValidator.ValidateProjectId(projectId);
        string directory = GetProjectDirectory(projectId);
        RejectReparsePath(WorkspaceRoot);
        if (Directory.Exists(directory)) RejectReparsePath(directory);
        if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any()) throw new IOException("项目标识已存在");
        Directory.CreateDirectory(directory);
        RejectReparsePath(directory);
        string assets = Path.Combine(directory, "Assets");
        Directory.CreateDirectory(assets);
        RejectReparsePath(assets);
        EditorProject project = new() { Snapshot = LauncherTemplateCatalog.Create(template) };
        project.Snapshot.ProjectId = projectId;
        project.Snapshot.ProjectName = string.IsNullOrWhiteSpace(projectName) ? "未命名启动器" : projectName.Trim();
        project.Snapshot.Theme.ServerListMode = options.ServerListMode;
        project.Snapshot.RemoteReleaseBaseUrl = options.RemoteReleaseBaseUrl.Trim();
        project.Snapshot.Servers[0].Address = options.ServerAddress.Trim();
        project.Snapshot.Servers[0].Port = options.ServerPort;
        project.Snapshot.DefaultMicro.Address = options.MicroAddress.Trim();
        project.Snapshot.DefaultMicro.Port = options.MicroPort;
        project.Snapshot.DefaultMicro.BackupAddress = options.BackupMicroAddress.Trim();
        project.Snapshot.DefaultMicro.BackupPort = options.BackupMicroPort;
        project.Snapshot.Defaults.Resolution = options.Resolution;
        project.Snapshot.Defaults.FullScreen = options.FullScreen;
        project.Snapshot.Announcements = new List<LauncherAnnouncement> { new() { Title = options.AnnouncementTitle.Trim(), Summary = options.AnnouncementSummary.Trim(), Date = DateTime.Today.ToString("yyyy-MM-dd") } };
        project.DeliveryMode = options.DeliveryMode;
        project.ImportedClientDirectory = options.ImportedClientDirectory.Trim();
        project.Snapshot.DefaultMicro.User = "u_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
        project.Brand.ProductName = project.Snapshot.ProjectName;
        project.Brand.WindowTitle = project.Snapshot.ProjectName;
        project.Brand.TaskbarName = project.Snapshot.ProjectName;
        project.Brand.CompanyName = options.CompanyName.Trim();
        project.Release.PlayerUpdateMode = options.PlayerUpdateMode;
        project.Gateway.CacheDirectory = options.GatewayCacheDirectory.Trim();
        project.Gateway.MemoryCacheMb = options.GatewayMemoryCacheMb;
        project.Gateway.DiskCacheMb = options.GatewayDiskCacheMb;
        ProjectReleaseKeyStore.EnsureProvisioned(project, directory);
        Save(project);
        return project;
    }

    public EditorProject Load(string projectId)
    {
        string directory = GetProjectDirectory(projectId);
        RejectReparsePath(directory);
        string file = Path.Combine(directory, "project.json");
        if (File.Exists(file) && (File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("项目文件不得为重解析点");
        if (!File.Exists(file) || new FileInfo(file).Length > 2 * 1024 * 1024) throw new InvalidDataException("项目文件不存在或超过大小限制");
        EditorProject project = JsonSerializer.Deserialize(File.ReadAllBytes(file), EditorProjectJsonContext.Default.EditorProject) ?? throw new InvalidDataException("项目文件为空");
        bool needsKeyMigration = string.IsNullOrWhiteSpace(project.Release.CurrentKeyId);
        bool needsIdentityInitialization = project.RegenerateMicroUserOnFirstLoad;
        if (needsIdentityInitialization)
        {
            project.Snapshot.DefaultMicro.User = "u_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
            project.RegenerateMicroUserOnFirstLoad = false;
        }
        ProjectReleaseKeyStore.EnsureProvisioned(project, directory);
        Validate(project);
        if (needsKeyMigration || needsIdentityInitialization) Save(project);
        return project;
    }

    public void Save(EditorProject project)
    {
        project.SynchronizeMicroIdentity();
        Validate(project);
        project.UpdatedAtUtc = DateTimeOffset.UtcNow;
        string directory = GetProjectDirectory(project.Snapshot.ProjectId);
        RejectReparsePath(WorkspaceRoot);
        if (Directory.Exists(directory)) RejectReparsePath(directory); else Directory.CreateDirectory(directory);
        RejectReparsePath(directory);
        string assets = Path.Combine(directory, "Assets");
        if (Directory.Exists(assets)) RejectReparsePath(assets); else Directory.CreateDirectory(assets);
        RejectReparsePath(assets);
        string target = Path.Combine(directory, "project.json");
        string temporary = target + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(project, EditorProjectJsonContext.Default.EditorProject));
            File.Move(temporary, target, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public string GetProjectDirectory(string projectId)
    {
        LauncherSnapshotValidator.ValidateProjectId(projectId);
        string directory = Path.GetFullPath(Path.Combine(WorkspaceRoot, projectId));
        if (!directory.StartsWith(WorkspaceRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("项目路径越界");
        return directory;
    }

    public ImportPreview ImportClientReadOnly(EditorProject project, string clientDirectory)
    {
        string root = Path.GetFullPath(clientDirectory);
        if (!Directory.Exists(root) || !File.Exists(Path.Combine(root, "Client.exe"))) throw new InvalidDataException("所选目录不包含 Client.exe");
        RejectReparsePath(root);
        Dictionary<string, string> values = ReadIni(Path.Combine(root, "Mir2Config.ini"), out List<string> unknown);
        var mapped = new List<string>();
        ApplyInt(values, "Graphics/Resolution", value => project.Snapshot.Defaults.Resolution = value is 1024 or 1280 or 1366 or 1920 ? value : 1024, mapped);
        ApplyBool(values, "Graphics/FullScreen", value => project.Snapshot.Defaults.FullScreen = value, mapped);
        ApplyBool(values, "Graphics/Borderless", value => project.Snapshot.Defaults.Borderless = value, mapped);
        ApplyBool(values, "Graphics/AlwaysOnTop", value => project.Snapshot.Defaults.TopMost = value, mapped);
        ApplyInt(values, "Graphics/MaxFPS", value => project.Snapshot.Defaults.MaxFps = Math.Clamp(value, 30, 240), mapped);
        ApplyInt(values, "Sound/Volume", value => project.Snapshot.Defaults.Volume = Math.Clamp(value, 0, 100), mapped);
        ApplyInt(values, "Sound/Music", value => project.Snapshot.Defaults.MusicVolume = Math.Clamp(value, 0, 100), mapped);
        ApplyString(values, "Network/IPAddress", value => project.Snapshot.Servers[0].Address = value, mapped);
        ApplyInt(values, "Network/Port", value => project.Snapshot.Servers[0].Port = value, mapped);
        ApplyString(values, "Launcher/ServerName", value => project.Snapshot.Servers[0].Name = value, mapped);
        ApplyString(values, "Micro/User", value => project.Snapshot.DefaultMicro.User = value, mapped);
        if (values.TryGetValue("Micro/BaseUrl", out string? microBase) && Uri.TryCreate(microBase, UriKind.Absolute, out Uri? uri) && uri.Scheme == Uri.UriSchemeHttp)
        {
            project.Snapshot.DefaultMicro.Enabled = true; project.Snapshot.DefaultMicro.Address = uri.Host; project.Snapshot.DefaultMicro.Port = uri.Port; mapped.Add("Micro/BaseUrl");
        }
        ImportRemoteManifest(project, Path.Combine(root, "RemoteLaunchManifest.json"), mapped, unknown);
        project.ImportedClientDirectory = root;
        return new ImportPreview(mapped, unknown, values.ContainsKey("Micro/Code") || values.ContainsKey("Game/Password") || values.ContainsKey("Launcher/Password"));
    }

    private static void ImportRemoteManifest(EditorProject project, string file, List<string> mapped, List<string> unknown)
    {
        if (!File.Exists(file)) return;
        if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0 || new FileInfo(file).Length > 1024 * 1024) throw new InvalidDataException("远程区服清单不允许重解析点或超过导入大小限制");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(file), new JsonDocumentOptions { MaxDepth = 16, CommentHandling = JsonCommentHandling.Disallow });
        JsonElement root = document.RootElement;
        var knownRoot = new HashSet<string>(StringComparer.Ordinal) { "version", "maxInstances", "patchUrl", "servers" };
        foreach (JsonProperty property in root.EnumerateObject()) if (!knownRoot.Contains(property.Name)) unknown.Add("RemoteLaunchManifest/" + property.Name);
        if (!root.TryGetProperty("servers", out JsonElement servers) || servers.ValueKind != JsonValueKind.Array) return;
        var imported = new List<LauncherServer>();
        int index = 0;
        foreach (JsonElement item in servers.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) { unknown.Add($"RemoteLaunchManifest/servers[{index}]"); index++; continue; }
            var known = new HashSet<string>(StringComparer.Ordinal) { "name", "serverAddress", "serverPort", "microEnabled", "microAddress", "microPort" };
            foreach (JsonProperty property in item.EnumerateObject()) if (!known.Contains(property.Name)) unknown.Add($"RemoteLaunchManifest/servers[{index}]/{property.Name}");
            if (!item.TryGetProperty("name", out JsonElement name) || name.ValueKind != JsonValueKind.String ||
                !item.TryGetProperty("serverAddress", out JsonElement address) || address.ValueKind != JsonValueKind.String ||
                !item.TryGetProperty("serverPort", out JsonElement port) || !port.TryGetInt32(out int serverPort)) { index++; continue; }
            var server = new LauncherServer { Id = "import-" + (index + 1), Group = "导入区服", Name = name.GetString() ?? "导入区服", Address = address.GetString() ?? "127.0.0.1", Port = serverPort };
            if (item.TryGetProperty("microEnabled", out JsonElement enabled) && enabled.ValueKind is JsonValueKind.True or JsonValueKind.False && enabled.GetBoolean() &&
                item.TryGetProperty("microAddress", out JsonElement microAddress) && microAddress.ValueKind == JsonValueKind.String &&
                item.TryGetProperty("microPort", out JsonElement microPort) && microPort.TryGetInt32(out int value))
                server.MicroOverride = new MicroEndpoint { Enabled = true, Address = microAddress.GetString() ?? string.Empty, Port = value, User = project.Snapshot.DefaultMicro.User };
            imported.Add(server); index++;
        }
        if (imported.Count > 0) { project.Snapshot.Servers = imported; mapped.Add("RemoteLaunchManifest/servers"); }
    }

    private static Dictionary<string, string> ReadIni(string file, out List<string> unknown)
    {
        unknown = new List<string>();
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(file)) return result;
        if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0 || new FileInfo(file).Length > 2 * 1024 * 1024) throw new InvalidDataException("Mir2Config.ini 不允许重解析点或超过 2 MiB");
        byte[] bytes = File.ReadAllBytes(file);
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        string text;
        try { text = new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException) { text = Encoding.GetEncoding(936).GetString(bytes); }
        string section = string.Empty;
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Graphics/Resolution", "Graphics/FullScreen", "Graphics/Borderless", "Graphics/AlwaysOnTop", "Graphics/MaxFPS", "Sound/Volume", "Sound/Music", "Network/IPAddress", "Network/Port", "Launcher/ServerName", "Micro/BaseUrl", "Micro/User", "Micro/Code", "Game/Password", "Launcher/Password" };
        foreach (string raw in text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#')) continue;
            if (line.StartsWith('[') && line.EndsWith(']')) { section = line[1..^1].Trim(); continue; }
            int separator = line.IndexOf('='); if (separator <= 0) { unknown.Add(line); continue; }
            string key = section + "/" + line[..separator].Trim();
            result[key] = line[(separator + 1)..].Trim();
            if (!known.Contains(key)) unknown.Add(key);
        }
        return result;
    }

    private static void ApplyString(Dictionary<string, string> values, string key, Action<string> write, List<string> mapped) { if (values.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value)) { write(value.Trim()); mapped.Add(key); } }
    private static void ApplyInt(Dictionary<string, string> values, string key, Action<int> write, List<string> mapped) { if (values.TryGetValue(key, out string? value) && int.TryParse(value, out int parsed)) { write(parsed); mapped.Add(key); } }
    private static void ApplyBool(Dictionary<string, string> values, string key, Action<bool> write, List<string> mapped) { if (values.TryGetValue(key, out string? value) && bool.TryParse(value, out bool parsed)) { write(parsed); mapped.Add(key); } }

    private static void Validate(EditorProject project)
    {
        if (project.Format != EditorProject.CurrentFormat) throw new InvalidDataException("编辑器项目格式不受支持");
        LauncherSnapshotValidator.Validate(project.Snapshot);
        if (project.Brand.OutputFileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || !project.Brand.OutputFileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("玩家入口文件名无效");
        if (project.Gateway.Port is < 1 or > 65535) throw new InvalidDataException("微端部署参数无效");
    }

    private static void RejectReparsePath(string path)
    {
        string full = Path.GetFullPath(path);
        string? current = Path.GetPathRoot(full);
        foreach (string segment in full[(current?.Length ?? 0)..].Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (segment.Length == 0) continue; current = Path.Combine(current ?? string.Empty, segment);
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("路径不得经过重解析点");
        }
    }
}
