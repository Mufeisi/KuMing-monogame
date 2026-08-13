using Server.MirForms.VisualMapInfo.Class;
using Server.MirForms.VisualMapInfo.Control;
using Microsoft.VisualBasic.PowerPacks;
using Server.MirEnvir;
using Server.MirDatabase;
using Server.Authoring;
using Server.Diagnostics;

namespace Server.MirForms.VisualMapInfo
{
    public partial class VForm : Form
    {
        ShapeContainer Canvas = new ShapeContainer();

        public Envir Envir => SMain.EditEnvir;

        public Point MouseDownLocation;

        private MapContentEditingSession _editingSession;
        private ToolStrip _contentToolbar;
        private ToolStripButton _undoButton;
        private ToolStripButton _redoButton;
        private ToolStripButton _reviewButton;
        private ToolStripButton _saveButton;
        private ToolStripButton _cancelButton;
        private ToolStripDropDownButton _layersButton;
        private ToolStripDropDownButton _diagnosticsButton;
        private readonly Dictionary<MapContentLayer, ToolStripMenuItem> _layerItems = new();
        private readonly Dictionary<string, RectangleShape> _pointMarkers = new(StringComparer.Ordinal);
        private IReadOnlyList<MapContentTarget> _mapTargets = Array.Empty<MapContentTarget>();
        private bool _discardConfirmed;
        private int _nextRespawnDraftIndex;
        private int _nextMineDraftIndex;

        public bool HasCommittedChanges { get; private set; }
        public MapContentTarget RequestedOwnerTarget { get; private set; }

        public VForm()
        {
            InitializeComponent();
            InitializeContentToolbar();
        }

        private void VForm_Load(object sender, EventArgs e)
        {
            InitializeMap();
            _editingSession = new MapContentEditingSession(
                VisualizerGlobal.MapInfo,
                Envir.MonsterInfoList.Select(item => item.Index),
                VisualizerGlobal.ClippingMap.Width,
                VisualizerGlobal.ClippingMap.Height);
            InitializeMineInfo();
            InitializeRespawnInfo();
            _nextMineDraftIndex = MiningPanel.Controls.OfType<MineEntry>().Count();
            _nextRespawnDraftIndex = RespawnPanel.Controls.OfType<RespawnEntry>().Count();
            RefreshMapTargets();
            RefreshNavigationDiagnostics();
            VisualizerGlobal.FocusModeActivated += FocusModeActivated;
            Text = $"内容生产工作台 - {VisualizerGlobal.MapInfo.Title}";
            UpdateHistoryButtons();
        }

        private void VForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (!_discardConfirmed && _editingSession != null && TryCaptureDraft(out MapContentDraft draft, out _))
            {
                MapContentReview review = _editingSession.Review(draft);
                if (review.HasChanges)
                {
                    DialogResult decision = MessageBox.Show(
                        "当前地图内容尚未保存。\n\n是：校验并保存\n否：放弃修改\n取消：继续编辑",
                        "关闭内容生产工作台",
                        MessageBoxButtons.YesNoCancel,
                        MessageBoxIcon.Warning);
                    if (decision == DialogResult.Cancel || decision == DialogResult.Yes && !TrySaveDraft(closeAfterSave: false))
                    {
                        e.Cancel = true;
                        return;
                    }
                    if (decision == DialogResult.No)
                        _discardConfirmed = true;
                }
            }

            VisualizerGlobal.ZoomLevel = 1;
            VisualizerGlobal.FocusModeActivated -= FocusModeActivated;
        }

        private void InitializeContentToolbar()
        {
            _contentToolbar = new ToolStrip
            {
                Name = "ContentAuthoringToolbar",
                Dock = DockStyle.Top,
                GripStyle = ToolStripGripStyle.Hidden,
                RenderMode = ToolStripRenderMode.System,
            };
            _undoButton = CreateContentButton("UndoContentButton", "撤销", (_, _) => UndoContent());
            _redoButton = CreateContentButton("RedoContentButton", "重做", (_, _) => RedoContent());
            _reviewButton = CreateContentButton("ReviewContentButton", "校验与差异", (_, _) => ShowContentReview());
            _layersButton = new ToolStripDropDownButton("叠层") { Name = "ContentLayersButton" };
            AddLayerItem(MapContentLayer.Exit, "ExitLayerButton", "出口");
            AddLayerItem(MapContentLayer.Npc, "NpcLayerButton", "NPC");
            AddLayerItem(MapContentLayer.Respawn, "RespawnLayerButton", "刷怪");
            AddLayerItem(MapContentLayer.MineZone, "MineLayerButton", "矿区");
            _diagnosticsButton = new ToolStripDropDownButton("诊断定位")
            {
                Name = "ContentDiagnosticsButton",
                Enabled = false,
            };
            _saveButton = CreateContentButton("SaveContentButton", "保存", (_, _) => TrySaveDraft(closeAfterSave: true));
            _cancelButton = CreateContentButton("CancelContentButton", "取消", (_, _) => CancelContent());
            _saveButton.Alignment = ToolStripItemAlignment.Right;
            _cancelButton.Alignment = ToolStripItemAlignment.Right;
            _contentToolbar.Items.AddRange(new ToolStripItem[]
            {
                _undoButton, _redoButton, new ToolStripSeparator(), _reviewButton,
                _layersButton, _diagnosticsButton,
                _cancelButton, _saveButton,
            });
            Controls.Add(_contentToolbar);
            _contentToolbar.BringToFront();
        }

        private void AddLayerItem(MapContentLayer layer, string name, string text)
        {
            var item = new ToolStripMenuItem(text)
            {
                Name = name,
                Checked = true,
                CheckOnClick = true,
            };
            item.CheckedChanged += (_, _) => ApplyLayerVisibility();
            _layerItems[layer] = item;
            _layersButton.DropDownItems.Add(item);
        }

        private static ToolStripButton CreateContentButton(string name, string text, EventHandler click)
        {
            var button = new ToolStripButton(text)
            {
                Name = name,
                DisplayStyle = ToolStripItemDisplayStyle.Text,
                AutoSize = true,
            };
            button.Click += click;
            return button;
        }

