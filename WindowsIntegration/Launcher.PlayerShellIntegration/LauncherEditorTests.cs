using System.IO.Compression;
using System.Security.Cryptography;
using System.Drawing;
using System.Diagnostics;
using Launcher.ThemeRuntime;
using LyoCrystal.LauncherEditor;
using Xunit;

namespace Launcher.PlayerShellIntegration;

public sealed class LauncherEditorTests
{
    [Fact]
    public void OfflineProjectCanBeCreatedSavedLoadedAndRendered()
    {
        using var scope = new EditorTempScope();
        var store = new EditorProjectStore(scope.Dir("workspace"));
        EditorProject project = store.Create("offline-project", "离线启动器", LauncherTemplateKind.Widescreen);
        project.Snapshot.Theme.ServerListMode = ServerListMode.Sidebar;
        project.Snapshot.Announcements.Add(new LauncherAnnouncement { Title = "离线公告", Summary = "无需网络", Date = "2026-08-11" });
        store.Save(project);
        EditorProject loaded = store.Load("offline-project");
        Assert.Equal("离线启动器", loaded.Snapshot.ProjectName);
        Assert.Equal(ServerListMode.Sidebar, loaded.Snapshot.Theme.ServerListMode);
        using Bitmap bitmap = LauncherRuntimeHost.RenderTemplateForEvidence(loaded.Snapshot, store.GetProjectDirectory("offline-project"), 1f);
        Assert.True(bitmap.Width > 1000);
    }

    [Fact]
    public void ClientImportIsReadOnlyMapsRemoteListAndOmitsSecrets()
    {
        using var scope = new EditorTempScope();
        string client = scope.Dir("client");
        File.WriteAllText(Path.Combine(client, "Client.exe"), "placeholder");
        string ini = Path.Combine(client, "Mir2Config.ini");
        File.WriteAllText(ini, "[Graphics]\r\nResolution=1280\r\nFullScreen=True\r\nUnknownVisual=1\r\n[Micro]\r\nBaseUrl=http://10.0.0.2:8080/\r\nUser=player\r\nCode=do-not-copy\r\n");
        string manifest = Path.Combine(client, "RemoteLaunchManifest.json");
        File.WriteAllText(manifest, "{\"version\":1,\"maxInstances\":1,\"patchUrl\":\"\",\"servers\":[{\"name\":\"导入一区\",\"serverAddress\":\"10.0.0.1\",\"serverPort\":7000,\"microEnabled\":true,\"microAddress\":\"10.0.0.2\",\"microPort\":8080}]}");
        string before = Hash(ini) + Hash(manifest);
        DateTime iniTime = File.GetLastWriteTimeUtc(ini);
        var store = new EditorProjectStore(scope.Dir("workspace"));
        EditorProject project = store.Create("import-project", "导入项目", LauncherTemplateKind.Classic);
        ImportPreview preview = store.ImportClientReadOnly(project, client);
        store.Save(project);
        Assert.Equal(before, Hash(ini) + Hash(manifest));
        Assert.Equal(iniTime, File.GetLastWriteTimeUtc(ini));
        Assert.True(preview.SensitiveValuesOmitted);
        Assert.Contains("Graphics/UnknownVisual", preview.UnknownFields);
        Assert.Equal("导入一区", project.Snapshot.Servers.Single().Name);
        Assert.DoesNotContain("do-not-copy", File.ReadAllText(Path.Combine(store.GetProjectDirectory("import-project"), "project.json")), StringComparison.Ordinal);
    }

