using System.IO.Compression;
using System.Security.Cryptography;
using System.Drawing;
using System.Diagnostics;
using Launcher.ThemeRuntime;
using LyoCrystal.LauncherEditor;
using Shared.Security;
using LyoCrystal.MicroGateway;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Launcher.PlayerShell;
using System.ComponentModel;
using System.Windows.Forms;
using Xunit;

namespace Launcher.PlayerShellIntegration;

public sealed class LauncherEditorTests
{
    [Fact]
    public void CanvasDocumentMaterializesRuntimeLayoutWithoutManualCoordinates()
    {
        LauncherSnapshot snapshot = LauncherTemplateCatalog.Create(LauncherTemplateKind.Widescreen);
        IReadOnlyDictionary<LauncherControlId, Rectangle> runtime = Enum.GetValues<LauncherControlId>()
            .ToDictionary(id => id, id => new Rectangle(20 + (int)id * 8, 30 + (int)id * 6, 120, 36));

        var document = new LauncherCanvasDocument(snapshot.Theme, runtime);

        Assert.Equal(Enum.GetValues<LauncherControlId>().Length, snapshot.Theme.Controls.Count);
        Assert.Equal(runtime[LauncherControlId.LaunchButton], document.GetBounds(LauncherControlId.LaunchButton));
        Assert.False(document.IsDirty);
    }

    [Fact]
    public void CanvasMoveResizeAndPropertyChangesRoundTripThroughUndoRedo()
    {
        LauncherSnapshot snapshot = LauncherTemplateCatalog.Create(LauncherTemplateKind.Compact);
        var document = CanvasDocument(snapshot);
        document.Select([LauncherControlId.LaunchButton]);
        Rectangle original = document.GetBounds(LauncherControlId.LaunchButton);

        document.MoveSelection(17, 11, snap: false);
        document.ResizeSelection(25, 9, snap: false);
        document.ChangeSelectionStyle(new LauncherCanvasStyleChange(ForeColor: "#112233", BackColor: "#445566", FontName: "Microsoft YaHei UI", FontSize: 12, Bold: true, OpacityPercent: 85));
        Rectangle changed = document.GetBounds(LauncherControlId.LaunchButton);

        Assert.Equal(new Rectangle(original.X + 17, original.Y + 11, original.Width + 25, original.Height + 9), changed);
        Assert.Equal("#112233", snapshot.Theme.Controls.Single(x => x.Id == LauncherControlId.LaunchButton).ForeColor);
        Assert.True(document.Undo());
        Assert.True(document.Undo());
        Assert.True(document.Undo());
        Assert.Equal(original, document.GetBounds(LauncherControlId.LaunchButton));
        Assert.True(document.Redo());
        Assert.True(document.Redo());
        Assert.True(document.Redo());
        Assert.Equal(changed, document.GetBounds(LauncherControlId.LaunchButton));
    }

    [Fact]
    public void CanvasMultiSelectionAlignDistributeSnapLayerLockHideAndRestoreAreUndoable()
    {
        LauncherSnapshot snapshot = LauncherTemplateCatalog.Create(LauncherTemplateKind.Widescreen);
        var document = CanvasDocument(snapshot);
        LauncherControlId[] ids = [LauncherControlId.ServerList, LauncherControlId.Announcements, LauncherControlId.LaunchButton];
        document.Select(ids);
        document.SetBounds(LauncherControlId.ServerList, new Rectangle(10, 20, 100, 40));
        document.SetBounds(LauncherControlId.Announcements, new Rectangle(180, 70, 100, 40));
        document.SetBounds(LauncherControlId.LaunchButton, new Rectangle(390, 110, 100, 40));
        document.AlignSelection(LauncherCanvasAlignment.Top);
        document.DistributeSelection(LauncherCanvasDistribution.Horizontal);
        document.SetLocked(ids, true);
        Assert.False(document.MoveSelection(50, 50, snap: true));
        document.SetLocked(ids, false);
        document.BringSelectionForward();
        document.SetVisible(ids, false);

        Assert.All(ids, id => Assert.False(snapshot.Theme.Controls.Single(x => x.Id == id).Visible));
        Assert.True(document.Undo());
        Assert.All(ids, id => Assert.True(snapshot.Theme.Controls.Single(x => x.Id == id).Visible));
        Assert.Equal(document.GetBounds(ids[0]).Y, document.GetBounds(ids[2]).Y);
        Assert.True(snapshot.Theme.Controls.FindIndex(x => x.Id == ids[2]) > 0);
    }

    [Fact]
    public void CanvasDeleteAndAddUseFixedControlCatalogAndRemainUndoable()
    {
        LauncherSnapshot snapshot = LauncherTemplateCatalog.Create(LauncherTemplateKind.Classic);
        var document = CanvasDocument(snapshot);
        document.Select([LauncherControlId.DiagnoseButton]);
        Assert.True(document.DeleteSelection());
        Assert.False(snapshot.Theme.Controls.Single(x => x.Id == LauncherControlId.DiagnoseButton).Visible);
        Assert.True(document.Undo());
        Assert.True(snapshot.Theme.Controls.Single(x => x.Id == LauncherControlId.DiagnoseButton).Visible);
        document.AddOrShow(LauncherControlId.DiagnoseButton);
        Assert.True(snapshot.Theme.Controls.Single(x => x.Id == LauncherControlId.DiagnoseButton).Visible);
    }