        private void InitializeMap()
        {
            ReadMap readMap = new ReadMap();

            readMap.mapFile = VisualizerGlobal.MapInfo.FileName;
            readMap.Load();

            MapImage.Image = VisualizerGlobal.ClippingMap;

            Canvas.Parent = MapImage;
            Canvas.BringToFront();

            MapDetailsLabel.Text =
                $"Map Name: {VisualizerGlobal.MapInfo.Title}   Width: {VisualizerGlobal.ClippingMap.Width}   Height: {VisualizerGlobal.ClippingMap.Height}";
        }

        private void InitializeMineInfo()
        {
            List<string> miningFilterItems = new() { { "Disabled" } };
            Settings.MineSetList.ForEach(x => miningFilterItems.Add(x.Name));
            miningFilterItems.Add("No Filter");

            MiningFilter.DataSource = miningFilterItems;
            MiningFilter.Text = "No Filter";

            for (int i = 0; i < VisualizerGlobal.MapInfo.MineZones.Count; i++)
            {
                MineEntry MineRegion = new MineEntry();
                MineRegion.Dock = DockStyle.Top;
                MineRegion.MineIndex = VisualizerGlobal.MapInfo.MineZones[i].Mine;
                MineRegion.X = VisualizerGlobal.MapInfo.MineZones[i].Location.X;
                MineRegion.Y = VisualizerGlobal.MapInfo.MineZones[i].Location.Y;
                MineRegion.tempRange = VisualizerGlobal.MapInfo.MineZones[i].Size;
                MineRegion.Range = VisualizerGlobal.MapInfo.MineZones[i].Size;
                MineRegion.Tag = i;
                MineRegion.ShowControl();

                MiningPanel.Controls.Add(MineRegion);

                MineRegion.RegionHighlight.Parent = Canvas;
            }            
        }

        private void InitializeRespawnInfo()
        {
            for (int i = 0; i < Envir.MonsterInfoList.Count; i++)
                RespawnsFilter.Items.Add(Envir.MonsterInfoList[i]);

            RespawnsFilter.Items.Add("No Filter");
            RespawnsFilter.Text = "No Filter";

            for (int i = 0; i < VisualizerGlobal.MapInfo.Respawns.Count; i++)
            {
                RespawnEntry RespawnRegion = new RespawnEntry();
                RespawnRegion.Dock = DockStyle.Top;
                RespawnRegion.MonsterIndex = VisualizerGlobal.MapInfo.Respawns[i].MonsterIndex;
                RespawnRegion.X = VisualizerGlobal.MapInfo.Respawns[i].Location.X;
                RespawnRegion.Y = VisualizerGlobal.MapInfo.Respawns[i].Location.Y;
                RespawnRegion.Range = VisualizerGlobal.MapInfo.Respawns[i].Spread;
                RespawnRegion.Count.Text = VisualizerGlobal.MapInfo.Respawns[i].Count.ToString();
                RespawnRegion.Delay.Text = VisualizerGlobal.MapInfo.Respawns[i].Delay.ToString();
                RespawnRegion.RoutePath = VisualizerGlobal.MapInfo.Respawns[i].RoutePath;
                RespawnRegion.Direction = VisualizerGlobal.MapInfo.Respawns[i].Direction;
                RespawnRegion.RandomDelay = VisualizerGlobal.MapInfo.Respawns[i].RandomDelay;
                RespawnRegion.RespawnIndex = VisualizerGlobal.MapInfo.Respawns[i].RespawnIndex;
                RespawnRegion.SaveRespawnTime = VisualizerGlobal.MapInfo.Respawns[i].SaveRespawnTime;
                RespawnRegion.RespawnTicks = VisualizerGlobal.MapInfo.Respawns[i].RespawnTicks;
                RespawnRegion.Tag = i;
                RespawnRegion.HideControl();

                RespawnPanel.Controls.Add(RespawnRegion);

                RespawnRegion.RegionHighlight.Parent = Canvas;
            }
        }

        private bool TryCaptureDraft(out MapContentDraft draft, out string error)
        {
            var respawns = new List<MapRespawnDraft>();
            var mineZones = new List<MapMineZoneDraft>();
            try
            {
                foreach (RespawnEntry item in OrderDraftControls(RespawnPanel.Controls.OfType<RespawnEntry>()))
                {
                    if (!ushort.TryParse(item.Count.Text, out ushort count))
                        throw new InvalidDataException($"刷怪数量不是有效整数：{item.Count.Text}");
                    if (!ushort.TryParse(item.Delay.Text, out ushort delay))
                        throw new InvalidDataException($"刷新时间不是有效整数：{item.Delay.Text}");
                    respawns.Add(new MapRespawnDraft(
                        item.MonsterIndex, new Point(item.X, item.Y), count, item.Range, delay,
                        item.Direction, item.RoutePath ?? string.Empty, item.RandomDelay,
                        item.RespawnIndex, item.SaveRespawnTime, item.RespawnTicks));
                }
                foreach (MineEntry item in OrderDraftControls(MiningPanel.Controls.OfType<MineEntry>()))
                    mineZones.Add(new MapMineZoneDraft(item.MineIndex, new Point(item.X, item.Y), item.Range));

                draft = new MapContentDraft(respawns, mineZones);
                error = string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                draft = null;
                error = ex.Message;
                return false;
            }
        }

