using System.Text.Json;
using System.Drawing;
using System.Diagnostics;
using System.Text;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using Launcher.ThemeRuntime;
using Launcher.PlayerShell;
using Shared.Security;
using Xunit;

namespace Launcher.PlayerShellIntegration;

public sealed class LauncherThemeRuntimeTests
{
    [Fact]
    public async Task ExternalAnnouncementFallsBackToSignedCardsWhenProbeFails()
    {
        LauncherSnapshot snapshot = CreateSnapshot("announcement");
        snapshot.AnnouncementMode = AnnouncementDisplayMode.ExternalPage;
        snapshot.ExternalAnnouncementUrl = "https://notice.example.invalid/";
        using var client = new HttpClient(new StubHttpHandler(HttpStatusCode.BadGateway));
        Assert.Equal(AnnouncementDisplayMode.NativeCards, await AnnouncementPresentationResolver.ResolveAsync(snapshot, client, CancellationToken.None));
        using var brokenBody = new HttpClient(new ThrowingHttpHandler());
        Assert.Equal(AnnouncementDisplayMode.NativeCards, await AnnouncementPresentationResolver.ResolveAsync(snapshot, brokenBody, CancellationToken.None));
    }

    [Fact]
    public async Task ExternalAnnouncementIsUsedOnlyAfterSuccessfulHttpProbe()
    {
        LauncherSnapshot snapshot = CreateSnapshot("announcement");
        snapshot.AnnouncementMode = AnnouncementDisplayMode.ExternalPage;
        snapshot.ExternalAnnouncementUrl = "https://notice.example.test/";
        using var client = new HttpClient(new StubHttpHandler(HttpStatusCode.OK));
        Assert.Equal(AnnouncementDisplayMode.ExternalPage, await AnnouncementPresentationResolver.ResolveAsync(snapshot, client, CancellationToken.None));
        string safeText = AnnouncementPresentationResolver.RenderSafeText("<script>not-executed()</script><b>公告</b>");
        Assert.DoesNotContain("<script>", safeText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("公告", safeText);
        IReadOnlyList<ExternalAnnouncementElement> document = SafeExternalAnnouncementDocument.Parse("<h1 style='color:#112233'>标题</h1><a href='/news'>详情</a><img src='images/a.png' alt='配图'><img src='http://127.0.0.1/private.png'><script>evil()</script>", new Uri("https://game.example.test/base/index.html"));
        Assert.Contains(document, item => item.Kind == ExternalAnnouncementElementKind.Heading && item.Bold && item.Color == "#112233");
        Assert.Contains(document, item => item.Kind == ExternalAnnouncementElementKind.Link && item.Url == "https://game.example.test/news");
        Assert.Contains(document, item => item.Kind == ExternalAnnouncementElementKind.Image && item.Url.EndsWith("/a.png", StringComparison.Ordinal));
        Assert.DoesNotContain(document, item => item.Kind == ExternalAnnouncementElementKind.Image && item.Url.Contains("127.0.0.1", StringComparison.Ordinal));
        Assert.False(ExternalAnnouncementHttp.IsPublicAddress(IPAddress.Loopback));
        Assert.False(ExternalAnnouncementHttp.IsPublicAddress(IPAddress.Parse("169.254.169.254")));
        Assert.False(ExternalAnnouncementHttp.IsPublicAddress(IPAddress.Parse("192.168.1.10")));
        Assert.True(ExternalAnnouncementHttp.IsPublicAddress(IPAddress.Parse("8.8.8.8")));
        byte[] pngHeader = new byte[24] { 137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 13, 73, 72, 68, 82, 0, 0, 16, 0, 0, 0, 8, 0 };
        Assert.True(SafeRasterImageMetadata.TryGetDimensions(pngHeader, out int imageWidth, out int imageHeight));
        Assert.Equal(4096, imageWidth); Assert.Equal(2048, imageHeight);
        string manyImages = string.Concat(Enumerable.Range(0, 12).Select(index => $"<img src='https://game.example.test/{index}.png'>"));
        Assert.Equal(6, SafeExternalAnnouncementDocument.Parse(manyImages, new Uri("https://game.example.test/news")).Count(item => item.Kind == ExternalAnnouncementElementKind.Image));
        Assert.DoesNotContain(document, item => item.Text.Contains("evil", StringComparison.OrdinalIgnoreCase));
        snapshot.ExternalAnnouncementUrl = "file:///C:/Windows/System32/cmd.exe";
        Assert.Throws<InvalidDataException>(() => LauncherSnapshotValidator.Validate(snapshot));
    }

    [Fact]
    public void ActionDispatcherAllowsOnlyDeclaredLocalActionsAndHttpLinks()
    {
        var opened = new List<Uri>();
        var invoked = new List<LauncherAction>();
        var dispatcher = new LauncherActionDispatcher(opened.Add, invoked.Add);
        dispatcher.Execute(LauncherAction.OfficialWebsite, "https://game.example.test/");
        dispatcher.Execute(LauncherAction.RepairClient);
        Assert.Single(opened);
        Assert.Equal(LauncherAction.RepairClient, Assert.Single(invoked));
        Assert.Throws<InvalidDataException>(() => dispatcher.Execute(LauncherAction.OfficialWebsite, "file:///C:/Windows/System32/cmd.exe"));
        Assert.Throws<InvalidOperationException>(() => dispatcher.Execute((LauncherAction)999));
    }

    [Fact]
    public void LoadFallsBackAcrossThreeCompleteLayers()
    {
        using var scope = new TempScope();
        string remote = scope.Dir("remote");
        string cache = scope.Dir("cache");
        string builtin = scope.Dir("builtin");
        File.WriteAllText(Path.Combine(remote, "launcher-snapshot.json"), "{}");
        WriteSnapshot(cache, CreateSnapshot("cache"));
        WriteSnapshot(builtin, CreateSnapshot("builtin"));
        LoadedLauncherSnapshot loaded = LauncherSnapshotLoader.Load(remote, cache, builtin, (_, _) => true);
        Assert.Equal(SnapshotSource.Cache, loaded.Source);
        Assert.Equal("cache", loaded.Snapshot.ProjectId);
        File.Delete(Path.Combine(cache, "launcher-snapshot.json"));
        Assert.Equal(SnapshotSource.BuiltIn, LauncherSnapshotLoader.Load(remote, cache, builtin, (_, _) => true).Source);
    }

    [Fact]
    public void UnsignedRemoteAndCacheAreNeverLoadedByDefault()
    {
        using var scope = new TempScope();
        string remote = scope.Dir("remote-unsigned");
        string cache = scope.Dir("cache-unsigned");
        string builtin = scope.Dir("builtin-safe");
        WriteSnapshot(remote, CreateSnapshot("remote"));
        WriteSnapshot(cache, CreateSnapshot("cache"));
        WriteSnapshot(builtin, CreateSnapshot("builtin"));
        Assert.Equal(SnapshotSource.BuiltIn, LauncherSnapshotLoader.Load(remote, cache, builtin).Source);
    }

    [Theory]
    [InlineData(LauncherTemplateKind.Classic, ServerListMode.Dropdown)]
    [InlineData(LauncherTemplateKind.Compact, ServerListMode.Dropdown)]
    [InlineData(LauncherTemplateKind.Widescreen, ServerListMode.Sidebar)]
    public void ThreeTemplatesAndGmFixedModesValidate(LauncherTemplateKind template, ServerListMode mode)
    {
        LauncherSnapshot snapshot = CreateSnapshot("theme");
        snapshot.Theme.Template = template;
        snapshot.Theme.ServerListMode = mode;
        LauncherSnapshotValidator.Validate(snapshot);
    }

    [Fact]
    public void ClientLocatorReturnsUniqueCandidatesWithoutFollowingReparsePoints()
    {
        using var scope = new TempScope();
        string root = scope.Dir("clients");
        string a = Directory.CreateDirectory(Path.Combine(root, "A")).FullName;
        string b = Directory.CreateDirectory(Path.Combine(root, "nested", "B")).FullName;
        File.WriteAllText(Path.Combine(a, "Client.exe"), "a");
        File.WriteAllText(Path.Combine(b, "Client.exe"), "b");
        Assert.Equal(new[] { a, b }, ClientLocator.Find("Client.exe", new[] { root }));
    }

    [Fact]
    public void ClientSelectionRequiresMatchingCapabilityMarker()
    {
        using var scope = new TempScope();
        string root = scope.Dir("compatible-client");
        File.WriteAllText(Path.Combine(root, "Client.exe"), "placeholder");
        Assert.False(ClientSelection.IsCompatible(root));
        File.WriteAllText(Path.Combine(root, "launcher-capabilities.json"), "{\"product\":\"LyoCrystal\",\"launchArgumentsVersion\":1}");
        Assert.True(ClientSelection.IsCompatible(root));
        File.WriteAllText(Path.Combine(root, "launcher-capabilities.json"), "{\"product\":\"LyoCrystal\",\"launchArgumentsVersion\":2}");
        Assert.False(ClientSelection.IsCompatible(root));
        File.WriteAllText(Path.Combine(root, "launcher-capabilities.json"), "{\"product\":\"LyoCrystal\",\"launchArgumentsVersion\":999999999999999999999}");
        Assert.False(ClientSelection.IsCompatible(root));
    }

    [Fact]
    public void ResourceOnlyClientDirectoryCanBeReusedWithoutCapabilityMarker()
    {
        using var scope = new TempScope();
        string root = scope.Dir("resource-client");
        foreach (string file in new[] { "Title.Lib", "ChrSel.Lib", "Prguse.Lib" })
        {
            string path = Path.Combine(root, "Data", file);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, file);
        }
        Assert.False(ClientSelection.IsCompatible(root));
        Assert.True(ClientSelection.IsResourceDirectory(root));
        LauncherCoreResource[] manifest = Directory.EnumerateFiles(Path.Combine(root, "Data"), "*.Lib").Select(path => new LauncherCoreResource
        {
            Path = "Data/" + Path.GetFileName(path), Size = new FileInfo(path).Length,
            Sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant(),
        }).ToArray();
        Assert.True(ClientSelection.IsTrustedResourceDirectory(root, manifest));
        IReadOnlyList<string> discovered = ClientLocator.Find("Title.Lib", new[] { Path.GetDirectoryName(root)! }, maximumDepth: 3,
            candidateFilter: dataDirectory => string.Equals(Path.GetFileName(dataDirectory), "Data", StringComparison.OrdinalIgnoreCase)
                && ClientSelection.IsTrustedResourceDirectory(Path.GetDirectoryName(dataDirectory)!, manifest));
        Assert.Equal(new[] { Path.Combine(root, "Data") }, discovered);
        File.AppendAllText(Path.Combine(root, "Data", "Title.Lib"), "tampered");
        Assert.False(ClientSelection.IsTrustedResourceDirectory(root, manifest));
    }

