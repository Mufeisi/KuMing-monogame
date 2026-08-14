using System.Security.Cryptography;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Shared.CustomGui;
using Shared.Release;
using Shared.Security;
using Xunit;

namespace Base05.Tests;

public sealed class CustomGuiValidationTests
{
    [Fact]
    public void 合法文档与Bootstrap资源绑定通过全部约束()
    {
        CustomGuiRuntimeDocument document = CreateValidDocument();
        CustomGuiResourceCatalog catalog = CreateCatalog();

        CustomGuiValidationReport report = CustomGuiValidationPolicy.Validate(document, catalog);

        Assert.True(report.IsValid);
        Assert.Empty(report.Diagnostics);
    }

    [Fact]
    public void 对象图布局安全区与数量限制失败关闭()
    {
        var cases = new (string Name, Action<CustomGuiRuntimeDocument> Mutate, string Code)[]
        {
            ("负修订", document => document.Revision = -1, "GUI05-DOC-001"),
            ("多个根", document => document.Elements.Add(new CustomGuiWindow { Id = "second-root", Layout = new(0, 0, 10, 10) }), "GUI05-GRAPH-001"),
            ("非法父类型", document => document.Elements.Single(value => value.Id == "title").ParentId = "banner", "GUI05-GRAPH-001"),
            ("循环父级", document => document.Elements.Single(value => value.Id == "root").ParentId = "panel", "GUI05-GRAPH-001"),
            ("负尺寸", document => document.Elements.Single(value => value.Id == "title").Layout = new(0, 0, -1, 10), "GUI05-LAYOUT-001"),
            ("越过父级", document => document.Elements.Single(value => value.Id == "title").Layout = new(800, 0, 200, 40), "GUI05-LAYOUT-001"),
            ("安全区策略错误", document => document.Viewport = document.Viewport with { SafeArea = (CustomGuiSafeAreaMode)999 }, "GUI05-LAYOUT-001"),
            ("对象超限", document => AddPanels(document, CustomGuiValidationLimits.MaximumElements), "GUI05-LIMIT-001"),
            ("嵌套超限", document => AddNestedPanels(document, CustomGuiValidationLimits.MaximumDepth + 1), "GUI05-LIMIT-001"),
        };

        foreach ((string name, Action<CustomGuiRuntimeDocument> mutate, string code) in cases)
        {
            CustomGuiRuntimeDocument document = CreateValidDocument();
            mutate(document);
            CustomGuiValidationReport report = CustomGuiValidationPolicy.Validate(document, CreateCatalog());
            Assert.False(report.IsValid);
            Assert.Contains(report.Diagnostics, item => item.Code == code);
        }
    }