        private void LoadDraft(MapContentDraft draft)
        {
            foreach (RespawnEntry item in RespawnPanel.Controls.OfType<RespawnEntry>().ToArray())
                item.RemoveEntry();
            foreach (MineEntry item in MiningPanel.Controls.OfType<MineEntry>().ToArray())
                item.RemoveEntry();

            for (int index = 0; index < draft.MineZones.Count; index++)
            {
                MapMineZoneDraft item = draft.MineZones[index];
                var control = new MineEntry
                {
                    Dock = DockStyle.Top, MineIndex = item.Mine,
                    X = item.Location.X, Y = item.Location.Y,
                    tempRange = item.Size, Range = item.Size, Tag = index,
                };
                control.ShowControl();
                control.RegionHighlight.Parent = Canvas;
                MiningPanel.Controls.Add(control);
            }
            for (int index = 0; index < draft.Respawns.Count; index++)
            {
                MapRespawnDraft item = draft.Respawns[index];
                var control = new RespawnEntry
                {
                    Dock = DockStyle.Top, MonsterIndex = item.MonsterIndex,
                    X = item.Location.X, Y = item.Location.Y, Range = item.Spread,
                    RoutePath = item.RoutePath, Direction = item.Direction,
                    RandomDelay = item.RandomDelay, RespawnIndex = item.RespawnIndex,
                    SaveRespawnTime = item.SaveRespawnTime, RespawnTicks = item.RespawnTicks, Tag = index,
                };
                control.Count.Text = item.Count.ToString();
                control.Delay.Text = item.Delay.ToString();
                control.ShowControl();
                control.RegionHighlight.Parent = Canvas;
                RespawnPanel.Controls.Add(control);
            }
            RegionTabs_SelectedIndexChanged(RegionTabs, EventArgs.Empty);
            _nextMineDraftIndex = draft.MineZones.Count;
            _nextRespawnDraftIndex = draft.Respawns.Count;
            RefreshMapTargets(draft);
            RefreshNavigationDiagnostics(draft);
        }

        private static IEnumerable<T> OrderDraftControls<T>(IEnumerable<T> controls) where T : System.Windows.Forms.Control
        {
            return controls
                .Select((control, currentIndex) => new
                {
                    Control = control,
                    OriginalIndex = control.Tag is int originalIndex ? originalIndex : int.MaxValue,
                    CurrentIndex = currentIndex,
                })
                .OrderBy(item => item.OriginalIndex)
                .ThenBy(item => item.CurrentIndex)
                .Select(item => item.Control);
        }