    [Fact]
    public void CanvasSnappingUsesCanvasAndPeerEdgesWithoutLeavingCanvas()
    {
        LauncherSnapshot snapshot = LauncherTemplateCatalog.Create(LauncherTemplateKind.Compact);
        var document = CanvasDocument(snapshot);
        document.SetBounds(LauncherControlId.ServerList, new Rectangle(0, 20, 100, 40));
        document.SetBounds(LauncherControlId.LaunchButton, new Rectangle(108, 20, 100, 40));
        document.Select([LauncherControlId.LaunchButton]);

        document.MoveSelection(-3, 0, snap: true);

        Assert.Equal(100, document.GetBounds(LauncherControlId.LaunchButton).X);
        Assert.Contains(document.SnapGuides, guide => guide.Vertical && guide.Position == 100);
        document.MoveSelection(-9999, -9999, snap: true);
        Assert.Equal(new Point(0, 0), document.GetBounds(LauncherControlId.LaunchButton).Location);
    }

    [Fact]
    public void CanvasLockBlocksBoundsAndStyleChangesUntilUnlocked()
    {
        LauncherSnapshot snapshot = LauncherTemplateCatalog.Create(LauncherTemplateKind.Classic);
        var document = CanvasDocument(snapshot);
        document.Select([LauncherControlId.LaunchButton]);
        Rectangle original = document.GetBounds(LauncherControlId.LaunchButton);
        string originalColor = snapshot.Theme.Controls.Single(x => x.Id == LauncherControlId.LaunchButton).ForeColor;
        document.SetLocked([LauncherControlId.LaunchButton], true);

        document.SetBounds(LauncherControlId.LaunchButton, new Rectangle(1, 1, 20, 20));
        document.ChangeSelectionStyle(new LauncherCanvasStyleChange(ForeColor: "#010203"));

        Assert.Equal(original, document.GetBounds(LauncherControlId.LaunchButton));
        Assert.Equal(originalColor, snapshot.Theme.Controls.Single(x => x.Id == LauncherControlId.LaunchButton).ForeColor);
    }

    [Fact]
    public void CanvasEditorRenderingUsesClientAreaCoordinatesWithoutWindowChromeOffset()
    {
        using var scope = new EditorTempScope();
        LauncherSnapshot snapshot = LauncherTemplateCatalog.Create(LauncherTemplateKind.Classic);
        using Bitmap canvas = LauncherRuntimeHost.RenderCanvasForEditor(snapshot, scope.Root);
        IReadOnlyDictionary<LauncherControlId, Rectangle> layout = LauncherRuntimeHost.CaptureControlLayoutForEditor(snapshot, scope.Root);

        Assert.Equal(new Size(snapshot.Theme.CanvasWidth, snapshot.Theme.CanvasHeight), canvas.Size);
        Assert.All(layout.Values, bounds => Assert.True(new Rectangle(Point.Empty, canvas.Size).Contains(bounds)));
    }

    [Fact]
    public void EditorShellUsesFiveStableModesAndSingleObjectTreeWorkspace()
    {
        using var scope = new EditorTempScope();
        var store = new EditorProjectStore(scope.Root);
        store.Create("shell-layout", "作者工具外壳", LauncherTemplateKind.Classic);
        using var form = new MainForm(store) { StartPosition = FormStartPosition.Manual, Location = new Point(-32000, -32000), Size = new Size(1280, 800) };
        form.Show();
        form.PrepareCanvasEvidence();
        (int objectTreeWidth, int propertiesWidth, Size canvasSize) = form.CaptureDesignWorkspaceLayoutForEvidence();

        Assert.Equal(["概览", "设计", "内容", "交付", "诊断"], form.CaptureWorkspaceModesForEvidence());
        Assert.Equal(190, objectTreeWidth);
        Assert.Equal(250, propertiesWidth);
        Assert.Equal(new Size(801, 554), canvasSize);
        form.Hide();
    }

    [Fact]
    public void CanvasLockPersistsInEditorProjectWithoutChangingPlayerSnapshotContract()
    {
        using var scope = new EditorTempScope();
        var store = new EditorProjectStore(scope.Root);
        EditorProject project = store.Create("canvas-metadata", "画布元数据", LauncherTemplateKind.Classic);
        var document = CanvasDocument(project.Snapshot, project.CanvasControls);
        document.SetLocked([LauncherControlId.LaunchButton], true);
        document.BringSelectionForward();
        store.Save(project);

        EditorProject loaded = store.Load("canvas-metadata");
        var restored = CanvasDocument(loaded.Snapshot, loaded.CanvasControls);
        string playerSnapshotJson = JsonSerializer.Serialize(loaded.Snapshot, LauncherSnapshotJsonContext.Default.LauncherSnapshot);

        Assert.True(restored.IsLocked(LauncherControlId.LaunchButton));
        Assert.DoesNotContain("Locked", playerSnapshotJson, StringComparison.Ordinal);
        Assert.DoesNotContain("ZIndex", playerSnapshotJson, StringComparison.Ordinal);
    }

    private static LauncherCanvasDocument CanvasDocument(LauncherSnapshot snapshot)
        => CanvasDocument(snapshot, null);

    private static LauncherCanvasDocument CanvasDocument(LauncherSnapshot snapshot, IList<LauncherCanvasControlState>? states)
    {
        IReadOnlyDictionary<LauncherControlId, Rectangle> baseline = Enum.GetValues<LauncherControlId>()
            .ToDictionary(id => id, id => new Rectangle(20 + (int)id * 30, 20 + (int)id * 20, 100, 32));
        return new LauncherCanvasDocument(snapshot.Theme, baseline, states);
    }