    [Fact]
    public void GatewayPackageContainsRunnableTemplateAndNonSecretProjectConfig()
    {
        using var scope = new EditorTempScope();
        using var template = new MemoryStream();
        using (var zip = new ZipArchive(template, ZipArchiveMode.Create, leaveOpen: true))
        {
            using StreamWriter writer = new(zip.CreateEntry("LyoCrystal.MicroGateway.App.exe").Open());
            writer.Write("MZ-placeholder");
        }
        template.Position = 0;
        EditorProject project = new() { Snapshot = LauncherTemplateCatalog.Create(LauncherTemplateKind.Compact) };
        project.Snapshot.ProjectId = "gateway-project";
        string output = Path.Combine(scope.Root, "gateway.zip");
        DeploymentPackageBuilder.CreateGatewayPackage(project, template, output, "gateway-code-not-plain");
        using var result = ZipFile.OpenRead(output);
        Assert.NotNull(result.GetEntry("LyoCrystal.MicroGateway.App.exe"));
        ZipArchiveEntry config = Assert.IsType<ZipArchiveEntry>(result.GetEntry("gateway-project.json"));
        using var reader = new StreamReader(config.Open());
        string json = reader.ReadToEnd();
        Assert.Contains("gateway-project", json, StringComparison.Ordinal);
        Assert.DoesNotContain("code", json, StringComparison.OrdinalIgnoreCase);
        ZipArchiveEntry secret = Assert.IsType<ZipArchiveEntry>(result.GetEntry("gateway-secret.import"));
        using var secretBytes = new MemoryStream(); using (Stream input = secret.Open()) input.CopyTo(secretBytes);
        Assert.Equal("gateway-code-not-plain", Shared.Security.MicroCredentialEnvelope.Open("gateway-project", secretBytes.ToArray()));
        Assert.DoesNotContain("gateway-code-not-plain", File.ReadAllText(output), StringComparison.Ordinal);
    }

    [Fact]
    public void AllSevenGmOperatingStatusesAreAvailable()
    {
        Assert.Equal(new[] { "Normal", "Busy", "Recommended", "NewServer", "Maintenance", "ComingSoon", "Hidden" }, Enum.GetNames<ServerOperatingStatus>());
    }

    [Fact]
    public void ServerMicroOverrideRequiresSharedCredentialEvenWhenDefaultIsDisabled()
    {
        EditorProject project = new() { Snapshot = LauncherTemplateCatalog.Create(LauncherTemplateKind.Compact) };
        project.Snapshot.DefaultMicro.Enabled = false;
        project.Snapshot.Servers[0].MicroOverride = new MicroEndpoint { Enabled = true, Address = "10.0.0.8", Port = 8080, User = "player" };
        Assert.True(PlayerArtifactBuilder.RequiresMicroCredential(project));
    }

    [Fact]
    public void ProjectSynchronizesOneMicroUserAcrossPlayerAndGateway()
    {
        EditorProject project = new() { Snapshot = LauncherTemplateCatalog.Create(LauncherTemplateKind.Compact) };
        project.Snapshot.DefaultMicro.User = "single-user";
        project.Gateway.User = "stale-gateway-user";
        project.Snapshot.Servers[0].MicroOverride = new MicroEndpoint { Enabled = true, Address = "10.0.0.8", Port = 8080, User = "stale-server-user" };
        project.SynchronizeMicroIdentity();
        Assert.Equal("single-user", project.Gateway.User);
        Assert.Equal("single-user", project.Snapshot.Servers[0].MicroOverride!.User);
    }

    [Fact]
    public void NewProjectsGenerateDifferentMicroUsers()
    {
        using var scope = new EditorTempScope();
        var store = new EditorProjectStore(scope.Dir("generated-users"));
        EditorProject first = store.Create("first-project", "项目一", LauncherTemplateKind.Classic);
        EditorProject second = store.Create("second-project", "项目二", LauncherTemplateKind.Classic);
        Assert.StartsWith("u_", first.Snapshot.DefaultMicro.User, StringComparison.Ordinal);
        Assert.NotEqual(first.Snapshot.DefaultMicro.User, second.Snapshot.DefaultMicro.User);
        Assert.Equal(first.Snapshot.DefaultMicro.User, first.Gateway.User);
    }

    [Fact]
    public void PreflightRejectsTruncatedServerName()
    {
        using var scope = new EditorTempScope();
        var store = new EditorProjectStore(scope.Dir("truncated-server"));
        EditorProject project = store.Create("truncated-project", "截断检查", LauncherTemplateKind.Compact);
        project.Snapshot.Servers[0].Name = new string('区', 80);
        IReadOnlyList<string> issues = EditorPreflightValidator.Validate(project, store.GetProjectDirectory("truncated-project"));
        Assert.Contains(issues, issue => issue.Contains("文字截断", StringComparison.Ordinal));
    }