    [Fact]
    public void UnmarkedLegacyClientIsRejectedWithoutModification()
    {
        using var scope = new TempScope();
        string root = scope.Dir("legacy-client");
        File.WriteAllText(Path.Combine(root, "Client.exe"), "placeholder");
        File.WriteAllText(Path.Combine(root, "Client.dll"), "legacy-placeholder");
        Assert.Equal(ClientLaunchCapability.Unsupported, ClientCapabilityProbe.Detect(root));
        Assert.False(ClientSelection.IsCompatible(root));
        Assert.False(File.Exists(Path.Combine(root, "launcher-capabilities.json")));
        var server = new LauncherServer { Address = "127.0.0.1", Port = 7000 };
        var micro = new MicroEndpoint { Enabled = true, Address = "127.0.0.1", Port = 8080, BackupAddress = "127.0.0.2", BackupPort = 8081 };
        Assert.Throws<InvalidOperationException>(() => GameProcessLaunchArguments.Create(server, micro, ClientLaunchCapability.Unsupported));
        Assert.Equal(15, GameProcessLaunchArguments.Create(server, micro, ClientLaunchCapability.Current15Arguments).Count);
    }

    [Fact]
    public void SessionFailoverOccursAtMostOnce()
    {
        var session = new MicroEndpointSession(new MicroEndpoint { Address = "10.0.0.1", Port = 8080, BackupAddress = "10.0.0.2", BackupPort = 8081 });
        Assert.Equal(("10.0.0.1", 8080), session.Current);
        Assert.True(session.TryFailOver());
        Assert.Equal(("10.0.0.2", 8081), session.Current);
        Assert.False(session.TryFailOver());
    }

