using Server.MirEnvir;
using Server.MirObjects;
using Server.Scripting;
using Server.Scripting.Ai;
using Server.Scripting.Debug;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Forms.Integration;
using Microsoft.CodeAnalysis;
using RoslynPad.Editor;
using RoslynPad.Roslyn;
using WpfFontFamily = System.Windows.Media.FontFamily;

namespace Server.MirForms.Systems
{
    public sealed class ScriptDebugForm : Form
    {
        private enum TestMode
        {
            ChatSpecific,
            ChatGeneric,
            CustomCommand,
            Trigger,
            MapEnter,
            MapCoord,
            AcceptQuest,
            FinishQuest,
            Daily,
            ClientEvent,
        }

        private sealed class TestEntry
        {
            public string Display { get; init; } = string.Empty;
            public string Key { get; init; } = string.Empty;
            public string Preset { get; init; } = string.Empty;
            public string Group { get; init; } = string.Empty;
            public TestMode Mode { get; init; }

            public override string ToString() => Display;
        }

        private sealed class TestBenchRequest
        {
            public bool DebugEnabled { get; init; }

            public string CommandLine { get; init; } = string.Empty;
            public string CustomCommand { get; init; } = string.Empty;
            public string TriggerKey { get; init; } = string.Empty;
            public string MapFileName { get; init; } = string.Empty;
            public int CoordX { get; init; }
            public int CoordY { get; init; }
            public int QuestIndex { get; init; }
            public string ClientPayload { get; init; } = string.Empty;
        }

        private sealed class OnlinePlayerEntry
        {
            public string Name { get; init; } = string.Empty;
            public int Level { get; init; }
            public string MapCode { get; init; } = string.Empty;

            public override string ToString() => string.IsNullOrWhiteSpace(MapCode) ? $"{Name} (Lv.{Level})" : $"{Name} (Lv.{Level}, {MapCode})";
        }

        private sealed class ScriptHistoryMeta
        {
            public string VersionId { get; init; } = string.Empty;
            public string OriginalRelativePath { get; init; } = string.Empty;
            public string OriginalFileName { get; init; } = string.Empty;
            public DateTime CreatedLocal { get; init; }
            public DateTime CreatedUtc { get; init; }
            public string Operator { get; init; } = string.Empty;
            public string Machine { get; init; } = string.Empty;
            public string Reason { get; init; } = string.Empty;
            public string FromVersionId { get; init; } = string.Empty;
        }

        private sealed class HistoryEntry
        {
            public string VersionId { get; init; } = string.Empty;
            public DateTime CreatedLocal { get; init; }
            public string Operator { get; init; } = string.Empty;
            public string Reason { get; init; } = string.Empty;
            public string SnapshotFilePath { get; init; } = string.Empty;
            public string MetaFilePath { get; init; } = string.Empty;

            public override string ToString() => $"{CreatedLocal:yyyy-MM-dd HH:mm:ss} {Operator} {Reason}";
        }

        private sealed class HotfixPackageMeta
        {
            public string PackageId { get; init; } = string.Empty;
            public DateTime CreatedLocal { get; init; }
            public DateTime CreatedUtc { get; init; }
            public string Operator { get; init; } = string.Empty;
            public string Machine { get; init; } = string.Empty;
            public string Reason { get; init; } = string.Empty;
            public List<HotfixPackageFile> Files { get; init; } = new();
        }

        private sealed class HotfixPackageFile
        {
            public string RelativePath { get; init; } = string.Empty;
            public long Length { get; init; }
            public string Sha256 { get; init; } = string.Empty;
        }

        private readonly ScriptCompiler _diagnosticCompiler = new();
        private readonly ScriptGenerationService _scriptGenerationService = ScriptGenerationService.CreateDefault();
        private readonly Dictionary<string, Encoding> _loadedFileEncodings = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, TreeNode> _fileNodeLookup = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<TestEntry> _allTestEntries = new();

        private readonly Button _refreshButton;
        private readonly Button _saveButton;
        private readonly Button _diagnoseCurrentButton;
        private readonly Button _diagnoseAllButton;
        private readonly Button _reloadButton;
        private readonly Button _copyDiagnosticsButton;
        private readonly Button _openScriptsFolderButton;
        private readonly TreeView _fileTreeView;
        private readonly TextBox _statusTextBox;
        private readonly RichTextBox _editorTextBox;
        private readonly CheckBox _useRoslynPadCheckBox;
        private ElementHost? _roslynElementHost;
        private RoslynHost? _roslynHost;
        private RoslynCodeEditor? _roslynEditor;
        private DocumentId? _roslynDocumentId;
        private bool _roslynEditorReady;
        private string _pendingRoslynText = string.Empty;
        private readonly Label _currentFileLabel;
        private readonly ListView _diagnosticsListView;

        private readonly TextBox _testSearchTextBox;
        private readonly ListBox _testKeyListBox;
        private readonly ComboBox _testPlayerComboBox;
        private readonly TextBox _testKeyTextBox;
        private readonly TextBox _testDescriptionTextBox;
        private readonly TextBox _testCommandLineTextBox;
        private readonly TextBox _testCustomCommandTextBox;
        private readonly TextBox _testTriggerTextBox;
        private readonly TextBox _testMapFileTextBox;
        private readonly NumericUpDown _testCoordXNumeric;
        private readonly NumericUpDown _testCoordYNumeric;
        private readonly NumericUpDown _testQuestIndexNumeric;
        private readonly TextBox _testClientPayloadTextBox;
        private readonly Button _testExecuteButton;
        private readonly TextBox _testResultTextBox;

        private readonly CheckBox _testDebugEnabledCheckBox;
        private readonly Button _testDebugRebuildButton;
        private readonly Button _testDebugPauseButton;
        private readonly Button _testDebugContinueButton;
        private readonly Button _testDebugStepButton;
        private readonly Button _testDebugCancelButton;
        private readonly Label _testDebugStateLabel;
        private readonly Label _testDebugLocationLabel;
        private readonly ListView _testDebugBreakpointsListView;
        private readonly TextBox _testDebugBreakpointFileTextBox;
        private readonly NumericUpDown _testDebugBreakpointLineNumeric;
        private readonly Button _testDebugAddBreakpointButton;
        private readonly Button _testDebugRemoveBreakpointButton;
        private readonly Button _testDebugClearBreakpointsButton;
        private readonly TextBox _testDebugWatchExpressionTextBox;
        private readonly Button _testDebugEvalExpressionButton;
        private readonly TextBox _testDebugWatchResultTextBox;

        private readonly ScriptDebugSession _testDebugSession = new ScriptDebugSession();
        private readonly ScriptDebugRuntime _testDebugRuntime = new ScriptDebugRuntime();
        private readonly System.Windows.Forms.Timer _testDebugUiTimer;
        private long _lastObservedPauseSequence;
        private PlayerObject? _testDebugLastPlayer;
        private string _testDebugLastExecutionId = string.Empty;

        private readonly ComboBox _aiKindComboBox;
        private readonly TextBox _aiTargetKeyTextBox;
        private readonly TextBox _aiDescriptionTextBox;
        private readonly TextBox _aiAdditionalRequirementsTextBox;
        private readonly CheckBox _aiIncludeCurrentScriptCheckBox;
        private readonly CheckBox _aiIncludeDiagnosticsCheckBox;
        private readonly CheckBox _aiIncludeReferenceScriptCheckBox;
        private readonly TextBox _aiReferenceScriptTextBox;
        private readonly Button _aiPickReferenceScriptButton;
        private readonly Button _aiClearReferenceScriptButton;
        private readonly Button _aiBuildPromptButton;
        private readonly Button _aiGenerateDraftButton;
        private readonly Button _aiFixButton;
        private readonly Button _aiApplyDraftButton;
        private readonly Button _aiSaveDraftButton;
        private readonly Button _aiDiagnoseDraftButton;
        private readonly Button _aiReloadDraftButton;
        private readonly TextBox _aiPromptTextBox;
        private readonly TextBox _aiDraftTextBox;
        private readonly TextBox _aiResultTextBox;

        private readonly Button _historyRefreshButton;
        private readonly Button _historyDiffButton;
        private readonly Button _historyRollbackToEditorButton;
        private readonly Button _historyRollbackReloadButton;
        private readonly ListView _historyListView;
        private readonly TextBox _historyPreviewTextBox;
        private readonly TextBox _historyDiffTextBox;

        private readonly Button _packageExportButton;
        private readonly Button _packageApplyButton;
        private readonly Button _packageOpenPackageFolderButton;
        private readonly TextBox _packageReasonTextBox;
        private readonly TextBox _packageLogTextBox;

        private ScriptDraft? _currentAiDraft;
        private HistoryEntry? _selectedHistoryEntry;

        private IReadOnlyList<ScriptDiagnostic> _currentDiagnostics = Array.Empty<ScriptDiagnostic>();
        private string _currentFilePath = string.Empty;
        private string _aiReferenceScriptFullPath = string.Empty;
        private bool _currentFileFromAiDraft;
        private bool _isDirty;
        private bool _suppressTextChanged;
        private int _onlinePlayerCount;
        private string _lastActionSummary = "尚未执行诊断。";
        private string _lastTestSummary = "尚未执行测试。";
        private string _lastAiSummary = "尚未生成 AI 草稿。";

        private Envir Envir => Envir.Main;
        private string ScriptsRootPath => Path.GetFullPath(Settings.CSharpScriptsPath);
        private string HotfixPackageRootPath => Path.Combine(ScriptsRootPath, "_hotfix_packages");

        public ScriptDebugForm()
        {
            Text = "脚本调试";
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(1260, 760);

            _refreshButton = CreateButton("刷新", (_, _) => RefreshView(true));
            _saveButton = CreateButton("保存", (_, _) => SaveCurrentFile(true));
            _diagnoseCurrentButton = CreateButton("当前文件诊断", async (_, _) => await DiagnoseCurrentFileAsync());
            _diagnoseAllButton = CreateButton("全量诊断", async (_, _) => await DiagnoseAllAsync());
            _reloadButton = CreateButton("编译并热更（Reload）", async (_, _) => await ReloadAsync(true));
            _copyDiagnosticsButton = CreateButton("复制错误信息", (_, _) => CopyDiagnostics());
            _openScriptsFolderButton = CreateButton("打开脚本目录", (_, _) => OpenScriptsFolder());
            _useRoslynPadCheckBox = new CheckBox { Text = "RoslynPad", AutoSize = true, Checked = true };
            _useRoslynPadCheckBox.CheckedChanged += (_, _) => ApplyEditorMode(syncText: true);

            _fileTreeView = new TreeView { Dock = DockStyle.Fill, HideSelection = false };
            _fileTreeView.BeforeSelect += FileTreeView_BeforeSelect;
            _fileTreeView.AfterSelect += FileTreeView_AfterSelect;

            _currentFileLabel = new Label { Dock = DockStyle.Top, Height = 26, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(4, 0, 0, 0), Text = "当前文件: (未选择)" };
            _statusTextBox = new TextBox { Dock = DockStyle.Top, Height = 140, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
            _editorTextBox = new RichTextBox { Dock = DockStyle.Fill, AcceptsTab = true, DetectUrls = false, HideSelection = false, WordWrap = false, Font = new Font("Consolas", 10F, FontStyle.Regular, GraphicsUnit.Point) };
            _editorTextBox.TextChanged += EditorTextBox_TextChanged;
            TryInitializeRoslynPadEditor();

            _diagnosticsListView = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true };
            _diagnosticsListView.Columns.Add("Severity", 90);
            _diagnosticsListView.Columns.Add("Id", 90);
            _diagnosticsListView.Columns.Add("File", 320);
            _diagnosticsListView.Columns.Add("Line", 60);
            _diagnosticsListView.Columns.Add("Column", 70);
            _diagnosticsListView.Columns.Add("Message", 640);
            _diagnosticsListView.DoubleClick += (_, _) => JumpToSelectedDiagnostic();

            _testSearchTextBox = new TextBox { Dock = DockStyle.Top };
            _testSearchTextBox.TextChanged += (_, _) => ApplyTestEntryFilter();
            _testKeyListBox = new ListBox { Dock = DockStyle.Fill, HorizontalScrollbar = true };
            _testKeyListBox.SelectedIndexChanged += (_, _) => UpdateSelectedTestEntry();
            _testPlayerComboBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 240 };
            _testKeyTextBox = CreateReadOnlyTextBox();
            _testDescriptionTextBox = CreateReadOnlyTextBox(true, 54);
            _testCommandLineTextBox = new TextBox { Width = 360 };
            _testCustomCommandTextBox = new TextBox { Width = 360 };
            _testTriggerTextBox = new TextBox { Width = 360 };
            _testMapFileTextBox = new TextBox { Width = 260 };
            _testCoordXNumeric = CreateNumeric();
            _testCoordYNumeric = CreateNumeric();
            _testQuestIndexNumeric = CreateNumeric(int.MaxValue);
            _testClientPayloadTextBox = new TextBox { Width = 360 };
            _testExecuteButton = CreateButton("执行测试", async (_, _) => await ExecuteSelectedTestAsync());
            _testResultTextBox = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, Font = new Font("Consolas", 9F, FontStyle.Regular, GraphicsUnit.Point) };

            _testDebugEnabledCheckBox = new CheckBox { Text = "插桩调试（断点/单步）", AutoSize = true };
            _testDebugEnabledCheckBox.CheckedChanged += (_, _) => UpdateTestDebugUiFromSession(forceRefreshBreakpoints: true);
            _testDebugRebuildButton = CreateButton("重建插桩", async (_, _) => await RebuildTestDebugRuntimeAsync());
            _testDebugPauseButton = CreateButton("暂停", (_, _) => _testDebugSession.RequestPause());
            _testDebugContinueButton = CreateButton("继续", (_, _) => _testDebugSession.Continue());
            _testDebugStepButton = CreateButton("单步", (_, _) => _testDebugSession.StepOnce());
            _testDebugCancelButton = CreateButton("停止", (_, _) => _testDebugSession.Cancel());
            _testDebugStateLabel = new Label { AutoSize = true, Text = "未启用（仅测试台插桩，断点暂停会阻塞主逻辑线程）" };
            _testDebugLocationLabel = new Label { AutoSize = true, Text = "位置：-" };