        private void UndoContent()
        {
            if (!TryCaptureDraft(out MapContentDraft current, out string error))
            {
                MessageBox.Show(error, "无法撤销", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            LoadDraft(_editingSession.Undo(current));
            UpdateHistoryButtons();
        }

        private void RedoContent()
        {
            LoadDraft(_editingSession.Redo());
            UpdateHistoryButtons();
        }

        private void UpdateHistoryButtons()
        {
            if (_editingSession == null)
                return;
            // 控件可能尚未进入历史；撤销动作会先捕获当前草稿再回退。
            _undoButton.Enabled = true;
            _redoButton.Enabled = _editingSession.CanRedo;
        }

        private void ShowContentReview()
        {
            if (!TryCaptureDraft(out MapContentDraft draft, out string error))
            {
                MessageBox.Show(error, "输入格式错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            _editingSession.Observe(draft);
            UpdateHistoryButtons();
            MapContentReview review = _editingSession.Review(draft);
            RefreshNavigationDiagnostics(draft, review.Diagnostics);
            MessageBox.Show(FormatReview(review), "保存前校验与差异", MessageBoxButtons.OK,
                review.HasErrors ? MessageBoxIcon.Error : MessageBoxIcon.Information);
        }

        private bool TrySaveDraft(bool closeAfterSave)
        {
            if (!TryCaptureDraft(out MapContentDraft draft, out string error))
            {
                MessageBox.Show(error, "输入格式错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            _editingSession.Observe(draft);
            MapContentReview review = _editingSession.Review(draft);
            if (!review.HasChanges)
            {
                if (closeAfterSave) Close();
                return true;
            }
            if (review.HasErrors)
            {
                MessageBox.Show(FormatReview(review), "保存前校验未通过", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            DialogResult confirmation = MessageBox.Show(
                FormatReview(review) + "\n\n确认保存以上变更吗？",
                "确认地图内容变更",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Question);
            if (confirmation != DialogResult.OK)
                return false;

            MapContentCommitResult result = _editingSession.TryCommit(draft, Envir.SaveDB);
            if (!result.Completed)
            {
                MessageBox.Show(result.Error, "保存失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            HasCommittedChanges = true;
            RefreshMapTargets(draft);
            RefreshNavigationDiagnostics(draft);
            MapDetailsLabel.Text = $"已保存：{review.Differences.Count} 项变更";
            if (closeAfterSave)
            {
                DialogResult = DialogResult.OK;
                Close();
            }
            return true;
        }

        private void RefreshMapTargets(MapContentDraft draft = null)
        {
            if (VisualizerGlobal.MapInfo == null)
                return;
            if (draft == null && _editingSession != null && TryCaptureDraft(out MapContentDraft current, out _))
                draft = current;
            _mapTargets = MapContentNavigation.BuildTargets(VisualizerGlobal.MapInfo, Envir.NPCInfoList, draft);
            RebuildPointMarkers();
            ApplyLayerVisibility();
        }

        public void LoadLayerTargets(IReadOnlyList<MapContentTarget> targets)
        {
            _mapTargets = targets ?? throw new ArgumentNullException(nameof(targets));
            RebuildPointMarkers();
            ApplyLayerVisibility();
        }

        private void RebuildPointMarkers()
        {
            foreach (RectangleShape marker in _pointMarkers.Values)
                marker.Dispose();
            _pointMarkers.Clear();

            foreach (MapContentTarget target in _mapTargets.Where(item =>
                         item.Layer is MapContentLayer.Exit or MapContentLayer.Npc))
            {
                Color color = target.Layer == MapContentLayer.Exit ? Color.DeepSkyBlue : Color.Gold;
                var marker = new RectangleShape
                {
                    Name = $"{target.Layer}Marker{target.EntityIndex ?? target.ListIndex}",
                    Size = new Size(12, 12),
                    BorderColor = Color.Black,
                    BorderWidth = 2,
                    FillColor = color,
                    FillStyle = FillStyle.Solid,
                    Cursor = Cursors.Hand,
                    Tag = target,
                };
                marker.MouseEnter += (_, _) => MapDetailsLabel.Text = target.Label;
                marker.MouseClick += (_, _) => RequestOwnerNavigation(target);
                marker.Parent = Canvas;
                _pointMarkers[target.Source] = marker;
            }
            PositionPointMarkers();
        }

        private void PositionPointMarkers()
        {
            foreach ((string source, RectangleShape marker) in _pointMarkers)
            {
                MapContentTarget target = _mapTargets.First(item => item.Source == source);
                marker.Left = target.Location.X * VisualizerGlobal.ZoomLevel - marker.Width / 2;
                marker.Top = target.Location.Y * VisualizerGlobal.ZoomLevel - marker.Height / 2;
            }
        }

        private bool IsLayerVisible(MapContentLayer layer) =>
            _layerItems.TryGetValue(layer, out ToolStripMenuItem item) && item.Checked;

        public void SetLayerVisibility(MapContentLayer layer, bool visible)
        {
            if (!_layerItems.TryGetValue(layer, out ToolStripMenuItem item))
                throw new ArgumentOutOfRangeException(nameof(layer), layer, "该类型不是可切换地图叠层。");
            item.Checked = visible;
            ApplyLayerVisibility();
        }

        public int GetVisibleTargetCount(MapContentLayer layer)
        {
            return layer switch
            {
                MapContentLayer.Exit or MapContentLayer.Npc => _pointMarkers.Values.Count(marker =>
                    marker.Visible && marker.Tag is MapContentTarget target && target.Layer == layer),
                MapContentLayer.Respawn => RespawnPanel.Controls.OfType<RespawnEntry>()
                    .Count(item => item.RegionHighlight.Visible),
                MapContentLayer.MineZone => MiningPanel.Controls.OfType<MineEntry>()
                    .Count(item => item.RegionHighlight.Visible),
                _ => 0,
            };
        }

        private void ApplyLayerVisibility()
        {
            foreach (RectangleShape marker in _pointMarkers.Values)
                if (marker.Tag is MapContentTarget target)
                    marker.Visible = IsLayerVisible(target.Layer);

            bool respawnsVisible = IsLayerVisible(MapContentLayer.Respawn);
            foreach (RespawnEntry item in RespawnPanel.Controls.OfType<RespawnEntry>())
                item.RegionHighlight.Visible = respawnsVisible && !item.RegionHidden && RespawnMatchesFilter(item);

            bool minesVisible = IsLayerVisible(MapContentLayer.MineZone);
            foreach (MineEntry item in MiningPanel.Controls.OfType<MineEntry>())
                item.RegionHighlight.Visible = minesVisible && !item.RegionHidden && MineMatchesFilter(item);
        }

        private bool RespawnMatchesFilter(RespawnEntry item) =>
            RespawnsFilter.Text == "No Filter" ||
            RespawnsFilter.SelectedItem is MonsterInfo info && item.MonsterIndex == info.Index;

        private bool MineMatchesFilter(MineEntry item) =>
            MiningFilter.Text == "No Filter" || item.MineIndex == MiningFilter.SelectedIndex;

        private void RefreshNavigationDiagnostics(
            MapContentDraft draft = null,
            IEnumerable<ProjectPreflightDiagnostic> draftDiagnostics = null)
        {
            if (_diagnosticsButton == null || VisualizerGlobal.MapInfo == null || VisualizerGlobal.ClippingMap == null)
                return;
            if (draft == null && _editingSession != null && TryCaptureDraft(out MapContentDraft current, out _))
                draft = current;

            var maps = Envir.MapInfoList
                .Select(item => item.Index == VisualizerGlobal.MapInfo.Index && draft != null
                    ? MapContentNavigation.CreatePreflightMap(item, draft)
                    : item)
                .ToArray();
            ProjectPreflightReport report = ProjectSemanticPreflight.ValidateMapContent(new ProjectPreflightRequest
            {
                MapDirectory = Settings.MapPath,
                NpcDirectory = Settings.NPCPath,
                CSharpNpcDirectory = Settings.CSharpScriptsPath,
                Maps = maps,
                Monsters = Envir.MonsterInfoList,
                Items = Envir.ItemInfoList,
                Npcs = Envir.NPCInfoList,
                MapBounds = [new ProjectMapBounds(
                    VisualizerGlobal.MapInfo.Index,
                    VisualizerGlobal.ClippingMap.Width,
                    VisualizerGlobal.ClippingMap.Height)],
                Scripts = Envir.CSharpScripts.CurrentRegistry,
            }, VisualizerGlobal.MapInfo.Index);
            IEnumerable<ProjectPreflightDiagnostic> diagnostics = report.Diagnostics
                .Where(item => MapContentNavigation.FindTarget(item.Source, _mapTargets) != null);
            if (draftDiagnostics != null)
                diagnostics = diagnostics.Concat(draftDiagnostics);

            foreach (ToolStripItem oldItem in _diagnosticsButton.DropDownItems.Cast<ToolStripItem>().ToArray())
                oldItem.Dispose();
            _diagnosticsButton.DropDownItems.Clear();
            foreach (ProjectPreflightDiagnostic diagnostic in diagnostics
                         .DistinctBy(item => (item.Code, item.Source, item.Message))
                         .OrderBy(item => item.Code, StringComparer.Ordinal)
                         .ThenBy(item => item.Source, StringComparer.Ordinal))
            {
                var item = new ToolStripMenuItem($"[{diagnostic.Code}] {diagnostic.Source}")
                {
                    ToolTipText = diagnostic.Message,
                    Tag = diagnostic,
                };
                item.Click += (_, _) => TryNavigateToDiagnostic(diagnostic);
                _diagnosticsButton.DropDownItems.Add(item);
            }
            _diagnosticsButton.Enabled = _diagnosticsButton.DropDownItems.Count > 0;
            _diagnosticsButton.Text = _diagnosticsButton.Enabled
                ? $"诊断定位 ({_diagnosticsButton.DropDownItems.Count})"
                : "诊断定位";
        }

        public bool TryNavigateToDiagnostic(ProjectPreflightDiagnostic diagnostic)
        {
            if (diagnostic == null)
                return false;
            if (TryCaptureDraft(out MapContentDraft draft, out _))
                RefreshMapTargets(draft);
            MapContentTarget target = MapContentNavigation.FindTarget(diagnostic.Source, _mapTargets);
            if (target == null)
                return false;
            NavigateToTarget(target);
            if (target.Layer is MapContentLayer.Exit or MapContentLayer.Npc)
                RequestOwnerNavigation(target, updateCanvas: false);
            return true;
        }

        private void RequestOwnerNavigation(MapContentTarget target, bool updateCanvas = true)
        {
            if (updateCanvas)
                NavigateToTarget(target);
            RequestedOwnerTarget = target;
            Close();
            if (!IsDisposed && Visible)
                RequestedOwnerTarget = null;
        }

        private void NavigateToTarget(MapContentTarget target)
        {
            if (_layerItems.TryGetValue(target.Layer, out ToolStripMenuItem layerItem))
                layerItem.Checked = true;
            ApplyLayerVisibility();

            if (target.Layer == MapContentLayer.Respawn)
            {
                RegionTabs.SelectedTab = tabPage2;
                RespawnEntry control = OrderDraftControls(RespawnPanel.Controls.OfType<RespawnEntry>())
                    .ElementAtOrDefault(target.ListIndex ?? -1);
                if (control != null)
                {
                    control.Selected.Checked = true;
                    RespawnPanel.ScrollControlIntoView(control);
                    control.RegionHighlight.BorderColor = Color.OrangeRed;
                    control.RegionHighlight.BringToFront();
                }
            }
            else if (target.Layer == MapContentLayer.MineZone)
            {
                RegionTabs.SelectedTab = tabPage4;
                MineEntry control = OrderDraftControls(MiningPanel.Controls.OfType<MineEntry>())
                    .ElementAtOrDefault(target.ListIndex ?? -1);
                if (control != null)
                {
                    control.Selected.Checked = true;
                    MiningPanel.ScrollControlIntoView(control);
                    control.RegionHighlight.BorderColor = Color.OrangeRed;
                    control.RegionHighlight.BringToFront();
                }
            }
            else if (_pointMarkers.TryGetValue(target.Source, out RectangleShape marker))
            {
                marker.BorderColor = Color.OrangeRed;
                marker.BorderWidth = 3;
                marker.BringToFront();
            }

            int x = Math.Max(0, target.Location.X * VisualizerGlobal.ZoomLevel - mapContainer1.ClientSize.Width / 2);
            int y = Math.Max(0, target.Location.Y * VisualizerGlobal.ZoomLevel - mapContainer1.ClientSize.Height / 2);
            mapContainer1.AutoScrollPosition = new Point(x, y);
            MapDetailsLabel.Text = $"已定位：{target.Label}；来源 {target.Source}";
        }

        private void CancelContent()
        {
            _discardConfirmed = true;
            DialogResult = DialogResult.Cancel;
            Close();
        }

        private static string FormatReview(MapContentReview review)
        {
            var lines = new List<string>
            {
                $"校验：{review.Diagnostics.Count(item => item.Severity == ProjectPreflightSeverity.Error)} 个错误，" +
                $"{review.Diagnostics.Count(item => item.Severity == ProjectPreflightSeverity.Warning)} 个警告",
                $"差异：{review.Differences.Count} 项",
            };
            foreach (ProjectPreflightDiagnostic item in review.Diagnostics.Take(20))
                lines.Add($"[{item.Code}] {item.Source}：{item.Message}");
            foreach (MapContentDifference item in review.Differences.Take(20))
                lines.Add($"[{item.Kind}] {item.Source}：{item.Summary}");
            if (review.Diagnostics.Count + review.Differences.Count > 40)
                lines.Add("其余项目未在此窗口展开，请先缩小单次修改范围。");
            return string.Join(Environment.NewLine, lines);
        }

        private void RedrawMap()
        {
            Bitmap Map = new Bitmap(
                VisualizerGlobal.ClippingMap.Width * VisualizerGlobal.ZoomLevel,
                VisualizerGlobal.ClippingMap.Height * VisualizerGlobal.ZoomLevel);

            using (Graphics g = Graphics.FromImage(Map))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                g.DrawImage(VisualizerGlobal.ClippingMap, 0, 0, Map.Width, Map.Height);
            }

            MapImage.Image = Map;

            PositionPointMarkers();
            ApplyLayerVisibility();

            if (VisualizerGlobal.SelectedFocusType == VisualizerGlobal.FocusType.Mining)
                VisualizerGlobal.FocusMineEntry.UpdateForFocus(); 
            if (VisualizerGlobal.SelectedFocusType == VisualizerGlobal.FocusType.Respawn)
                VisualizerGlobal.FocusRespawnEntry.UpdateForFocus();
        }

        private void FocusModeActivated(object sender, EventArgs e)
        {
            for (int i = MiningPanel.Controls.Count - 1; i > -1; i--)
                try
                {
                    MineEntry MineControl = (MineEntry)MiningPanel.Controls[i];

                    MineControl.Visible = false;
                    MineControl.RegionHighlight.Visible = false;
                }
                catch (Exception)
                {
                    continue;
                }

            for (int i = RespawnPanel.Controls.Count - 1; i > -1; i--)
                try
                {
                    RespawnEntry RespawnControl = (RespawnEntry)RespawnPanel.Controls[i];

                    RespawnControl.Visible = false;
                    RespawnControl.RegionHighlight.Visible = false;
                }
                catch (Exception)
                {
                    continue;
                }

            if (VisualizerGlobal.SelectedFocusType == VisualizerGlobal.FocusType.Mining)
            {
                VisualizerGlobal.FocusMineEntry.Visible = true;
                VisualizerGlobal.FocusMineEntry.RegionHighlight.Visible = true;
                VisualizerGlobal.FocusMineEntry.UpdateForFocus();
            }
            if (VisualizerGlobal.SelectedFocusType == VisualizerGlobal.FocusType.Respawn)
            {
                VisualizerGlobal.FocusRespawnEntry.Visible = true;
                VisualizerGlobal.FocusRespawnEntry.RegionHighlight.Visible = true;
                VisualizerGlobal.FocusRespawnEntry.UpdateForFocus();
            }

            EndFocus.Visible = true;
            FocusBreak.Visible = true;

            ToolSelectedChanged(MoveButton, null);
        }

        private void ToolSelectedChanged(object sender, EventArgs e)
        {
            MapImage.Cursor = Cursors.Arrow;

            ToolStripButton[] ToolButtons = new ToolStripButton[] { SelectButton, AddButton, MoveButton, ResizeButton };

            foreach (var Tool in ToolButtons)
                Tool.Checked = false;

            ToolStripButton ToolSender = (ToolStripButton)sender;
            ToolSender.Checked = true;

            switch (ToolSender.Text)
            {
                case "Select Region":
                    VisualizerGlobal.SelectedTool = VisualizerGlobal.Tool.Select;
                    VisualizerGlobal.Cursor = Cursors.Arrow;
                    break;
                case "Add Region":
                    VisualizerGlobal.SelectedTool = VisualizerGlobal.Tool.Add;
                    VisualizerGlobal.Cursor = Cursors.UpArrow;
                    break;
                case "Move Region":
                    VisualizerGlobal.SelectedTool = VisualizerGlobal.Tool.Move;
                    VisualizerGlobal.Cursor = Cursors.SizeAll;
                    break;
                case "Resize Region":
                    VisualizerGlobal.SelectedTool = VisualizerGlobal.Tool.Resize;
                    VisualizerGlobal.Cursor = Cursors.SizeWE;
                    break;
                default:
                    break;
            }
        }
        
        private void EndFocus_Click(object sender, EventArgs e)
        {
            EndFocus.Visible = false;
            FocusBreak.Visible = false;

            MiningFilter.Enabled = true;
            MiningRemoveSelected.Enabled = true;

            RespawnsFilter.Enabled = true;
            RespawnsRemoveSelected.Enabled = true;

            VisualizerGlobal.ZoomLevel = 1;
            RedrawMap();

            if (VisualizerGlobal.SelectedFocusType == VisualizerGlobal.FocusType.Mining)
                MiningFilter_SelectedIndexChanged(MiningFilter, null);
            if (VisualizerGlobal.SelectedFocusType == VisualizerGlobal.FocusType.Respawn)
                RespawnsFilter_SelectedIndexChanged(RespawnsFilter, null);
            ApplyLayerVisibility();

            VisualizerGlobal.FocusMineEntry = null;
            VisualizerGlobal.FocusRespawnEntry = null;
            VisualizerGlobal.SelectedFocusType = VisualizerGlobal.FocusType.None;
        }

        private void MapImage_Click(object sender, EventArgs e)
        {
            if (RegionTabs.SelectedTab.Text == "Mining")
                if (VisualizerGlobal.SelectedTool == VisualizerGlobal.Tool.Add)
                {
                    MineEntry MineControl = new MineEntry()
                    {
                        Dock = DockStyle.Top,
                        X = MouseDownLocation.X,
                        Y = MouseDownLocation.Y,
                        Range = 50,
                        Tag = _nextMineDraftIndex++,
                    };

                    MineControl.ShowControl();
                    MineControl.RegionHighlight.Parent = Canvas;

                    MiningPanel.Controls.Add(MineControl);

                    ToolSelectedChanged(MoveButton, e);
                }

            if (RegionTabs.SelectedTab.Text == "Respawns")
                if (VisualizerGlobal.SelectedTool == VisualizerGlobal.Tool.Add)
                {
                    RespawnEntry RespawnControl = new RespawnEntry()
                    {
                        Dock = DockStyle.Top,
                        X = MouseDownLocation.X,
                        Y = MouseDownLocation.Y,
                        Range = 50,
                        Tag = _nextRespawnDraftIndex++,
                    };

                    RespawnControl.ShowControl();
                    RespawnControl.RegionHighlight.Parent = Canvas;

                    RespawnPanel.Controls.Add(RespawnControl);

                    ToolSelectedChanged(MoveButton, e);
                }
        }

        private void RegionTabs_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (RegionTabs.SelectedTab.Text == "Mining")
            {
                for (int i = RespawnPanel.Controls.Count; i > -1; --i)
                    try
                    {
                        RespawnEntry RespawnControl = (RespawnEntry)RespawnPanel.Controls[i];
                        RespawnControl.HideControl();
                    }
                    catch (Exception) { continue; }
                
                MiningFilter_SelectedIndexChanged(MiningFilter, null);
            }
            else if (RegionTabs.SelectedTab.Text == "Respawns")
            {
                for (int i = MiningPanel.Controls.Count; i > -1; --i)
                    try
                    {
                        MineEntry MineControl = (MineEntry)MiningPanel.Controls[i];
                        MineControl.HideControl();
                    }
                    catch (Exception) { continue; }

                RespawnsFilter_SelectedIndexChanged(RespawnsFilter, null);
            ApplyLayerVisibility();
            }
        }

        private void MapImage_MouseDown(object sender, MouseEventArgs e)
        {
            if (VisualizerGlobal.SelectedTool == VisualizerGlobal.Tool.Select) return;

            MouseDownLocation = e.Location;
        } 

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x02000000;  // Turn on WS_EX_COMPOSITED
                return cp;
            }
        }

        // Quick Keys
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.Z))
            {
                UndoContent();
                return true;
            }

            if (keyData == (Keys.Control | Keys.Y))
            {
                RedoContent();
                return true;
            }

            if (keyData == (Keys.Control | Keys.S))
            {
                TrySaveDraft(closeAfterSave: false);
                return true;
            }

            if (keyData == Keys.M)
            {
                ToolSelectedChanged(MoveButton, new EventArgs());

                return true;
            }

            if (keyData == Keys.S)
            {
                ToolSelectedChanged(SelectButton, new EventArgs());

                return true;
            }

            if (keyData == Keys.R)
            {
                ToolSelectedChanged(ResizeButton, new EventArgs());

                return true;
            }

            if (keyData == Keys.A)
            {
                ToolSelectedChanged(AddButton, new EventArgs());

                return true;
            }

            if (keyData == Keys.Add && VisualizerGlobal.FocusModeActive == true)
            {
                if (VisualizerGlobal.ZoomLevel != 6)
                    VisualizerGlobal.ZoomLevel++;

                RedrawMap();

                return true;
            }

            if (keyData == Keys.Subtract && VisualizerGlobal.FocusModeActive == true)
            {
                if (VisualizerGlobal.ZoomLevel != 1)
                    VisualizerGlobal.ZoomLevel--;

                RedrawMap();

                return true;
            }

            if (keyData == Keys.Left && VisualizerGlobal.FocusModeActive == true)
            {
                if (VisualizerGlobal.SelectedFocusType == VisualizerGlobal.FocusType.Mining)
                {
                    VisualizerGlobal.FocusMineEntry.X--;
                    VisualizerGlobal.FocusMineEntry.Range = VisualizerGlobal.FocusMineEntry.tempRange;
                }
                else if (VisualizerGlobal.SelectedFocusType == VisualizerGlobal.FocusType.Respawn)
                {
                    VisualizerGlobal.FocusRespawnEntry.X--;
                    VisualizerGlobal.FocusRespawnEntry.Range = VisualizerGlobal.FocusRespawnEntry.tempRange;
                }

                return true;
            } 
            
            if (keyData == Keys.Right && VisualizerGlobal.FocusModeActive == true)
            {
                if (VisualizerGlobal.SelectedFocusType == VisualizerGlobal.FocusType.Mining)
                {
                    VisualizerGlobal.FocusMineEntry.X++;
                    VisualizerGlobal.FocusMineEntry.Range = VisualizerGlobal.FocusMineEntry.tempRange;
                }
                else if (VisualizerGlobal.SelectedFocusType == VisualizerGlobal.FocusType.Respawn)
                {
                    VisualizerGlobal.FocusRespawnEntry.X++;
                    VisualizerGlobal.FocusRespawnEntry.Range = VisualizerGlobal.FocusRespawnEntry.tempRange;
                }

                return true;
            }

            if (keyData == Keys.Up && VisualizerGlobal.FocusModeActive == true)
            {
                if (VisualizerGlobal.SelectedFocusType == VisualizerGlobal.FocusType.Mining)
                {
                    VisualizerGlobal.FocusMineEntry.Y--;
                    VisualizerGlobal.FocusMineEntry.Range = VisualizerGlobal.FocusMineEntry.tempRange;
                }
                else if (VisualizerGlobal.SelectedFocusType == VisualizerGlobal.FocusType.Respawn)
                {
                    VisualizerGlobal.FocusRespawnEntry.Y--;
                    VisualizerGlobal.FocusRespawnEntry.Range = VisualizerGlobal.FocusRespawnEntry.tempRange;
                }

                return true;
            }

            if (keyData == Keys.Down && VisualizerGlobal.FocusModeActive == true)
            {
                if (VisualizerGlobal.SelectedFocusType == VisualizerGlobal.FocusType.Mining)
                {
                    VisualizerGlobal.FocusMineEntry.Y++;
                    VisualizerGlobal.FocusMineEntry.Range = VisualizerGlobal.FocusMineEntry.tempRange;
                }
                else if (VisualizerGlobal.SelectedFocusType == VisualizerGlobal.FocusType.Respawn)
                {
                    VisualizerGlobal.FocusRespawnEntry.Y++;
                    VisualizerGlobal.FocusRespawnEntry.Range = VisualizerGlobal.FocusRespawnEntry.tempRange;
                }

                return true;
            }

            if (keyData == Keys.Escape && VisualizerGlobal.FocusModeActive == true)
            {
                EndFocus_Click(EndFocus, null);

                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        #region "START Mining Tool Bar"

        private void MiningSelectAll_Click(object sender, EventArgs e)
        {
            for (int i = MiningPanel.Controls.Count - 1; i > -1; i--)
            {
                MineEntry MineControl = (MineEntry)MiningPanel.Controls[i];
                MineControl.Selected.Checked = true;
            }
        }

        private void MiningSelectNone_Click(object sender, EventArgs e)
        {
            for (int i = MiningPanel.Controls.Count - 1; i > -1; i--)
            {
                MineEntry MineControl = (MineEntry)MiningPanel.Controls[i];
                MineControl.Selected.Checked = false;
            }
        }

        private void MiningInvertSelection_Click(object sender, EventArgs e)
        {
            for (int i = MiningPanel.Controls.Count - 1; i > -1; i--)
            {
                MineEntry MineControl = (MineEntry)MiningPanel.Controls[i];
                MineControl.Selected.Checked = !MineControl.Selected.Checked;
            }
        }

        private void MiningRemoveSelected_Click(object sender, EventArgs e)
        {
            if (MiningPanel.Controls.Count == 0) return;

            DialogResult result = MessageBox.Show("Remove selected records?", "", MessageBoxButtons.YesNoCancel);
            if (result != DialogResult.Yes) return;

            for (int i = MiningPanel.Controls.Count; i > -1; --i)
            {
                try
                {
                    MineEntry MineControl = (MineEntry)MiningPanel.Controls[i];
                    if (MineControl.Selected.Checked == true)
                        MineControl.RemoveEntry();
                }
                catch (Exception)
                {
                    continue;
                }
            }
        }

        private void MiningHideRegion_Click(object sender, EventArgs e)
        {
            for (int i = MiningPanel.Controls.Count - 1; i > -1; i--)
            {
                try
                {
                    MineEntry MineControl = (MineEntry)MiningPanel.Controls[i];
                    if (MineControl.Selected.Checked == true)
                        MineControl.HideRegion();
                }
                catch (Exception)
                {
                    continue;
                }
            }
        }

        private void MiningShowRegion_Click(object sender, EventArgs e)
        {
            for (int i = MiningPanel.Controls.Count - 1; i > -1; i--)
            {
                try
                {
                    MineEntry MineControl = (MineEntry)MiningPanel.Controls[i];
                    if (MineControl.Selected.Checked == true)
                        MineControl.ShowRegion();
                }
                catch (Exception)
                {
                    continue;
                }
            }
        }

        private void MiningFocusRegion_Click(object sender, EventArgs e)
        {
            VisualizerGlobal.SelectedTool = VisualizerGlobal.Tool.Focus;
            VisualizerGlobal.Cursor = Cursors.Hand;

            MiningFilter.Enabled = false;
            MiningRemoveSelected.Enabled = false;
        }

        private void MiningFilter_SelectedIndexChanged(object sender, EventArgs e)
        {
            VisualizerGlobal.ZoomLevel = 1;

            if (MiningFilter.Text == "No Filter")
                for (int i = MiningPanel.Controls.Count - 1; i > -1; i--)
                    try
                    {
                        MineEntry MineControl = (MineEntry)MiningPanel.Controls[i];

                        MineControl.Visible = true;
                        if (!MineControl.RegionHidden)
                            MineControl.RegionHighlight.Visible = true;
                    }
                    catch (Exception)
                    {
                        continue;
                    }
            else
                for (int i = MiningPanel.Controls.Count - 1; i > -1; i--)
                    try
                    {
                        MineEntry MineControl = (MineEntry)MiningPanel.Controls[i];

                        if (MineControl.MineIndex == MiningFilter.SelectedIndex)
                        {
                            MineControl.Visible = true;

                            if (!MineControl.RegionHidden)
                                MineControl.RegionHighlight.Visible = true;
                        }
                        else
                        {
                            MineControl.RegionHighlight.Visible = false;
                            MineControl.Visible = false;
                        }
                    }
                    catch (Exception)
                    {
                        continue;
                    }
            ApplyLayerVisibility();
        }

        #endregion "END Mining Tool Bar"

        #region "START Respawn Tool Bar

        private void RespawnsSelectAll_Click(object sender, EventArgs e)
        {
            for (int i = RespawnPanel.Controls.Count - 1; i > -1; i--)
            {
                RespawnEntry RespawnControl = (RespawnEntry)RespawnPanel.Controls[i];
                RespawnControl.Selected.Checked = true;
            }
        }

        private void RespawnsSelectNone_Click(object sender, EventArgs e)
        {
            for (int i = RespawnPanel.Controls.Count - 1; i > -1; i--)
            {
                RespawnEntry RespawnControl = (RespawnEntry)RespawnPanel.Controls[i];
                RespawnControl.Selected.Checked = false;
            }
        }

        private void RespawnsRemoveSelected_Click(object sender, EventArgs e)
        {
            if (RespawnPanel.Controls.Count == 0) return;

            DialogResult result = MessageBox.Show("Remove selected records?", "", MessageBoxButtons.YesNoCancel);
            if (result != DialogResult.Yes) return;

            for (int i = RespawnPanel.Controls.Count; i > -1; --i)
            {
                try
                {
                    RespawnEntry RespawnControl = (RespawnEntry)RespawnPanel.Controls[i];
                    if (RespawnControl.Selected.Checked == true)
                    {
                        RespawnControl.RemoveEntry();
                    }
                }
                catch (Exception)
                {
                    continue;
                }
            }
        }

        private void ResapwnsHideRegion_Click(object sender, EventArgs e)
        {
            for (int i = RespawnPanel.Controls.Count - 1; i > -1; i--)
            {
                try
                {
                    RespawnEntry RespawnControl = (RespawnEntry)RespawnPanel.Controls[i];
                    if (RespawnControl.Selected.Checked == true)
                        RespawnControl.HideRegion();
                }
                catch (Exception)
                {
                    continue;
                }
            }
        }

        private void ResapwnsShowRegion_Click(object sender, EventArgs e)
        {
            for (int i = RespawnPanel.Controls.Count - 1; i > -1; i--)
            {
                try
                {
                    RespawnEntry RespawnControl = (RespawnEntry)RespawnPanel.Controls[i];
                    if (RespawnControl.Selected.Checked == true)
                        RespawnControl.ShowRegion();
                }
                catch (Exception)
                {
                    continue;
                }
            }
        }

        private void ResapwnsFocusRegion_Click(object sender, EventArgs e)
        {
            VisualizerGlobal.SelectedTool = VisualizerGlobal.Tool.Focus;
            VisualizerGlobal.Cursor = Cursors.Hand;

            RespawnsFilter.Enabled = false;
            RespawnsRemoveSelected.Enabled = false;
        }
        
        private void RespawnsInvertSelection_Click(object sender, EventArgs e)
        {
            for (int i = RespawnPanel.Controls.Count - 1; i > -1; i--)
            {
                RespawnEntry RespawnControl = (RespawnEntry)RespawnPanel.Controls[i];
                RespawnControl.Selected.Checked = !RespawnControl.Selected.Checked;
            }
        }

        private void RespawnsFilter_SelectedIndexChanged(object sender, EventArgs e)
        {
            MonsterInfo info = RespawnsFilter.SelectedItem as MonsterInfo;

            VisualizerGlobal.ZoomLevel = 1;

            if (RespawnsFilter.Text == "No Filter")
                for (int i = RespawnPanel.Controls.Count - 1; i > -1; i--)
                    try
                    {
                        RespawnEntry RespawnControl = (RespawnEntry)RespawnPanel.Controls[i];

                        RespawnControl.Visible = true;
                        if (!RespawnControl.RegionHidden)
                            RespawnControl.RegionHighlight.Visible = true;
                    }
                    catch (Exception)
                    {
                        continue;
                    }
            else
                for (int i = RespawnPanel.Controls.Count - 1; i > -1; i--)
                    try
                    {
                        RespawnEntry RespawnControl = (RespawnEntry)RespawnPanel.Controls[i];

                        if (RespawnControl.MonsterIndex == info.Index)
                        {
                            RespawnControl.Visible = true;

                            if (!RespawnControl.RegionHidden)
                                RespawnControl.RegionHighlight.Visible = true;
                        }
                        else
                        {
                            RespawnControl.RegionHighlight.Visible = false;
                            RespawnControl.Visible = false;
                        }
                    }
                    catch (Exception)
                    {
                        continue;
                    }
            ApplyLayerVisibility();
        }

        #endregion "END Respawn Tool Bar

        private void RegionTabs_Selecting(object sender, TabControlCancelEventArgs e)
        {
            if (VisualizerGlobal.SelectedFocusType != VisualizerGlobal.FocusType.None)
                e.Cancel = true;
        }



        

        

    }

}