    [Fact]
    public void ClassicTemplateWithoutImagesStillRendersVisibleBuiltInSkin()
    {
        using var scope = new EditorTempScope();
        LauncherSnapshot snapshot = LauncherTemplateCatalog.Create(LauncherTemplateKind.Classic);
        Assert.Empty(snapshot.Theme.BackgroundImage);
        Assert.Empty(snapshot.Theme.LaunchButtonImage);
        using Bitmap rendered = LauncherForm.BuildClassicBackground(new Size(snapshot.Theme.CanvasWidth, snapshot.Theme.CanvasHeight));
        Assert.Equal(new Size(801, 554), rendered.Size);
        Color center = rendered.GetPixel(rendered.Width / 2, rendered.Height / 2);
        Color header = rendered.GetPixel(100, 25);
        Assert.True(center.B > center.R / 2);
        Assert.NotEqual(center.ToArgb(), header.ToArgb());
        string output = Path.Combine(scope.Root, "classic-no-assets.png");
        rendered.Save(output, System.Drawing.Imaging.ImageFormat.Png);
        Assert.True(new FileInfo(output).Length > 4_000);
    }

    [Fact]
    public void ClassicBackgroundIsCenteredWithoutResampling()
    {
        using Bitmap originalSize = LauncherForm.BuildClassicBackground(new Size(800, 550));
        using Bitmap classicCanvas = LauncherForm.BuildClassicBackground(new Size(801, 554));
        for (int y = 0; y < originalSize.Height; y++)
            for (int x = 0; x < originalSize.Width; x++)
                Assert.Equal(originalSize.GetPixel(x, y), classicCanvas.GetPixel(x, y + 2));
    }

    [Fact]
    public void QuickLauncherNameAlsoControlsGeneratedFileName()
    {
        EditorProject project = new();
        QuickProductionPanel.ApplyLauncherName(project, "酷明传奇");
        Assert.Equal("酷明传奇", project.Snapshot.ProjectName);
        Assert.Equal("酷明传奇.exe", project.Brand.OutputFileName);
        QuickProductionPanel.ApplyLauncherName(project, "酷明:传奇");
        Assert.Equal("酷明_传奇.exe", project.Brand.OutputFileName);
        Assert.Equal("未命名启动器.exe", QuickProductionPanel.ToExecutableFileName("..."));
        Assert.Equal("启动器-CON.exe", QuickProductionPanel.ToExecutableFileName("CON"));
    }

    [Fact]
    public void UnmodifiedClassicTemplateUsesOriginalLauncher()
    {
        LauncherSnapshot snapshot = LauncherTemplateCatalog.Create(LauncherTemplateKind.Classic);
        Assert.True(LauncherRuntimeHost.UsesOriginalClassicLauncher(snapshot));
        snapshot.Theme.BackgroundImage = "Assets/custom.png";
        Assert.False(LauncherRuntimeHost.UsesOriginalClassicLauncher(snapshot));
        snapshot.Theme.BackgroundImage = string.Empty;
        snapshot.Theme.LaunchButtonHoverImage = "Assets/hover.png";
        Assert.False(LauncherRuntimeHost.UsesOriginalClassicLauncher(snapshot));
    }

    [Fact]
    public async Task EnabledMicroMustBeReachableBeforeGameLaunch()
    {
        int unavailablePort;
        using (var listener = new TcpListener(IPAddress.Loopback, 0))
        {
            listener.Start();
            unavailablePort = ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        var endpoint = new MicroEndpoint { Enabled = true, Address = "127.0.0.1", Port = unavailablePort, User = "player" };
        Assert.False(await MicroGatewayReadiness.ProbeAsync(endpoint, CancellationToken.None));
        endpoint.Enabled = false;
        Assert.True(await MicroGatewayReadiness.ProbeAsync(endpoint, CancellationToken.None));
    }

    [Fact]
    public async Task MissingLoginLibrariesAreDownloadedBeforeGameLaunch()
    {
        using var scope = new EditorTempScope();
        string resources = scope.Dir("gateway-resources");
        foreach (string relative in new[] { "Data/Title.Lib", "Data/ChrSel.Lib", "Data/Prguse.Lib" })
        {
            string path = Path.Combine(resources, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, System.Text.Encoding.ASCII.GetBytes("library-" + relative));
        }
        int port = FreePort();
        await using var host = new StaticFileHost(resources, port);
        await host.StartAsync();
        string client = scope.Dir("client");
        var endpoint = new MicroEndpoint { Enabled = true, Address = "127.0.0.1", Port = port, User = "player" };
        LauncherCoreResource[] manifest = new[] { "Title.Lib", "ChrSel.Lib", "Prguse.Lib" }.Select(file =>
        {
            string path = Path.Combine(resources, "Data", file);
            return new LauncherCoreResource { Path = "Data/" + file, Size = new FileInfo(path).Length, Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant() };
        }).ToArray();
        Assert.True(await MicroGatewayReadiness.EnsureCoreLibrariesAsync(endpoint, "test-project", client,
            manifest, null, CancellationToken.None));
        Assert.All(new[] { "Title.Lib", "ChrSel.Lib", "Prguse.Lib" }, file =>
            Assert.True(new FileInfo(Path.Combine(client, "Data", file)).Length > 0));
        Assert.Empty(Directory.EnumerateFiles(client, "*.downloading-*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task PrimaryMicroFailureFallsBackToBackupForLoginLibraries()
    {
        using var scope = new EditorTempScope();
        string resources = scope.Dir("backup-gateway-resources");
        foreach (string relative in new[] { "Data/Title.Lib", "Data/ChrSel.Lib", "Data/Prguse.Lib" })
        {
            string path = Path.Combine(resources, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, System.Text.Encoding.ASCII.GetBytes("backup-" + relative));
        }
        int unavailablePort = FreePort();
        int backupPort = FreePort();
        await using var host = new StaticFileHost(resources, backupPort);
        await host.StartAsync();
        string client = scope.Dir("fallback-client");
        var endpoint = new MicroEndpoint
        {
            Enabled = true,
            Address = "127.0.0.1",
            Port = unavailablePort,
            BackupAddress = "127.0.0.1",
            BackupPort = backupPort,
            User = "player",
        };
        LauncherCoreResource[] manifest = new[] { "Title.Lib", "ChrSel.Lib", "Prguse.Lib" }.Select(file =>
        {
            string path = Path.Combine(resources, "Data", file);
            return new LauncherCoreResource { Path = "Data/" + file, Size = new FileInfo(path).Length, Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant() };
        }).ToArray();

        Assert.True(await MicroGatewayReadiness.EnsureCoreLibrariesAsync(endpoint, "fallback-project", client, manifest, null, CancellationToken.None));
        Assert.All(manifest, resource => Assert.Equal(resource.Sha256, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(client, resource.Path.Replace('/', Path.DirectorySeparatorChar))))).ToLowerInvariant()));
    }

    [Fact]
    public async Task ValidLocalLoginLibrariesDoNotRequireRunningMicroGateway()
    {
        using var scope = new EditorTempScope();
        string client = scope.Dir("local-client");
        var resources = new List<LauncherCoreResource>();
        foreach (string file in new[] { "Title.Lib", "ChrSel.Lib", "Prguse.Lib" })
        {
            string path = Path.Combine(client, "Data", file);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "local-" + file);
            resources.Add(new LauncherCoreResource { Path = "Data/" + file, Size = new FileInfo(path).Length, Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant() });
        }
        var endpoint = new MicroEndpoint { Enabled = true, Address = "127.0.0.1", Port = 1, User = "player" };
        Assert.True(await MicroGatewayReadiness.EnsureCoreLibrariesAsync(endpoint, "local-project", client, resources, null, CancellationToken.None));
    }