    [Fact]
    public void SessionFailoverRequiresThreeConsecutiveFailures()
    {
        var failover = new ConsecutiveFailureFailover(3);
        Assert.False(failover.RegisterFailure(backupAvailable: true));
        Assert.False(failover.RegisterFailure(backupAvailable: true));
        failover.RegisterSuccess();
        Assert.False(failover.RegisterFailure(backupAvailable: true));
        Assert.False(failover.RegisterFailure(backupAvailable: true));
        Assert.True(failover.RegisterFailure(backupAvailable: true));
        Assert.True(failover.UsingBackup);
        Assert.False(failover.RegisterFailure(backupAvailable: true));
    }

    [Fact]
    public void SnapshotRejectsCodeAndUnsafeAssetsBySchema()
    {
        LauncherSnapshot snapshot = CreateSnapshot("safe");
        snapshot.Theme.BackgroundImage = "../secret.png";
        Assert.Throws<InvalidDataException>(() => LauncherSnapshotValidator.Validate(snapshot));
        string json = JsonSerializer.Serialize(CreateSnapshot("safe"), LauncherSnapshotJsonContext.Default.LauncherSnapshot);
        Assert.DoesNotContain("Code", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AllTemplatesRenderAtFourDpiScalesWithoutOverflowException()
    {
        using var scope = new TempScope();
        foreach (LauncherTemplateKind kind in Enum.GetValues<LauncherTemplateKind>())
        foreach (float scale in new[] { 1f, 1.25f, 1.5f, 2f })
        {
            using Bitmap bitmap = LauncherRuntimeHost.RenderTemplateForEvidence(LauncherTemplateCatalog.Create(kind), scope.Dir("render"), scale);
            Assert.True(bitmap.Width >= 640 * scale);
            Assert.True(bitmap.Height >= 420 * scale);
            Color background = bitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2);
            int differentSamples = 0;
            for (int y = 0; y < bitmap.Height; y += Math.Max(1, bitmap.Height / 20))
            for (int x = 0; x < bitmap.Width; x += Math.Max(1, bitmap.Width / 20))
                if (bitmap.GetPixel(x, y).ToArgb() != background.ToArgb()) differentSamples++;
            Assert.True(differentSamples >= 10, $"{kind} {scale:P0} 主题没有渲染足够的可见控件");
        }
    }

    [Theory]
    [InlineData(LauncherTemplateKind.Classic, 96)]
    [InlineData(LauncherTemplateKind.Classic, 120)]
    [InlineData(LauncherTemplateKind.Classic, 144)]
    [InlineData(LauncherTemplateKind.Classic, 192)]
    [InlineData(LauncherTemplateKind.Compact, 96)]
    [InlineData(LauncherTemplateKind.Compact, 120)]
    [InlineData(LauncherTemplateKind.Compact, 144)]
    [InlineData(LauncherTemplateKind.Compact, 192)]
    [InlineData(LauncherTemplateKind.Widescreen, 96)]
    [InlineData(LauncherTemplateKind.Widescreen, 120)]
    [InlineData(LauncherTemplateKind.Widescreen, 144)]
    [InlineData(LauncherTemplateKind.Widescreen, 192)]
    public void PerMonitorV2WindowProcessesRealDpiMessageAndHitTests(LauncherTemplateKind kind, int dpi)
    {
        using var scope = new TempScope();
        LauncherDpiLayoutResult result = LauncherRuntimeHost.ValidatePerMonitorDpiForEvidence(LauncherTemplateCatalog.Create(kind), scope.Dir("dpi-message"), dpi);
        Assert.Equal(dpi, result.ActualDpi);
        Assert.True(result.AllControlsInsideCanvas, $"{kind} {dpi} DPI 存在越界控件：{result.Details}");
        Assert.True(result.ClickTargetsMatch, $"{kind} {dpi} DPI 点击区域不一致：{result.Details}");
    }

    [Fact]
    public void ProgressReportsTwoLevelsSpeedAndRemainingCapacity()
    {
        var state = new LauncherProgressState("下载资源", "Data.pak", 25, 100, 250, 1000, 1024 * 1024);
        Assert.Equal(.25, state.CurrentFraction);
        Assert.Equal(.25, state.OverallFraction);
        Assert.Equal(750, state.RemainingBytes);
    }

    [Fact]
    public void ProgressChannelPublishesRealTwoLevelStateAtomically()
    {
        string project = "progress-" + Guid.NewGuid().ToString("N");
        try
        {
            var publisher = new LauncherDownloadProgressPublisher(project);
            publisher.Queue("a", "Data/a.lib");
            publisher.Queue("b", "Data/b.lib");
            publisher.Report("a", "Data/a.lib", 25, 100);
            publisher.Report("b", "Data/b.lib", 50, 200);
            Assert.True(LauncherProgressChannel.TryRead(project, out LauncherProgressSnapshot? snapshot));
            Assert.NotNull(snapshot);
            Assert.Equal("Data/b.lib", snapshot.State.CurrentFile);
            Assert.Equal(75, snapshot.State.OverallReceived);
            Assert.Equal(300, snapshot.State.OverallTotal);
            Assert.Equal(225, snapshot.State.RemainingBytes);
            Assert.Equal(0, snapshot.State.PendingFiles);
            publisher.Complete("a", succeeded: true);
            publisher.Complete("b", succeeded: false);
            Assert.True(LauncherProgressChannel.TryRead(project, out snapshot));
            Assert.Equal(100, snapshot!.State.OverallReceived);
            Assert.Equal(100, snapshot.State.OverallTotal);
        }
        finally { LauncherProgressChannel.Clear(project); }
    }

    [Fact]
    public void IniSanitizerRemovesLegacyMicroCode()
    {
        using var scope = new TempScope();
        string ini = Path.Combine(scope.Dir("ini"), "Mir2Config.ini");
        File.WriteAllText(ini, "[Micro]\r\nUser=player\r\nCode=plain-secret\r\n[Graphics]\r\nResolution=1024\r\n");
        Shared.Security.SensitiveIniSanitizer.Sanitize(ini, out string migrated);
        string sanitized = File.ReadAllText(ini);
        Assert.DoesNotContain("Code=", sanitized, StringComparison.Ordinal);
        Assert.Contains("User=player", sanitized, StringComparison.Ordinal);
        Assert.Equal("plain-secret", migrated);
    }

    [Fact]
    public void CredentialEnvelopeIsProjectBoundAndNotPlaintext()
    {
        const string code = "project-secret-9482";
        byte[] envelope = Shared.Security.MicroCredentialEnvelope.Create("project-a", code);
        Assert.True(envelope.AsSpan().IndexOf(Encoding.UTF8.GetBytes(code)) < 0);
        Assert.Equal(code, Shared.Security.MicroCredentialEnvelope.Open("project-a", envelope));
        Assert.Throws<InvalidDataException>(() => Shared.Security.MicroCredentialEnvelope.Open("project-b", envelope));
    }

    [Fact]
    public void CredentialManagerSeparatesProjects()
    {
        string suffix = Guid.NewGuid().ToString("N");
        string projectA = "test-a-" + suffix;
        string projectB = "test-b-" + suffix;
        try
        {
            Shared.Security.ProtectedClientSecretStore.WriteMicroCode(projectA, "alpha");
            Shared.Security.ProtectedClientSecretStore.WriteMicroCode(projectB, "beta");
            Assert.Equal("alpha", Shared.Security.ProtectedClientSecretStore.ReadMicroCode(projectA));
            Assert.Equal("beta", Shared.Security.ProtectedClientSecretStore.ReadMicroCode(projectB));
        }
        finally
        {
            Shared.Security.ProtectedClientSecretStore.WriteMicroCode(projectA, string.Empty);
            Shared.Security.ProtectedClientSecretStore.WriteMicroCode(projectB, string.Empty);
        }
    }

    [Fact]
    public void DisplayModesAreIntersectionWithFixedEngineCapabilities()
    {
        int[] engineWidths = { 1024, 1280, 1366, 1920 };
        Assert.All(DisplayModeCatalog.GetSupportedModes(), mode => Assert.Contains(mode.Width, engineWidths));
    }

    [Fact]
    public void SettingsPanelComesFromSharedCapabilityRegistry()
    {
        Assert.Equal(new[] { "resolution", "fullScreen", "borderless", "fpsCap", "maxFps", "topMost", "autoStart", "volume", "musicVolume", "microCacheLimitMb", "advancedLogs" }, LauncherSettingsRegistry.All.Select(item => item.Key));
        Assert.All(LauncherSettingsRegistry.All, item => Assert.False(string.IsNullOrWhiteSpace(item.Label)));
    }

    [Fact]
    public void PlayerSettingsRoundTripEveryExposedCapability()
    {
        using var scope = new TempScope();
        string root = scope.Dir("settings-roundtrip");
        var expected = new LauncherPlayerSettings { Resolution = 1280, FullScreen = true, Borderless = false, FpsCap = false, MaxFps = 144, TopMost = false, AutoStart = true, Volume = 37, MusicVolume = 62, AdvancedLogs = true, MicroCacheLimitMb = 4096 };
        ClientSettingsWriter.Write(root, expected);
        LauncherPlayerSettings actual = ClientSettingsWriter.Read(root, new LauncherPlayerSettings());
        Assert.Equal(JsonSerializer.Serialize(expected, LauncherSnapshotJsonContext.Default.LauncherPlayerSettings), JsonSerializer.Serialize(actual, LauncherSnapshotJsonContext.Default.LauncherPlayerSettings));
    }

    [Fact]
    public void MicroResponseCacheStoresOnlyBoundedRebuildableCacheData()
    {
        using var scope = new TempScope();
        string client = scope.Dir("cache-client");
        var cache = new BoundedMicroResponseCache(client, 256);
        byte[] expected = Encoding.UTF8.GetBytes("micro-response");
        cache.Write("http://127.0.0.1/file/1", expected);
        Assert.True(cache.TryRead("http://127.0.0.1/file/1", TimeSpan.FromMinutes(1), out byte[] actual));
        Assert.Equal(expected, actual);
        string cachedFile = Assert.Single(Directory.EnumerateFiles(Path.Combine(client, "Cache", "MicroResponses"), "*.bin"));
        byte[] damaged = File.ReadAllBytes(cachedFile);
        damaged[^1] ^= 0x5A;
        File.WriteAllBytes(cachedFile, damaged);
        Assert.False(cache.TryRead("http://127.0.0.1/file/1", TimeSpan.FromMinutes(1), out _));
        Assert.False(File.Exists(cachedFile));
        Assert.True(cache.Trim() <= 256L * 1024 * 1024);
        Assert.False(Directory.Exists(Path.Combine(client, "Data")));
    }

    [Fact]
    public void MicroResponseCacheRejectsParentJunctionEscape()
    {
        using var scope = new TempScope();
        string client = scope.Dir("cache-junction-client");
        string outside = scope.Dir("cache-junction-outside");
        string link = Path.Combine(client, "Cache");
        using Process process = Process.Start(new ProcessStartInfo("cmd.exe", $"/d /c mklink /J \"{link}\" \"{outside}\"") { UseShellExecute = false, CreateNoWindow = true })!;
        Assert.True(process.WaitForExit(10_000));
        Assert.Equal(0, process.ExitCode);
        try
        {
            var cache = new BoundedMicroResponseCache(client, 256);
            Assert.False(cache.Write("outside", "blocked"u8.ToArray()));
            Assert.Empty(Directory.EnumerateFiles(outside, "*", SearchOption.AllDirectories));
            Assert.Equal(0, cache.Trim());
            cache.Invalidate("outside");
        }
        finally { Directory.Delete(link); }
    }

    [Fact]
    public void ThemeAssetsRejectJunctionEscape()
    {
        using var scope = new TempScope();
        string root = scope.Dir("theme-root");
        string outside = scope.Dir("outside");
        File.WriteAllText(Path.Combine(outside, "secret.png"), "not-an-image");
        string link = Path.Combine(root, "linked");
        using Process process = Process.Start(new ProcessStartInfo("cmd.exe", $"/d /c mklink /J \"{link}\" \"{outside}\"") { UseShellExecute = false, CreateNoWindow = true })!;
        Assert.True(process.WaitForExit(10_000));
        Assert.Equal(0, process.ExitCode);
        try { Assert.Throws<InvalidDataException>(() => LauncherSnapshotValidator.ResolveAsset(root, "linked/secret.png")); }
        finally { Directory.Delete(link); }
    }

    [Fact]
    public async Task SignedRemoteReleasePromotesAtomicallyAndKeepsLastKnownGood()
    {
        using var scope = new TempScope();
        using ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        const string keyId = "launcher-test-key";
        var keys = new Dictionary<string, BootstrapManifestTrustedKey>
        {
            [keyId] = new() { KeyId = keyId, SubjectPublicKeyInfo = Convert.ToBase64String(signer.ExportSubjectPublicKeyInfo()), NotBeforeSequence = 1, NotAfterSequence = 100 },
        };
        LauncherSnapshot snapshot = CreateSnapshot("remote-project");
        byte[] snapshotBytes = JsonSerializer.SerializeToUtf8Bytes(snapshot, LauncherSnapshotJsonContext.Default.LauncherSnapshot);
        var descriptor = new LauncherReleaseDescriptor
        {
            ResourceVersion = "launcher-v1",
            Files = new List<LauncherReleaseFile> { new() { Name = "launcher-snapshot.json", Sha256 = Sha256(snapshotBytes) } },
        };
        byte[] descriptorBytes = JsonSerializer.SerializeToUtf8Bytes(descriptor, LauncherSnapshotJsonContext.Default.LauncherReleaseDescriptor);
        var manifest = new BootstrapSignedManifest
        {
            Format = BootstrapManifestSignaturePolicy.Format,
            Algorithm = BootstrapManifestSignaturePolicy.Algorithm,
            KeyId = keyId,
            Sequence = 1,
            GeneratedAtUtc = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
            ResourceVersion = "launcher-v1",
            MinimumClientVersion = "1.0.0",
            Packages = new List<BootstrapSignedPackage>
            {
                Package("launcher-release.json", descriptorBytes),
                Package("launcher-snapshot.json", snapshotBytes),
            },
            Signature = string.Empty,
        };
        manifest.Signature = Convert.ToBase64String(signer.SignData(
            BootstrapManifestSignaturePolicy.BuildCanonicalPayload(manifest),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
        byte[] manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest);
        using var client = new HttpClient(new DictionaryHttpHandler(new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["bootstrap-manifest.json"] = manifestBytes,
            ["launcher-release.json"] = descriptorBytes,
            ["launcher-snapshot.json"] = snapshotBytes,
        }));
        string accepted = scope.Dir("accepted");
        string lkg = scope.Dir("lkg");
        string state = Path.Combine(scope.Dir("state"), "security.json");
        string error = string.Empty;
        Assert.True(await LauncherReleaseUpdater.TryRefreshAsync("http://launcher.test/", accepted, lkg, state, CancellationToken.None, client, keys, new Version(1, 0, 0), message => error = message), error);
        string acceptedRoot = Assert.IsType<string>(LauncherReleaseUpdater.ResolveCurrentRoot(accepted, state, keys, new Version(1, 0, 0)));
        string lkgRoot = Assert.IsType<string>(LauncherReleaseUpdater.ResolveCurrentRoot(lkg, state, keys, new Version(1, 0, 0)));
        Assert.Equal("remote-project", LauncherSnapshotLoader.Load(acceptedRoot, lkgRoot, scope.Dir("unused"), (_, _) => true).Snapshot.ProjectId);
        Assert.Equal(File.ReadAllBytes(Path.Combine(acceptedRoot, "launcher-snapshot.json")), File.ReadAllBytes(Path.Combine(lkgRoot, "launcher-snapshot.json")));

        string descriptorPath = Path.Combine(acceptedRoot, "launcher-release.json");
        File.WriteAllBytes(descriptorPath, JsonSerializer.SerializeToUtf8Bytes(
            new LauncherReleaseDescriptor { ResourceVersion = "launcher-v1", Files = new List<LauncherReleaseFile>() },
            LauncherSnapshotJsonContext.Default.LauncherReleaseDescriptor));
        Assert.False(LauncherReleaseAuthorization.IsAuthorized(acceptedRoot, state, keys, new Version(1, 0, 0)));
        File.WriteAllBytes(descriptorPath, descriptorBytes);
        Assert.True(LauncherReleaseAuthorization.IsAuthorized(acceptedRoot, state, keys, new Version(1, 0, 0)));
        manifest.Sequence = 2;
        manifest.Signature = Convert.ToBase64String(signer.SignData(
            BootstrapManifestSignaturePolicy.BuildCanonicalPayload(manifest),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
        File.WriteAllBytes(Path.Combine(acceptedRoot, "bootstrap-manifest.json"), JsonSerializer.SerializeToUtf8Bytes(manifest));
        Assert.False(LauncherReleaseAuthorization.IsAuthorized(acceptedRoot, state, keys, new Version(1, 0, 0)));
        File.WriteAllBytes(Path.Combine(acceptedRoot, "bootstrap-manifest.json"), manifestBytes);
        File.WriteAllText(Path.Combine(acceptedRoot, "unsigned-extra.txt"), "unsigned");
        Assert.False(LauncherReleaseAuthorization.IsAuthorized(acceptedRoot, state, keys, new Version(1, 0, 0)));
        File.Delete(Path.Combine(acceptedRoot, "unsigned-extra.txt"));

        File.WriteAllText(Path.Combine(acceptedRoot, "launcher-snapshot.json"), "corrupted");
        Assert.True(await LauncherReleaseUpdater.TryRefreshAsync("http://launcher.test/", accepted, lkg, state, CancellationToken.None, client, keys, new Version(1, 0, 0), message => error = message), error);
        string repairedRoot = Assert.IsType<string>(LauncherReleaseUpdater.ResolveCurrentRoot(accepted, state, keys, new Version(1, 0, 0)));
        string repairedLkgRoot = Assert.IsType<string>(LauncherReleaseUpdater.ResolveCurrentRoot(lkg, state, keys, new Version(1, 0, 0)));
        Assert.Equal("remote-project", LauncherSnapshotLoader.Load(repairedRoot, repairedLkgRoot, scope.Dir("unused-repaired"), (_, _) => true).Snapshot.ProjectId);
        Assert.True(File.Exists(Path.Combine(repairedRoot, "bootstrap-manifest.json")));

        using var brokenClient = new HttpClient(new DictionaryHttpHandler(new Dictionary<string, byte[]> { ["bootstrap-manifest.json"] = "broken"u8.ToArray() }));
        Assert.False(await LauncherReleaseUpdater.TryRefreshAsync("http://launcher.test/", accepted, lkg, state, CancellationToken.None, brokenClient, keys, new Version(1, 0, 0)));
        Assert.Equal(repairedRoot, LauncherReleaseUpdater.ResolveCurrentRoot(accepted, state, keys, new Version(1, 0, 0)));
        Assert.Equal(repairedLkgRoot, LauncherReleaseUpdater.ResolveCurrentRoot(lkg, state, keys, new Version(1, 0, 0)));
    }

    [Fact]
    public async Task SignedPlayerEntryUpdateStagesSameDirectoryAndBindsReplacementJournal()
    {
        using var scope = new TempScope();
        using ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        const string keyId = "player-update-key";
        var keys = new Dictionary<string, BootstrapManifestTrustedKey>
        {
            [keyId] = new() { KeyId = keyId, SubjectPublicKeyInfo = Convert.ToBase64String(signer.ExportSubjectPublicKeyInfo()), NotBeforeSequence = 1 },
        };
        string target = Path.Combine(scope.Dir("player-target"), "玩家入口.exe");
        File.Copy(Environment.ProcessPath!, target);
        byte[] entryBytes = "MZ-signed-player-entry-v999"u8.ToArray();
        var descriptor = new PlayerUpdateDescriptor { Version = "999.0.0.0", Required = true, PackageName = "player-entry.exe" };
        byte[] descriptorBytes = JsonSerializer.SerializeToUtf8Bytes(descriptor, LauncherSnapshotJsonContext.Default.PlayerUpdateDescriptor);
        var manifest = new BootstrapSignedManifest
        {
            Format = BootstrapManifestSignaturePolicy.Format,
            Algorithm = BootstrapManifestSignaturePolicy.Algorithm,
            KeyId = keyId,
            Sequence = 9,
            GeneratedAtUtc = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
            ResourceVersion = "player-r9",
            MinimumClientVersion = "1.0.0",
            Packages = new List<BootstrapSignedPackage> { Package("player-update.json", descriptorBytes), Package("player-entry.exe", entryBytes) },
        };
        manifest.Signature = Convert.ToBase64String(signer.SignData(BootstrapManifestSignaturePolicy.BuildCanonicalPayload(manifest), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
        byte[] manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest);
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["current.txt"] = "r9-test\n"u8.ToArray(),
            ["bootstrap-manifest.json"] = manifestBytes,
            ["player-update.json"] = descriptorBytes,
            ["player-entry.exe"] = entryBytes,
        };
        using var client = new HttpClient(new DictionaryHttpHandler(files));
        string state = Path.Combine(scope.Dir("player-state"), "accepted.json");
        PlayerEntryUpdatePlan plan = Assert.IsType<PlayerEntryUpdatePlan>(await PlayerEntryUpdateService.InspectAsync("http://player.test/", target, state, keys, CancellationToken.None, client));
        Assert.True(plan.Descriptor.Required);
        string barrier = Path.Combine(scope.Dir("player-barrier"), "required.json");
        PlayerEntryUpdateService.PersistRequiredBarrier(plan, barrier);
        Assert.True(PlayerEntryUpdateService.IsRequiredBarrierActive(barrier, target, keys, out string barrierMessage, out Version? barrierVersion), barrierMessage);
        Assert.Equal(new Version(999, 0, 0, 0), barrierVersion);
        await PlayerEntryUpdateService.StageAsync(plan, target, state, keys, CancellationToken.None, client);
        Assert.Equal(entryBytes, File.ReadAllBytes(target + ".new"));
        string journal = Path.Combine(Path.GetDirectoryName(target)!, "player-replacement.json");
        Assert.True(PlayerReplacementCoordinator.ValidatePending(journal, target, keys, BootstrapManifestTrustConfiguration.CurrentClientCompatibilityVersion, state));

        files["player-update.json"] = "tampered"u8.ToArray();
        await Assert.ThrowsAsync<InvalidDataException>(() => PlayerEntryUpdateService.InspectAsync("http://player.test/", target, state, keys, CancellationToken.None, client));
        files["player-update.json"] = descriptorBytes;
        manifest.Sequence = 8;
        manifest.ResourceVersion = "player-r8";
        manifest.Signature = Convert.ToBase64String(signer.SignData(BootstrapManifestSignaturePolicy.BuildCanonicalPayload(manifest), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
        files["bootstrap-manifest.json"] = JsonSerializer.SerializeToUtf8Bytes(manifest);
        await Assert.ThrowsAsync<InvalidDataException>(() => PlayerEntryUpdateService.InspectAsync("http://player.test/", target, state, keys, CancellationToken.None, client));
    }

    [Fact]
    public void RunningGameSessionMarkerDefersPlayerEntryReplacement()
    {
        using var scope = new TempScope();
        string player = Path.Combine(scope.Dir("running-game"), "玩家入口.exe");
        File.WriteAllText(player, "placeholder");
        using Process current = Process.GetCurrentProcess();
        PlayerGameSessionMarker.Record(player, current);
        using Process second = Process.Start(new ProcessStartInfo("cmd.exe", "/d /c ping 127.0.0.1 -n 30 >nul") { UseShellExecute = false, CreateNoWindow = true })!;
        PlayerGameSessionMarker.Record(player, second);
        second.Kill(entireProcessTree: true); second.WaitForExit();
        Assert.True(PlayerGameSessionMarker.IsGameRunning(player));
    }

    private static BootstrapSignedPackage Package(string name, byte[] bytes) => new() { Name = name, Size = bytes.LongLength, Sha256 = Sha256(bytes) };
    private static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private sealed class DictionaryHttpHandler : HttpMessageHandler
    {
        private readonly IReadOnlyDictionary<string, byte[]> _files;
        public DictionaryHttpHandler(IReadOnlyDictionary<string, byte[]> files) => _files = files;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string name = Uri.UnescapeDataString(request.RequestUri!.Segments[^1]);
            var response = new HttpResponseMessage(_files.TryGetValue(name, out byte[]? bytes) ? HttpStatusCode.OK : HttpStatusCode.NotFound);
            if (bytes is not null) response.Content = new ByteArrayContent(bytes);
            return Task.FromResult(response);
        }
    }

    private sealed class StubHttpHandler(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { RequestMessage = request, Content = new StringContent("<html><body>公告</body></html>") });
    }

    private sealed class ThrowingHttpHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => throw new IOException("模拟响应体读取失败");
    }

    private static LauncherSnapshot CreateSnapshot(string id) => new()
    {
        ProjectId = id,
        ProjectName = "测试启动器",
        Servers = new List<LauncherServer> { new() { Id = "s1", Name = "一区", Address = "127.0.0.1", Port = 7000 } },
        Announcements = new List<LauncherAnnouncement> { new() { Title = "公告", Summary = "内容" } },
    };

    private static void WriteSnapshot(string root, LauncherSnapshot snapshot) => File.WriteAllText(Path.Combine(root, "launcher-snapshot.json"), JsonSerializer.Serialize(snapshot, LauncherSnapshotJsonContext.Default.LauncherSnapshot));

    private sealed class TempScope : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "launcher-theme-tests-" + Guid.NewGuid().ToString("N"));
        public string Dir(string name) { string path = Path.Combine(_root, name); Directory.CreateDirectory(path); return path; }
        public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    }
}
