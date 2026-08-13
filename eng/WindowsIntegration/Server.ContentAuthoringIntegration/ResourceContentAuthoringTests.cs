using LibraryEditor.Authoring;
using LibraryEditor;
using Xunit;

namespace Server.ContentAuthoringIntegration.Windows;

public sealed class ResourceContentAuthoringTests
{
    [Fact]
    public void 资源引用图同时报告缺失反向重复与未使用候选()
    {
        ResourceAsset[] assets =
        [
            new("Data/Items.Lib", 120, "AAA"),
            new("Data/Copy.Lib", 120, "AAA"),
            new("Data/Unused.Lib", 12, "BBB")
        ];
        ResourceReference[] references =
        [
            new("core-startup", "data\\items.lib"),
            new("data-items", "Data/Items.Lib"),
            new("missing-pack", "Data/Missing.Lib")
        ];

        ResourceReferenceReport report = ResourceReferenceAnalyzer.Analyze(assets, references);

        ResourceReferenceDiagnostic missing = Assert.Single(report.MissingReferences);
        Assert.Equal("CONTENT05-RESOURCE-001", missing.Code);
        Assert.Equal("Data/Missing.Lib", missing.ResourcePath);
        Assert.Equal(["core-startup", "data-items"], report.GetOwners("DATA/ITEMS.LIB"));
        ResourceDuplicateCandidate duplicate = Assert.Single(report.DuplicateCandidates);
        Assert.Equal(["Data/Copy.Lib", "Data/Items.Lib"], duplicate.ResourcePaths);
        Assert.Equal(["Data/Copy.Lib", "Data/Unused.Lib"], report.UnusedCandidates);
    }