    [Fact]
    public void BmpThemeAssetIsConvertedToPng()
    {
        using var scope = new EditorTempScope();
        string projectRoot = scope.Dir("bmp-project");
        string source = Path.Combine(scope.Root, "button.bmp");
        using (var image = new Bitmap(20, 10)) image.Save(source, System.Drawing.Imaging.ImageFormat.Bmp);
        string relative = ThemeAssetImporter.Import(projectRoot, source);
        Assert.Equal("Assets/button.png", relative);
        using Image converted = Image.FromFile(Path.Combine(projectRoot, relative));
        Assert.Equal(System.Drawing.Imaging.ImageFormat.Png.Guid, converted.RawFormat.Guid);
    }

    [Fact]
    public void ImportRejectsOversizedIniBeforeReadingIt()
    {
        using var scope = new EditorTempScope();
        string client = scope.Dir("oversized-client");
        File.WriteAllText(Path.Combine(client, "Client.exe"), "placeholder");
        using (var stream = new FileStream(Path.Combine(client, "Mir2Config.ini"), FileMode.CreateNew, FileAccess.Write)) stream.SetLength(2L * 1024 * 1024 + 1);
        var store = new EditorProjectStore(scope.Dir("workspace"));
        EditorProject project = store.Create("oversized-import", "超限导入", LauncherTemplateKind.Classic);
        Assert.Throws<InvalidDataException>(() => store.ImportClientReadOnly(project, client));
    }

    [Fact]
    public void ProjectCreationRejectsExistingJunctionBeforeWritingAssets()
    {
        using var scope = new EditorTempScope();
        string workspace = scope.Dir("junction-workspace");
        string outside = scope.Dir("junction-outside");
        string link = Path.Combine(workspace, "linked-project");
        CreateJunction(link, outside);
        try
        {
            var store = new EditorProjectStore(workspace);
            Assert.Throws<InvalidDataException>(() => store.Create("linked-project", "越界项目", LauncherTemplateKind.Classic));
            Assert.Empty(Directory.EnumerateFileSystemEntries(outside));
        }
        finally { if (Directory.Exists(link)) Directory.Delete(link); }
    }

    [Fact]
    public void ButtonBaseImageAndFourStateOverridesRenderFromProjectAssets()
    {
        using var scope = new EditorTempScope();
        var store = new EditorProjectStore(scope.Dir("workspace"));
        EditorProject project = store.Create("button-images", "按钮四态", LauncherTemplateKind.Compact);
        string assets = Path.Combine(store.GetProjectDirectory("button-images"), "Assets");
        foreach ((string name, Color color) in new[] { ("base.png", Color.Goldenrod), ("hover.png", Color.Gold), ("pressed.png", Color.DarkGoldenrod), ("disabled.png", Color.Gray) })
        {
            using var image = new Bitmap(180, 54); using Graphics graphics = Graphics.FromImage(image); graphics.Clear(color); image.Save(Path.Combine(assets, name));
        }
        project.Snapshot.Theme.LaunchButtonImage = "Assets/base.png";
        project.Snapshot.Theme.LaunchButtonHoverImage = "Assets/hover.png";
        project.Snapshot.Theme.LaunchButtonPressedImage = "Assets/pressed.png";
        project.Snapshot.Theme.LaunchButtonDisabledImage = "Assets/disabled.png";
        project.Snapshot.Theme.Controls.Add(new LauncherControlOverride { Id = LauncherControlId.LaunchButton, X = 540, Y = 370, Width = 180, Height = 54, Visible = true, ForeColor = "#FFFFFF", BackColor = "#222222", FontSize = 11, Bold = true });
        store.Save(project);
        using Bitmap rendered = LauncherRuntimeHost.RenderTemplateForEvidence(project.Snapshot, store.GetProjectDirectory("button-images"), 1f);
        Assert.True(rendered.Width > 700);
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static void CreateJunction(string link, string target)
    {
        using Process process = Process.Start(new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            ArgumentList = { "/d", "/c", "mklink", "/J", link, target },
        }) ?? throw new InvalidOperationException("无法启动 junction 夹具");
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException("无法创建 junction 夹具");
    }

    private sealed class EditorTempScope : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "launcher-editor-tests", Guid.NewGuid().ToString("N"));
        public EditorTempScope() => Directory.CreateDirectory(Root);
        public string Dir(string name) { string path = Path.Combine(Root, name); Directory.CreateDirectory(path); return path; }
        public void Dispose() { try { Directory.Delete(Root, true); } catch { } }
    }
}