    [Fact]
    public void 文本资源字体图集和输入上限失败关闭()
    {
        var cases = new (Action<CustomGuiRuntimeDocument> Mutate, string Code)[]
        {
            (document => ((CustomGuiText)document.Elements.Single(value => value.Id == "title")).Content = new string('字', CustomGuiValidationLimits.MaximumTextLength + 1), "GUI05-TEXT-001"),
            (document => ((CustomGuiText)document.Elements.Single(value => value.Id == "title")).Content = "[url=https://invalid.example]外链[/url]", "GUI05-TEXT-001"),
            (document => ((CustomGuiText)document.Elements.Single(value => value.Id == "title")).FontId = "unknown-font", "GUI05-RESOURCE-001"),
            (document => ((CustomGuiImage)document.Elements.Single(value => value.Id == "banner")).AssetId = "https://invalid.example/banner.png", "GUI05-RESOURCE-001"),
            (document => ((CustomGuiImage)document.Elements.Single(value => value.Id == "banner")).AssetId = "missing/banner", "GUI05-RESOURCE-001"),
            (document => ((CustomGuiTextInput)document.Elements.Single(value => value.Id == "code")).MaxLength = CustomGuiValidationLimits.MaximumInputLength + 1, "GUI05-LIMIT-001"),
            (document => AddListItems((CustomGuiList)document.Elements.Single(value => value.Id == "rewards"), CustomGuiValidationLimits.MaximumListItems + 1), "GUI05-LIMIT-001"),
            (document => AddDuplicateListItem((CustomGuiList)document.Elements.Single(value => value.Id == "rewards")), "GUI05-DOC-001"),
            (document => AddTotalListItems(document), "GUI05-LIMIT-001"),
            (document => AddTotalText(document), "GUI05-LIMIT-001"),
        };

        foreach ((Action<CustomGuiRuntimeDocument> mutate, string code) in cases)
        {
            CustomGuiRuntimeDocument document = CreateValidDocument();
            mutate(document);
            CustomGuiValidationReport report = CustomGuiValidationPolicy.Validate(document, CreateCatalog());
            Assert.False(report.IsValid);
            Assert.Contains(report.Diagnostics, item => item.Code == code);
        }

        BootstrapPackageManifestDocument manifest = CreateResourceManifest();
        CustomGuiValidationException atlasError = Assert.Throws<CustomGuiValidationException>(() =>
            CustomGuiResourceCatalog.FromBootstrapManifest(
                manifest,
                [new("activity/banner", "custom-gui", "gui/banner.png", "gui/missing-atlas.png")],
                [new("default-cn", "custom-gui", "gui/default-cn.fnt")]));
        Assert.Equal("GUI05-RESOURCE-001", atlasError.Code);

        byte[] bindings = CreateBindingsBytes();
        Assert.Equal(2, CustomGuiResourceBindingsCodec.Deserialize(bindings).Assets.Count);
        string bindingJson = Encoding.UTF8.GetString(bindings);
        CustomGuiValidationException unknown = Assert.Throws<CustomGuiValidationException>(() =>
            CustomGuiResourceBindingsCodec.Deserialize(Encoding.UTF8.GetBytes(bindingJson.Replace("{", "{\"unknown\":true,", StringComparison.Ordinal))));
        Assert.Equal("GUI05-RESOURCE-001", unknown.Code);
        CustomGuiValidationException duplicate = Assert.Throws<CustomGuiValidationException>(() =>
            CustomGuiResourceBindingsCodec.Deserialize(Encoding.UTF8.GetBytes(bindingJson.Replace("\"schemaVersion\":", "\"schemaVersion\":1,\"schemaVersion\":", StringComparison.Ordinal))));
        Assert.Equal("GUI05-RESOURCE-001", duplicate.Code);
        CustomGuiValidationException bindingLimit = Assert.Throws<CustomGuiValidationException>(() =>
            CustomGuiResourceBindingsCodec.Deserialize(new byte[CustomGuiValidationLimits.MaximumResourceBindingsBytes + 1]));
        Assert.Equal("GUI05-LIMIT-001", bindingLimit.Code);
    }

