using Xunit;
using Server.MirDatabase;
using Server.Authoring;
using Server.Diagnostics;
using Server.MirForms.VisualMapInfo.Class;
using Server.MirForms.VisualMapInfo.Control;
using Microsoft.VisualBasic.PowerPacks;

namespace Server.ContentAuthoringIntegration.Windows;

public sealed class MapContentAuthoringFormTests
{
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