    [Fact]
    public void 资源目录与既有包清单可生成可定位的引用报告()
    {
        string root = Path.Combine(Path.GetTempPath(), "LyoCrystalContent05", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Data"));
        try
        {
            File.WriteAllText(Path.Combine(root, "Data", "Items.Lib"), "same");
            File.WriteAllText(Path.Combine(root, "Data", "Copy.Lib"), "same");
            File.WriteAllText(Path.Combine(root, "Data", "Unused.Lib"), "unused");
            string manifest = Path.Combine(root, "bootstrap-packages.json");
            File.WriteAllText(manifest, """
                {
                  "Packs": [
                    { "Name": "items", "Assets": ["Data/Items.Lib"] },
                    { "Name": "missing", "Assets": ["Data/Missing.Lib"] }
                  ]
                }
                """);

            ResourceReferenceWorkspace workspace = ResourceReferenceWorkspace.Load(root, manifest);

            Assert.Equal(3, workspace.Assets.Count);
            Assert.Equal(2, workspace.References.Count);
            Assert.Equal("CONTENT05-RESOURCE-001", Assert.Single(workspace.Report.MissingReferences).Code);
            Assert.Equal(["items"], workspace.Report.GetOwners("Data/Items.Lib"));
            Assert.Equal(["Data/Copy.Lib", "Data/Items.Lib"],
                Assert.Single(workspace.Report.DuplicateCandidates).ResourcePaths);
            Assert.Equal(["Data/Copy.Lib", "Data/Unused.Lib"], workspace.Report.UnusedCandidates);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void 资源目录复用主清单与分包清单合并语义()
    {
        string root = Path.Combine(Path.GetTempPath(), "LyoCrystalContent05Manifest", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "BootstrapAssets", "bootstrap-package-manifests"));
        Directory.CreateDirectory(Path.Combine(root, "Data"));
        try
        {
            File.WriteAllText(Path.Combine(root, "Data", "Items.Lib"), "asset");
            string manifest = Path.Combine(root, "bootstrap-packages.json");
            File.WriteAllText(manifest, """
                { "Packs": [ { "Name": "items", "ManifestPath": "BootstrapAssets/bootstrap-package-manifests/items.json" } ] }
                """);
            string child = Path.Combine(root, "BootstrapAssets", "bootstrap-package-manifests", "items.json");
            File.WriteAllText(child, """
                { "Name": "items", "Assets": ["Data/Items.Lib", "Data/Missing.Lib"] }
                """);

            ResourceReferenceWorkspace workspace = ResourceReferenceWorkspace.Load(root, manifest);

            Assert.Equal(2, workspace.References.Count);
            Assert.Equal(Path.GetFullPath(child), workspace.GetOwnerPath("items"));
            Assert.Equal("CONTENT05-RESOURCE-001", Assert.Single(workspace.Report.MissingReferences).Code);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void 资源库编辑会话显式保存重载且失败保留草稿()
    {
        string root = Path.Combine(Path.GetTempPath(), "LyoCrystalContent05Lib", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "Items.Lib");
        try
        {
            using (var bitmap = new Bitmap(2, 2))
            {
                var initial = new MLibraryV2(path);
                initial.AddImage(bitmap, 1, 2, false);
                initial.Save();
            }
            byte[] originalBytes = File.ReadAllBytes(path);
            var draft = new MLibraryV2(path);
            var session = new LibraryContentEditingSession(draft);
            session.Draft.GetMImage(0).X = 9;

            Assert.True(session.IsDirty);
            Assert.Equal(1, session.Fact.GetMImage(0).X);
            Assert.NotNull(session.Draft.GetMImage(0).Image);
            Assert.Equal(Color.FromArgb(0, 0, 0, 0), session.Draft.GetPreview(0).GetPixel(0, 0));
            Assert.Contains("图像 0", session.DescribeChanges());
            Assert.Equal(originalBytes, File.ReadAllBytes(path));
            Assert.False(session.TryCommit(_ => throw new IOException("占用"), out string failure));
            Assert.Contains("占用", failure);
            Assert.True(session.IsDirty);
            Assert.Equal(1, session.Fact.GetMImage(0).X);
            Assert.Equal(originalBytes, File.ReadAllBytes(path));

            Assert.True(session.TryCommit(library => library.Save(), out string error), error);
            Assert.False(session.IsDirty);
            Assert.NotSame(session.Fact, session.Draft);
            Assert.Equal(9, session.Fact.GetMImage(0).X);
            var reloaded = new MLibraryV2(path);
            Assert.Equal(9, reloaded.GetMImage(0).X);
            reloaded.GetMImage(0).X = 4;
            session.Reload();
            Assert.False(session.IsDirty);
            Assert.Equal(9, session.Draft.GetMImage(0).X);

            Bitmap oldDraftImage = session.Draft.GetMImage(0).Image;
            session.Draft.Frames[MirAction.站立动作] = new Frame(0, 4, 0, 100);
            Assert.True(session.IsDirty);
            Assert.Contains("帧表", session.DescribeChanges());
            session.Reload();
            Assert.Throws<ArgumentException>(() => oldDraftImage.GetPixel(0, 0));
            Bitmap finalImage = session.Draft.GetMImage(0).Image;
            session.Dispose();
            Assert.Throws<ArgumentException>(() => finalImage.GetPixel(0, 0));

            using var retryFact = new MLibraryV2(path);
            using var retrySession = new LibraryContentEditingSession(
                retryFact,
                _ => throw new IOException("重载失败"));
            retrySession.Draft.GetMImage(0).X = 12;
            MLibraryV2 retainedDraft = retrySession.Draft;
            Assert.Throws<IOException>(() => retrySession.Reload());
            Assert.Same(retainedDraft, retrySession.Draft);
            Assert.True(retrySession.IsDirty);
            Assert.Equal(12, retrySession.Draft.GetMImage(0).X);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void 资源库写入异常释放临时文件且保留原文件()
    {
        string root = Path.Combine(Path.GetTempPath(), "LyoCrystalContent05Atomic", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "Items.Lib");
        try
        {
            using (var bitmap = new Bitmap(2, 2))
            {
                var seed = new MLibraryV2(path);
                seed.AddImage(bitmap, 1, 2, false);
                seed.Save();
            }
            byte[] original = File.ReadAllBytes(path);
            var invalid = new MLibraryV2(path);
            invalid.Images.Add(null);
            invalid.Count++;

            Assert.ThrowsAny<Exception>(() => invalid.Save());
            Assert.Equal(original, File.ReadAllBytes(path));
            Assert.Empty(Directory.GetFiles(root, ".*.tmp"));
            using FileStream writable = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void 非法资源库草稿在持久化前被稳定诊断阻断()
    {
        string root = Path.Combine(Path.GetTempPath(), "LyoCrystalContent05Invalid", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "Invalid.Lib");
        try
        {
            var library = new MLibraryV2(path);
            library.Images.Add(null);
            library.Count = 1;
            library.Frames[MirAction.站立动作] = new Frame(0, -1, 0, -5);
            var session = new LibraryContentEditingSession(library);
            var persisted = false;

            Assert.False(session.TryValidateAndCommit(_ => persisted = true, out var diagnostics, out _));

            Assert.False(persisted);
            Assert.Contains(diagnostics, item => item.Code == "CONTENT05-LIB-001" && item.ImageIndex == 0);
            Assert.Contains(diagnostics, item => item.Code == "CONTENT05-LIB-002");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void 资源编辑器提供引用定位差异显式保存失败恢复与重载()
    {
        Exception failure = null;
        var thread = new Thread(() =>
        {
            string root = Path.Combine(Path.GetTempPath(), "LyoCrystalContent05Form", Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "Data"));
                string libraryPath = Path.Combine(root, "Data", "Items.Lib");
                using (var bitmap = new Bitmap(2, 2))
                {
                    var seed = new MLibraryV2(libraryPath);
                    seed.AddImage(bitmap, 1, 2, false);
                    seed.Save();
                }
                File.Copy(libraryPath, Path.Combine(root, "Data", "Copy.Lib"));
                string manifest = Path.Combine(root, "bootstrap-packages.json");
                File.WriteAllText(manifest, """
                    { "Packs": [
                      { "Name": "items", "Assets": ["Data/Items.Lib"] },
                      { "Name": "missing", "Assets": ["Data/Missing.Lib"] }
                    ] }
                    """);
                var failPersist = true;
                var persistCalls = 0;
                using var form = new LMain(library =>
                {
                    persistCalls++;
                    if (failPersist) throw new IOException("模拟占用");
                    library.Save();
                }, _ => true);
                form.LoadLibraryForAuthoring(libraryPath);
                form.LoadResourceWorkspace(root, manifest);

                Assert.Equal(["items"], form.GetResourceOwners("data/items.lib"));
                Assert.Contains("CONTENT05-RESOURCE-001", form.GetResourceAnalysis());
                Assert.Contains("Data/Items.Lib ← items", form.GetResourceAnalysis());
                Assert.Contains("不会自动删除", form.GetResourceAnalysis());
                Assert.True(form.NavigateToResource("Data/Items.Lib"));
                Assert.True(form.SetImageOffsetForAuthoring(0, 9, 8));
                Assert.True(form.HasUnsavedChanges);
                Assert.Contains("图像 0", form.GetDraftChanges());
                Assert.False(form.TrySaveDraft(out string referenceFailure));
                Assert.Contains("CONTENT05-RESOURCE-001", referenceFailure);
                Assert.Equal(0, persistCalls);
                Assert.EndsWith("bootstrap-packages.json", form.CurrentResourceOwnerPath, StringComparison.OrdinalIgnoreCase);
                File.WriteAllText(Path.Combine(root, "Data", "Missing.Lib"), "resolved");
                form.LoadResourceWorkspace(root, manifest);
                Assert.False(form.TrySaveDraft(out string failureMessage));
                Assert.Contains("模拟占用", failureMessage);
                Assert.True(form.HasUnsavedChanges);
                Assert.Equal(1, new MLibraryV2(libraryPath).GetMImage(0).X);

                failPersist = false;
                Assert.True(form.TrySaveDraft(out string saveError), saveError);
                Assert.False(form.HasUnsavedChanges);
                Assert.Equal(9, new MLibraryV2(libraryPath).GetMImage(0).X);
                Assert.True(form.SetImageOffsetForAuthoring(0, 4, 3));
                Assert.True(form.ReloadDraft());
                Assert.False(form.HasUnsavedChanges);
                Assert.Equal("无变更", form.GetDraftChanges());
                Assert.Equal(9, new MLibraryV2(libraryPath).GetMImage(0).X);
                Assert.Equal("AddButton", form.MoveWorkspaceFocusForAuthoring(false));
                Assert.Equal("PreviewListView", form.MoveWorkspaceFocusForAuthoring(false));
                Assert.Equal("ResourceShowAnalysisButton", form.MoveWorkspaceFocusForAuthoring(false));
                Assert.Equal("PreviewListView", form.MoveWorkspaceFocusForAuthoring(true));

                var loadCalls = 0;
                using var reloadFailureForm = new LMain(null, _ => true, fileName =>
                {
                    loadCalls++;
                    if (loadCalls > 1) throw new IOException("模拟重载损坏");
                    return new MLibraryV2(fileName);
                });
                reloadFailureForm.LoadLibraryForAuthoring(libraryPath);
                Assert.True(reloadFailureForm.SetImageOffsetForAuthoring(0, 14, 13));
                Assert.False(reloadFailureForm.ReloadDraft());
                Assert.True(reloadFailureForm.HasUnsavedChanges);
                Assert.Contains("重载失败", reloadFailureForm.GetAuthoringStatus());
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "资源编辑器窗口行为测试超时。");
        Assert.Null(failure);
    }

    [Theory]
    [InlineData(96)]
    [InlineData(120)]
    [InlineData(144)]
    [InlineData(192)]
    public void 资源作者工具四档DPI保持命令可达且互不重叠(int dpi)
    {
        Exception failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var form = new NonActivatingLibraryEditorForm();
                form.ClientSize = new Size(1280, 800);
                float scale = dpi / 96F;
                form.Scale(new SizeF(scale, scale));
                form.Opacity = 0;
                form.ShowInTaskbar = false;
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-32000, -32000);
                form.Show();
                Application.DoEvents();
                FlowLayoutPanel toolbar = Assert.IsType<FlowLayoutPanel>(
                    Assert.Single(form.Controls.Find("ResourceAuthoringToolbar", true)));
                toolbar.PerformLayout();
                Button[] buttons = toolbar.Controls.Cast<Control>().OfType<Button>().Where(button => button.Visible).ToArray();
                Assert.NotEmpty(buttons);
                Assert.All(buttons, button =>
                {
                    Assert.True(button.Font.Size >= 12F);
                    Assert.True(button.Width >= button.PreferredSize.Width);
                    Assert.True(button.Height >= button.PreferredSize.Height);
                });
                for (var left = 0; left < buttons.Length; left++)
                    for (var right = left + 1; right < buttons.Length; right++)
                        Assert.False(buttons[left].Bounds.IntersectsWith(buttons[right].Bounds));
                form.Close();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), $"资源作者工具 DPI {dpi} 测试超时。");
        Assert.Null(failure);
    }

    [Fact]
    public void 资源作者工作区可后台渲染宽屏与折叠态截图()
    {
        Exception failure = null;
        var thread = new Thread(() =>
        {
            string root = Path.Combine(Path.GetTempPath(), "LyoCrystalContent05Shot", Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "Data"));
                string libraryPath = Path.Combine(root, "Data", "Items.Lib");
                using (var bitmap = new Bitmap(2, 2))
                {
                    var seed = new MLibraryV2(libraryPath);
                    seed.AddImage(bitmap, 1, 2, false);
                    seed.Save();
                }
                File.Copy(libraryPath, Path.Combine(root, "Data", "Copy.Lib"));
                string manifest = Path.Combine(root, "bootstrap-packages.json");
                File.WriteAllText(manifest, """
                    { "Packs": [
                      { "Name": "items", "Assets": ["Data/Items.Lib"] },
                      { "Name": "missing", "Assets": ["Data/Missing.Lib"] }
                    ] }
                    """);
                using var form = new NonActivatingLibraryEditorForm();
                form.ClientSize = new Size(1280, 800);
                form.Opacity = 0;
                form.ShowInTaskbar = false;
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-32000, -32000);
                form.Show();
                form.LoadLibraryForAuthoring(libraryPath);
                form.LoadResourceWorkspace(root, manifest);
                form.SetImageOffsetForAuthoring(0, 9, 8);
                Application.DoEvents();
                Panel panel = Assert.IsType<Panel>(Assert.Single(form.Controls.Find("ResourceAnalysisPanel", true)));
                Assert.True(panel.Visible);
                string evidenceRoot = Path.GetFullPath(Path.Combine(
                    AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..",
                    "Docs", "Evidence", "LEG-06-20260813"));
                Directory.CreateDirectory(evidenceRoot);
                SaveControlImage(form, Path.Combine(evidenceRoot, "CONTENT-05-resource-workspace-1280x800.png"));
                SaveControlImage(panel, Path.Combine(evidenceRoot, "CONTENT-05-resource-analysis-panel.png"));

                form.ClientSize = new Size(1100, 700);
                form.PerformLayout();
                Application.DoEvents();
                Assert.False(panel.Visible);
                Button show = Assert.IsType<Button>(Assert.Single(form.Controls.Find("ResourceShowAnalysisButton", true)));
                Assert.True(show.Visible);
                SaveControlImage(form, Path.Combine(evidenceRoot, "CONTENT-05-resource-workspace-1100x700.png"));
                form.SetAnalysisPanelVisible(true);
                form.PerformLayout();
                Assert.True(panel.Visible);
                Assert.True(panel.Width >= 280);
                form.Close();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "资源作者工作区后台截图测试超时。");
        Assert.Null(failure);
    }

    private static void SaveControlImage(Control control, string path)
    {
        using var bitmap = new Bitmap(control.ClientSize.Width, control.ClientSize.Height);
        control.DrawToBitmap(bitmap, new Rectangle(Point.Empty, control.ClientSize));
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        Assert.True(new FileInfo(path).Length > 1000);
    }

    private sealed class NonActivatingLibraryEditorForm : LMain
    {
        public NonActivatingLibraryEditorForm() : base(null, _ => true) { }

        protected override bool ShowWithoutActivation => true;
    }

    [Fact]
    public void 资源编辑器拒绝放弃时保持当前草稿并取消关闭()
    {
        Exception failure = null;
        var thread = new Thread(() =>
        {
            string root = Path.Combine(Path.GetTempPath(), "LyoCrystalContent05Discard", Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(root);
                string firstPath = Path.Combine(root, "First.Lib");
                string secondPath = Path.Combine(root, "Second.Lib");
                using (var bitmap = new Bitmap(2, 2))
                {
                    foreach (string path in new[] { firstPath, secondPath })
                    {
                        var seed = new MLibraryV2(path);
                        seed.AddImage(bitmap, 1, 2, false);
                        seed.Save();
                    }
                }
                using var form = new NonActivatingRejectingLibraryEditorForm();
                form.Opacity = 0;
                form.ShowInTaskbar = false;
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-32000, -32000);
                form.Show();
                form.LoadLibraryForAuthoring(firstPath);
                Assert.True(form.SetImageOffsetForAuthoring(0, 9, 8));

                form.LoadLibraryForAuthoring(secondPath);
                Assert.Equal(Path.GetFullPath(firstPath), Path.GetFullPath(form.CurrentLibraryPath));
                Assert.True(form.HasUnsavedChanges);
                form.Close();
                Application.DoEvents();
                Assert.False(form.IsDisposed);
                Assert.True(form.Visible);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "资源编辑器取消关闭测试超时。");
        Assert.Null(failure);
    }

    private sealed class NonActivatingRejectingLibraryEditorForm : LMain
    {
        public NonActivatingRejectingLibraryEditorForm() : base(null, _ => false) { }

        protected override bool ShowWithoutActivation => true;
    }
}