    [Fact]
    public void 签名包加载复用Bootstrap验签摘要防降级与大小门禁()
    {
        using ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var keys = new Dictionary<string, BootstrapManifestTrustedKey>
        {
            ["resource-main"] = new()
            {
                KeyId = "resource-main",
                SubjectPublicKeyInfo = Convert.ToBase64String(signer.ExportSubjectPublicKeyInfo()),
                NotBeforeSequence = 1,
            },
        };
        string root = Path.Combine(Path.GetTempPath(), "LyoCrystal-GUI05-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string packagePath = Path.Combine(root, "custom-gui.zip");
        try
        {
            CustomGuiRuntimeDocument initialDocument = CreateValidDocument();
            WritePackage(packagePath, initialDocument);
            BootstrapSignedManifest manifest = Sign(CreateSignedManifest(packagePath, 10, "gui-v10"), signer);
            var request = new CustomGuiPackageVerificationRequest
            {
                BootstrapManifestJson = JsonSerializer.Serialize(manifest),
                TrustedKeys = keys,
                CurrentClientVersion = new Version(1, 0, 0),
                AcceptedState = AcceptedState(manifest),
                PackageName = "custom-gui.zip",
                PackagePath = packagePath,
                BootstrapResourceManifest = CreateResourceManifest(),
            };

            CustomGuiAcceptedPackage accepted = CustomGuiPackageVerifier.VerifyAndLoad(request);
            Assert.Equal("gui-v10", accepted.ResourceVersion);
            Assert.Equal("new-player-event", accepted.Document.DocumentId);
            request.AcceptedDocumentState = new(accepted.Document.DocumentId, accepted.Document.Revision, accepted.DocumentSha256);
            Assert.Equal(accepted.DocumentSha256, CustomGuiPackageVerifier.VerifyAndLoad(request).DocumentSha256);

            CustomGuiRuntimeDocument replaced = CreateValidDocument();
            ((CustomGuiText)replaced.Elements.Single(value => value.Id == "title")).Content = "同修订号篡改内容";
            WritePackage(packagePath, replaced);
            BootstrapSignedManifest replacedManifest = Sign(CreateSignedManifest(packagePath, 11, "gui-v11"), signer);
            request.BootstrapManifestJson = JsonSerializer.Serialize(replacedManifest);
            request.AcceptedState = AcceptedState(manifest);
            CustomGuiValidationException reusedRevision = Assert.Throws<CustomGuiValidationException>(() => CustomGuiPackageVerifier.VerifyAndLoad(request));
            Assert.Equal("GUI05-DOC-001", reusedRevision.Code);

            replaced.Revision++;
            WritePackage(packagePath, replaced);
            BootstrapSignedManifest nextManifest = Sign(CreateSignedManifest(packagePath, 11, "gui-v11"), signer);
            request.BootstrapManifestJson = JsonSerializer.Serialize(nextManifest);
            Assert.Equal(6, CustomGuiPackageVerifier.VerifyAndLoad(request).Document.Revision);

            File.AppendAllText(packagePath, "tampered");
            CustomGuiValidationException tampered = Assert.Throws<CustomGuiValidationException>(() => CustomGuiPackageVerifier.VerifyAndLoad(request));
            Assert.Equal("GUI05-SIGN-001", tampered.Code);

            WritePackage(packagePath, replaced);
            BootstrapManifestAcceptedState newer = AcceptedState(Sign(CreateSignedManifest(packagePath, 12, "gui-v12"), signer));
            request.AcceptedState = newer;
            CustomGuiValidationException downgrade = Assert.Throws<CustomGuiValidationException>(() => CustomGuiPackageVerifier.VerifyAndLoad(request));
            Assert.Equal("GUI05-SIGN-001", downgrade.Code);

            request.AcceptedState = null;
            using (FileStream stream = new(packagePath, FileMode.Open, FileAccess.Write, FileShare.None))
                stream.SetLength(CustomGuiValidationLimits.MaximumPackageBytes + 1L);
            CustomGuiValidationException oversized = Assert.Throws<CustomGuiValidationException>(() => CustomGuiPackageVerifier.VerifyAndLoad(request));
            Assert.Equal("GUI05-LIMIT-001", oversized.Code);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void 签名ZIP拒绝穿越路径重复条目和超限运行描述()
    {
        using ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var keys = new Dictionary<string, BootstrapManifestTrustedKey>
        {
            ["resource-main"] = new()
            {
                KeyId = "resource-main",
                SubjectPublicKeyInfo = Convert.ToBase64String(signer.ExportSubjectPublicKeyInfo()),
                NotBeforeSequence = 1,
            },
        };
        string root = Path.Combine(Path.GetTempPath(), "LyoCrystal-GUI05-Zip-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string packagePath = Path.Combine(root, "custom-gui.zip");
        try
        {
            byte[] document = CustomGuiDocumentCodec.Serialize(CreateValidDocument());
            byte[] bindings = CreateBindingsBytes();
            var attacks = new (string Name, (string Name, byte[] Bytes)[] Entries, string Code)[]
            {
                ("路径穿越", [(CustomGuiPackageVerifier.DocumentEntryName, document), (CustomGuiPackageVerifier.ResourceBindingsEntryName, bindings), ("../escape.bin", [1])], "GUI05-SIGN-001"),
                ("重复条目", [(CustomGuiPackageVerifier.DocumentEntryName, document), (CustomGuiPackageVerifier.DocumentEntryName.ToUpperInvariant(), document), (CustomGuiPackageVerifier.ResourceBindingsEntryName, bindings)], "GUI05-SIGN-001"),
                ("描述超限", [(CustomGuiPackageVerifier.DocumentEntryName, new byte[CustomGuiValidationLimits.MaximumDocumentBytes + 1]), (CustomGuiPackageVerifier.ResourceBindingsEntryName, bindings)], "GUI05-LIMIT-001"),
            };

            long sequence = 20;
            foreach ((string name, (string Name, byte[] Bytes)[] entries, string code) in attacks)
            {
                WriteRawPackage(packagePath, entries);
                BootstrapSignedManifest manifest = Sign(CreateSignedManifest(packagePath, sequence++, "gui-attack-" + sequence), signer);
                var request = new CustomGuiPackageVerificationRequest
                {
                    BootstrapManifestJson = JsonSerializer.Serialize(manifest),
                    TrustedKeys = keys,
                    CurrentClientVersion = new Version(1, 0, 0),
                    PackageName = "custom-gui.zip",
                    PackagePath = packagePath,
                    BootstrapResourceManifest = CreateResourceManifest(),
                };

                CustomGuiValidationException error = Assert.Throws<CustomGuiValidationException>(() => CustomGuiPackageVerifier.VerifyAndLoad(request));
                Assert.Equal(code, error.Code);
            }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static CustomGuiRuntimeDocument CreateValidDocument() => new()
    {
        DocumentId = "new-player-event",
        Revision = 5,
        Viewport = new(1280, 720, CustomGuiScaleMode.Fit, CustomGuiSafeAreaMode.Required),
        Elements =
        [
            new CustomGuiWindow { Id = "root", Layout = new(200, 80, 880, 560), Title = "新手活动" },
            new CustomGuiPanel { Id = "panel", ParentId = "root", Layout = new(40, 50, 800, 450), ClipChildren = true, BackgroundColor = "#20252E" },
            new CustomGuiImage { Id = "banner", ParentId = "panel", Layout = new(20, 20, 760, 90), AssetId = "activity/banner", AlternateText = "活动横幅" },
            new CustomGuiText { Id = "title", ParentId = "panel", Layout = new(20, 125, 500, 42), Content = "欢迎领取新手奖励", Format = CustomGuiTextFormat.Rich, FontId = "default-cn", FontSize = 20, Color = "#FFFFFF" },
            new CustomGuiTextInput { Id = "code", ParentId = "panel", Layout = new(20, 180, 320, 48), Placeholder = "输入兑换码", MaxLength = 16, BindingKey = "event.code" },
            new CustomGuiList { Id = "rewards", ParentId = "panel", Layout = new(20, 245, 460, 160), SelectionBindingKey = "event.reward", Items = [new("day-1", "第一天", "木剑", "items/wood-sword")] },
            new CustomGuiProgressBar { Id = "progress", ParentId = "panel", Layout = new(500, 245, 260, 42), Minimum = 0, Maximum = 7, Value = 3, Text = "3/7", BindingKey = "event.progress" },
            new CustomGuiItemSlot { Id = "slot", ParentId = "panel", Layout = new(500, 305, 120, 100), AssetId = "items/wood-sword", DisplayName = "木剑", Quantity = 1, BindingKey = "event.item" },
            new CustomGuiButton { Id = "claim", ParentId = "root", Layout = new(330, 495, 220, 48), Text = "领取", ActionId = "event.claim", Enabled = true },
        ],
    };

    private static CustomGuiResourceCatalog CreateCatalog() => CustomGuiResourceCatalog.FromBootstrapManifest(
        CreateResourceManifest(),
        [
            new("activity/banner", "custom-gui", "gui/banner.png", "gui/banner.atlas"),
            new("items/wood-sword", "data-items", "Data/Items.Lib"),
        ],
        [new("default-cn", "custom-gui", "gui/default-cn.fnt")]);

    private static BootstrapPackageManifestDocument CreateResourceManifest() => new()
    {
        Packs =
        [
            new() { Name = "custom-gui", Assets = ["gui/banner.png", "gui/banner.atlas", "gui/default-cn.fnt"] },
            new() { Name = "data-items", Assets = ["Data/Items.Lib"] },
        ],
    };

    private static void AddPanels(CustomGuiRuntimeDocument document, int count)
    {
        for (int index = 0; index < count; index++)
            document.Elements.Add(new CustomGuiPanel { Id = "extra-" + index, ParentId = "panel", Layout = new(0, 0, 1, 1) });
    }

    private static void AddListItems(CustomGuiList list, int count)
    {
        list.Items.Clear();
        for (int index = 0; index < count; index++) list.Items.Add(new(index.ToString(), "奖励", string.Empty, "items/wood-sword"));
    }

    private static void AddDuplicateListItem(CustomGuiList list) =>
        list.Items.Add(list.Items[0] with { PrimaryText = "重复" });

    private static void AddNestedPanels(CustomGuiRuntimeDocument document, int count)
    {
        string parentId = "panel";
        for (int index = 0; index < count; index++)
        {
            string id = "nested-" + index;
            document.Elements.Add(new CustomGuiPanel { Id = id, ParentId = parentId, Layout = new(0, 0, 1, 1) });
            parentId = id;
        }
    }

    private static void AddTotalListItems(CustomGuiRuntimeDocument document)
    {
        for (int listIndex = 0; listIndex < 4; listIndex++)
        {
            var list = new CustomGuiList { Id = "bulk-list-" + listIndex, ParentId = "panel", Layout = new(0, 0, 1, 1) };
            AddListItems(list, CustomGuiValidationLimits.MaximumListItems);
            document.Elements.Add(list);
        }
    }

    private static void AddTotalText(CustomGuiRuntimeDocument document)
    {
        for (int index = 0; index < 17; index++)
            document.Elements.Add(new CustomGuiText { Id = "bulk-text-" + index, ParentId = "panel", Layout = new(0, 0, 1, 1), Content = new string('字', CustomGuiValidationLimits.MaximumTextLength) });
    }

    private static BootstrapSignedManifest CreateSignedManifest(string packagePath, long sequence, string resourceVersion) => new()
    {
        Format = BootstrapManifestSignaturePolicy.Format,
        Algorithm = BootstrapManifestSignaturePolicy.Algorithm,
        KeyId = "resource-main",
        Sequence = sequence,
        GeneratedAtUtc = "2026-08-14T00:00:00Z",
        ResourceVersion = resourceVersion,
        MinimumClientVersion = "1.0.0",
        Packages =
        [
            new()
            {
                Name = "custom-gui.zip",
                Size = new FileInfo(packagePath).Length,
                Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(packagePath))).ToLowerInvariant(),
            },
        ],
    };

    private static BootstrapSignedManifest Sign(BootstrapSignedManifest manifest, ECDsa signer)
    {
        manifest.Signature = Convert.ToBase64String(signer.SignData(
            BootstrapManifestSignaturePolicy.BuildCanonicalPayload(manifest),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
        return manifest;
    }

    private static BootstrapManifestAcceptedState AcceptedState(BootstrapSignedManifest manifest) => new()
    {
        Sequence = manifest.Sequence,
        ResourceVersion = manifest.ResourceVersion,
        CanonicalPayloadSha256 = Convert.ToHexString(SHA256.HashData(BootstrapManifestSignaturePolicy.BuildCanonicalPayload(manifest))).ToLowerInvariant(),
    };

    private static void WritePackage(string path, CustomGuiRuntimeDocument document)
    {
        if (File.Exists(path)) File.Delete(path);
        using ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create);
        WriteEntry(archive, CustomGuiPackageVerifier.DocumentEntryName, CustomGuiDocumentCodec.Serialize(document));
        WriteEntry(archive, CustomGuiPackageVerifier.ResourceBindingsEntryName, CreateBindingsBytes());
        WriteEntry(archive, "gui/banner.png", [1, 2, 3]);
        WriteEntry(archive, "gui/banner.atlas", [4, 5, 6]);
        WriteEntry(archive, "gui/default-cn.fnt", Encoding.UTF8.GetBytes("font"));
    }

    private static void WriteEntry(ZipArchive archive, string name, byte[] bytes)
    {
        ZipArchiveEntry entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
        using Stream stream = entry.Open();
        stream.Write(bytes);
    }

    private static byte[] CreateBindingsBytes() => CustomGuiResourceBindingsCodec.Serialize(new CustomGuiResourceBindingsDocument
    {
        Assets =
        [
            new("activity/banner", "custom-gui", "gui/banner.png", "gui/banner.atlas"),
            new("items/wood-sword", "data-items", "Data/Items.Lib"),
        ],
        Fonts = [new("default-cn", "custom-gui", "gui/default-cn.fnt")],
    });

    private static void WriteRawPackage(string path, IEnumerable<(string Name, byte[] Bytes)> entries)
    {
        if (File.Exists(path)) File.Delete(path);
        using ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach ((string name, byte[] bytes) in entries) WriteEntry(archive, name, bytes);
    }
}
