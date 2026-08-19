using Xunit;
using Server.MirDatabase;
using Server.Authoring;
using Server.Diagnostics;
using Server.MirForms.VisualMapInfo.Class;
using Server.MirForms.VisualMapInfo.Control;
using Microsoft.VisualBasic.PowerPacks;
using Server.MirForms.DropBuilder;
using Server.Scripting;

namespace Server.ContentAuthoringIntegration.Windows;

public sealed class MapContentAuthoringFormTests
{
    [Theory]
    [InlineData(96)]
    [InlineData(120)]
    [InlineData(144)]
    [InlineData(192)]
    public void 掉落分析面板四档DPI关键命令保持边界内(int dpi)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var envir = global::Server.SMain.EditEnvir;
            MonsterInfo[] originalMonsters = envir.MonsterInfoList.ToArray();
            string root = Path.Combine(Path.GetTempPath(), "LyoCrystalContent04Dpi", Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(root);
                File.WriteAllText(Path.Combine(root, "测试怪.txt"), ";Gold\r\n1/5 Gold 100\r\n");
                envir.MonsterInfoList.Clear();
                envir.MonsterInfoList.Add(new MonsterInfo { Name = "测试怪" });
                using var form = new DropGenForm(name => name == "木剑", _ => null, root);
                form.ClientSize = new Size(1280, 800);
                float scale = dpi / 96F;
                form.Scale(new SizeF(scale, scale));
                form.CreateControl();

                Panel panel = Assert.IsType<Panel>(Assert.Single(form.Controls.Find("DropAuthoringPanel", true)));
                FlowLayoutPanel toolbar = Assert.IsType<FlowLayoutPanel>(Assert.Single(form.Controls.Find("DropAuthoringToolbar", true)));
                panel.PerformLayout();
                toolbar.PerformLayout();
                Button[] buttons = toolbar.Controls.Cast<Button>().ToArray();
                Assert.All(buttons, button =>
                {
                    Assert.True(button.Width > 0 && button.Height > 0);
                    Assert.True(button.Font.Size >= 12F);
                    Assert.True(button.PreferredSize.Width <= button.Width);
                    Assert.True(button.PreferredSize.Height <= button.Height);
                });
                for (var left = 0; left < buttons.Length; left++)
                    for (var right = left + 1; right < buttons.Length; right++)
                        Assert.False(buttons[left].Bounds.IntersectsWith(buttons[right].Bounds));
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                envir.MonsterInfoList.Clear();
                envir.MonsterInfoList.AddRange(originalMonsters);
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), $"掉落 DPI {dpi} 窗口测试超时。");
        Assert.Null(failure);
    }

    [Fact]
    public void 掉落分析工作区可后台渲染代表性截图()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var envir = global::Server.SMain.EditEnvir;
            MonsterInfo[] originalMonsters = envir.MonsterInfoList.ToArray();
            string root = Path.Combine(Path.GetTempPath(), "LyoCrystalContent04Shot", Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(root);
                File.WriteAllText(Path.Combine(root, "测试怪.txt"), "1/5 木剑");
                envir.MonsterInfoList.Clear();
                envir.MonsterInfoList.Add(new MonsterInfo { Name = "测试怪" });
                using var form = new NonActivatingDropGenForm(_ => true, root);
                form.ClientSize = new Size(1280, 800);
                form.Opacity = 0;
                form.ShowInTaskbar = false;
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-32000, -32000);
                form.Show();
                Application.DoEvents();
                form.SetDraftText("1/5 Gold 100");
                Assert.True(form.TrySaveDraft((_, _) => { }, out string baselineError), baselineError);
                form.RefreshAnalysis();
                Panel panel = Assert.IsType<Panel>(Assert.Single(form.Controls.Find("DropAuthoringPanel", true)));
                panel.PerformLayout();
                string evidenceRoot = Path.GetFullPath(Path.Combine(
                    AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..",
                    "Docs", "Evidence", "LEG-06-20260813"));
                Directory.CreateDirectory(evidenceRoot);
                string panelEvidence = Path.Combine(evidenceRoot, "CONTENT-04-drop-analysis-panel.png");
                using (var bitmap = new Bitmap(panel.ClientSize.Width, panel.ClientSize.Height))
                {
                    panel.DrawToBitmap(bitmap, new Rectangle(Point.Empty, panel.ClientSize));
                    bitmap.Save(panelEvidence, System.Drawing.Imaging.ImageFormat.Png);
                }
                Assert.True(new FileInfo(panelEvidence).Length > 1000);

                string wideEvidence = Path.Combine(evidenceRoot, "CONTENT-04-drop-workspace-1280x800.png");
                using (var bitmap = new Bitmap(form.ClientSize.Width, form.ClientSize.Height))
                {
                    form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.ClientSize));
                    bitmap.Save(wideEvidence, System.Drawing.Imaging.ImageFormat.Png);
                }
                Assert.True(new FileInfo(wideEvidence).Length > 1000);

                form.ClientSize = new Size(1100, 700);
                form.SetAnalysisPanelExpanded(false);
                form.PerformLayout();
                Application.DoEvents();
                string compactEvidence = Path.Combine(evidenceRoot, "CONTENT-04-drop-workspace-1100x700.png");
                using (var bitmap = new Bitmap(form.ClientSize.Width, form.ClientSize.Height))
                {
                    form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.ClientSize));
                    bitmap.Save(compactEvidence, System.Drawing.Imaging.ImageFormat.Png);
                }
                Assert.True(new FileInfo(compactEvidence).Length > 1000);
                form.Close();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                envir.MonsterInfoList.Clear();
                envir.MonsterInfoList.AddRange(originalMonsters);
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "掉落工作区后台截图测试超时。");
        Assert.Null(failure);
    }

    private sealed class NonActivatingDropGenForm : DropGenForm
    {
        public NonActivatingDropGenForm(Func<string, bool> itemExists, string dropRoot)
            : base(itemExists, _ => null, dropRoot)
        {
        }

        protected override bool ShowWithoutActivation => true;
    }


    [Fact]
    public void 掉落编辑器提供分析差异显式保存重载且预览不隐式写盘()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var envir = global::Server.SMain.EditEnvir;
            MonsterInfo[] originalMonsters = envir.MonsterInfoList.ToArray();
            string root = Path.Combine(Path.GetTempPath(), "LyoCrystalContent04", Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(root);
                string file = Path.Combine(root, "测试怪.txt");
                const string originalText = ";武器\r\n1/10 木剑\r\n";
                File.WriteAllText(file, originalText);
                envir.MonsterInfoList.Clear();
                envir.MonsterInfoList.Add(new MonsterInfo { Name = "测试怪" });

                using var form = new DropGenForm(name => name == "木剑", _ => null, root);
                FlowLayoutPanel toolbar = Assert.IsType<FlowLayoutPanel>(Assert.Single(form.Controls.Find("DropAuthoringToolbar", true)));
                Assert.Equal(["分析", "差异", "保存", "重载"], toolbar.Controls.Cast<Button>().Select(value => value.Text));
                Assert.Equal(700, form.MinimumSize.Height);
                int displayBoundWidth = (Screen.PrimaryScreen?.WorkingArea.Width ?? 1100)
                    - SystemInformation.FrameBorderSize.Width * 2;
                Assert.True(
                    form.MinimumSize.Width == 1100 || form.MinimumSize.Width >= displayBoundWidth,
                    $"窗体最小宽度应为 1100，或受当前显示环境约束；实际 {form.MinimumSize.Width}，显示上限 {displayBoundWidth}。");
                Assert.True(Assert.IsType<TextBox>(Assert.Single(form.Controls.Find("DropAnalysisTextBox", true))).Font.Size >= 12F);
                form.ClientSize = new Size(1100, 700);
                Assert.False(Assert.IsType<Panel>(Assert.Single(form.Controls.Find("DropAuthoringPanel", true))).Visible);
                Assert.Single(form.Controls.Find("ShowDropAnalysisButton", true));
                form.SetAnalysisPanelExpanded(true);
                Assert.True(form.IsAnalysisPanelExpanded);
                form.ClientSize = new Size(1280, 800);

                form.SetDraftText(";武器\r\n1/5 木剑\r\n");
                Assert.True(form.HasPendingChanges);
                Assert.Equal(originalText, File.ReadAllText(file));
                Assert.Contains(form.GetDraftDiff(), value => value.After.Contains("1/5 木剑"));

                form.RefreshAnalysis();
                Assert.Contains("概率展开", form.AnalysisText);
                Assert.Equal(originalText, File.ReadAllText(file));

                int persisted = 0;
                Assert.True(form.TrySaveDraft((path, text) =>
                {
                    persisted++;
                    Assert.Equal(file, path);
                    File.WriteAllText(path, text.Replace("\n", "\r\n"));
                }, out string saveError), saveError);
                Assert.Equal(1, persisted);
                Assert.False(form.HasPendingChanges);
                Assert.Contains("1/5 木剑", File.ReadAllText(file));

                persisted = 0;
                Assert.True(form.TrySaveDraft((_, _) => persisted++, out string unchangedError), unchangedError);
                Assert.Equal(0, persisted);

                form.SetDraftText("1/0 木剑");
                persisted = 0;
                Assert.False(form.TrySaveDraft((_, _) => persisted++, out string validationError));
                Assert.Contains("CONTENT04-DROP-001", validationError);
                Assert.Equal(0, persisted);

                var scripted = new DropTableDefinition("Drops/测试怪");
                scripted.Drops.Add(DropEntryDefinition.Item(1, "木剑"));
                using (var scriptedForm = new DropGenForm(name => name == "木剑", _ => scripted, root))
                {
                    scriptedForm.SetDraftText("1/0 木剑");
                    persisted = 0;
                    Assert.False(scriptedForm.TrySaveDraft((_, _) => persisted++, out string scriptedValidationError));
                    Assert.Contains("CONTENT04-DROP-001", scriptedValidationError);
                    Assert.Equal(0, persisted);
                    scriptedForm.RefreshAnalysis();
                    Assert.Contains("来源：C# 脚本定义", scriptedForm.AnalysisText);
                    Assert.Contains("脚本定义对比", scriptedForm.AnalysisText);
                    scriptedForm.ReloadDraft(false);
                }

                form.SetDraftText("1/2 木剑");
                Assert.False(form.TrySaveDraft((_, _) => throw new IOException("磁盘不可写"), out string persistError));
                Assert.Equal("磁盘不可写", persistError);
                Assert.True(form.HasPendingChanges);
                form.ReloadDraft(false);
                Assert.False(form.HasPendingChanges);
                Assert.Contains("木剑", form.AnalysisText);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                envir.MonsterInfoList.Clear();
                envir.MonsterInfoList.AddRange(originalMonsters);
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "掉落内容闭环窗口测试超时。");
        Assert.Null(failure);
    }

    [Fact]
    public void 地图内容窗体提供显式编辑会话入口且构造不修改原地图()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var form = new global::Server.MirForms.VisualMapInfo.VForm();
                ToolStrip toolbar = Assert.IsType<ToolStrip>(Assert.Single(form.Controls.Find("ContentAuthoringToolbar", true)));
                Assert.Equal("撤销", Assert.Single(toolbar.Items.Find("UndoContentButton", true)).Text);
                Assert.Equal("重做", Assert.Single(toolbar.Items.Find("RedoContentButton", true)).Text);
                Assert.Equal("校验与差异", Assert.Single(toolbar.Items.Find("ReviewContentButton", true)).Text);
                var layers = Assert.IsType<ToolStripDropDownButton>(Assert.Single(toolbar.Items.Find("ContentLayersButton", true)));
                Assert.Equal(["出口", "NPC", "刷怪", "矿区"], layers.DropDownItems.Cast<ToolStripMenuItem>().Select(item => item.Text));
                Assert.All(layers.DropDownItems.Cast<ToolStripMenuItem>(), item => Assert.True(item.Checked));
                Assert.Equal("诊断定位", Assert.Single(toolbar.Items.Find("ContentDiagnosticsButton", true)).Text);
                Assert.Equal("保存", Assert.Single(toolbar.Items.Find("SaveContentButton", true)).Text);
                Assert.Equal("取消", Assert.Single(toolbar.Items.Find("CancelContentButton", true)).Text);
                Assert.Equal(DialogResult.None, form.DialogResult);
                Assert.False(form.HasCommittedChanges);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "地图内容窗体宿主测试超时。");
        Assert.Null(failure);
    }

    [Fact]
    public void NPC编辑器可按稳定索引定位真实记录()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var envir = global::Server.SMain.EditEnvir;
            var original = envir.NPCInfoList.ToArray();
            try
            {
                envir.NPCInfoList.Clear();
                envir.NPCInfoList.AddRange([
                    new NPCInfo { Index = 2, FileName = "first" },
                    new NPCInfo { Index = 7, FileName = "target" },
                ]);
                using var form = new global::Server.NPCInfoForm();
                Assert.True(form.SelectNpc(7));
                ListBox list = Assert.IsType<ListBox>(Assert.Single(form.Controls.Find("NPCInfoListBox", true)));
                Assert.Equal(7, Assert.IsType<NPCInfo>(list.SelectedItem).Index);
                Assert.False(form.SelectNpc(999));
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                envir.NPCInfoList.Clear();
                envir.NPCInfoList.AddRange(original);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "NPC 编辑器定位测试超时。");
        Assert.Null(failure);
    }

    [Fact]
    public void NPC编辑器公开显式会话和脚本闭环入口且草稿不直接修改事实对象()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var envir = global::Server.SMain.EditEnvir;
            NPCInfo[] original = envir.NPCInfoList.ToArray();
            try
            {
                var npc = new NPCInfo { Index = 7, FileName = "merchant", Name = "原名称", MapIndex = 10, Location = new Point(8, 9) };
                envir.NPCInfoList.Clear();
                envir.NPCInfoList.Add(npc);
                using var form = new global::Server.NPCInfoForm(7);

                FlowLayoutPanel toolbar = Assert.IsType<FlowLayoutPanel>(Assert.Single(form.Controls.Find("NpcAuthoringToolbar", true)));
                Assert.Equal("保存", Assert.Single(toolbar.Controls.Find("SaveNpcContentButton", true)).Text);
                Assert.Equal("重载", Assert.Single(toolbar.Controls.Find("ReloadNpcContentButton", true)).Text);
                Assert.Equal("差异", Assert.Single(toolbar.Controls.Find("DiffNpcContentButton", true)).Text);
                Assert.Single(form.Controls.Find("NpcScriptWorkflowTab", true));
                Assert.Single(form.Controls.Find("PreviewNpcScriptButton", true));
                Assert.Single(form.Controls.Find("OpenNpcScriptButton", true));
                Assert.Single(form.Controls.Find("OpenNpcResourceButton", true));

                Assert.NotNull(form.SelectedDraft);
                form.SelectedDraft!.Name = "草稿名称";
                Assert.Equal("原名称", npc.Name);
                Assert.True(form.HasPendingChanges);
                Assert.Contains(form.GetDraftDiff(), value => value.EntityIndex == 7 && value.Summary.Contains(nameof(NPCInfo.Name)));
                form.ReloadDraft();
                Assert.Equal("原名称", form.SelectedDraft!.Name);
                Assert.False(form.HasPendingChanges);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                envir.NPCInfoList.Clear();
                envir.NPCInfoList.AddRange(original);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "NPC 内容闭环窗口测试超时。");
        Assert.Null(failure);
    }

    [Fact]
    public void 脚本调试器可通过公共入口定位指定现有脚本()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            string root = Path.Combine(Path.GetTempPath(), "LyoCrystalContent03", Guid.NewGuid().ToString("N"));
            string originalRoot = global::Server.Settings.CSharpScriptsPath;
            try
            {
                Directory.CreateDirectory(root);
                string file = Path.Combine(root, "Merchant.cs");
                File.WriteAllText(file, "// test");
                global::Server.Settings.CSharpScriptsPath = root;
                using var form = new Server.MirForms.Systems.ScriptDebugForm(file);
                form.Show();
                Application.DoEvents();
                Assert.Equal(Path.GetFullPath(file), form.CurrentFilePath);
                form.Close();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                global::Server.Settings.CSharpScriptsPath = originalRoot;
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "脚本调试器定位测试超时。");
        Assert.Null(failure);
    }

    [Fact]
    public void NPC窗体保存成功校验阻断与保存失败均保持可观察会话语义()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var envir = global::Server.SMain.EditEnvir;
            NPCInfo[] originalNpcs = envir.NPCInfoList.ToArray();
            MapInfo[] originalMaps = envir.MapInfoList.ToArray();
            int originalHighWatermark = envir.NPCIndex;
            string npcRoot = Path.Combine(Path.GetTempPath(), "LyoCrystalContent03Npc", Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(npcRoot);
                envir.MapInfoList.Clear();
                envir.MapInfoList.Add(new MapInfo { Index = 10, FileName = "test" });
                envir.NPCInfoList.Clear();
                var npc = new NPCInfo { Index = 7, FileName = "merchant", Name = "原名称", MapIndex = 10, Location = new Point(8, 9), Image = 3 };
                envir.NPCInfoList.Add(npc);
                envir.NPCIndex = 99;

                using (var form = new global::Server.NPCInfoForm(7, npcRoot))
                {
                    form.SelectedDraft!.Name = "已保存名称";
                    NPCInfo added = form.AddDraft();
                    added.FileName = "added";
                    added.Name = "新增 NPC";
                    added.MapIndex = 10;
                    added.Location = new Point(3, 4);
                    Assert.Equal(100, added.Index);
                    int persisted = 0;
                    Assert.True(form.TrySaveDraft(() =>
                    {
                        persisted++;
                        Assert.Equal(100, envir.NPCIndex);
                    }, out string error), error);
                    Assert.Equal(1, persisted);
                    Assert.Equal("已保存名称", npc.Name);
                    Assert.Contains(envir.NPCInfoList, value => value.Index == 100);
                    Assert.False(form.HasPendingChanges);
                }

                File.WriteAllLines(Path.Combine(npcRoot, "merchant.txt"), ["[@MAIN]", "<坏链接/@MISSING>"]);
                using (var form = new global::Server.NPCInfoForm(7, npcRoot))
                {
                    form.SelectedDraft!.Name = "链接失败草稿";
                    int persisted = 0;
                    Assert.False(form.TrySaveDraft(() => persisted++, out string error));
                    Assert.Contains("CONTENT03-LINK-001", error);
                    Assert.Equal(0, persisted);
                    Assert.Equal("已保存名称", npc.Name);
                    Assert.True(form.HasPendingChanges);
                    form.ReloadDraft();
                }
                File.Delete(Path.Combine(npcRoot, "merchant.txt"));

                using (var form = new global::Server.NPCInfoForm(7, npcRoot))
                {
                    form.SelectedDraft!.Location = new Point(-1, 9);
                    Assert.False(form.TrySaveDraft(() => throw new InvalidOperationException("不应执行"), out string error));
                    Assert.Contains("LEG02-NPC-002", error);
                    Assert.Equal(new Point(8, 9), npc.Location);
                }

                using (var form = new global::Server.NPCInfoForm(7, npcRoot))
                {
                    form.SelectedDraft!.Name = "失败草稿";
                    int previousHighWatermark = envir.NPCIndex;
                    int observedHighWatermark = 0;
                    Assert.False(form.TrySaveDraft(() =>
                    {
                        observedHighWatermark = envir.NPCIndex;
                        throw new IOException("磁盘不可写");
                    }, out string error));
                    Assert.Equal("磁盘不可写", error);
                    Assert.Equal(100, observedHighWatermark);
                    Assert.Equal(previousHighWatermark, envir.NPCIndex);
                    Assert.Equal("已保存名称", npc.Name);
                    Assert.Equal("失败草稿", form.SelectedDraft!.Name);
                    Assert.True(form.HasPendingChanges);
                    form.ReloadDraft();
                }

                using var resolverForm = new global::Server.NPCInfoForm(7, npcRoot);
                Assert.Equal(Path.Combine(npcRoot, "merchant.txt"), resolverForm.ResolveSelectedScriptPath());
                Assert.EndsWith(Path.Combine("Previews", "NPC", "3.bmp"), resolverForm.GetSelectedPreviewResourcePath(), StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                envir.NPCInfoList.Clear();
                envir.NPCInfoList.AddRange(originalNpcs);
                envir.MapInfoList.Clear();
                envir.MapInfoList.AddRange(originalMaps);
                envir.NPCIndex = originalHighWatermark;
                if (Directory.Exists(npcRoot)) Directory.Delete(npcRoot, true);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "NPC 保存门禁窗口测试超时。");
        Assert.Null(failure);
    }

    [Fact]
    public void 四类叠层可同时显示独立关闭且诊断定位真实刷怪记录()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            MapInfo? originalMap = VisualizerGlobal.MapInfo;
            try
            {
                var map = new MapInfo { Index = 10, Title = "测试地图" };
                map.Movements.Add(new MovementInfo { Source = new Point(2, 3), MapIndex = 11 });
                map.Respawns.Add(new RespawnInfo { MonsterIndex = 5, Location = new Point(4, 5), Count = 1, Delay = 1 });
                map.MineZones.Add(new MineZone { Mine = 1, Location = new Point(6, 7), Size = 2 });
                var npc = new NPCInfo { Index = 7, MapIndex = 10, FileName = "merchant", Location = new Point(8, 9) };
                VisualizerGlobal.MapInfo = map;

                using var form = new global::Server.MirForms.VisualMapInfo.VForm();
                ShapeContainer canvas = new();
                PictureBox mapImage = Assert.IsType<PictureBox>(Assert.Single(form.Controls.Find("MapImage", true)));
                canvas.Parent = mapImage;

                Panel respawnPanel = Assert.IsType<Panel>(Assert.Single(form.Controls.Find("RespawnPanel", true)));
                ComboBox respawnFilter = Assert.IsType<ComboBox>(Assert.Single(form.Controls.Find("RespawnsFilter", true)));
                respawnFilter.Items.Add("No Filter");
                respawnFilter.SelectedItem = "No Filter";
                var respawn = new RespawnEntry { MonsterIndex = 5, X = 4, Y = 5, Range = 2, Tag = 0 };
                respawn.Count.Text = "1";
                respawn.Delay.Text = "1";
                respawn.RegionHighlight.Parent = canvas;
                respawn.ShowControl();
                respawnPanel.Controls.Add(respawn);

                Panel minePanel = Assert.IsType<Panel>(Assert.Single(form.Controls.Find("MiningPanel", true)));
                ComboBox mineFilter = Assert.IsType<ComboBox>(Assert.Single(form.Controls.Find("MiningFilter", true)));
                mineFilter.Items.Add("No Filter");
                mineFilter.SelectedItem = "No Filter";
                var mine = new MineEntry { MineIndex = 1, X = 6, Y = 7, Range = 2, Tag = 0 };
                mine.RegionHighlight.Parent = canvas;
                mine.ShowControl();
                minePanel.Controls.Add(mine);

                IReadOnlyList<MapContentTarget> targets = MapContentNavigation.BuildTargets(
                    map, [npc], MapContentEditingSession.Capture(map));
                form.LoadLayerTargets(targets);
                form.SetLayerVisibility(MapContentLayer.Respawn, true);
                form.SetLayerVisibility(MapContentLayer.MineZone, true);

                Assert.Equal(1, form.GetVisibleTargetCount(MapContentLayer.Exit));
                Assert.Equal(1, form.GetVisibleTargetCount(MapContentLayer.Npc));
                Assert.Equal(1, form.GetVisibleTargetCount(MapContentLayer.Respawn));
                Assert.Equal(1, form.GetVisibleTargetCount(MapContentLayer.MineZone));

                foreach (MapContentLayer hidden in new[]
                         {
                             MapContentLayer.Exit, MapContentLayer.Npc,
                             MapContentLayer.Respawn, MapContentLayer.MineZone,
                         })
                {
                    form.SetLayerVisibility(hidden, false);
                    Assert.Equal(0, form.GetVisibleTargetCount(hidden));
                    foreach (MapContentLayer visible in new[]
                             {
                                 MapContentLayer.Exit, MapContentLayer.Npc,
                                 MapContentLayer.Respawn, MapContentLayer.MineZone,
                             }.Where(item => item != hidden))
                        Assert.Equal(1, form.GetVisibleTargetCount(visible));
                    form.SetLayerVisibility(hidden, true);
                }

                var diagnostic = new ProjectPreflightDiagnostic(
                    "LEG02-SPAWN-001", ProjectPreflightSeverity.Error,
                    "MapInfo[10].Respawns[0]", "怪物不存在");
                Assert.True(form.TryNavigateToDiagnostic(diagnostic));
                Assert.True(respawn.Selected.Checked);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                VisualizerGlobal.MapInfo = originalMap;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "地图叠层行为测试超时。");
        Assert.Null(failure);
    }

    [Fact]
    public void 地图拥有者编辑器可定位出口并识别NPC记录()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var envir = global::Server.SMain.EditEnvir;
            MapInfo[] originalMaps = envir.MapInfoList.ToArray();
            NPCInfo[] originalNpcs = envir.NPCInfoList.ToArray();
            try
            {
                var map = new MapInfo { Index = 10, Title = "测试地图" };
                map.Movements.Add(new MovementInfo { Source = new Point(2, 3), MapIndex = 11 });
                var npc = new NPCInfo { Index = 7, MapIndex = 10, FileName = "merchant" };
                envir.MapInfoList.Clear();
                envir.MapInfoList.AddRange([map, new MapInfo { Index = 11 }]);
                envir.NPCInfoList.Clear();
                envir.NPCInfoList.Add(npc);

                using var form = new global::Server.MapInfoForm();
                ListBox maps = Assert.IsType<ListBox>(Assert.Single(form.Controls.Find("MapInfoListBox", true)));
                form.Show();
                maps.Focus();
                maps.SelectedItem = map;

                IReadOnlyList<MapContentTarget> targets = MapContentNavigation.BuildTargets(map, [npc]);
                Assert.True(form.NavigateToContentOwner(Assert.Single(targets, item => item.Layer == MapContentLayer.Exit)));
                ListBox movements = Assert.IsType<ListBox>(Assert.Single(form.Controls.Find("MovementInfoListBox", true)));
                Assert.Same(map.Movements[0], movements.SelectedItem);
                Assert.True(form.NavigateToContentOwner(
                    Assert.Single(targets, item => item.Layer == MapContentLayer.Npc), showNpcEditor: false));
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                envir.MapInfoList.Clear();
                envir.MapInfoList.AddRange(originalMaps);
                envir.NPCInfoList.Clear();
                envir.NPCInfoList.AddRange(originalNpcs);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "地图拥有者定位测试超时。");
        Assert.Null(failure);
    }
}