    [Fact]
    public void EditorChineseCatalogCoversEveryVisibleChoice()
    {
        string[] texts = Enum.GetValues<LauncherTemplateKind>().Select(EditorChineseText.Template)
            .Concat(Enum.GetValues<ServerListMode>().Select(EditorChineseText.ServerList))
            .Concat(Enum.GetValues<AnnouncementDisplayMode>().Select(EditorChineseText.Announcement))
            .Concat(Enum.GetValues<ClientDeliveryMode>().Select(EditorChineseText.Delivery))
            .Concat(Enum.GetValues<PlayerUpdateMode>().Select(EditorChineseText.Update))
            .Concat(Enum.GetValues<ServerOperatingStatus>().Select(EditorChineseText.ServerStatus))
            .Concat(Enum.GetValues<LauncherAction>().Select(EditorChineseText.Action))
            .Concat(Enum.GetValues<LauncherControlId>().Select(EditorChineseText.Control)).ToArray();
        Assert.All(texts, text => { Assert.NotEmpty(text); Assert.DoesNotMatch("[A-Za-z]", text); });
        TypeConverter delivery = TypeDescriptor.GetProperties(typeof(ProjectBrandPropertyView))[nameof(ProjectBrandPropertyView.DeliveryMode)]!.Converter;
        Assert.Equal("微端按需下载（推荐）", delivery.ConvertToString(ClientDeliveryMode.MicroOnDemand));
        var boolean = new ChineseBooleanConverter();
        Assert.Equal("是", boolean.ConvertToString(true)); Assert.Equal("否", boolean.ConvertToString(false));
    }

    [Fact]
    public void ProjectCreationOptionsPersistAllWizardDomains()
    {
        using var scope = new EditorTempScope();
        var store = new EditorProjectStore(scope.Dir("workspace"));
        EditorProject project = store.Create(new EditorProjectCreationOptions
        {
            ProjectId = "wizard-project", ProjectName = "向导项目", Template = LauncherTemplateKind.Widescreen,
            CompanyName = "测试公司", RemoteReleaseBaseUrl = "http://release.example.test/launcher/",
            ServerAddress = "10.0.0.8", ServerPort = 7010, MicroAddress = "10.0.0.9", MicroPort = 8088,
            BackupMicroAddress = "10.0.0.10", BackupMicroPort = 8089, Resolution = 1920, FullScreen = true,
            AnnouncementTitle = "开服公告", AnnouncementSummary = "欢迎", PlayerUpdateMode = PlayerUpdateMode.Required,
            GatewayCacheDirectory = "GatewayCache", GatewayMemoryCacheMb = 256, GatewayDiskCacheMb = 4096,
            DeliveryMode = ClientDeliveryMode.FullClient, ServerListMode = ServerListMode.Sidebar,
        });
        EditorProject loaded = store.Load(project.Snapshot.ProjectId);
        Assert.Equal("测试公司", loaded.Brand.CompanyName);
        Assert.Equal("10.0.0.8", loaded.Snapshot.Servers[0].Address);
        Assert.Equal(ClientDeliveryMode.FullClient, loaded.DeliveryMode);
        Assert.Equal(ServerListMode.Sidebar, loaded.Snapshot.Theme.ServerListMode);
        Assert.Equal("10.0.0.10", loaded.Snapshot.DefaultMicro.BackupAddress);
        Assert.Equal(1920, loaded.Snapshot.Defaults.Resolution);
        Assert.Equal("开服公告", loaded.Snapshot.Announcements[0].Title);
        Assert.Equal(PlayerUpdateMode.Required, loaded.Release.PlayerUpdateMode);
        Assert.Equal(4096, loaded.Gateway.DiskCacheMb);
    }