            _testDebugBreakpointsListView = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true };
            _testDebugBreakpointsListView.Columns.Add("File", 360);
            _testDebugBreakpointsListView.Columns.Add("Line", 60);
            _testDebugBreakpointsListView.DoubleClick += (_, _) => JumpToSelectedTestDebugBreakpoint();

            _testDebugBreakpointFileTextBox = new TextBox { Width = 420 };
            _testDebugBreakpointLineNumeric = CreateNumeric(int.MaxValue);
            _testDebugBreakpointLineNumeric.Width = 90;
            _testDebugAddBreakpointButton = CreateButton("添加断点", (_, _) => AddTestDebugBreakpoint());
            _testDebugRemoveBreakpointButton = CreateButton("删除选中", (_, _) => RemoveSelectedTestDebugBreakpoint());
            _testDebugClearBreakpointsButton = CreateButton("清空", (_, _) => ClearAllTestDebugBreakpoints());

            _testDebugWatchExpressionTextBox = new TextBox { Width = 520 };
            _testDebugEvalExpressionButton = CreateButton("求值", async (_, _) => await EvaluateTestDebugExpressionAsync());
            _testDebugWatchResultTextBox = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, Font = new Font("Consolas", 9F, FontStyle.Regular, GraphicsUnit.Point) };

            _testDebugUiTimer = new System.Windows.Forms.Timer { Interval = 200 };
            _testDebugUiTimer.Tick += (_, _) => UpdateTestDebugUiFromSession(forceRefreshBreakpoints: false);

            ScriptDebugHook.Session = _testDebugSession;

            _aiKindComboBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
            _aiKindComboBox.Items.AddRange(Enum.GetValues(typeof(ScriptGenerationKind)).Cast<object>().ToArray());
            _aiKindComboBox.SelectedItem = ScriptGenerationKind.GenericModule;
            _aiTargetKeyTextBox = new TextBox { Width = 360 };
            _aiDescriptionTextBox = new TextBox { Multiline = true, Height = 72, ScrollBars = ScrollBars.Vertical };
            _aiAdditionalRequirementsTextBox = new TextBox { Multiline = true, Height = 54, ScrollBars = ScrollBars.Vertical };
            _aiIncludeCurrentScriptCheckBox = new CheckBox { Text = "引用当前脚本", AutoSize = true };
            _aiIncludeDiagnosticsCheckBox = new CheckBox { Text = "引用诊断信息", AutoSize = true };
            _aiIncludeReferenceScriptCheckBox = new CheckBox { Text = "引用参考脚本", AutoSize = true };
            _aiReferenceScriptTextBox = new TextBox { Width = 420, ReadOnly = true, BackColor = SystemColors.Window };
            _aiPickReferenceScriptButton = CreateButton("选择...", (_, _) => PickAiReferenceScriptFile());
            _aiClearReferenceScriptButton = CreateButton("清除", (_, _) => ClearAiReferenceScriptFile());
            _aiBuildPromptButton = CreateButton("构建 Prompt", (_, _) => BuildAiPrompt());
            _aiGenerateDraftButton = CreateButton("生成草稿", async (_, _) => await GenerateAiDraftAsync());
            _aiFixButton = CreateButton("一键修复", async (_, _) => await FixCurrentScriptAsync());
            _aiApplyDraftButton = CreateButton("应用到编辑器", (_, _) => ApplyAiDraftToEditor());
            _aiSaveDraftButton = CreateButton("保存到脚本目录", (_, _) => SaveAiDraftToScriptsRoot());
            _aiDiagnoseDraftButton = CreateButton("草稿编译检查", async (_, _) => await DiagnoseAiDraftAsync());
            _aiReloadDraftButton = CreateButton("保存并热更", async (_, _) => await ReloadAiDraftAsync());
            _aiPromptTextBox = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, Font = new Font("Consolas", 9F, FontStyle.Regular, GraphicsUnit.Point) };
            _aiDraftTextBox = new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Both, Font = new Font("Consolas", 9F, FontStyle.Regular, GraphicsUnit.Point) };
            _aiResultTextBox = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 9F, FontStyle.Regular, GraphicsUnit.Point) };

            _historyRefreshButton = CreateButton("刷新版本列表", (_, _) => ReloadHistoryEntries());
            _historyDiffButton = CreateButton("显示 Diff", (_, _) => ShowSelectedHistoryDiff());
            _historyRollbackToEditorButton = CreateButton("回滚到编辑器", (_, _) => RollbackSelectedHistoryToEditor());
            _historyRollbackReloadButton = CreateButton("回滚并热更", async (_, _) => await RollbackSelectedHistoryAndReloadAsync());
            _historyListView = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true };
            _historyListView.Columns.Add("Time", 170);
            _historyListView.Columns.Add("Operator", 130);
            _historyListView.Columns.Add("Reason", 240);
            _historyListView.Columns.Add("VersionId", 320);
            _historyListView.SelectedIndexChanged += (_, _) => UpdateSelectedHistoryEntry();
            _historyPreviewTextBox = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, Font = new Font("Consolas", 9F, FontStyle.Regular, GraphicsUnit.Point) };
            _historyDiffTextBox = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, Font = new Font("Consolas", 9F, FontStyle.Regular, GraphicsUnit.Point) };

            _packageExportButton = CreateButton("导出热更包...", async (_, _) => await ExportHotfixPackageAsync());
            _packageApplyButton = CreateButton("导入并应用...", async (_, _) => await ApplyHotfixPackageAsync());
            _packageOpenPackageFolderButton = CreateButton("打开包目录", (_, _) => OpenHotfixPackageFolder());
            _packageReasonTextBox = new TextBox { Width = 520 };
            _packageLogTextBox = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, Font = new Font("Consolas", 9F, FontStyle.Regular, GraphicsUnit.Point) };

            Controls.Add(BuildMainLayout());
            Controls.Add(BuildToolbar());

            FormClosing += ScriptDebugForm_FormClosing;
            Shown += (_, _) =>
            {
                RefreshView(true);
                _testDebugUiTimer.Start();
                UpdateTestDebugUiFromSession(forceRefreshBreakpoints: true);
            };
        }

        private System.Windows.Forms.Control BuildToolbar()
        {
            var panel = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8), WrapContents = true };
            panel.Controls.Add(_refreshButton);
            panel.Controls.Add(_saveButton);
            panel.Controls.Add(_diagnoseCurrentButton);
            panel.Controls.Add(_diagnoseAllButton);
            panel.Controls.Add(_reloadButton);
            panel.Controls.Add(_copyDiagnosticsButton);
            panel.Controls.Add(_openScriptsFolderButton);
            panel.Controls.Add(_useRoslynPadCheckBox);
            return panel;
        }

        private System.Windows.Forms.Control BuildMainLayout()
        {
            var editorPanel = new Panel { Dock = DockStyle.Fill };
            editorPanel.Controls.Add(_editorTextBox);
            if (_roslynElementHost != null)
            {
                editorPanel.Controls.Add(_roslynElementHost);
            }
            editorPanel.Controls.Add(_statusTextBox);
            editorPanel.Controls.Add(_currentFileLabel);

            var tabs = new TabControl { Dock = DockStyle.Fill };
            var diagnosticsTab = new TabPage("诊断列表");
            diagnosticsTab.Controls.Add(_diagnosticsListView);
            var testTab = new TabPage("测试台");
            testTab.Controls.Add(BuildTestBenchLayout());
            var aiTab = new TabPage("AI 草稿");
            aiTab.Controls.Add(BuildAiLayout());
            var historyTab = new TabPage("历史版本");
            historyTab.Controls.Add(BuildHistoryLayout());
            var packageTab = new TabPage("热更包");
            packageTab.Controls.Add(BuildHotfixPackageLayout());
            tabs.TabPages.Add(diagnosticsTab);
            tabs.TabPages.Add(testTab);
            tabs.TabPages.Add(aiTab);
            tabs.TabPages.Add(historyTab);
            tabs.TabPages.Add(packageTab);

            var rightSplit = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 390 };
            rightSplit.Panel1.Controls.Add(editorPanel);
            rightSplit.Panel2.Controls.Add(tabs);

            var mainSplit = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 280 };
            mainSplit.Panel1.Controls.Add(_fileTreeView);
            mainSplit.Panel2.Controls.Add(rightSplit);
            return mainSplit;
        }

        private System.Windows.Forms.Control BuildTestBenchLayout()
        {
            var leftPanel = new Panel { Dock = DockStyle.Fill };
            leftPanel.Controls.Add(_testKeyListBox);
            leftPanel.Controls.Add(_testSearchTextBox);
            leftPanel.Controls.Add(new Label { Dock = DockStyle.Top, Height = 20, Text = "筛选可执行 Key / Group" });

            var table = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, AutoSize = true, Padding = new Padding(8) };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120F));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            AddRow(table, "在线玩家", _testPlayerComboBox);
            AddRow(table, "选中 Key", _testKeyTextBox);
            AddRow(table, "执行说明", _testDescriptionTextBox);
            AddRow(table, "命令行", _testCommandLineTextBox);
            AddRow(table, "自定义命令", _testCustomCommandTextBox);
            AddRow(table, "TriggerKey", _testTriggerTextBox);
            AddRow(table, "地图文件", _testMapFileTextBox);
            AddRow(table, "地图坐标", BuildFlow(_testCoordXNumeric, _testCoordYNumeric));
            AddRow(table, "QuestIndex", _testQuestIndexNumeric);
            AddRow(table, "Client Payload", _testClientPayloadTextBox);
            AddRow(table, "执行", BuildFlow(_testExecuteButton));

            var rightPanel = new Panel { Dock = DockStyle.Fill };
            rightPanel.Controls.Add(_testResultTextBox);
            rightPanel.Controls.Add(new Label { Dock = DockStyle.Top, Height = 20, Padding = new Padding(8, 0, 0, 0), Text = "执行结果 / 新增日志" });
            rightPanel.Controls.Add(BuildTestDebugLayout());
            rightPanel.Controls.Add(table);

            var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 300 };
            split.Panel1.Controls.Add(leftPanel);
            split.Panel2.Controls.Add(rightPanel);
            return split;
        }

        private System.Windows.Forms.Control BuildTestDebugLayout()
        {
            var group = new GroupBox
            {
                Dock = DockStyle.Top,
                Height = 260,
                Text = "调试（方案2：仅测试台插桩）",
                Padding = new Padding(8),
            };

            var header = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true };
            header.Controls.Add(_testDebugEnabledCheckBox);
            header.Controls.Add(_testDebugRebuildButton);
            header.Controls.Add(_testDebugPauseButton);
            header.Controls.Add(_testDebugContinueButton);
            header.Controls.Add(_testDebugStepButton);
            header.Controls.Add(_testDebugCancelButton);

            var statePanel = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true };
            statePanel.Controls.Add(new Label { AutoSize = true, Text = "状态：" });
            statePanel.Controls.Add(_testDebugStateLabel);
            statePanel.Controls.Add(new Label { AutoSize = true, Text = "    " });
            statePanel.Controls.Add(_testDebugLocationLabel);

            var bpToolbar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true };
            bpToolbar.Controls.Add(new Label { AutoSize = true, Text = "File" });
            bpToolbar.Controls.Add(_testDebugBreakpointFileTextBox);
            bpToolbar.Controls.Add(new Label { AutoSize = true, Text = "Line" });
            bpToolbar.Controls.Add(_testDebugBreakpointLineNumeric);
            bpToolbar.Controls.Add(_testDebugAddBreakpointButton);
            bpToolbar.Controls.Add(_testDebugRemoveBreakpointButton);
            bpToolbar.Controls.Add(_testDebugClearBreakpointsButton);

            var bpPanel = new Panel { Dock = DockStyle.Fill };
            bpPanel.Controls.Add(_testDebugBreakpointsListView);
            bpPanel.Controls.Add(bpToolbar);

            var watchToolbar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true };
            watchToolbar.Controls.Add(new Label { AutoSize = true, Text = "表达式（可用：player / context / api / envir / executionId）" });
            watchToolbar.Controls.Add(_testDebugWatchExpressionTextBox);
            watchToolbar.Controls.Add(_testDebugEvalExpressionButton);

            var watchPanel = new Panel { Dock = DockStyle.Fill };
            watchPanel.Controls.Add(_testDebugWatchResultTextBox);
            watchPanel.Controls.Add(watchToolbar);

            var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 520 };
            split.Panel1.Controls.Add(bpPanel);
            split.Panel2.Controls.Add(watchPanel);

            group.Controls.Add(split);
            group.Controls.Add(statePanel);
            group.Controls.Add(header);
            return group;
        }

        private System.Windows.Forms.Control BuildAiLayout()
        {
            var topTable = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, AutoSize = true, Padding = new Padding(8) };
            topTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130F));
            topTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            AddRow(topTable, "脚本类型", _aiKindComboBox);
            AddRow(topTable, "目标 Key", _aiTargetKeyTextBox);
            AddRow(topTable, "自然语言描述", _aiDescriptionTextBox);
            AddRow(topTable, "附加要求", _aiAdditionalRequirementsTextBox);
            AddRow(topTable, "参考脚本", BuildAiReferenceScriptRow());
            AddRow(topTable, "选项", BuildFlow(_aiIncludeCurrentScriptCheckBox, _aiIncludeDiagnosticsCheckBox));
            AddRow(topTable, "操作", BuildFlow(_aiBuildPromptButton, _aiGenerateDraftButton, _aiFixButton, _aiApplyDraftButton, _aiSaveDraftButton, _aiDiagnoseDraftButton, _aiReloadDraftButton));

            var promptGroup = new GroupBox { Dock = DockStyle.Fill, Text = "Prompt 预览" };
            promptGroup.Controls.Add(_aiPromptTextBox);

            var draftGroup = new GroupBox { Dock = DockStyle.Fill, Text = "生成草稿（可编辑）" };
            draftGroup.Controls.Add(_aiDraftTextBox);

            var resultGroup = new GroupBox { Dock = DockStyle.Fill, Text = "生成结果" };
            resultGroup.Controls.Add(_aiResultTextBox);

            var verticalSplit = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 200 };
            verticalSplit.Panel1.Controls.Add(promptGroup);
            verticalSplit.Panel2.Controls.Add(draftGroup);

            var mainSplit = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 760 };
            mainSplit.Panel1.Controls.Add(verticalSplit);
            mainSplit.Panel2.Controls.Add(resultGroup);

            var panel = new Panel { Dock = DockStyle.Fill };
            panel.Controls.Add(mainSplit);
            panel.Controls.Add(topTable);
            return panel;
        }

        private System.Windows.Forms.Control BuildAiReferenceScriptRow()
        {
            var table = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 4 };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            _aiReferenceScriptTextBox.Dock = DockStyle.Fill;
            table.Controls.Add(_aiIncludeReferenceScriptCheckBox, 0, 0);
            table.Controls.Add(_aiReferenceScriptTextBox, 1, 0);
            table.Controls.Add(_aiPickReferenceScriptButton, 2, 0);
            table.Controls.Add(_aiClearReferenceScriptButton, 3, 0);
            return table;
        }

        private System.Windows.Forms.Control BuildHistoryLayout()
        {
            var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8), WrapContents = true };
            toolbar.Controls.Add(_historyRefreshButton);
            toolbar.Controls.Add(_historyDiffButton);
            toolbar.Controls.Add(_historyRollbackToEditorButton);
            toolbar.Controls.Add(_historyRollbackReloadButton);

            var previewGroup = new GroupBox { Dock = DockStyle.Fill, Text = "版本预览" };
            previewGroup.Controls.Add(_historyPreviewTextBox);

            var diffGroup = new GroupBox { Dock = DockStyle.Fill, Text = "Diff（选中版本 vs 当前编辑器）" };
            diffGroup.Controls.Add(_historyDiffTextBox);

            var rightSplit = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 240 };
            rightSplit.Panel1.Controls.Add(previewGroup);
            rightSplit.Panel2.Controls.Add(diffGroup);

            var mainSplit = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 560 };
            mainSplit.Panel1.Controls.Add(_historyListView);
            mainSplit.Panel2.Controls.Add(rightSplit);

            var panel = new Panel { Dock = DockStyle.Fill };
            panel.Controls.Add(mainSplit);
            panel.Controls.Add(toolbar);
            return panel;
        }

        private System.Windows.Forms.Control BuildHotfixPackageLayout()
        {
            var table = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, AutoSize = true, Padding = new Padding(8) };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120F));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            AddRow(table, "备注/原因", _packageReasonTextBox);
            AddRow(table, "操作", BuildFlow(_packageExportButton, _packageApplyButton, _packageOpenPackageFolderButton));

            var group = new GroupBox { Dock = DockStyle.Fill, Text = "操作日志" };
            group.Controls.Add(_packageLogTextBox);

            var panel = new Panel { Dock = DockStyle.Fill };
            panel.Controls.Add(group);
            panel.Controls.Add(table);
            return panel;
        }

        private static Button CreateButton(string text, EventHandler handler)
        {
            var button = new Button { Text = text, AutoSize = true };
            button.Click += handler;
            return button;
        }

        private static TextBox CreateReadOnlyTextBox(bool multiline = false, int height = 24)
        {
            return new TextBox { ReadOnly = true, Multiline = multiline, Height = height, ScrollBars = multiline ? ScrollBars.Vertical : ScrollBars.None, BackColor = SystemColors.Window };
        }

        private static NumericUpDown CreateNumeric(int maximum = 2000)
        {
            return new NumericUpDown { Minimum = 0, Maximum = maximum, Width = 90 };
        }

        private static FlowLayoutPanel BuildFlow(params System.Windows.Forms.Control[] controls)
        {
            var panel = new FlowLayoutPanel { AutoSize = true, WrapContents = false, FlowDirection = FlowDirection.LeftToRight };
            foreach (var control in controls) panel.Controls.Add(control);
            return panel;
        }

        private static void AddRow(TableLayoutPanel table, string label, System.Windows.Forms.Control control)
        {
            var row = table.RowCount++;
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(3, 6, 3, 6) }, 0, row);
            control.Dock = DockStyle.Fill;
            control.Margin = new Padding(3, 3, 3, 6);
            table.Controls.Add(control, 1, row);
        }

        private void RefreshView(bool reloadTree)
        {
            if (reloadTree) ReloadFileTree();
            ReloadOnlinePlayers();
            ReloadTestEntries();
            ReloadHistoryEntries();
            _statusTextBox.Text = BuildStatusText();
            RenderDiagnostics(_currentDiagnostics);
            UpdateEditorState();
            UpdateTestInputState();
            UpdateHistoryState();
        }

        private void ReloadFileTree()
        {
            _fileNodeLookup.Clear();
            _fileTreeView.BeginUpdate();
            try
            {
                _fileTreeView.Nodes.Clear();
                if (!Directory.Exists(ScriptsRootPath))
                {
                    _fileTreeView.Nodes.Add(new TreeNode($"目录不存在: {ScriptsRootPath}") { ForeColor = Color.DarkRed });
                    return;
                }

                var root = new TreeNode(Path.GetFileName(ScriptsRootPath)) { Tag = ScriptsRootPath };
                _fileTreeView.Nodes.Add(root);

                foreach (var path in EnumerateVisibleScriptFiles())
                    AddFileNode(root, path);

                root.Expand();
            }
            finally
            {
                _fileTreeView.EndUpdate();
            }

            if (!string.IsNullOrWhiteSpace(_currentFilePath))
                TrySelectFileNode(_currentFilePath);
        }

        private IEnumerable<string> EnumerateVisibleScriptFiles()
        {
            var files = new List<string>();
            if (Directory.Exists(ScriptsRootPath))
                files.AddRange(Directory.GetFiles(ScriptsRootPath, "*.cs", SearchOption.AllDirectories).Where(path => !IsUnderAuxiliaryUnderscoreDirectory(path)));
            if (Settings.CSharpScriptsFallbackToTxt && Directory.Exists(ScriptsRootPath))
                files.AddRange(Directory.GetFiles(ScriptsRootPath, "*.txt", SearchOption.AllDirectories).Where(path => !IsUnderAuxiliaryUnderscoreDirectory(path)));
            files.Sort(StringComparer.OrdinalIgnoreCase);
            return files;
        }

        private void AddFileNode(TreeNode rootNode, string fullPath)
        {
            var relativePath = Path.GetRelativePath(ScriptsRootPath, fullPath);
            var segments = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var current = rootNode;
            var builder = ScriptsRootPath;

            foreach (var segment in segments)
            {
                builder = Path.Combine(builder, segment);
                var existing = current.Nodes.Cast<TreeNode>().FirstOrDefault(node => string.Equals(node.Text, segment, StringComparison.OrdinalIgnoreCase));
                if (existing == null)
                {
                    existing = new TreeNode(segment) { Tag = builder };
                    current.Nodes.Add(existing);
                }
                current = existing;
            }

            current.Tag = fullPath;
            _fileNodeLookup[fullPath] = current;
        }

        private void FileTreeView_BeforeSelect(object? sender, TreeViewCancelEventArgs e)
        {
            if (!HasPendingChanges()) return;
            if (e.Node?.Tag is not string nextPath) return;
            if (string.Equals(nextPath, _currentFilePath, StringComparison.OrdinalIgnoreCase)) return;
            if (!ConfirmSaveIfDirty("切换文件")) e.Cancel = true;
        }

        private void FileTreeView_AfterSelect(object? sender, TreeViewEventArgs e)
        {
            if (e.Node?.Tag is not string selectedPath) return;
            if (!File.Exists(selectedPath)) return;
            LoadFile(selectedPath);
        }

        private void LoadFile(string path)
        {
            try
            {
                var text = ReadAllText(path, out var encoding);
                _loadedFileEncodings[path] = encoding;
                _currentFilePath = path;
                _testDebugBreakpointFileTextBox.Text = path;
                _currentFileFromAiDraft = false;
                _suppressTextChanged = true;
                _editorTextBox.Text = text;
                _editorTextBox.SelectionStart = 0;
                _editorTextBox.SelectionLength = 0;

                if (_roslynEditorReady && _roslynEditor != null)
                {
                    _roslynEditor.Text = text;
                    _roslynEditor.Select(0, 0);
                    _roslynEditor.CaretOffset = 0;
                    _roslynEditor.ScrollToLine(1);
                }
                else
                {
                    _pendingRoslynText = text;
                }
                _suppressTextChanged = false;
                _isDirty = false;
                ApplyEditorMode(syncText: false);
                UpdateEditorState();
                ReloadHistoryEntries();
                UpdateHistoryState();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.ToString(), "读取脚本文件失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void TryInitializeRoslynPadEditor()
        {
            try
            {
                _roslynHost = RoslynPadScriptHost.CreateHost(debugBuild: true);

                _roslynEditor = new RoslynCodeEditor
                {
                    FontFamily = new WpfFontFamily("Consolas"),
                    FontSize = 13,
                };

                _roslynEditor.Loaded += RoslynEditor_Loaded;
                _roslynEditor.TextChanged += RoslynEditor_TextChanged;

                _roslynElementHost = new ElementHost { Dock = DockStyle.Fill, Child = _roslynEditor, Visible = false };
            }
            catch (Exception ex)
            {
                _useRoslynPadCheckBox.Checked = false;
                _useRoslynPadCheckBox.Enabled = false;
                _lastActionSummary = $"RoslynPad 初始化失败（已降级为文本编辑器）：{ex.Message}";
            }
        }

        private async void RoslynEditor_Loaded(object sender, EventArgs e)
        {
            if (_roslynEditor == null || _roslynHost == null)
            {
                return;
            }

            _roslynEditor.Loaded -= RoslynEditor_Loaded;

            try
            {
                _roslynDocumentId = await _roslynEditor.InitializeAsync(
                    _roslynHost,
                    new ClassificationHighlightColors(),
                    ScriptsRootPath,
                    string.Empty,
                    SourceCodeKind.Regular).ConfigureAwait(true);

                _roslynEditorReady = true;

                var initialText = IsEditableScriptFile(_currentFilePath) ? _editorTextBox.Text : _pendingRoslynText;
                if (!string.IsNullOrEmpty(initialText))
                {
                    _suppressTextChanged = true;
                    _roslynEditor.Text = initialText;
                    _roslynEditor.Select(0, 0);
                    _roslynEditor.CaretOffset = 0;
                    _roslynEditor.ScrollToLine(1);
                    _suppressTextChanged = false;
                }

                _pendingRoslynText = string.Empty;
                ApplyEditorMode(syncText: false);
                UpdateEditorState();
            }
            catch (Exception ex)
            {
                _roslynEditorReady = false;
                _useRoslynPadCheckBox.Checked = false;
                _useRoslynPadCheckBox.Enabled = false;

                MessageBox.Show(this, ex.ToString(), "RoslynPad 初始化失败", MessageBoxButtons.OK, MessageBoxIcon.Error);

                ApplyEditorMode(syncText: false);
                UpdateEditorState();
            }
        }

        private void RoslynEditor_TextChanged(object? sender, EventArgs e)
        {
            if (_suppressTextChanged || string.IsNullOrWhiteSpace(_currentFilePath)) return;
            if (!IsEditableScriptFile(_currentFilePath)) return;
            if (!IsUsingRoslynPadEditor()) return;
            _isDirty = true;
            UpdateEditorState();
        }

        private bool IsUsingRoslynPadEditor()
        {
            return _useRoslynPadCheckBox.Checked &&
                   _roslynEditorReady &&
                   _roslynEditor != null &&
                   _roslynElementHost != null &&
                   _roslynElementHost.Visible &&
                   IsEditableScriptFile(_currentFilePath);
        }

        private void ApplyEditorMode(bool syncText)
        {
            if (_roslynElementHost == null || _roslynEditor == null)
            {
                _editorTextBox.Visible = true;
                return;
            }

            var shouldUseRoslyn = _useRoslynPadCheckBox.Checked &&
                                  _roslynEditorReady &&
                                  IsEditableScriptFile(_currentFilePath);

            if (syncText && IsEditableScriptFile(_currentFilePath))
            {
                _suppressTextChanged = true;
                if (shouldUseRoslyn)
                {
                    _roslynEditor.Text = _editorTextBox.Text;
                }
                else
                {
                    _editorTextBox.Text = _roslynEditor.Text;
                }
                _suppressTextChanged = false;
            }

            _roslynElementHost.Visible = shouldUseRoslyn;
            _editorTextBox.Visible = !shouldUseRoslyn;
        }

        private string GetCurrentEditorText()
        {
            if (IsUsingRoslynPadEditor())
            {
                return _roslynEditor!.Text;
            }

            return _editorTextBox.Text;
        }

        private void EditorTextBox_TextChanged(object? sender, EventArgs e)
        {
            if (_suppressTextChanged || string.IsNullOrWhiteSpace(_currentFilePath)) return;
            _isDirty = true;
            UpdateEditorState();
        }

        private void UpdateEditorState()
        {
            var hasFile = !string.IsNullOrWhiteSpace(_currentFilePath);
            var isCsFile = IsEditableScriptFile(_currentFilePath);
            var canEdit = hasFile && isCsFile;

            ApplyEditorMode(syncText: false);

            _editorTextBox.ReadOnly = !canEdit;
            if (_roslynEditor != null)
            {
                _roslynEditor.IsReadOnly = !canEdit;
            }

            _saveButton.Enabled = canEdit;
            _diagnoseCurrentButton.Enabled = canEdit;

            if (!hasFile)
            {
                _currentFileLabel.Text = "当前文件: (未选择)";
                return;
            }

            var relativePath = Path.GetRelativePath(ScriptsRootPath, _currentFilePath);
            var mode = isCsFile ? string.Empty : " [只读]";
            var dirty = _isDirty ? " *未保存" : string.Empty;
            _currentFileLabel.Text = $"当前文件: {relativePath}{mode}{dirty}";
        }

        private async Task DiagnoseCurrentFileAsync()
        {
            if (!IsEditableScriptFile(_currentFilePath))
            {
                MessageBox.Show(this, "请先选择一个 .cs 脚本文件。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (!ConfirmSaveIfDirty("当前文件诊断")) return;

            await RunBusyAsync(async () =>
            {
                var result = await Task.Run(() => _diagnosticCompiler.CompileFromFiles(ScriptsRootPath, new[] { _currentFilePath }, $"LomScripts_Current_{Environment.TickCount64}", true));
                ApplyDiagnosticResult(result, $"当前文件诊断: {Path.GetFileName(_currentFilePath)}");
            });
        }

        private async Task DiagnoseAllAsync()
        {
            if (!ConfirmSaveIfDirty("全量诊断")) return;
            await RunBusyAsync(async () =>
            {
                var result = await Task.Run(() => _diagnosticCompiler.CompileFromDirectory(ScriptsRootPath, $"LomScripts_Full_{Environment.TickCount64}", true));
                ApplyDiagnosticResult(result, "全量诊断");
            });
        }

        private async Task ReloadAsync(bool showFeedback)
        {
            if (!ConfirmSaveIfDirty("编译并热更（Reload）")) return;
            await RunBusyAsync(async () =>
            {
                var stopwatch = Stopwatch.StartNew();
                await Task.Run(() => Envir.CSharpScripts.Reload());
                stopwatch.Stop();

                var manager = Envir.CSharpScripts;
                _currentDiagnostics = manager.LastDiagnostics;
                var handlerCount = manager.LastRegisteredHandlerCount;
                var elapsed = stopwatch.ElapsedMilliseconds;
                var success = _currentDiagnostics.Count == 0 && string.IsNullOrWhiteSpace(manager.LastError);

                _lastActionSummary = $"编译并热更（Reload）: {(success ? "成功" : "失败")}，耗时 {elapsed}ms，Handlers {handlerCount}，诊断数 {_currentDiagnostics.Count}";

                if (showFeedback)
                {
                    MessageBox.Show(this, BuildReloadFeedbackText(success, elapsed, handlerCount, manager.LastError, _currentDiagnostics), success ? "热更成功" : "热更失败", MessageBoxButtons.OK, success ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
                }
            });
        }

        private static string BuildReloadFeedbackText(bool success, long elapsedMilliseconds, int handlerCount, string? lastError, IReadOnlyList<ScriptDiagnostic> diagnostics)
        {
            var sb = new StringBuilder();
            sb.AppendLine(success ? "脚本已成功编译并热更。" : "脚本编译/热更失败。");
            sb.AppendLine($"Elapsed: {elapsedMilliseconds}ms");
            sb.AppendLine($"Handlers: {handlerCount}");
            sb.AppendLine($"Diagnostics: {diagnostics.Count}");
            if (!string.IsNullOrWhiteSpace(lastError))
                sb.AppendLine("LastError: " + lastError);

            if (diagnostics.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("诊断摘要（最多 20 条）：");
                foreach (var diagnostic in diagnostics.Take(20))
                    sb.AppendLine("- " + diagnostic);
            }

            if (diagnostics.Count > 20)
                sb.AppendLine($"(已省略 {diagnostics.Count - 20} 条诊断)");

            return sb.ToString().TrimEnd();
        }

        private void ApplyDiagnosticResult(ScriptCompileResult result, string actionName)
        {
            _currentDiagnostics = result.Diagnostics;
            _lastActionSummary = $"{actionName}: {(result.Success ? "成功" : "失败")}，耗时 {result.ElapsedMilliseconds}ms，诊断数 {result.Diagnostics.Count}";
        }

        private void RenderDiagnostics(IReadOnlyList<ScriptDiagnostic> diagnostics)
        {
            _diagnosticsListView.BeginUpdate();
            try
            {
                _diagnosticsListView.Items.Clear();
                foreach (var diagnostic in diagnostics)
                {
                    var item = new ListViewItem(diagnostic.Severity);
                    item.SubItems.Add(diagnostic.Id);
                    item.SubItems.Add(ToDisplayPath(diagnostic.FilePath));
                    item.SubItems.Add(diagnostic.Line > 0 ? diagnostic.Line.ToString() : string.Empty);
                    item.SubItems.Add(diagnostic.Column > 0 ? diagnostic.Column.ToString() : string.Empty);
                    item.SubItems.Add(diagnostic.Message);
                    item.Tag = diagnostic;
                    _diagnosticsListView.Items.Add(item);
                }
            }
            finally
            {
                _diagnosticsListView.EndUpdate();
            }
        }

        private void JumpToSelectedDiagnostic()
        {
            if (_diagnosticsListView.SelectedItems.Count == 0) return;
            if (_diagnosticsListView.SelectedItems[0].Tag is not ScriptDiagnostic diagnostic) return;
            if (string.IsNullOrWhiteSpace(diagnostic.FilePath)) return;
            if (!TrySelectFileNode(diagnostic.FilePath))
            {
                MessageBox.Show(this, $"无法在脚本树中找到文件：{diagnostic.FilePath}", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            GoToLineColumn(diagnostic.Line, diagnostic.Column);
        }

        private bool TrySelectFileNode(string fullPath)
        {
            var normalized = Path.GetFullPath(fullPath);
            if (!_fileNodeLookup.TryGetValue(normalized, out var node)) return false;
            _fileTreeView.SelectedNode = node;
            node.EnsureVisible();
            return true;
        }

        private void GoToLineColumn(int line, int column)
        {
            if (line <= 0) return;

            if (IsUsingRoslynPadEditor() && _roslynEditor?.Document != null)
            {
                var doc = _roslynEditor.Document;
                var targetLine = Math.Max(1, Math.Min(line, doc.LineCount));
                var targetColumn = Math.Max(1, column);
                var lineInfo = doc.GetLineByNumber(targetLine);
                targetColumn = Math.Min(targetColumn, lineInfo.Length + 1);
                var offset = doc.GetOffset(targetLine, targetColumn);

                _roslynEditor.Select(offset, 0);
                _roslynEditor.CaretOffset = offset;
                _roslynEditor.ScrollToLine(targetLine);
                _roslynEditor.Focus();
                return;
            }

            var legacyTargetLine = Math.Max(0, Math.Min(line - 1, _editorTextBox.Lines.Length - 1));
            if (legacyTargetLine < 0) return;
            var firstChar = _editorTextBox.GetFirstCharIndexFromLine(legacyTargetLine);
            if (firstChar < 0) return;
            var legacyTargetColumn = Math.Max(0, column - 1);
            var lineLength = _editorTextBox.Lines[legacyTargetLine].Length;
            _editorTextBox.Focus();
            _editorTextBox.SelectionStart = firstChar + Math.Min(legacyTargetColumn, lineLength);
            _editorTextBox.SelectionLength = 0;
            _editorTextBox.ScrollToCaret();
        }

        private void CopyDiagnostics()
        {
            if (_currentDiagnostics.Count == 0)
            {
                MessageBox.Show(this, "当前没有可复制的诊断信息。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            Clipboard.SetText(string.Join(Environment.NewLine, _currentDiagnostics.Select(item => item.ToString())));
        }

        private void OpenScriptsFolder()
        {
            try
            {
                if (!Directory.Exists(ScriptsRootPath))
                {
                    MessageBox.Show(this, $"脚本目录不存在：{ScriptsRootPath}", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                Process.Start(new ProcessStartInfo { FileName = ScriptsRootPath, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.ToString(), "打开脚本目录失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OpenHotfixPackageFolder()
        {
            try
            {
                if (!Directory.Exists(ScriptsRootPath))
                {
                    MessageBox.Show(this, $"脚本目录不存在：{ScriptsRootPath}", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                Directory.CreateDirectory(HotfixPackageRootPath);
                Process.Start(new ProcessStartInfo { FileName = HotfixPackageRootPath, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.ToString(), "打开热更包目录失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async Task ExportHotfixPackageAsync()
        {
            if (!ConfirmSaveIfDirty("导出热更包")) return;

            await RunBusyAsync(async () =>
            {
                if (!Directory.Exists(ScriptsRootPath))
                    throw new InvalidOperationException($"脚本目录不存在：{ScriptsRootPath}");

                Directory.CreateDirectory(HotfixPackageRootPath);

                var scriptFiles = CollectScriptFilesForHotfixPackage();
                if (scriptFiles.Count == 0)
                {
                    _lastActionSummary = "导出热更包失败：未找到可打包的 .cs 脚本文件（已自动忽略 '_' 辅助目录）。";
                    _packageLogTextBox.Text = _lastActionSummary;
                    return;
                }

                var packageId = BuildHotfixPackageId();
                var reason = (_packageReasonTextBox.Text ?? string.Empty).Trim();

                using var dialog = new SaveFileDialog
                {
                    Filter = "脚本热更包 (*.zip)|*.zip|所有文件 (*.*)|*.*",
                    FileName = $"ScriptsHotfix_{packageId}.zip",
                    InitialDirectory = Directory.Exists(HotfixPackageRootPath) ? HotfixPackageRootPath : ScriptsRootPath,
                    OverwritePrompt = true,
                };

                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    _lastActionSummary = "已取消导出热更包。";
                    _packageLogTextBox.Text = _lastActionSummary;
                    return;
                }

                var outputPath = dialog.FileName;

                var meta = new HotfixPackageMeta
                {
                    PackageId = packageId,
                    CreatedLocal = DateTime.Now,
                    CreatedUtc = DateTime.UtcNow,
                    Operator = Environment.UserName,
                    Machine = Environment.MachineName,
                    Reason = reason,
                };

                var exportDir = Path.Combine(HotfixPackageRootPath, "_exported", packageId);
                Directory.CreateDirectory(exportDir);

                await Task.Run(() => CreateHotfixPackage(outputPath, meta, scriptFiles));

                var metaJson = JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(Path.Combine(exportDir, "meta.json"), metaJson + Environment.NewLine, new UTF8Encoding(false));

                var recordZipPath = Path.Combine(exportDir, Path.GetFileName(outputPath));
                if (!string.Equals(Path.GetFullPath(recordZipPath), Path.GetFullPath(outputPath), StringComparison.OrdinalIgnoreCase))
                    File.Copy(outputPath, recordZipPath, overwrite: true);

                _lastActionSummary = $"热更包导出完成：PackageId={packageId}, Files={meta.Files.Count}";
                _packageLogTextBox.Text =
                    $"[OK] 热更包导出完成\r\n" +
                    $"PackageId: {packageId}\r\n" +
                    $"Files: {meta.Files.Count}\r\n" +
                    $"Out: {outputPath}\r\n" +
                    $"Record: {exportDir}\r\n";
            });
        }

        private async Task ApplyHotfixPackageAsync()
        {
            if (!ConfirmSaveIfDirty("导入并应用热更包")) return;

            await RunBusyAsync(async () =>
            {
                if (!Directory.Exists(ScriptsRootPath))
                    throw new InvalidOperationException($"脚本目录不存在：{ScriptsRootPath}");

                Directory.CreateDirectory(HotfixPackageRootPath);

                using var dialog = new OpenFileDialog
                {
                    Filter = "脚本热更包 (*.zip)|*.zip|所有文件 (*.*)|*.*",
                    InitialDirectory = Directory.Exists(HotfixPackageRootPath) ? HotfixPackageRootPath : ScriptsRootPath,
                    Multiselect = false,
                    CheckFileExists = true,
                };

                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    _lastActionSummary = "已取消应用热更包。";
                    _packageLogTextBox.Text = _lastActionSummary;
                    return;
                }

                var packagePath = dialog.FileName;

                var parsed = await Task.Run(() =>
                {
                    var result = ReadHotfixPackage(packagePath);
                    ApplyHotfixPackageFiles(result.Meta, result.Files);
                    return result;
                });

                var appliedDir = Path.Combine(HotfixPackageRootPath, "_applied", parsed.Meta.PackageId);
                Directory.CreateDirectory(appliedDir);

                var metaJson = JsonSerializer.Serialize(parsed.Meta, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(Path.Combine(appliedDir, "meta.json"), metaJson + Environment.NewLine, new UTF8Encoding(false));

                var appliedZipPath = Path.Combine(appliedDir, Path.GetFileName(packagePath));
                if (!string.Equals(Path.GetFullPath(appliedZipPath), Path.GetFullPath(packagePath), StringComparison.OrdinalIgnoreCase))
                    File.Copy(packagePath, appliedZipPath, overwrite: true);

                RefreshView(true);

                await Task.Run(() => Envir.CSharpScripts.Reload());
                _currentDiagnostics = Envir.CSharpScripts.LastDiagnostics;

                _lastActionSummary = _currentDiagnostics.Count == 0
                    ? $"热更包已应用并热更：PackageId={parsed.Meta.PackageId}（无诊断信息）"
                    : $"热更包已应用并热更：PackageId={parsed.Meta.PackageId}（诊断数: {_currentDiagnostics.Count}）";

                _packageLogTextBox.Text =
                    $"[OK] 热更包已应用并热更\r\n" +
                    $"PackageId: {parsed.Meta.PackageId}\r\n" +
                    $"Files: {parsed.Meta.Files.Count}\r\n" +
                    $"Package: {packagePath}\r\n" +
                    $"Record: {appliedDir}\r\n" +
                    $"Diagnostics: {_currentDiagnostics.Count}\r\n";
            });
        }

        private static string BuildHotfixPackageId()
        {
            return "pkg_" + BuildHistoryVersionId();
        }

        private List<string> CollectScriptFilesForHotfixPackage()
        {
            var files = new List<string>();

            foreach (var fullPath in Directory.GetFiles(ScriptsRootPath, "*.cs", SearchOption.AllDirectories))
            {
                if (IsUnderAuxiliaryUnderscoreDirectory(fullPath))
                    continue;

                files.Add(Path.GetFullPath(fullPath));
            }

            files.Sort(StringComparer.OrdinalIgnoreCase);
            return files;
        }

        private bool IsUnderAuxiliaryUnderscoreDirectory(string fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath))
                return true;

            try
            {
                var normalized = Path.GetFullPath(fullPath);

                var root = ScriptsRootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!normalized.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    return true;

                var relative = normalized.Substring(root.Length);
                var segments = relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);

                for (var i = 0; i < segments.Length - 1; i++)
                {
                    if (segments[i].StartsWith("_", StringComparison.Ordinal))
                        return true;
                }

                return false;
            }
            catch
            {
                return true;
            }
        }

        private void CreateHotfixPackage(string outputZipPath, HotfixPackageMeta meta, IReadOnlyList<string> scriptFiles)
        {
            if (meta == null) throw new ArgumentNullException(nameof(meta));
            if (scriptFiles == null) throw new ArgumentNullException(nameof(scriptFiles));

            meta.Files.Clear();

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputZipPath)) ?? ".");

            using var fileStream = new FileStream(outputZipPath, FileMode.Create, FileAccess.Write, FileShare.None);
            using var archive = new ZipArchive(fileStream, ZipArchiveMode.Create, leaveOpen: false, entryNameEncoding: Encoding.UTF8);

            for (var i = 0; i < scriptFiles.Count; i++)
            {
                var fullPath = Path.GetFullPath(scriptFiles[i]);
                var relativePath = Path.GetRelativePath(ScriptsRootPath, fullPath);
                if (relativePath.StartsWith("..", StringComparison.Ordinal))
                    throw new InvalidOperationException($"脚本文件不在脚本根目录下：{fullPath}");

                var normalizedRelative = NormalizeRelativePathForPackage(relativePath);

                var bytes = ReadAllBytesShared(fullPath);
                var sha256 = ComputeSha256Hex(bytes);

                meta.Files.Add(new HotfixPackageFile
                {
                    RelativePath = normalizedRelative,
                    Length = bytes.Length,
                    Sha256 = sha256,
                });

                var entry = archive.CreateEntry("files/" + normalizedRelative, CompressionLevel.Optimal);
                using (var entryStream = entry.Open())
                {
                    entryStream.Write(bytes, 0, bytes.Length);
                }
            }

            var metaJson = JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true });
            var metaEntry = archive.CreateEntry("meta.json", CompressionLevel.Optimal);
            using (var writer = new StreamWriter(metaEntry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                writer.Write(metaJson);
                writer.Write(Environment.NewLine);
            }
        }

        private static (HotfixPackageMeta Meta, List<(string RelativePath, byte[] Bytes)> Files) ReadHotfixPackage(string packageZipPath)
        {
            using var fileStream = new FileStream(packageZipPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var archive = new ZipArchive(fileStream, ZipArchiveMode.Read, leaveOpen: false, entryNameEncoding: Encoding.UTF8);

            var metaEntry = archive.GetEntry("meta.json");
            if (metaEntry == null)
                throw new InvalidOperationException("热更包缺少 meta.json。");

            HotfixPackageMeta meta;
            using (var reader = new StreamReader(metaEntry.Open(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
            {
                var json = reader.ReadToEnd();
                meta = JsonSerializer.Deserialize<HotfixPackageMeta>(json) ?? throw new InvalidOperationException("meta.json 解析失败。");
            }

            if (string.IsNullOrWhiteSpace(meta.PackageId))
                throw new InvalidOperationException("meta.json 中 PackageId 为空。");

            if (meta.Files == null || meta.Files.Count == 0)
                throw new InvalidOperationException("meta.json 中 Files 为空。");

            var files = new List<(string RelativePath, byte[] Bytes)>(meta.Files.Count);

            for (var i = 0; i < meta.Files.Count; i++)
            {
                var file = meta.Files[i];
                var relative = NormalizeRelativePathForPackage(file.RelativePath);
                if (string.IsNullOrWhiteSpace(relative))
                    throw new InvalidOperationException("meta.json 中存在空 RelativePath。");

                var entry = archive.GetEntry("files/" + relative);
                if (entry == null)
                    throw new InvalidOperationException($"热更包缺少文件条目：files/{relative}");

                byte[] bytes;
                using (var entryStream = entry.Open())
                using (var ms = new MemoryStream())
                {
                    entryStream.CopyTo(ms);
                    bytes = ms.ToArray();
                }

                if (file.Length > 0 && bytes.Length != file.Length)
                    throw new InvalidOperationException($"文件长度不匹配：{relative} (expected={file.Length}, actual={bytes.Length})");

                var sha256 = ComputeSha256Hex(bytes);
                if (!string.IsNullOrWhiteSpace(file.Sha256) && !string.Equals(sha256, file.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"文件哈希不匹配：{relative} (expected={file.Sha256}, actual={sha256})");

                files.Add((relative, bytes));
            }

            return (meta, files);
        }

        private void ApplyHotfixPackageFiles(HotfixPackageMeta meta, IReadOnlyList<(string RelativePath, byte[] Bytes)> files)
        {
            if (meta == null) throw new ArgumentNullException(nameof(meta));
            if (files == null) throw new ArgumentNullException(nameof(files));

            for (var i = 0; i < files.Count; i++)
            {
                var relative = files[i].RelativePath.Replace('/', Path.DirectorySeparatorChar);

                if (!TryResolveFullPathUnderScriptsRoot(relative, out var fullPath))
                    throw new InvalidOperationException($"非法脚本相对路径（疑似路径穿越）：{files[i].RelativePath}");

                if (!string.Equals(Path.GetExtension(fullPath), ".cs", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"热更包仅允许写入 .cs 文件：{files[i].RelativePath}");

                var dir = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrWhiteSpace(dir))
                    Directory.CreateDirectory(dir);

                if (File.Exists(fullPath))
                {
                    if (!TryCreateHistorySnapshot(fullPath, "HotfixPackageApply", meta.PackageId, out var historyError))
                        throw new InvalidOperationException($"创建历史快照失败：{historyError}");
                }

                File.WriteAllBytes(fullPath, files[i].Bytes);
            }
        }

        private bool TryResolveFullPathUnderScriptsRoot(string relativePath, out string fullPath)
        {
            fullPath = string.Empty;

            if (string.IsNullOrWhiteSpace(relativePath))
                return false;

            try
            {
                var combined = Path.Combine(ScriptsRootPath, relativePath);
                var normalized = Path.GetFullPath(combined);

                var root = ScriptsRootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!normalized.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    return false;

                fullPath = normalized;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string NormalizeRelativePathForPackage(string relativePath)
        {
            relativePath = (relativePath ?? string.Empty).Trim();
            relativePath = relativePath.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
            relativePath = relativePath.TrimStart('/');

            if (string.IsNullOrWhiteSpace(relativePath))
                return string.Empty;

            // Zip 内部路径一律用 "/"，且禁止 ".."。
            var segments = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Any(s => string.Equals(s, "..", StringComparison.Ordinal)))
                throw new InvalidOperationException($"非法相对路径：{relativePath}");

            return string.Join("/", segments);
        }

        private static string ComputeSha256Hex(byte[] bytes)
        {
            var hash = SHA256.HashData(bytes ?? Array.Empty<byte>());
            return Convert.ToHexString(hash);
        }

        private static byte[] ReadAllBytesShared(string filePath)
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return ms.ToArray();
        }

        private bool SaveCurrentFile(bool showFeedback)
        {
            if (!IsEditableScriptFile(_currentFilePath)) return false;
            try
            {
                var text = GetCurrentEditorText();

                if (_currentFileFromAiDraft && !EnsureAiScriptSafeToPersist(text, out var guardMessage))
                {
                    MessageBox.Show(this, guardMessage, "AI 脚本安全检查未通过", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }

                if (_isDirty && !TryCreateHistorySnapshot(_currentFilePath, _currentFileFromAiDraft ? "AiDraftSave" : "ManualSave", fromVersionId: string.Empty, out var historyError))
                {
                    MessageBox.Show(this, historyError, "创建历史快照失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return false;
                }

                var encoding = _loadedFileEncodings.TryGetValue(_currentFilePath, out var loadedEncoding) ? loadedEncoding : new UTF8Encoding(false);
                File.WriteAllText(_currentFilePath, text, encoding);

                _suppressTextChanged = true;
                if (IsUsingRoslynPadEditor())
                {
                    _editorTextBox.Text = text;
                }
                else if (_roslynEditorReady && _roslynEditor != null)
                {
                    _roslynEditor.Text = text;
                }
                _suppressTextChanged = false;
                _isDirty = false;
                UpdateEditorState();
                ReloadHistoryEntries();
                UpdateHistoryState();
                if (showFeedback) MessageBox.Show(this, "脚本已保存。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.ToString(), "保存脚本失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        private bool ConfirmSaveIfDirty(string actionName)
        {
            if (!HasPendingChanges()) return true;
            var result = MessageBox.Show(this, $"当前文件有未保存修改，是否先保存后再执行“{actionName}”？", "未保存修改", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (result == DialogResult.Cancel) return false;
            return result != DialogResult.Yes || SaveCurrentFile(false);
        }

        private bool HasPendingChanges() => _isDirty && IsEditableScriptFile(_currentFilePath);

        private void ScriptDebugForm_FormClosing(object? sender, FormClosingEventArgs e)
        {
            if (!ConfirmSaveIfDirty("关闭窗口"))
            {
                e.Cancel = true;
                return;
            }

            try
            {
                _testDebugUiTimer.Stop();
                _testDebugSession.Cancel();
            }
            catch
            {
            }

            ScriptDebugHook.Session = null!;
        }

        private async Task RunBusyAsync(Func<Task> action)
        {
            SetBusyState(false);
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.ToString(), "脚本调试执行失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                SetBusyState(true);
                _statusTextBox.Text = BuildStatusText();
                RenderDiagnostics(_currentDiagnostics);
                UpdateEditorState();
                UpdateTestInputState();
            }
        }

        private void SetBusyState(bool enabled)
        {
            _refreshButton.Enabled = enabled;
            _saveButton.Enabled = enabled && IsEditableScriptFile(_currentFilePath);
            _diagnoseCurrentButton.Enabled = enabled && IsEditableScriptFile(_currentFilePath);
            _diagnoseAllButton.Enabled = enabled;
            _reloadButton.Enabled = enabled;
            _copyDiagnosticsButton.Enabled = enabled;
            _openScriptsFolderButton.Enabled = enabled;
            _useRoslynPadCheckBox.Enabled = enabled && _roslynHost != null;
            _fileTreeView.Enabled = enabled;
            _editorTextBox.Enabled = enabled;
            if (_roslynElementHost != null)
            {
                _roslynElementHost.Enabled = enabled;
            }
            _testSearchTextBox.Enabled = enabled;
            _testKeyListBox.Enabled = enabled;
            _testPlayerComboBox.Enabled = enabled;
            _testExecuteButton.Enabled = enabled && _testKeyListBox.SelectedItem is TestEntry && _testPlayerComboBox.Items.Count > 0;
            _aiKindComboBox.Enabled = enabled;
            _aiTargetKeyTextBox.Enabled = enabled;
            _aiDescriptionTextBox.Enabled = enabled;
            _aiAdditionalRequirementsTextBox.Enabled = enabled;
            _aiIncludeCurrentScriptCheckBox.Enabled = enabled;
            _aiIncludeDiagnosticsCheckBox.Enabled = enabled;
            _aiIncludeReferenceScriptCheckBox.Enabled = enabled;
            _aiReferenceScriptTextBox.Enabled = enabled;
            _aiPickReferenceScriptButton.Enabled = enabled;
            _aiClearReferenceScriptButton.Enabled = enabled && !string.IsNullOrWhiteSpace(_aiReferenceScriptTextBox.Text);
            _aiBuildPromptButton.Enabled = enabled;
            _aiGenerateDraftButton.Enabled = enabled;
            _aiFixButton.Enabled = enabled && IsEditableScriptFile(_currentFilePath);
            _aiApplyDraftButton.Enabled = enabled && !string.IsNullOrWhiteSpace(_aiDraftTextBox.Text);
            _aiSaveDraftButton.Enabled = enabled && !string.IsNullOrWhiteSpace(_aiDraftTextBox.Text);
            _aiDiagnoseDraftButton.Enabled = enabled && !string.IsNullOrWhiteSpace(_aiDraftTextBox.Text);
            _aiReloadDraftButton.Enabled = enabled && !string.IsNullOrWhiteSpace(_aiDraftTextBox.Text);

            _historyRefreshButton.Enabled = enabled;
            _historyListView.Enabled = enabled;
            _historyPreviewTextBox.Enabled = enabled;
            _historyDiffTextBox.Enabled = enabled;
            _historyDiffButton.Enabled = enabled && _historyListView.SelectedItems.Count > 0;
            _historyRollbackToEditorButton.Enabled = enabled && _historyListView.SelectedItems.Count > 0;
            _historyRollbackReloadButton.Enabled = enabled && _historyListView.SelectedItems.Count > 0;

            _packageExportButton.Enabled = enabled;
            _packageApplyButton.Enabled = enabled;
            _packageOpenPackageFolderButton.Enabled = enabled;
            _packageReasonTextBox.Enabled = enabled;
            _packageLogTextBox.Enabled = enabled;
        }

        private void ReloadOnlinePlayers()
        {
            var selectedName = (_testPlayerComboBox.SelectedItem as OnlinePlayerEntry)?.Name ?? string.Empty;
            var players = GetOnlinePlayersSnapshot();
            _onlinePlayerCount = players.Count;

            _testPlayerComboBox.BeginUpdate();
            try
            {
                _testPlayerComboBox.Items.Clear();
                foreach (var player in players) _testPlayerComboBox.Items.Add(player);
            }
            finally
            {
                _testPlayerComboBox.EndUpdate();
            }

            if (_testPlayerComboBox.Items.Count == 0) return;
            _testPlayerComboBox.SelectedItem = _testPlayerComboBox.Items.Cast<OnlinePlayerEntry>().FirstOrDefault(item => string.Equals(item.Name, selectedName, StringComparison.OrdinalIgnoreCase)) ?? _testPlayerComboBox.Items[0];
        }

        private List<OnlinePlayerEntry> GetOnlinePlayersSnapshot()
        {
            try
            {
                if (!Envir.Running) return new List<OnlinePlayerEntry>();
                return Envir.InvokeOnMainThread(() => Envir.Players
                    .Where(player => player?.Info != null)
                    .OrderBy(player => player.Info.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(player => new OnlinePlayerEntry { Name = player.Info.Name, Level = player.Level, MapCode = player.CurrentMap?.Info?.FileName ?? string.Empty })
                    .ToList(), 5000) ?? new List<OnlinePlayerEntry>();
            }
            catch
            {
                return new List<OnlinePlayerEntry>();
            }
        }

        private void ReloadTestEntries()
        {
            var previousDisplay = (_testKeyListBox.SelectedItem as TestEntry)?.Display ?? string.Empty;
            _allTestEntries.Clear();

            var registry = Envir.CSharpScripts.CurrentRegistry;
            foreach (var key in registry.Handlers.Keys.OrderBy(item => item, StringComparer.Ordinal))
            {
                if (string.Equals(key, ScriptHookKeys.OnPlayerChatCommand, StringComparison.Ordinal))
                    _allTestEntries.Add(new TestEntry { Display = "[Player/Chat] hooks/player/chatcommand (通用)", Group = "Player/Chat", Key = key, Mode = TestMode.ChatGeneric });
                else if (key.StartsWith(ScriptHookKeys.OnPlayerChatCommand + "/", StringComparison.Ordinal))
                    _allTestEntries.Add(new TestEntry { Display = $"[Player/Chat] {key}", Group = "Player/Chat", Key = key, Mode = TestMode.ChatSpecific, Preset = key[(ScriptHookKeys.OnPlayerChatCommand.Length + 1)..] });
                else if (string.Equals(key, ScriptHookKeys.OnPlayerTrigger, StringComparison.Ordinal))
                    _allTestEntries.Add(new TestEntry { Display = "[Player] hooks/player/trigger", Group = "Player", Key = key, Mode = TestMode.Trigger });
                else if (string.Equals(key, ScriptHookKeys.OnPlayerMapEnter, StringComparison.Ordinal))
                    _allTestEntries.Add(new TestEntry { Display = "[Player] hooks/player/mapenter", Group = "Player", Key = key, Mode = TestMode.MapEnter });
                else if (string.Equals(key, ScriptHookKeys.OnPlayerMapCoord, StringComparison.Ordinal))
                    _allTestEntries.Add(new TestEntry { Display = "[Player] hooks/player/mapcoord", Group = "Player", Key = key, Mode = TestMode.MapCoord });
                else if (string.Equals(key, ScriptHookKeys.OnPlayerAcceptQuest, StringComparison.Ordinal))
                    _allTestEntries.Add(new TestEntry { Display = "[Player] hooks/player/acceptquest", Group = "Player", Key = key, Mode = TestMode.AcceptQuest });
                else if (string.Equals(key, ScriptHookKeys.OnPlayerFinishQuest, StringComparison.Ordinal))
                    _allTestEntries.Add(new TestEntry { Display = "[Player] hooks/player/finishquest", Group = "Player", Key = key, Mode = TestMode.FinishQuest });
                else if (string.Equals(key, ScriptHookKeys.OnPlayerDaily, StringComparison.Ordinal))
                    _allTestEntries.Add(new TestEntry { Display = "[Player] hooks/player/daily", Group = "Player", Key = key, Mode = TestMode.Daily });
                else if (string.Equals(key, ScriptHookKeys.OnClientEvent, StringComparison.Ordinal))
                    _allTestEntries.Add(new TestEntry { Display = "[Client] hooks/client/event", Group = "Client", Key = key, Mode = TestMode.ClientEvent });
            }

            if (registry.Handlers.ContainsKey(ScriptHookKeys.OnPlayerCustomCommand))
            {
                var commands = registry.CustomCommands.OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToArray();
                if (commands.Length == 0)
                    _allTestEntries.Add(new TestEntry { Display = "[Player] hooks/player/customcommand", Group = "Player", Key = ScriptHookKeys.OnPlayerCustomCommand, Mode = TestMode.CustomCommand });
                else
                    foreach (var command in commands)
                        _allTestEntries.Add(new TestEntry { Display = $"[Player/Custom] {command}", Group = "Player/Custom", Key = ScriptHookKeys.OnPlayerCustomCommand, Mode = TestMode.CustomCommand, Preset = command });
            }

            ApplyTestEntryFilter(previousDisplay);
        }

        private void ApplyTestEntryFilter(string preferredDisplay = "")
        {
            var search = _testSearchTextBox.Text.Trim();
            var filtered = _allTestEntries.Where(entry =>
                string.IsNullOrWhiteSpace(search) ||
                entry.Display.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                entry.Key.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                entry.Group.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                entry.Preset.Contains(search, StringComparison.OrdinalIgnoreCase)).OrderBy(entry => entry.Display, StringComparer.OrdinalIgnoreCase).ToArray();

            _testKeyListBox.BeginUpdate();
            try
            {
                _testKeyListBox.Items.Clear();
                foreach (var entry in filtered) _testKeyListBox.Items.Add(entry);
            }
            finally
            {
                _testKeyListBox.EndUpdate();
            }

            if (_testKeyListBox.Items.Count == 0)
            {
                _testKeyTextBox.Text = string.Empty;
                _testDescriptionTextBox.Text = "当前筛选结果为空。";
                UpdateTestInputState();
                return;
            }

            _testKeyListBox.SelectedItem = _testKeyListBox.Items.Cast<TestEntry>().FirstOrDefault(entry => string.Equals(entry.Display, preferredDisplay, StringComparison.Ordinal)) ?? _testKeyListBox.Items[0];
        }

        private void UpdateSelectedTestEntry()
        {
            if (_testKeyListBox.SelectedItem is not TestEntry entry)
            {
                _testKeyTextBox.Text = string.Empty;
                _testDescriptionTextBox.Text = "当前未选择可执行项。";
                UpdateTestInputState();
                return;
            }

            _testKeyTextBox.Text = entry.Key;
            _testDescriptionTextBox.Text = BuildTestEntryDescription(entry);

            if (!string.IsNullOrWhiteSpace(entry.Preset))
            {
                if (entry.Mode == TestMode.ChatSpecific && string.IsNullOrWhiteSpace(_testCommandLineTextBox.Text))
                    _testCommandLineTextBox.Text = entry.Preset;
                if (entry.Mode == TestMode.CustomCommand && string.IsNullOrWhiteSpace(_testCustomCommandTextBox.Text))
                    _testCustomCommandTextBox.Text = entry.Preset;
            }

            UpdateTestInputState();
        }

        private string BuildTestEntryDescription(TestEntry entry)
        {
            return entry.Mode switch
            {
                TestMode.ChatSpecific => $"真实调用聊天命令 Hook。预设命令: {entry.Preset}。命令行输入框填写不带 @ 的命令文本。",
                TestMode.ChatGeneric => "调用通用聊天命令 Hook。命令行格式为“命令 参数1 参数2”。",
                TestMode.CustomCommand => $"调用自定义命令 Hook。若脚本注册了命令，默认值会自动带出：{entry.Preset}",
                TestMode.Trigger => "调用 Player Trigger Hook。请输入 TriggerKey。",
                TestMode.MapEnter => "调用 Player MapEnter Hook。请输入地图文件名。",
                TestMode.MapCoord => "调用 Player MapCoord Hook。请输入地图文件名与坐标。",
                TestMode.AcceptQuest => "调用 Player AcceptQuest Hook。请输入 QuestIndex。",
                TestMode.FinishQuest => "调用 Player FinishQuest Hook。请输入 QuestIndex。",
                TestMode.Daily => "调用 Player Daily Hook。无需额外参数。",
                TestMode.ClientEvent => "调用 ClientEvent Hook。当前将字符串作为 payload 透传。",
                _ => string.Empty,
            };
        }

        private void UpdateTestDebugUiFromSession(bool forceRefreshBreakpoints)
        {
            var enabled = _testDebugEnabledCheckBox.Checked;

            _testDebugRebuildButton.Enabled = enabled;
            _testDebugPauseButton.Enabled = enabled;
            _testDebugContinueButton.Enabled = enabled;
            _testDebugStepButton.Enabled = enabled;
            _testDebugCancelButton.Enabled = enabled;

            _testDebugBreakpointFileTextBox.Enabled = enabled;
            _testDebugBreakpointLineNumeric.Enabled = enabled;
            _testDebugAddBreakpointButton.Enabled = enabled;
            _testDebugRemoveBreakpointButton.Enabled = enabled;
            _testDebugClearBreakpointsButton.Enabled = enabled;
            _testDebugBreakpointsListView.Enabled = enabled;

            _testDebugWatchExpressionTextBox.Enabled = enabled;
            _testDebugEvalExpressionButton.Enabled = enabled;

            if (!enabled)
            {
                _testDebugStateLabel.Text = "未启用（仅测试台插桩，断点暂停会阻塞主逻辑线程）";
                _testDebugLocationLabel.Text = "位置：-";
                return;
            }

            var paused = _testDebugSession.IsPaused;
            var mode = _testDebugSession.RunMode;
            var reason = _testDebugSession.PauseReason;
            var location = _testDebugSession.CurrentLocation;

            if (paused)
            {
                _testDebugStateLabel.Text = $"已暂停（{reason}），模式={mode}（提示：主线程已暂停）";
            }
            else
            {
                _testDebugStateLabel.Text = $"运行中，模式={mode}";
            }

            if (location.IsValid)
            {
                var fileName = Path.GetFileName(location.FilePath);
                _testDebugLocationLabel.Text = location.Column > 0
                    ? $"位置：{fileName}:{location.Line}:{location.Column}"
                    : $"位置：{fileName}:{location.Line}";
            }
            else
            {
                _testDebugLocationLabel.Text = "位置：-";
            }

            if (forceRefreshBreakpoints)
            {
                RefreshTestDebugBreakpointsList();
            }

            if (paused)
            {
                var seq = _testDebugSession.PauseSequence;
                if (seq != _lastObservedPauseSequence)
                {
                    _lastObservedPauseSequence = seq;
                    TryJumpToTestDebugLocation(location);
                }
            }
        }

        private void RefreshTestDebugBreakpointsList()
        {
            var breakpoints = _testDebugSession.GetBreakpointsSnapshot();

            _testDebugBreakpointsListView.BeginUpdate();
            try
            {
                _testDebugBreakpointsListView.Items.Clear();

                for (var i = 0; i < breakpoints.Length; i++)
                {
                    var bp = breakpoints[i];

                    var fileText = bp.FilePath;
                    try
                    {
                        var root = ScriptsRootPath;
                        if (!string.IsNullOrWhiteSpace(root) && bp.FilePath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                        {
                            fileText = Path.GetRelativePath(root, bp.FilePath).Replace('\\', '/');
                        }
                    }
                    catch
                    {
                    }

                    var item = new ListViewItem(fileText);
                    item.SubItems.Add(bp.Line.ToString());
                    item.Tag = bp;
                    _testDebugBreakpointsListView.Items.Add(item);
                }
            }
            finally
            {
                _testDebugBreakpointsListView.EndUpdate();
            }
        }

        private void AddTestDebugBreakpoint()
        {
            var filePath = _testDebugBreakpointFileTextBox.Text.Trim();
            var line = Decimal.ToInt32(_testDebugBreakpointLineNumeric.Value);

            if (string.IsNullOrWhiteSpace(filePath))
            {
                MessageBox.Show(this, "请先填写断点 File（可直接使用当前文件路径）。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (line <= 0)
            {
                MessageBox.Show(this, "断点行号必须大于 0。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            _testDebugSession.AddBreakpoint(filePath, line);
            RefreshTestDebugBreakpointsList();
        }

        private void RemoveSelectedTestDebugBreakpoint()
        {
            if (_testDebugBreakpointsListView.SelectedItems.Count == 0) return;
            if (_testDebugBreakpointsListView.SelectedItems[0].Tag is not ScriptDebugBreakpoint bp) return;

            _testDebugSession.RemoveBreakpoint(bp.FilePath, bp.Line);
            RefreshTestDebugBreakpointsList();
        }

        private void ClearAllTestDebugBreakpoints()
        {
            _testDebugSession.ClearBreakpoints();
            RefreshTestDebugBreakpointsList();
        }

        private void JumpToSelectedTestDebugBreakpoint()
        {
            if (_testDebugBreakpointsListView.SelectedItems.Count == 0) return;
            if (_testDebugBreakpointsListView.SelectedItems[0].Tag is not ScriptDebugBreakpoint bp) return;

            if (!TrySelectFileNode(bp.FilePath))
            {
                MessageBox.Show(this, $"无法在脚本树中找到文件：{bp.FilePath}", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            GoToLineColumn(bp.Line, 1);
        }

        private void TryJumpToTestDebugLocation(ScriptDebugLocation location)
        {
            if (!location.IsValid) return;

            if (HasPendingChanges() && !string.Equals(_currentFilePath, location.FilePath, StringComparison.OrdinalIgnoreCase))
                return;

            if (!TrySelectFileNode(location.FilePath))
                return;

            GoToLineColumn(location.Line, location.Column);
        }

        private async Task RebuildTestDebugRuntimeAsync()
        {
            if (!_testDebugEnabledCheckBox.Checked)
            {
                MessageBox.Show(this, "请先勾选“插桩调试（断点/单步）”。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            await RunBusyAsync(async () =>
            {
                _testDebugStateLabel.Text = "正在编译插桩…";
                _testDebugLocationLabel.Text = "位置：-";

                await Task.Run(() => _testDebugRuntime.ReloadInstrumented(ScriptsRootPath));

                if (!string.IsNullOrWhiteSpace(_testDebugRuntime.LastError))
                {
                    _testDebugStateLabel.Text = "插桩未就绪：编译/加载失败（详见变量窗口输出）";
                    _testDebugWatchResultTextBox.Text = BuildTestDebugRuntimeFailureText(_testDebugRuntime.LastError, _testDebugRuntime.LastCompileResult);
                    return;
                }

                _testDebugStateLabel.Text = $"插桩就绪：handlers={_testDebugRuntime.Registry.Count}，Elapsed={_testDebugRuntime.LastCompileResult.ElapsedMilliseconds}ms";
            });
        }

        private static string BuildTestDebugRuntimeFailureText(string error, ScriptCompileResult compileResult)
        {
            var sb = new StringBuilder();
            sb.AppendLine("[FAIL] 插桩编译/加载失败");
            if (!string.IsNullOrWhiteSpace(error)) sb.AppendLine(error.Trim());

            if (compileResult.Diagnostics != null && compileResult.Diagnostics.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Diagnostics:");

                foreach (var d in compileResult.Diagnostics.Where(d => string.Equals(d.Severity, "Error", StringComparison.OrdinalIgnoreCase)).Take(50))
                {
                    sb.AppendLine(d.ToString());
                }
            }

            return sb.ToString();
        }

        private sealed class TestDebugGlobals
        {
            public Envir envir { get; init; } = Envir.Main;
            public ScriptContext context { get; init; } = new ScriptContext();
            public ScriptApi api => context.Api;
            public PlayerObject? player { get; init; }
            public string executionId { get; init; } = string.Empty;
        }

        private async Task EvaluateTestDebugExpressionAsync()
        {
            if (!_testDebugEnabledCheckBox.Checked)
            {
                MessageBox.Show(this, "请先勾选“插桩调试（断点/单步）”。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var expr = _testDebugWatchExpressionTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(expr))
            {
                MessageBox.Show(this, "请输入要计算的表达式。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (!System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported)
            {
                MessageBox.Show(this, "当前运行环境不支持动态代码（可能为 NativeAOT/受限运行时），无法进行表达式求值。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var globals = new TestDebugGlobals
            {
                envir = Envir.Main,
                context = _testDebugRuntime.Context,
                player = _testDebugLastPlayer,
                executionId = _testDebugLastExecutionId,
            };

            try
            {
                _testDebugWatchResultTextBox.Text = "正在求值…";
                var result = await Task.Run(() => ScriptDebugExpressionEvaluator.Evaluate(expr, globals));
                _testDebugWatchResultTextBox.Text = result;
            }
            catch (Exception ex)
            {
                _testDebugWatchResultTextBox.Text = "求值失败：" + ex;
            }
        }

        private void UpdateTestInputState()
        {
            var entry = _testKeyListBox.SelectedItem as TestEntry;
            var hasEntry = entry != null;
            _testCommandLineTextBox.Enabled = hasEntry && (entry.Mode == TestMode.ChatSpecific || entry.Mode == TestMode.ChatGeneric);
            _testCustomCommandTextBox.Enabled = hasEntry && entry.Mode == TestMode.CustomCommand;
            _testTriggerTextBox.Enabled = hasEntry && entry.Mode == TestMode.Trigger;
            _testMapFileTextBox.Enabled = hasEntry && (entry.Mode == TestMode.MapEnter || entry.Mode == TestMode.MapCoord);
            _testCoordXNumeric.Enabled = hasEntry && entry.Mode == TestMode.MapCoord;
            _testCoordYNumeric.Enabled = hasEntry && entry.Mode == TestMode.MapCoord;
            _testQuestIndexNumeric.Enabled = hasEntry && (entry.Mode == TestMode.AcceptQuest || entry.Mode == TestMode.FinishQuest);
            _testClientPayloadTextBox.Enabled = hasEntry && entry.Mode == TestMode.ClientEvent;
            _testExecuteButton.Enabled = hasEntry && _testPlayerComboBox.Items.Count > 0;
        }

        private async Task ExecuteSelectedTestAsync()
        {
            if (_testKeyListBox.SelectedItem is not TestEntry entry)
            {
                MessageBox.Show(this, "请先选择一个可执行 Key。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (_testPlayerComboBox.SelectedItem is not OnlinePlayerEntry player)
            {
                MessageBox.Show(this, "请先选择一个在线玩家。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (!Envir.Running)
            {
                MessageBox.Show(this, "测试台仅支持服务器运行中执行。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (!ValidateTestInput(entry, out var error))
            {
                MessageBox.Show(this, error, "输入无效", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            await RunBusyAsync(async () =>
            {
                var executionId = Guid.NewGuid().ToString("N")[..8];

                var request = new TestBenchRequest
                {
                    DebugEnabled = _testDebugEnabledCheckBox.Checked,
                    CommandLine = _testCommandLineTextBox.Text,
                    CustomCommand = _testCustomCommandTextBox.Text,
                    TriggerKey = _testTriggerTextBox.Text,
                    MapFileName = _testMapFileTextBox.Text,
                    CoordX = Decimal.ToInt32(_testCoordXNumeric.Value),
                    CoordY = Decimal.ToInt32(_testCoordYNumeric.Value),
                    QuestIndex = Decimal.ToInt32(_testQuestIndexNumeric.Value),
                    ClientPayload = _testClientPayloadTextBox.Text,
                };

                if (request.DebugEnabled)
                {
                    var desiredMode = _testDebugSession.RunMode;
                    _testDebugSession.ResetForNewRun();
                    if (desiredMode == ScriptDebugRunMode.Step)
                    {
                        _testDebugSession.StepOnce();
                    }
                }

                var beforeLogs = MessageQueue.Instance.MessageLog.ToArray();
                var stopwatch = Stopwatch.StartNew();
                var result = await Task.Run(() => ExecuteSelectedTestInternal(entry, player.Name, executionId, request));
                stopwatch.Stop();
                var afterLogs = MessageQueue.Instance.MessageLog.ToArray();
                _lastTestSummary = $"ExecutionId={executionId}，{result}，耗时 {stopwatch.ElapsedMilliseconds}ms";
                _testResultTextBox.Text = BuildTestResultText(executionId, result, stopwatch.ElapsedMilliseconds, ExtractLogsForExecution(beforeLogs, afterLogs, executionId));
            });
        }

        private bool ValidateTestInput(TestEntry entry, out string error)
        {
            error = string.Empty;
            switch (entry.Mode)
            {
                case TestMode.ChatSpecific:
                case TestMode.ChatGeneric:
                    if (string.IsNullOrWhiteSpace(_testCommandLineTextBox.Text) && string.IsNullOrWhiteSpace(entry.Preset)) error = "聊天命令不能为空。";
                    break;
                case TestMode.CustomCommand:
                    if (string.IsNullOrWhiteSpace(_testCustomCommandTextBox.Text) && string.IsNullOrWhiteSpace(entry.Preset)) error = "自定义命令不能为空。";
                    break;
                case TestMode.Trigger:
                    if (string.IsNullOrWhiteSpace(_testTriggerTextBox.Text)) error = "TriggerKey 不能为空。";
                    break;
                case TestMode.MapEnter:
                case TestMode.MapCoord:
                    if (string.IsNullOrWhiteSpace(_testMapFileTextBox.Text)) error = "地图文件不能为空。";
                    break;
            }
            return string.IsNullOrWhiteSpace(error);
        }

        private string ExecuteSelectedTestInternal(TestEntry entry, string playerName, string executionId, TestBenchRequest request)
        {
            try
            {
                if (request.DebugEnabled && (!_testDebugRuntime.LastCompileResult.Success || !string.IsNullOrWhiteSpace(_testDebugRuntime.LastError)))
                {
                    _testDebugRuntime.ReloadInstrumented(ScriptsRootPath);
                }

                if (request.DebugEnabled && !string.IsNullOrWhiteSpace(_testDebugRuntime.LastError))
                {
                    return "测试失败：插桩未就绪。请点击“重建插桩”并查看失败信息。";
                }

                var timeoutMs = request.DebugEnabled ? Timeout.Infinite : 5000;

                return Envir.InvokeOnMainThread(() =>
                {
                    MessageQueue.Instance.Enqueue(BuildTestBenchMarker(executionId, "BEGIN", entry, playerName));
                    var player = Envir.Players.FirstOrDefault(item => item?.Info != null && string.Equals(item.Info.Name, playerName, StringComparison.OrdinalIgnoreCase));
                    if (player == null)
                    {
                        MessageQueue.Instance.Enqueue(BuildTestBenchMarker(executionId, "END", entry, playerName, "player-not-found"));
                        return $"测试失败：在线玩家 {playerName} 不存在或已下线。";
                    }

                    if (request.DebugEnabled)
                    {
                        _testDebugLastPlayer = player;
                        _testDebugLastExecutionId = executionId;
                    }

                    var result = entry.Mode switch
                    {
                        TestMode.ChatSpecific or TestMode.ChatGeneric => ExecuteChatCommand(player, entry, request),
                        TestMode.CustomCommand => ExecuteCustomCommand(player, entry, request),
                        TestMode.Trigger => ExecuteTrigger(player, request),
                        TestMode.MapEnter => ExecuteMapEnter(player, request),
                        TestMode.MapCoord => ExecuteMapCoord(player, request),
                        TestMode.AcceptQuest => ExecuteAcceptQuest(player, request),
                        TestMode.FinishQuest => ExecuteFinishQuest(player, request),
                        TestMode.Daily => ExecuteDaily(player, request),
                        TestMode.ClientEvent => ExecuteClientEvent(player, request),
                        _ => "不支持的测试模式。",
                    };
                    MessageQueue.Instance.Enqueue(BuildTestBenchMarker(executionId, "END", entry, playerName, "completed"));
                    return result;
                }, timeoutMs) ?? "测试失败：主线程执行超时。";
            }
            catch (Exception ex)
            {
                MessageQueue.Instance.Enqueue($"[Scripts][TestBench:{executionId}] END detail=exception");
                return "测试执行异常: " + ex;
            }
        }

        private string ExecuteChatCommand(PlayerObject player, TestEntry entry, TestBenchRequest request)
        {
            var commandLine = request.CommandLine?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(commandLine) && !string.IsNullOrWhiteSpace(entry.Preset))
                commandLine = entry.Preset;
            if (commandLine.StartsWith("@", StringComparison.Ordinal)) commandLine = commandLine[1..];
            var parts = commandLine.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var command = parts.Length > 0 ? parts[0] : string.Empty;
            var args = parts.Length > 1 ? parts.Skip(1).ToArray() : Array.Empty<string>();

            bool handled;
            if (request.DebugEnabled)
            {
                handled = ExecuteChatCommandInstrumented(player, commandLine, command, args);
            }
            else
            {
                handled = Envir.CSharpScripts.TryHandlePlayerChatCommand(player, commandLine, command, args);
            }

            var resolvedKey = string.IsNullOrWhiteSpace(command) ? ScriptHookKeys.OnPlayerChatCommand : ScriptHookKeys.OnPlayerChatCommandName(command);
            return $"ChatCommand: handled={handled}, resolvedKey={resolvedKey}, commandLine={commandLine}";
        }

        private bool ExecuteChatCommandInstrumented(PlayerObject player, string commandLine, string command, IReadOnlyList<string> args)
        {
            commandLine ??= string.Empty;
            command ??= string.Empty;
            args ??= Array.Empty<string>();

            if (command.Length > 0)
            {
                if (_testDebugRuntime.TryInvoke<OnPlayerChatCommandHook>(ScriptHookKeys.OnPlayerChatCommandName(command), h =>
                        h(_testDebugRuntime.Context, player, commandLine, command, args)))
                {
                    return true;
                }
            }

            return _testDebugRuntime.TryInvoke<OnPlayerChatCommandHook>(ScriptHookKeys.OnPlayerChatCommand, h =>
                h(_testDebugRuntime.Context, player, commandLine, command, args));
        }

        private string ExecuteCustomCommand(PlayerObject player, TestEntry entry, TestBenchRequest request)
        {
            var command = string.IsNullOrWhiteSpace(request.CustomCommand) ? entry.Preset : request.CustomCommand.Trim();

            bool handled;
            if (request.DebugEnabled)
            {
                handled = _testDebugRuntime.TryInvoke<OnPlayerCustomCommandHook>(ScriptHookKeys.OnPlayerCustomCommand, h =>
                    h(_testDebugRuntime.Context, player, command));
            }
            else
            {
                handled = Envir.CSharpScripts.TryHandlePlayerCustomCommand(player, command);
            }

            return $"CustomCommand: handled={handled}, command={command}";
        }

        private string ExecuteTrigger(PlayerObject player, TestBenchRequest request)
        {
            var key = request.TriggerKey?.Trim() ?? string.Empty;

            bool handled;
            if (request.DebugEnabled)
            {
                handled = _testDebugRuntime.TryInvoke<OnPlayerTriggerHook>(ScriptHookKeys.OnPlayerTrigger, h =>
                    h(_testDebugRuntime.Context, player, key));
            }
            else
            {
                handled = Envir.CSharpScripts.TryHandlePlayerTrigger(player, key);
            }

            return $"Trigger: handled={handled}, key={key}";
        }

        private string ExecuteMapEnter(PlayerObject player, TestBenchRequest request)
        {
            var mapFileName = request.MapFileName?.Trim() ?? string.Empty;

            bool handled;
            if (request.DebugEnabled)
            {
                handled = _testDebugRuntime.TryInvoke<OnPlayerMapEnterHook>(ScriptHookKeys.OnPlayerMapEnter, h =>
                    h(_testDebugRuntime.Context, player, mapFileName));
            }
            else
            {
                handled = Envir.CSharpScripts.TryHandlePlayerMapEnter(player, mapFileName);
            }

            return $"MapEnter: handled={handled}, map={mapFileName}";
        }

        private string ExecuteMapCoord(PlayerObject player, TestBenchRequest request)
        {
            var mapFileName = request.MapFileName?.Trim() ?? string.Empty;
            var x = request.CoordX;
            var y = request.CoordY;

            bool handled;
            if (request.DebugEnabled)
            {
                handled = _testDebugRuntime.TryInvoke<OnPlayerMapCoordHook>(ScriptHookKeys.OnPlayerMapCoord, h =>
                    h(_testDebugRuntime.Context, player, mapFileName, x, y));
            }
            else
            {
                handled = Envir.CSharpScripts.TryHandlePlayerMapCoord(player, mapFileName, x, y);
            }

            return $"MapCoord: handled={handled}, map={mapFileName}, coord=({x},{y})";
        }

        private string ExecuteAcceptQuest(PlayerObject player, TestBenchRequest request)
        {
            var questIndex = request.QuestIndex;

            bool handled;
            if (request.DebugEnabled)
            {
                handled = _testDebugRuntime.TryInvoke<OnPlayerAcceptQuestHook>(ScriptHookKeys.OnPlayerAcceptQuest, h =>
                    h(_testDebugRuntime.Context, player, questIndex));
            }
            else
            {
                handled = Envir.CSharpScripts.TryHandlePlayerAcceptQuest(player, questIndex);
            }

            return $"AcceptQuest: handled={handled}, questIndex={questIndex}";
        }

        private string ExecuteFinishQuest(PlayerObject player, TestBenchRequest request)
        {
            var questIndex = request.QuestIndex;

            bool handled;
            if (request.DebugEnabled)
            {
                handled = _testDebugRuntime.TryInvoke<OnPlayerFinishQuestHook>(ScriptHookKeys.OnPlayerFinishQuest, h =>
                    h(_testDebugRuntime.Context, player, questIndex));
            }
            else
            {
                handled = Envir.CSharpScripts.TryHandlePlayerFinishQuest(player, questIndex);
            }

            return $"FinishQuest: handled={handled}, questIndex={questIndex}";
        }

        private string ExecuteDaily(PlayerObject player, TestBenchRequest request)
        {
            bool handled;
            if (request.DebugEnabled)
            {
                handled = _testDebugRuntime.TryInvoke<OnPlayerDailyHook>(ScriptHookKeys.OnPlayerDaily, h =>
                    h(_testDebugRuntime.Context, player));
            }
            else
            {
                handled = Envir.CSharpScripts.TryHandlePlayerDaily(player);
            }

            return $"Daily: handled={handled}";
        }

        private string ExecuteClientEvent(PlayerObject player, TestBenchRequest request)
        {
            var payload = request.ClientPayload ?? string.Empty;

            bool handled;
            if (request.DebugEnabled)
            {
                handled = _testDebugRuntime.TryInvoke<OnClientEventHook>(ScriptHookKeys.OnClientEvent, h =>
                    h(_testDebugRuntime.Context, player, payload));
            }
            else
            {
                handled = Envir.CSharpScripts.TryHandleClientEvent(player, payload);
            }

            return $"ClientEvent: handled={handled}, payload={payload}";
        }

        private static string[] ExtractLogsForExecution(string[] beforeLogs, string[] afterLogs, string executionId)
        {
            var appended = afterLogs.Length > beforeLogs.Length ? afterLogs.Skip(beforeLogs.Length).ToArray() : afterLogs;
            var beginMarker = $"[Scripts][TestBench:{executionId}] BEGIN";
            var endMarker = $"[Scripts][TestBench:{executionId}] END";

            var beginIndex = Array.FindIndex(appended, item => item.Contains(beginMarker, StringComparison.Ordinal));
            var endIndex = Array.FindLastIndex(appended, item => item.Contains(endMarker, StringComparison.Ordinal));

            if (beginIndex >= 0 && endIndex >= beginIndex)
            {
                return appended
                    .Skip(beginIndex)
                    .Take(endIndex - beginIndex + 1)
                    .ToArray();
            }

            return appended
                .Where(item => item.Contains($"[TestBench:{executionId}]", StringComparison.Ordinal) || item.Contains("[Scripts]", StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        private static string BuildTestResultText(string executionId, string result, long elapsedMilliseconds, IReadOnlyList<string> logs)
        {
            var sb = new StringBuilder();
            sb.AppendLine("测试执行结果");
            sb.AppendLine($"ExecutionId: {executionId}");
            sb.AppendLine(result);
            sb.AppendLine($"Elapsed: {elapsedMilliseconds}ms");
            sb.AppendLine();
            sb.AppendLine("新增日志");
            if (logs.Count == 0) sb.AppendLine("(无新增日志)");
            else foreach (var log in logs) sb.AppendLine(log.TrimEnd());
            return sb.ToString();
        }

        private static string BuildTestBenchMarker(string executionId, string phase, TestEntry entry, string playerName, string detail = "")
        {
            var suffix = string.IsNullOrWhiteSpace(detail) ? string.Empty : $" detail={detail}";
            return $"[Scripts][TestBench:{executionId}] {phase} key={entry.Key} mode={entry.Mode} player={playerName}{suffix}";
        }

        private void BuildAiPrompt()
        {
            var request = BuildAiRequest();
            if (!ValidateAiRequestForUi(request, out var error))
            {
                MessageBox.Show(this, error, "AI 输入无效", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            var prompt = _scriptGenerationService.BuildPrompt(request);
            _aiPromptTextBox.Text = prompt;
            _lastAiSummary = $"已构建 Prompt，Key={request.TargetKey}，Type={request.Kind}";
            _aiResultTextBox.Text = "Prompt 构建完成。";
        }

        private async Task GenerateAiDraftAsync()
        {
            var request = BuildAiRequest();
            if (!ValidateAiRequestForUi(request, out var error))
            {
                MessageBox.Show(this, error, "AI 输入无效", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            await RunBusyAsync(async () =>
            {
                var draft = await _scriptGenerationService.GenerateDraftAsync(request);
                _currentAiDraft = draft;
                _aiPromptTextBox.Text = draft.Prompt;
                _aiDraftTextBox.Text = draft.GeneratedCode;
                _aiResultTextBox.Text = BuildAiResultText(draft);
                _lastAiSummary = $"AI 草稿已生成，Provider={draft.ProviderName}，Model={draft.ModelName}，Path={draft.SuggestedRelativePath}";
                TryWriteAiAudit(request, draft);
            });
        }

        private async Task FixCurrentScriptAsync()
        {
            if (!IsEditableScriptFile(_currentFilePath))
            {
                MessageBox.Show(this, "请先选择一个 .cs 脚本文件。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (!ConfirmSaveIfDirty("一键修复")) return;

            ScriptDraft? generatedDraft = null;

            await RunBusyAsync(async () =>
            {
                var compileResult = await Task.Run(() => _diagnosticCompiler.CompileFromFiles(
                    ScriptsRootPath,
                    new[] { _currentFilePath },
                    $"LomScripts_Fix_{Environment.TickCount64}",
                    debugBuild: true));

                ApplyDiagnosticResult(compileResult, $"编译检查（用于修复）: {Path.GetFileName(_currentFilePath)}");

                var hasErrors = compileResult.Diagnostics.Any(d => string.Equals(d.Severity, "Error", StringComparison.OrdinalIgnoreCase));
                if (!hasErrors)
                {
                    _aiResultTextBox.Text = "当前脚本没有编译错误，不需要修复。";
                    _lastAiSummary = "一键修复：无编译错误，跳过生成。";
                    return;
                }

                var kind = _aiKindComboBox.SelectedItem is ScriptGenerationKind selectedKind
                    ? selectedKind
                    : ScriptGenerationKind.GenericModule;

                var targetKey = _aiTargetKeyTextBox.Text.Trim();
                if (string.IsNullOrWhiteSpace(targetKey))
                {
                    var relativePath = Path.GetRelativePath(ScriptsRootPath, _currentFilePath);
                    var withoutExtension = Path.ChangeExtension(relativePath, null) ?? relativePath;
                    targetKey = withoutExtension
                        .Replace(Path.DirectorySeparatorChar, '/')
                        .Replace('\\', '/')
                        .TrimStart('/');

                    _aiTargetKeyTextBox.Text = targetKey;
                }

                var description = _aiDescriptionTextBox.Text.Trim();
                if (string.IsNullOrWhiteSpace(description))
                {
                    const string defaultDescription = "请修复该脚本的编译错误，使其通过编译，并尽量保持原有逻辑不变。";
                    description = defaultDescription;
                    _aiDescriptionTextBox.Text = defaultDescription;
                }

                var request = new ScriptGenerationRequest
                {
                    Kind = kind,
                    TargetKey = targetKey,
                    NaturalLanguageDescription = description,
                    AdditionalRequirements = _aiAdditionalRequirementsTextBox.Text.Trim(),
                    ExistingScriptContent = GetCurrentEditorText(),
                    Diagnostics = compileResult.Diagnostics,
                };

                if (!ValidateAiRequest(request, out var error))
                {
                    _aiResultTextBox.Text = error;
                    _lastAiSummary = "一键修复：AI 输入无效。";
                    return;
                }

                generatedDraft = await _scriptGenerationService.GenerateDraftAsync(request);
                _currentAiDraft = generatedDraft;
                _aiPromptTextBox.Text = generatedDraft.Prompt;
                _aiDraftTextBox.Text = generatedDraft.GeneratedCode;
                _aiResultTextBox.Text = BuildAiResultText(generatedDraft);
                _lastAiSummary = $"一键修复草稿已生成，Provider={generatedDraft.ProviderName}，Model={generatedDraft.ModelName}，Path={generatedDraft.SuggestedRelativePath}";
                TryWriteAiAudit(request, generatedDraft);
            });

            if (generatedDraft != null && !string.IsNullOrWhiteSpace(_aiDraftTextBox.Text))
            {
                var choice = MessageBox.Show(this, "修复草稿已生成，是否立即“保存并热更”？", "一键修复", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (choice == DialogResult.Yes)
                {
                    await ReloadAiDraftAsync();
                }
            }
        }

        private void ApplyAiDraftToEditor()
        {
            if (_currentAiDraft == null || string.IsNullOrWhiteSpace(_aiDraftTextBox.Text))
            {
                MessageBox.Show(this, "当前没有可应用的 AI 草稿。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var relativePath = ResolveAiDraftRelativePath(_currentAiDraft);
            var fullPath = Path.Combine(ScriptsRootPath, relativePath);
            LoadDraftIntoEditor(fullPath, _aiDraftTextBox.Text);
            _lastAiSummary = $"已将 AI 草稿应用到编辑器：{relativePath}";
            _statusTextBox.Text = BuildStatusText();
        }

        private void SaveAiDraftToScriptsRoot()
        {
            if (_currentAiDraft == null || string.IsNullOrWhiteSpace(_aiDraftTextBox.Text))
            {
                MessageBox.Show(this, "当前没有可保存的 AI 草稿。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (!EnsureAiScriptSafeToPersist(_aiDraftTextBox.Text, out var guardMessage))
            {
                MessageBox.Show(this, guardMessage, "AI 草稿安全检查未通过", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                var relativePath = ResolveAiDraftRelativePath(_currentAiDraft);
                var fullPath = Path.Combine(ScriptsRootPath, relativePath);
                var directory = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);

                if (File.Exists(fullPath) && !TryCreateHistorySnapshot(fullPath, "AiDraftOverwrite", fromVersionId: string.Empty, out var historyError))
                {
                    MessageBox.Show(this, historyError, "创建历史快照失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                File.WriteAllText(fullPath, _aiDraftTextBox.Text, new UTF8Encoding(false));
                ReloadFileTree();
                TrySelectFileNode(fullPath);
                _lastAiSummary = $"AI 草稿已保存到脚本目录：{relativePath}";
                _aiResultTextBox.Text = $"已保存：{relativePath}{Environment.NewLine}{BuildAiResultText(_currentAiDraft)}";
                _statusTextBox.Text = BuildStatusText();
                ReloadHistoryEntries();
                UpdateHistoryState();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.ToString(), "保存 AI 草稿失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async Task DiagnoseAiDraftAsync()
        {
            if (!EnsureAiDraftReady())
                return;

            SaveAiDraftToScriptsRoot();
            if (string.IsNullOrWhiteSpace(_currentFilePath))
                return;

            await DiagnoseCurrentFileAsync();
        }

        private async Task ReloadAiDraftAsync()
        {
            if (!EnsureAiDraftReady())
                return;

            SaveAiDraftToScriptsRoot();
            if (string.IsNullOrWhiteSpace(_currentFilePath))
                return;

            await ReloadAsync(true);
        }

        private ScriptGenerationRequest BuildAiRequest()
        {
            var kind = _aiKindComboBox.SelectedItem is ScriptGenerationKind selectedKind
                ? selectedKind
                : ScriptGenerationKind.GenericModule;

            var existingScriptSections = new List<string>();
            if (_aiIncludeCurrentScriptCheckBox.Checked && IsEditableScriptFile(_currentFilePath) && File.Exists(_currentFilePath))
                existingScriptSections.Add(BuildExistingScriptSection("当前脚本", ToDisplayPath(_currentFilePath), GetCurrentEditorText()));

            if (_aiIncludeReferenceScriptCheckBox.Checked && TryResolveAiReferenceScript(out var referenceFullPath, out _))
                existingScriptSections.Add(BuildExistingScriptSection("参考脚本", ToDisplayPath(referenceFullPath), ReadAllText(referenceFullPath, out _)));

            return new ScriptGenerationRequest
            {
                Kind = kind,
                TargetKey = _aiTargetKeyTextBox.Text.Trim(),
                NaturalLanguageDescription = _aiDescriptionTextBox.Text.Trim(),
                AdditionalRequirements = _aiAdditionalRequirementsTextBox.Text.Trim(),
                ExistingScriptContent = string.Join(Environment.NewLine + Environment.NewLine, existingScriptSections),
                Diagnostics = _aiIncludeDiagnosticsCheckBox.Checked ? _currentDiagnostics : Array.Empty<ScriptDiagnostic>(),
            };
        }

        private bool ValidateAiRequestForUi(ScriptGenerationRequest request, out string error)
        {
            if (!ValidateAiRequest(request, out error))
                return false;

            if (_aiIncludeReferenceScriptCheckBox.Checked && !TryResolveAiReferenceScript(out _, out var referenceError))
            {
                error = "参考脚本无效：" + referenceError;
                return false;
            }

            return true;
        }

        private static string BuildExistingScriptSection(string title, string displayPath, string content)
        {
            var sb = new StringBuilder();
            var header = string.IsNullOrWhiteSpace(displayPath) ? title : $"{title}（{displayPath}）";
            sb.AppendLine($"### {header}");
            sb.AppendLine("```csharp");
            sb.AppendLine((content ?? string.Empty).TrimEnd());
            sb.AppendLine("```");
            return sb.ToString().TrimEnd();
        }

        private void PickAiReferenceScriptFile()
        {
            if (!Directory.Exists(ScriptsRootPath))
            {
                MessageBox.Show(this, $"脚本目录不存在：{ScriptsRootPath}", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using var dialog = new OpenFileDialog
            {
                Title = "选择参考脚本（用于 AI 续写/改造）",
                InitialDirectory = ScriptsRootPath,
                Filter = "C# 脚本 (*.cs)|*.cs|所有文件 (*.*)|*.*",
                CheckFileExists = true,
                CheckPathExists = true,
                Multiselect = false,
                RestoreDirectory = true,
            };

            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;

            if (!TrySetAiReferenceScriptFullPath(dialog.FileName, out var error))
            {
                MessageBox.Show(this, error, "参考脚本无效", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _aiIncludeReferenceScriptCheckBox.Checked = true;
        }

        private void ClearAiReferenceScriptFile()
        {
            _aiReferenceScriptFullPath = string.Empty;
            _aiReferenceScriptTextBox.Text = string.Empty;
            _aiIncludeReferenceScriptCheckBox.Checked = false;
        }

        private bool TrySetAiReferenceScriptFullPath(string path, out string error)
        {
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(path))
            {
                ClearAiReferenceScriptFile();
                return true;
            }

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(path);
            }
            catch (Exception ex)
            {
                error = "路径无效：" + ex.Message;
                return false;
            }

            if (!File.Exists(fullPath))
            {
                error = "文件不存在：" + fullPath;
                return false;
            }

            if (!IsEditableScriptFile(fullPath))
            {
                error = "参考脚本仅支持 .cs 文件。";
                return false;
            }

            if (!IsPathUnderRoot(fullPath, ScriptsRootPath))
            {
                error = "参考脚本必须位于脚本目录下：" + ScriptsRootPath;
                return false;
            }

            _aiReferenceScriptFullPath = fullPath;
            _aiReferenceScriptTextBox.Text = ToDisplayPath(fullPath);
            return true;
        }

        private bool TryResolveAiReferenceScript(out string fullPath, out string error)
        {
            error = string.Empty;
            fullPath = string.Empty;

            if (string.IsNullOrWhiteSpace(_aiReferenceScriptFullPath))
            {
                error = "未选择参考脚本文件。";
                return false;
            }

            try
            {
                fullPath = Path.GetFullPath(_aiReferenceScriptFullPath);
            }
            catch (Exception ex)
            {
                error = "路径无效：" + ex.Message;
                return false;
            }

            if (!File.Exists(fullPath))
            {
                error = "文件不存在：" + fullPath;
                return false;
            }

            if (!IsEditableScriptFile(fullPath))
            {
                error = "参考脚本仅支持 .cs 文件。";
                return false;
            }

            if (!IsPathUnderRoot(fullPath, ScriptsRootPath))
            {
                error = "参考脚本必须位于脚本目录下。";
                return false;
            }

            return true;
        }

        private static bool IsPathUnderRoot(string fullPath, string rootDirectory)
        {
            if (string.IsNullOrWhiteSpace(fullPath) || string.IsNullOrWhiteSpace(rootDirectory))
                return false;

            var rootFullPath = Path.GetFullPath(rootDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

            var targetFullPath = Path.GetFullPath(fullPath);
            return targetFullPath.StartsWith(rootFullPath, StringComparison.OrdinalIgnoreCase);
        }

        private static bool ValidateAiRequest(ScriptGenerationRequest request, out string error)
        {
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(request.TargetKey))
                error = "目标 Key 不能为空。";
            else if (string.IsNullOrWhiteSpace(request.NaturalLanguageDescription))
                error = "自然语言描述不能为空。";

            return string.IsNullOrWhiteSpace(error);
        }

        private static string BuildAiResultText(ScriptDraft draft)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Success: {draft.Success}");
            sb.AppendLine($"Provider: {draft.ProviderName}");
            sb.AppendLine($"Model: {draft.ModelName}");
            sb.AppendLine($"SuggestedPath: {draft.SuggestedRelativePath}");

            if (!string.IsNullOrWhiteSpace(draft.ErrorMessage))
                sb.AppendLine("Message: " + draft.ErrorMessage);

            if (draft.Warnings.Count > 0)
            {
                sb.AppendLine("Warnings:");
                foreach (var warning in draft.Warnings)
                    sb.AppendLine("- " + warning);
            }

            return sb.ToString().TrimEnd();
        }

        private static bool EnsureAiScriptSafeToPersist(string code, out string message)
        {
            message = string.Empty;
            var issues = AiScriptOutputGuard.Check(code);
            if (issues.Count == 0)
                return true;

            var sb = new StringBuilder();
            sb.AppendLine("检测到 AI 草稿包含潜在危险引用/程序集，已阻止保存/热更。");
            sb.AppendLine("如确有需要，请改为手工编写并通过代码审查后再落盘。");
            sb.AppendLine();
            sb.AppendLine("问题列表：");
            foreach (var issue in issues)
                sb.AppendLine("- " + issue);
            message = sb.ToString().TrimEnd();
            return false;
        }

        private void TryWriteAiAudit(ScriptGenerationRequest request, ScriptDraft draft)
        {
            try
            {
                var auditDirectory = Path.Combine(ScriptsRootPath, "_ai_audit", DateTime.Now.ToString("yyyyMMdd"));
                Directory.CreateDirectory(auditDirectory);

                var safeKey = SanitizeForFileName(string.IsNullOrWhiteSpace(request.TargetKey) ? "UnknownKey" : request.TargetKey.Replace('/', '_').Replace('\\', '_'));
                var fileName = $"{DateTime.Now:yyyyMMdd_HHmmss}_{request.Kind}_{safeKey}.md";
                var fullPath = Path.Combine(auditDirectory, fileName);

                var guardIssues = AiScriptOutputGuard.Check(draft.GeneratedCode);

                var sb = new StringBuilder();
                sb.AppendLine("# AI 脚本生成审计");
                sb.AppendLine();
                sb.AppendLine($"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine($"Provider: {draft.ProviderName}");
                sb.AppendLine($"Model: {draft.ModelName}");
                sb.AppendLine($"Kind: {request.Kind}");
                sb.AppendLine($"TargetKey: {request.TargetKey}");
                sb.AppendLine($"SuggestedPath: {draft.SuggestedRelativePath}");
                sb.AppendLine($"IncludeCurrentScript: {_aiIncludeCurrentScriptCheckBox.Checked}");
                sb.AppendLine($"IncludeDiagnostics: {_aiIncludeDiagnosticsCheckBox.Checked}");
                sb.AppendLine($"IncludeReferenceScript: {_aiIncludeReferenceScriptCheckBox.Checked}");
                sb.AppendLine($"ReferenceScriptPath: {ToDisplayPath(_aiReferenceScriptFullPath)}");

                sb.AppendLine();
                sb.AppendLine("## Request");
                sb.AppendLine();
                sb.AppendLine("### 自然语言描述");
                sb.AppendLine(request.NaturalLanguageDescription);

                if (!string.IsNullOrWhiteSpace(request.AdditionalRequirements))
                {
                    sb.AppendLine();
                    sb.AppendLine("### 附加要求");
                    sb.AppendLine(request.AdditionalRequirements);
                }

                sb.AppendLine();
                sb.AppendLine($"### ExistingScriptContentLength: {request.ExistingScriptContent.Length}");
                sb.AppendLine($"### DiagnosticsCount: {request.Diagnostics.Count}");

                if (request.Diagnostics.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("```");
                    foreach (var diagnostic in request.Diagnostics)
                        sb.AppendLine(diagnostic.ToString());
                    sb.AppendLine("```");
                }

                sb.AppendLine();
                sb.AppendLine("## Prompt");
                sb.AppendLine();
                sb.AppendLine("```");
                sb.AppendLine(draft.Prompt);
                sb.AppendLine("```");

                sb.AppendLine();
                sb.AppendLine("## Output");
                sb.AppendLine();
                sb.AppendLine("```csharp");
                sb.AppendLine(draft.GeneratedCode);
                sb.AppendLine("```");

                if (!string.IsNullOrWhiteSpace(draft.ErrorMessage))
                {
                    sb.AppendLine();
                    sb.AppendLine("## Message");
                    sb.AppendLine();
                    sb.AppendLine(draft.ErrorMessage);
                }

                if (draft.Warnings.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("## Warnings");
                    sb.AppendLine();
                    foreach (var warning in draft.Warnings)
                        sb.AppendLine("- " + warning);
                }

                if (guardIssues.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("## Guard");
                    sb.AppendLine();
                    foreach (var issue in guardIssues)
                        sb.AppendLine("- " + issue);
                }

                File.WriteAllText(fullPath, sb.ToString().TrimEnd() + Environment.NewLine, new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                _aiResultTextBox.Text = _aiResultTextBox.Text + Environment.NewLine + "[Audit] 写入失败: " + ex.Message;
            }
        }

        private static string SanitizeForFileName(string value, int maxLength = 80)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "Empty";

            var invalidChars = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(value.Length);
            foreach (var ch in value)
                sb.Append(Array.IndexOf(invalidChars, ch) >= 0 ? '_' : ch);

            var sanitized = sb.ToString().Replace(' ', '_').Trim('_');
            if (sanitized.Length > maxLength)
                sanitized = sanitized.Substring(0, maxLength).Trim('_');
            return string.IsNullOrWhiteSpace(sanitized) ? "Empty" : sanitized;
        }

        private string HistoryRootPath => Path.Combine(ScriptsRootPath, "_history");

        private void UpdateHistoryState()
        {
            var hasSelection = _selectedHistoryEntry != null && _historyListView.SelectedItems.Count > 0;
            var hasFile = IsEditableScriptFile(_currentFilePath);
            _historyDiffButton.Enabled = hasFile && hasSelection;
            _historyRollbackToEditorButton.Enabled = hasFile && hasSelection;
            _historyRollbackReloadButton.Enabled = hasFile && hasSelection;
        }

        private void ReloadHistoryEntries()
        {
            _selectedHistoryEntry = null;
            _historyListView.BeginUpdate();
            try
            {
                _historyListView.Items.Clear();
                _historyPreviewTextBox.Clear();
                _historyDiffTextBox.Clear();

                if (!IsEditableScriptFile(_currentFilePath))
                {
                    _historyDiffTextBox.Text = "请选择一个 .cs 脚本文件以查看历史版本。";
                    return;
                }

                var historyDirectory = GetHistoryDirectoryForFile(_currentFilePath);
                if (!Directory.Exists(historyDirectory))
                {
                    _historyDiffTextBox.Text = "(暂无历史版本)";
                    return;
                }

                var entries = new List<HistoryEntry>();
                foreach (var versionDirectory in Directory.GetDirectories(historyDirectory))
                {
                    var metaPath = Path.Combine(versionDirectory, "meta.json");
                    var meta = TryReadHistoryMeta(metaPath);
                    var snapshotFilePath = meta != null && !string.IsNullOrWhiteSpace(meta.OriginalFileName)
                        ? Path.Combine(versionDirectory, meta.OriginalFileName)
                        : Directory.GetFiles(versionDirectory, "*.cs").FirstOrDefault() ?? string.Empty;

                    if (string.IsNullOrWhiteSpace(snapshotFilePath) || !File.Exists(snapshotFilePath))
                        continue;

                    entries.Add(new HistoryEntry
                    {
                        VersionId = string.IsNullOrWhiteSpace(meta?.VersionId) ? Path.GetFileName(versionDirectory) : meta!.VersionId,
                        CreatedLocal = meta?.CreatedLocal ?? Directory.GetCreationTime(versionDirectory),
                        Operator = meta?.Operator ?? string.Empty,
                        Reason = meta?.Reason ?? string.Empty,
                        SnapshotFilePath = snapshotFilePath,
                        MetaFilePath = metaPath,
                    });
                }

                entries.Sort((left, right) => right.CreatedLocal.CompareTo(left.CreatedLocal));

                foreach (var entry in entries)
                {
                    var item = new ListViewItem(entry.CreatedLocal.ToString("yyyy-MM-dd HH:mm:ss"));
                    item.SubItems.Add(entry.Operator);
                    item.SubItems.Add(entry.Reason);
                    item.SubItems.Add(entry.VersionId);
                    item.Tag = entry;
                    _historyListView.Items.Add(item);
                }
            }
            finally
            {
                _historyListView.EndUpdate();
                UpdateHistoryState();
            }
        }

        private void UpdateSelectedHistoryEntry()
        {
            _historyDiffTextBox.Clear();
            if (_historyListView.SelectedItems.Count == 0 || _historyListView.SelectedItems[0].Tag is not HistoryEntry entry)
            {
                _selectedHistoryEntry = null;
                _historyPreviewTextBox.Text = "(未选择版本)";
                UpdateHistoryState();
                return;
            }

            _selectedHistoryEntry = entry;
            try
            {
                _historyPreviewTextBox.Text = ReadAllText(entry.SnapshotFilePath, out _);
            }
            catch (Exception ex)
            {
                _historyPreviewTextBox.Text = ex.ToString();
            }

            UpdateHistoryState();
        }

        private void ShowSelectedHistoryDiff()
        {
            if (_selectedHistoryEntry == null || !IsEditableScriptFile(_currentFilePath))
            {
                MessageBox.Show(this, "请先选择一个脚本文件与一个历史版本。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                var oldText = ReadAllText(_selectedHistoryEntry.SnapshotFilePath, out _);
                var newText = GetCurrentEditorText();
                _historyDiffTextBox.Text = BuildUnifiedDiff(oldText, newText);
            }
            catch (Exception ex)
            {
                _historyDiffTextBox.Text = ex.ToString();
            }
        }

        private void RollbackSelectedHistoryToEditor()
        {
            if (_selectedHistoryEntry == null || !IsEditableScriptFile(_currentFilePath))
            {
                MessageBox.Show(this, "请先选择一个脚本文件与一个历史版本。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (!ConfirmSaveIfDirty("回滚到编辑器")) return;

            try
            {
                var content = ReadAllText(_selectedHistoryEntry.SnapshotFilePath, out var encoding);
                _loadedFileEncodings[_currentFilePath] = encoding;
                _currentFileFromAiDraft = false;
                _suppressTextChanged = true;
                _editorTextBox.Text = content;
                _editorTextBox.SelectionStart = 0;
                _editorTextBox.SelectionLength = 0;
                if (_roslynEditorReady && _roslynEditor != null)
                {
                    _roslynEditor.Text = content;
                    _roslynEditor.Select(0, 0);
                    _roslynEditor.CaretOffset = 0;
                    _roslynEditor.ScrollToLine(1);
                }
                else
                {
                    _pendingRoslynText = content;
                }
                _suppressTextChanged = false;
                _isDirty = true;
                _lastActionSummary = $"已将历史版本加载到编辑器：{_selectedHistoryEntry.VersionId}";
                ApplyEditorMode(syncText: false);
                UpdateEditorState();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.ToString(), "回滚到编辑器失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async Task RollbackSelectedHistoryAndReloadAsync()
        {
            if (_selectedHistoryEntry == null || !IsEditableScriptFile(_currentFilePath))
            {
                MessageBox.Show(this, "请先选择一个脚本文件与一个历史版本。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (!ConfirmSaveIfDirty("回滚并热更")) return;

            await RunBusyAsync(async () =>
            {
                if (!TryCreateHistorySnapshot(_currentFilePath, "RollbackBackup", _selectedHistoryEntry.VersionId, out var historyError))
                    throw new InvalidOperationException(historyError);

                await Task.Run(() => ReplaceFileWithSnapshot(_selectedHistoryEntry.SnapshotFilePath, _currentFilePath));

                LoadFile(_currentFilePath);

                await Task.Run(() => Envir.CSharpScripts.Reload());
                _currentDiagnostics = Envir.CSharpScripts.LastDiagnostics;
                _lastActionSummary = _currentDiagnostics.Count == 0
                    ? "回滚并热更完成，无诊断信息。"
                    : $"回滚并热更完成，诊断数: {_currentDiagnostics.Count}";

                ReloadHistoryEntries();
            });
        }

        private bool TryCreateHistorySnapshot(string fullPath, string reason, string fromVersionId, out string errorMessage)
        {
            errorMessage = string.Empty;
            if (string.IsNullOrWhiteSpace(fullPath) || !File.Exists(fullPath))
            {
                errorMessage = "文件不存在，无法创建历史快照。";
                return false;
            }

            try
            {
                var relativePath = Path.GetRelativePath(ScriptsRootPath, fullPath);
                if (relativePath.StartsWith("..", StringComparison.Ordinal))
                {
                    errorMessage = "当前文件不在脚本目录下，无法创建历史快照。";
                    return false;
                }

                var key = (Path.ChangeExtension(relativePath, null) ?? relativePath).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var versionId = BuildHistoryVersionId();
                var versionDirectory = Path.Combine(HistoryRootPath, key, versionId);
                Directory.CreateDirectory(versionDirectory);

                var fileName = Path.GetFileName(fullPath);
                var snapshotFilePath = Path.Combine(versionDirectory, fileName);
                using (var source = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var target = new FileStream(snapshotFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
                    source.CopyTo(target);

                var meta = new ScriptHistoryMeta
                {
                    VersionId = versionId,
                    OriginalRelativePath = relativePath.Replace(Path.DirectorySeparatorChar, '/'),
                    OriginalFileName = fileName,
                    CreatedLocal = DateTime.Now,
                    CreatedUtc = DateTime.UtcNow,
                    Operator = Environment.UserName,
                    Machine = Environment.MachineName,
                    Reason = reason ?? string.Empty,
                    FromVersionId = fromVersionId ?? string.Empty,
                };

                var json = JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(Path.Combine(versionDirectory, "meta.json"), json + Environment.NewLine, new UTF8Encoding(false));
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.ToString();
                return false;
            }
        }

        private string GetHistoryDirectoryForFile(string fullPath)
        {
            var relativePath = Path.GetRelativePath(ScriptsRootPath, fullPath);
            var key = (Path.ChangeExtension(relativePath, null) ?? relativePath).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return Path.Combine(HistoryRootPath, key);
        }

        private static ScriptHistoryMeta? TryReadHistoryMeta(string metaFilePath)
        {
            try
            {
                if (!File.Exists(metaFilePath))
                    return null;

                var json = File.ReadAllText(metaFilePath, Encoding.UTF8);
                return JsonSerializer.Deserialize<ScriptHistoryMeta>(json);
            }
            catch
            {
                return null;
            }
        }

        private static void ReplaceFileWithSnapshot(string snapshotFilePath, string targetFilePath)
        {
            using var source = new FileStream(snapshotFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var target = new FileStream(targetFilePath, FileMode.Create, FileAccess.Write, FileShare.Read);
            source.CopyTo(target);
        }

        private static string BuildHistoryVersionId()
        {
            var user = SanitizeForFileName(Environment.UserName, 24);
            return $"{DateTime.Now:yyyyMMdd_HHmmss_fff}_{user}_{Guid.NewGuid():N}".TrimEnd('_');
        }

        private static string BuildUnifiedDiff(string oldText, string newText)
        {
            var oldLines = SplitLinesNormalized(oldText);
            var newLines = SplitLinesNormalized(newText);

            if (oldLines.Length + newLines.Length > 8000)
                return "文件过大，已跳过 Diff。建议使用外部 Diff 工具对比。";

            var edits = BuildMyersDiff(oldLines, newLines);
            if (edits.Count > 12000)
                return "Diff 结果过长，已跳过展示。建议使用外部 Diff 工具对比。";

            var sb = new StringBuilder();
            sb.AppendLine("--- snapshot");
            sb.AppendLine("+++ editor");
            foreach (var edit in edits)
                sb.AppendLine(edit);
            return sb.ToString().TrimEnd();
        }

        private static string[] SplitLinesNormalized(string? text)
        {
            if (string.IsNullOrEmpty(text))
                return Array.Empty<string>();

            return text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
        }

        private static IReadOnlyList<string> BuildMyersDiff(IReadOnlyList<string> oldLines, IReadOnlyList<string> newLines)
        {
            var n = oldLines.Count;
            var m = newLines.Count;
            var max = n + m;
            var size = 2 * max + 1;
            var offset = max;
            var v = new int[size];
            var trace = new List<int[]>(max + 1);

            for (var d = 0; d <= max; d++)
            {
                for (var k = -d; k <= d; k += 2)
                {
                    var kIndex = offset + k;
                    int x;
                    if (k == -d || (k != d && v[offset + k - 1] < v[offset + k + 1]))
                        x = v[offset + k + 1];
                    else
                        x = v[offset + k - 1] + 1;

                    var y = x - k;
                    while (x < n && y < m && string.Equals(oldLines[x], newLines[y], StringComparison.Ordinal))
                    {
                        x++;
                        y++;
                    }

                    v[kIndex] = x;
                    if (x >= n && y >= m)
                    {
                        trace.Add((int[])v.Clone());
                        return BacktrackMyersDiff(trace, oldLines, newLines, offset);
                    }
                }

                trace.Add((int[])v.Clone());
            }

            return BacktrackMyersDiff(trace, oldLines, newLines, offset);
        }

        private static IReadOnlyList<string> BacktrackMyersDiff(IReadOnlyList<int[]> trace, IReadOnlyList<string> oldLines, IReadOnlyList<string> newLines, int offset)
        {
            var edits = new List<string>();
            var x = oldLines.Count;
            var y = newLines.Count;

            for (var d = trace.Count - 1; d > 0; d--)
            {
                var v = trace[d - 1];
                var k = x - y;

                int prevK;
                if (k == -d || (k != d && v[offset + k - 1] < v[offset + k + 1]))
                    prevK = k + 1;
                else
                    prevK = k - 1;

                var prevX = v[offset + prevK];
                var prevY = prevX - prevK;

                while (x > prevX && y > prevY)
                {
                    edits.Add(" " + oldLines[x - 1]);
                    x--;
                    y--;
                }

                if (x == prevX)
                {
                    if (prevY >= 0 && prevY < newLines.Count)
                        edits.Add("+" + newLines[prevY]);
                }
                else
                {
                    if (prevX >= 0 && prevX < oldLines.Count)
                        edits.Add("-" + oldLines[prevX]);
                }

                x = prevX;
                y = prevY;
            }

            while (x > 0 && y > 0)
            {
                edits.Add(" " + oldLines[x - 1]);
                x--;
                y--;
            }

            while (x > 0)
            {
                edits.Add("-" + oldLines[x - 1]);
                x--;
            }

            while (y > 0)
            {
                edits.Add("+" + newLines[y - 1]);
                y--;
            }

            edits.Reverse();
            return edits;
        }

        private bool EnsureAiDraftReady()
        {
            if (_currentAiDraft != null && !string.IsNullOrWhiteSpace(_aiDraftTextBox.Text))
                return true;

            MessageBox.Show(this, "当前没有可操作的 AI 草稿。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }

        private string ResolveAiDraftRelativePath(ScriptDraft draft)
        {
            if (!string.IsNullOrWhiteSpace(draft.SuggestedRelativePath))
                return draft.SuggestedRelativePath.Replace('/', Path.DirectorySeparatorChar);

            var request = BuildAiRequest();
            return (request.TargetKey.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar) + ".cs").TrimStart(Path.DirectorySeparatorChar);
        }

        private void LoadDraftIntoEditor(string fullPath, string content)
        {
            _currentFilePath = fullPath;
            _currentFileFromAiDraft = true;
            _loadedFileEncodings[fullPath] = new UTF8Encoding(false);
            _suppressTextChanged = true;
            _editorTextBox.Text = content;
            _editorTextBox.SelectionStart = 0;
            _editorTextBox.SelectionLength = 0;
            if (_roslynEditorReady && _roslynEditor != null)
            {
                _roslynEditor.Text = content;
                _roslynEditor.Select(0, 0);
                _roslynEditor.CaretOffset = 0;
                _roslynEditor.ScrollToLine(1);
            }
            else
            {
                _pendingRoslynText = content;
            }
            _suppressTextChanged = false;
            _isDirty = true;
            ApplyEditorMode(syncText: false);
            UpdateEditorState();
            ReloadHistoryEntries();
            UpdateHistoryState();
        }

        private static string ReadAllText(string path, out Encoding encoding)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream, Encoding.UTF8, true);
            var text = reader.ReadToEnd();
            encoding = reader.CurrentEncoding;
            return text;
        }

        private bool IsEditableScriptFile(string? path) => !string.IsNullOrWhiteSpace(path) && File.Exists(path) && string.Equals(Path.GetExtension(path), ".cs", StringComparison.OrdinalIgnoreCase);

        private string BuildStatusText()
        {
            var manager = Envir.CSharpScripts;
            var sb = new StringBuilder();
            sb.AppendLine("脚本系统状态");
            sb.AppendLine($"Enabled: {manager.Enabled}");
            sb.AppendLine($"Version: {manager.Version}");
            sb.AppendLine($"Handlers: {manager.LastRegisteredHandlerCount}");
            sb.AppendLine($"LastSuccessUtc: {FormatUtc(manager.LastSuccessUtc)}");
            sb.AppendLine($"LastFailureUtc: {FormatUtc(manager.LastFailureUtc)}");
            sb.AppendLine($"Config: CSharpScriptsEnabled={Settings.CSharpScriptsEnabled}, HotReload={Settings.CSharpScriptsHotReloadEnabled}, DebounceMs={Settings.CSharpScriptsDebounceMs}");
            sb.AppendLine($"ScriptsPath: {ScriptsRootPath}");
            sb.AppendLine($"VisibleFiles: {_fileNodeLookup.Count}");
            sb.AppendLine($"TestBenchKeys: {_allTestEntries.Count}");
            sb.AppendLine($"OnlinePlayers: {_onlinePlayerCount}");
            if (!string.IsNullOrWhiteSpace(manager.LastError)) sb.AppendLine("LastError: " + manager.LastError);
            sb.AppendLine("LastAction: " + _lastActionSummary);
            sb.AppendLine("CurrentDiagnostics: " + _currentDiagnostics.Count);
            sb.AppendLine("LastTestBench: " + _lastTestSummary);
            sb.AppendLine("LastAiDraft: " + _lastAiSummary);
            return sb.ToString();
        }

        private string ToDisplayPath(string? fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath)) return string.Empty;
            try
            {
                var normalized = Path.GetFullPath(fullPath);
                return normalized.StartsWith(ScriptsRootPath, StringComparison.OrdinalIgnoreCase) ? Path.GetRelativePath(ScriptsRootPath, normalized) : normalized;
            }
            catch
            {
                return fullPath;
            }
        }

        private static string FormatUtc(DateTime? utc) => utc.HasValue ? utc.Value.ToString("yyyy-MM-dd HH:mm:ss 'UTC'") : "-";
    }
}