    [Fact]
    public void FullClientDeliveryPackageContainsClientAndSinglePlayerEntry()
    {
        using var scope = new EditorTempScope();
        string client = scope.Dir("client");
        File.WriteAllText(Path.Combine(client, "Client.exe"), "client");
        File.WriteAllText(Path.Combine(client, "launcher-capabilities.json"), "{\"product\":\"LyoCrystal\",\"launchArgumentsVersion\":1}");
        Directory.CreateDirectory(Path.Combine(client, "Data"));
        File.WriteAllText(Path.Combine(client, "Data", "Map.dat"), "resource");
        var store = new EditorProjectStore(scope.Dir("full-client-workspace"));
        EditorProject project = store.Create("full-client", "完整客户端", LauncherTemplateKind.Classic);
        project.DeliveryMode = ClientDeliveryMode.FullClient; project.ImportedClientDirectory = client;
        project.Snapshot.TrustedReleaseKeys = new List<BootstrapManifestTrustedKey>
        {
            new() { KeyId = project.Release.CurrentKeyId, SubjectPublicKeyInfo = project.Release.CurrentPublicKey, NotBeforeSequence = project.Release.CurrentKeyNotBeforeSequence },
            new() { KeyId = project.Release.NextKeyId, SubjectPublicKeyInfo = project.Release.NextPublicKey, NotBeforeSequence = project.Release.NextKeyNotBeforeSequence },
        };
        string shell = Path.Combine(scope.Root, "shell.exe"); File.WriteAllBytes(shell, "MZ-test-shell"u8.ToArray());
        string payload = scope.Dir("full-player-payload"); File.WriteAllText(Path.Combine(payload, "Client.exe"), "client");
        string builtIn = Directory.CreateDirectory(Path.Combine(payload, "Launcher", "BuiltIn")).FullName;
        File.WriteAllBytes(Path.Combine(builtIn, "launcher-snapshot.json"), JsonSerializer.SerializeToUtf8Bytes(project.Snapshot, LauncherSnapshotJsonContext.Default.LauncherSnapshot));
        string entry = Path.Combine(scope.Root, "玩家入口.exe"); PlayerPayloadPackage.Create(shell, payload, entry, "Client.exe");
        string output = Path.Combine(scope.Root, "full.zip");
        FullClientDistributionBuilder.Create(project, entry, output);
        using var archive = System.IO.Compression.ZipFile.OpenRead(output);
        Assert.Contains(archive.Entries, item => item.FullName == "Client/Client.exe");
        Assert.Contains(archive.Entries, item => item.FullName == "玩家入口.exe");
    }
    [Fact]
    public void RepositorySampleProjectCanBeCopiedAndOpened()
    {
        using var scope = new EditorTempScope();
        string repository = FindRepositoryRoot(AppContext.BaseDirectory);
        string source = Path.Combine(repository, "Docs", "Examples", "launcher-editor-sample", "project.json");
        string projectRoot = Path.Combine(scope.Dir("sample-workspace"), "launcher-editor-sample");
        Directory.CreateDirectory(projectRoot);
        File.Copy(source, Path.Combine(projectRoot, "project.json"));

        var store = new EditorProjectStore(Path.GetDirectoryName(projectRoot)!);
        EditorProject project = store.Load("launcher-editor-sample");

        Assert.Equal("传奇启动器示例", project.Snapshot.ProjectName);
        Assert.Equal(ServerListMode.Sidebar, project.Snapshot.Theme.ServerListMode);
        Assert.Equal(2, project.Snapshot.Servers.Count);
        Assert.StartsWith("u_", project.Snapshot.DefaultMicro.User, StringComparison.Ordinal);
        Assert.NotEqual("u_sample_pending_initialization", project.Snapshot.DefaultMicro.User);
        Assert.Equal(project.Snapshot.DefaultMicro.User, project.Gateway.User);
        Assert.False(project.RegenerateMicroUserOnFirstLoad);
        Assert.True(ProjectReleaseKeyStore.HasPrivateKeys(project, projectRoot));
    }

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
    public void PreflightRejectsMicroOverrideWithDifferentResourceIdentity()
    {
        using var scope = new EditorTempScope();
        var store = new EditorProjectStore(scope.Dir("identity-workspace"));
        EditorProject project = store.Create("identity-project", "身份项目", LauncherTemplateKind.Classic);
        project.Snapshot.Servers[0].MicroOverride = new MicroEndpoint { Enabled = true, Address = "127.0.0.1", Port = 8081, ResourceVersion = "other", SigningIdentity = "other-key" };
        IReadOnlyList<string> issues = EditorPreflightValidator.Validate(project, store.GetProjectDirectory(project.Snapshot.ProjectId));
        Assert.Contains(issues, item => item.Contains("资源版本或签名身份", StringComparison.Ordinal));
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
    public void BmpThemeAssetCanKeepOriginalWhenOptimizationIsDisabled()
    {
        using var scope = new EditorTempScope();
        string projectRoot = scope.Dir("bmp-original-project");
        string source = Path.Combine(scope.Root, "button-original.bmp");
        using (var image = new Bitmap(20, 10)) image.Save(source, System.Drawing.Imaging.ImageFormat.Bmp);

        string relative = ThemeAssetImporter.Import(projectRoot, source, optimize: false);

        Assert.Equal("Assets/button-original.bmp", relative);
        using Image copied = Image.FromFile(Path.Combine(projectRoot, relative));
        Assert.Equal(System.Drawing.Imaging.ImageFormat.Bmp.Guid, copied.RawFormat.Guid);
    }

    [Fact]
    public void ThemeTemplatePackageRoundTripsAppearanceWithoutProjectSecrets()
    {
        using var scope = new EditorTempScope();
        var sourceStore = new EditorProjectStore(scope.Dir("theme-source-workspace"));
        EditorProject source = sourceStore.Create("theme-source", "主题来源", LauncherTemplateKind.Widescreen);
        string sourceRoot = sourceStore.GetProjectDirectory(source.Snapshot.ProjectId);
        using (var image = new Bitmap(24, 12)) image.Save(Path.Combine(sourceRoot, "Assets", "background.png"));
        source.Snapshot.Theme.BackgroundImage = "Assets/background.png";
        source.Snapshot.Theme.ServerListMode = ServerListMode.Sidebar;
        string package = Path.Combine(scope.Root, "widescreen.lyotheme");

        ThemeTemplatePackage.Export(source, sourceRoot, package);

        using (ZipArchive zip = ZipFile.OpenRead(package))
        {
            Assert.NotNull(zip.GetEntry("theme.json"));
            Assert.DoesNotContain(zip.Entries, entry => entry.FullName.Contains("secret", StringComparison.OrdinalIgnoreCase));
        }
        var targetStore = new EditorProjectStore(scope.Dir("theme-target-workspace"));
        EditorProject target = targetStore.Create("theme-target", "主题目标", LauncherTemplateKind.Classic);
        string targetRoot = targetStore.GetProjectDirectory(target.Snapshot.ProjectId);
        ThemeTemplatePackage.Import(target, targetRoot, package);
        Assert.Equal(LauncherTemplateKind.Widescreen, target.Snapshot.Theme.Template);
        Assert.Equal(ServerListMode.Sidebar, target.Snapshot.Theme.ServerListMode);
        Assert.True(File.Exists(Path.Combine(targetRoot, target.Snapshot.Theme.BackgroundImage)));
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

    [Fact]
    public void ProjectKeysAreUniqueRecoverableAndNeverStoredInProjectJson()
    {
        using var scope = new EditorTempScope();
        var store = new EditorProjectStore(scope.Dir("key-workspace"));
        EditorProject first = store.Create("key-first", "密钥项目一", LauncherTemplateKind.Classic);
        EditorProject second = store.Create("key-second", "密钥项目二", LauncherTemplateKind.Classic);
        Assert.NotEqual(first.Release.CurrentKeyId, second.Release.CurrentKeyId);
        Assert.NotEqual(first.Release.CurrentPublicKey, first.Release.NextPublicKey);
        string firstRoot = store.GetProjectDirectory(first.Snapshot.ProjectId);
        string projectJson = File.ReadAllText(Path.Combine(firstRoot, "project.json"));
        Assert.DoesNotContain("PrivateKey", projectJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("dpapi", projectJson, StringComparison.OrdinalIgnoreCase);
        string recovery = Path.Combine(scope.Root, "key-first.recovery");
        ProjectReleaseKeyStore.ExportRecovery(first, firstRoot, "Strong-Recovery-Password-2026", recovery);
        string secrets = Path.Combine(firstRoot, ".secrets"), held = Path.Combine(scope.Root, "held-secrets");
        Directory.Move(secrets, held);
        Assert.False(ProjectReleaseKeyStore.HasPrivateKeys(first, firstRoot));
        Assert.Throws<InvalidDataException>(() => ProjectReleaseKeyStore.ImportRecovery(first, firstRoot, "Wrong-Password-2026", recovery));
        ProjectReleaseKeyStore.ImportRecovery(first, firstRoot, "Strong-Recovery-Password-2026", recovery);
        Assert.True(ProjectReleaseKeyStore.HasPrivateKeys(first, firstRoot));
        byte[] privateKey = ProjectReleaseKeyStore.LoadCurrentPrivateKey(first, firstRoot);
        try
        {
            using ECDsa signer = ECDsa.Create(); signer.ImportPkcs8PrivateKey(privateKey, out int read);
            Assert.Equal(privateKey.Length, read);
            Assert.Equal(first.Release.CurrentPublicKey, Convert.ToBase64String(signer.ExportSubjectPublicKeyInfo()));
        }
        finally { CryptographicOperations.ZeroMemory(privateKey); }
    }

    [Fact]
    public void ImmutablePublishRecoversSequenceAndRollbackCreatesHigherSignedVersion()
    {
        using var scope = new EditorTempScope();
        var store = new EditorProjectStore(scope.Dir("release-workspace"));
        EditorProject project = store.Create("release-project", "发布项目", LauncherTemplateKind.Compact);
        AttachLoginResources(project, scope.Dir("release-client"));
        string projectRoot = store.GetProjectDirectory(project.Snapshot.ProjectId);
        string publishRoot = scope.Dir("publish-root");
        ProjectReleaseResult first = ProjectReleasePublisher.Publish(project, projectRoot, publishRoot, "首发");
        Assert.Equal(1, first.Sequence);
        project.Release.NextSequence = 1; // 模拟指针已切换、项目文件尚未保存时强停。
        project.Snapshot.ProjectName = "第二版";
        ProjectReleaseResult second = ProjectReleasePublisher.Publish(project, projectRoot, publishRoot, "第二版");
        Assert.Equal(2, second.Sequence);
        string offlineSecond = Path.Combine(scope.Root, "offline-second.zip");
        ProjectReleasePublisher.CreateOfflineDeploymentPackage(publishRoot, offlineSecond);
        ProjectReleaseDiff diff = ProjectReleasePublisher.CompareVersions(project, publishRoot, first.VersionName, second.VersionName);
        Assert.Contains("launcher-snapshot.json", diff.Changed);
        string historicalSnapshot = Path.Combine(first.VersionDirectory, "launcher-snapshot.json");
        byte[] originalSnapshot = File.ReadAllBytes(historicalSnapshot);
        File.AppendAllText(historicalSnapshot, "tampered");
        Assert.ThrowsAny<Exception>(() => ProjectReleasePublisher.Rollback(project, projectRoot, publishRoot, first.VersionName, "拒绝被篡改历史"));
        File.WriteAllBytes(historicalSnapshot, originalSnapshot);
        ProjectReleaseResult rollback = ProjectReleasePublisher.Rollback(project, projectRoot, publishRoot, first.VersionName, "回滚到首发内容");
        Assert.Equal(3, rollback.Sequence);
        Assert.Equal(first.Sequence, project.Release.History[^1].RolledBackFromSequence);
        Assert.Equal(rollback.VersionName, File.ReadAllText(Path.Combine(publishRoot, "current.txt")).Trim());
        string manifestJson = File.ReadAllText(Path.Combine(rollback.VersionDirectory, "bootstrap-manifest.json"));
        var keys = new Dictionary<string, BootstrapManifestTrustedKey>(StringComparer.Ordinal)
        {
            [project.Release.CurrentKeyId] = new() { KeyId = project.Release.CurrentKeyId, SubjectPublicKeyInfo = project.Release.CurrentPublicKey, NotBeforeSequence = 1 },
            [project.Release.NextKeyId] = new() { KeyId = project.Release.NextKeyId, SubjectPublicKeyInfo = project.Release.NextPublicKey, NotBeforeSequence = 1 },
        };
        BootstrapManifestVerificationResult verified = BootstrapManifestSignaturePolicy.Verify(manifestJson, keys, new Version(1, 0, 0));
        Assert.True(verified.IsValid, verified.Error);
        string offline = Path.Combine(scope.Root, "offline.zip");
        ProjectReleasePublisher.CreateOfflineDeploymentPackage(publishRoot, offline);
        using ZipArchive archive = ZipFile.OpenRead(offline);
        Assert.NotNull(archive.GetEntry("current.txt"));
        Assert.Contains(archive.Entries, entry => entry.FullName.EndsWith("/bootstrap-manifest.json", StringComparison.Ordinal));
        string directTarget = Path.Combine(scope.Root, "gateway-direct-import");
        var directKeys = project.Release.RetiredPublicKeys.Concat(new[]
        {
            new BootstrapManifestTrustedKey { KeyId = project.Release.CurrentKeyId, SubjectPublicKeyInfo = project.Release.CurrentPublicKey, NotBeforeSequence = project.Release.CurrentKeyNotBeforeSequence },
            new BootstrapManifestTrustedKey { KeyId = project.Release.NextKeyId, SubjectPublicKeyInfo = project.Release.NextPublicKey, NotBeforeSequence = project.Release.NextKeyNotBeforeSequence },
        }).ToDictionary(item => item.KeyId, StringComparer.Ordinal);
        BootstrapOfflineInstallResult direct = BootstrapOfflinePackageInstaller.Install(offline, directTarget, directKeys, new Version(1, 0, 0, 0));
        Assert.Equal(rollback.Sequence, direct.Sequence);
        File.Delete(Path.Combine(directTarget, "current.txt"));
        BootstrapOfflineInstallResult resumed = BootstrapOfflinePackageInstaller.Install(offline, directTarget, directKeys, new Version(1, 0, 0, 0));
        Assert.Equal(direct.Sequence, resumed.Sequence);
        Assert.Throws<InvalidDataException>(() => BootstrapOfflinePackageInstaller.Install(offlineSecond, directTarget, directKeys, new Version(1, 0, 0, 0)));
        string importedRoot = Path.Combine(scope.Root, "offline-imported");
        ProjectReleaseResult imported = ProjectReleasePublisher.ImportOfflineDeploymentPackage(project, offline, importedRoot);
        Assert.Equal(rollback.Sequence, imported.Sequence);
        Assert.True(File.Exists(Path.Combine(imported.VersionDirectory, "launcher-snapshot.json")));
    }

    [Fact]
    public void TamperedKeyIdCannotEscapeProjectSecretsDirectory()
    {
        using var scope = new EditorTempScope();
        var store = new EditorProjectStore(scope.Dir("key-path-workspace"));
        EditorProject project = store.Create("key-path", "密钥路径", LauncherTemplateKind.Classic);
        project.Release.CurrentKeyId = "..\\outside-key";
        Assert.Throws<InvalidDataException>(() => ProjectReleaseKeyStore.LoadCurrentPrivateKey(project, store.GetProjectDirectory(project.Snapshot.ProjectId)));
        Assert.False(File.Exists(Path.Combine(scope.Root, "outside-key.dpapi")));
    }

    [Fact]
    public void SignedSnapshotChainCarriesTrustAcrossTwoKeyRotations()
    {
        using var scope = new EditorTempScope();
        var store = new EditorProjectStore(scope.Dir("rotation-workspace"));
        EditorProject project = store.Create("rotation-project", "轮换项目", LauncherTemplateKind.Classic);
        AttachLoginResources(project, scope.Dir("rotation-client"));
        string projectRoot = store.GetProjectDirectory(project.Snapshot.ProjectId), publishRoot = scope.Dir("rotation-publish"), chainRoot = scope.Dir("rotation-chain");
        var anchors = new Dictionary<string, BootstrapManifestTrustedKey>(StringComparer.Ordinal)
        {
            [project.Release.CurrentKeyId] = new() { KeyId = project.Release.CurrentKeyId, SubjectPublicKeyInfo = project.Release.CurrentPublicKey, NotBeforeSequence = 1 },
            [project.Release.NextKeyId] = new() { KeyId = project.Release.NextKeyId, SubjectPublicKeyInfo = project.Release.NextPublicKey, NotBeforeSequence = 1 },
        };
        string originalCurrentKey = project.Release.CurrentKeyId;
        ProjectReleaseResult first = ProjectReleasePublisher.Publish(project, projectRoot, publishRoot, "轮换前");
        BootstrapTrustChainStore.Record(first.VersionDirectory, chainRoot, anchors, new Version(1, 0, 0));
        ProjectReleaseKeyStore.Rotate(project, projectRoot);
        ProjectReleaseResult second = ProjectReleasePublisher.Publish(project, projectRoot, publishRoot, "第一次轮换");
        BootstrapTrustChainStore.Record(second.VersionDirectory, chainRoot, anchors, new Version(1, 0, 0));
        IReadOnlyDictionary<string, BootstrapManifestTrustedKey> afterFirst = BootstrapTrustChainStore.Resolve(chainRoot, anchors, new Version(1, 0, 0));
        Assert.Contains(project.Release.NextKeyId, afterFirst.Keys);
        Assert.Equal(1, afterFirst[originalCurrentKey].NotAfterSequence);
        ProjectReleaseKeyStore.Rotate(project, projectRoot);
        ProjectReleaseResult third = ProjectReleasePublisher.Publish(project, projectRoot, publishRoot, "第二次轮换");
        string manifest = File.ReadAllText(Path.Combine(third.VersionDirectory, "bootstrap-manifest.json"));
        Assert.True(BootstrapManifestSignaturePolicy.Verify(manifest, afterFirst, new Version(1, 0, 0)).IsValid);
    }

    [Fact]
    public async Task SignedReleaseLoadsFromRealStaticHttpAndMicroLauncherEndpoint()
    {
        using var scope = new EditorTempScope();
        var store = new EditorProjectStore(scope.Dir("http-source-workspace"));
        EditorProject project = store.Create("http-source-project", "HTTP 发布源", LauncherTemplateKind.Compact);
        AttachLoginResources(project, scope.Dir("http-source-client"));
        string projectRoot = store.GetProjectDirectory(project.Snapshot.ProjectId), publishRoot = scope.Dir("http-publish");
        _ = ProjectReleasePublisher.Publish(project, projectRoot, publishRoot, "真实 HTTP 源");
        var keys = new Dictionary<string, BootstrapManifestTrustedKey>(StringComparer.Ordinal)
        {
            [project.Release.CurrentKeyId] = new() { KeyId = project.Release.CurrentKeyId, SubjectPublicKeyInfo = project.Release.CurrentPublicKey, NotBeforeSequence = project.Release.CurrentKeyNotBeforeSequence },
            [project.Release.NextKeyId] = new() { KeyId = project.Release.NextKeyId, SubjectPublicKeyInfo = project.Release.NextPublicKey, NotBeforeSequence = project.Release.NextKeyNotBeforeSequence },
        };
        int staticPort = FreePort();
        await using (var staticHost = new StaticFileHost(publishRoot, staticPort))
        {
            await staticHost.StartAsync();
            Assert.True(await LauncherReleaseUpdater.TryRefreshAsync($"http://127.0.0.1:{staticPort}/", scope.Dir("static-accepted"), scope.Dir("static-lkg"), Path.Combine(scope.Dir("static-state"), "state.json"), CancellationToken.None, trustedKeys: keys));
        }
        int microPort = FreePort();
        await using var micro = new MicroHttpListenerHost();
        await micro.StartAsync($"http://127.0.0.1:{microPort}/", new MicroGatewayOptions(scope.Dir("micro-resources"), "reader", "code", publishRoot));
        Assert.True(await LauncherReleaseUpdater.TryRefreshAsync($"http://127.0.0.1:{microPort}/launcher/", scope.Dir("micro-accepted"), scope.Dir("micro-lkg"), Path.Combine(scope.Dir("micro-state"), "state.json"), CancellationToken.None, trustedKeys: keys));
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static void AttachLoginResources(EditorProject project, string clientRoot)
    {
        foreach (string file in new[] { "Title.Lib", "ChrSel.Lib", "Prguse.Lib" })
        {
            string path = Path.Combine(clientRoot, "Data", file);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "test-library-" + file);
        }
        project.ImportedClientDirectory = clientRoot;
    }

    private static string FindRepositoryRoot(string start)
    {
        DirectoryInfo? directory = new(start);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Legend of Mir.sln"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("未找到仓库根目录");
    }

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

    private static int FreePort() { var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); int port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop(); return port; }

    private sealed class StaticFileHost : IAsyncDisposable
    {
        private readonly string _root; private readonly HttpListener _listener = new(); private Task? _loop;
        public StaticFileHost(string root, int port) { _root = Path.GetFullPath(root); _listener.Prefixes.Add($"http://127.0.0.1:{port}/"); }
        public Task StartAsync() { _listener.Start(); _loop = Task.Run(LoopAsync); return Task.CompletedTask; }
        private async Task LoopAsync()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext context; try { context = await _listener.GetContextAsync(); } catch { break; }
                try
                {
                    string requestPath = context.Request.Url!.AbsolutePath;
                    string relative = Uri.UnescapeDataString(requestPath.StartsWith("/api/file/", StringComparison.OrdinalIgnoreCase)
                        ? requestPath["/api/file/".Length..]
                        : requestPath.TrimStart('/')).Replace('/', Path.DirectorySeparatorChar);
                    string path = Path.GetFullPath(Path.Combine(_root, relative));
                    if (!path.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) { context.Response.StatusCode = 404; }
                    else { byte[] bytes = await File.ReadAllBytesAsync(path); context.Response.ContentLength64 = bytes.Length; await context.Response.OutputStream.WriteAsync(bytes); }
                }
                finally { context.Response.Close(); }
            }
        }
        public async ValueTask DisposeAsync() { _listener.Close(); if (_loop is not null) await _loop; }
    }

    private sealed class EditorTempScope : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "launcher-editor-tests", Guid.NewGuid().ToString("N"));
        public EditorTempScope() => Directory.CreateDirectory(Root);
        public string Dir(string name) { string path = Path.Combine(Root, name); Directory.CreateDirectory(path); return path; }
        public void Dispose() { try { Directory.Delete(Root, true); } catch { } }
    }
}
