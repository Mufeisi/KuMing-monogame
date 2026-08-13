using Server.MirDatabase;
using Server.MirEnvir;
using Server.Authoring;
using Server.Diagnostics;
using Server.MirForms.Systems;
using Server.Scripting;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Server
{
    public partial class NPCInfoForm : Form
    {
        public string NPCListPath = Path.Combine(Settings.ExportPath, "NPCList.txt");

        public Envir Envir => SMain.EditEnvir;

        private List<NPCInfo> _selectedNPCInfos;
        private readonly NpcContentEditingSession _editingSession;
        private ListBox _scriptPageListBox;
        private TextBox _scriptPreviewTextBox;
        private ListBox _scriptDiagnosticsListBox;
        private Label _scriptSourceLabel;
        private NpcScriptPreview _scriptPreview = new(string.Empty, string.Empty, [], []);
        private readonly string _npcDirectory;
        public int? SelectedNpcIndex => NPCInfoListBox.SelectedItem is NPCInfo item ? item.Index : null;
        public NPCInfo SelectedDraft => NPCInfoListBox.SelectedItem as NPCInfo;
        public bool HasPendingChanges => _editingSession.IsDirty;
        public NpcScriptPreview CurrentScriptPreview => _scriptPreview;

        public NPCInfoForm(int? selectedNpcIndex = null, string npcDirectory = "")
        {
            InitializeComponent();
            _npcDirectory = string.IsNullOrWhiteSpace(npcDirectory) ? Settings.NPCPath : npcDirectory;
            _editingSession = new NpcContentEditingSession(Envir.NPCInfoList, Envir.NPCIndex);
            InitializeAuthoringInterface();

            for (int i = 0; i < Envir.MapInfoList.Count; i++) MapComboBox.Items.Add(Envir.MapInfoList[i]);

            if (ConquestHidden_combo.Items.Count != Envir.ConquestInfoList.Count)
            {
                ConquestHidden_combo.Items.Clear();

                ConquestHidden_combo.Items.Add("");
                for (int i = 0; i < Envir.ConquestInfoList.Count; i++)
                {
                    ConquestHidden_combo.Items.Add(Envir.ConquestInfoList[i]);
                }
            }

            UpdateInterface();
            if (selectedNpcIndex.HasValue)
                SelectNpc(selectedNpcIndex.Value);
        }

        private void InitializeAuthoringInterface()
        {
            const int toolbarHeight = 42;
            foreach (Control control in Controls.Cast<Control>().ToArray()) control.Top += toolbarHeight;
            ClientSize = new Size(ClientSize.Width, ClientSize.Height + toolbarHeight);
            var toolbar = new FlowLayoutPanel { Name = "NpcAuthoringToolbar", Dock = DockStyle.Top, Height = toolbarHeight, Padding = new Padding(8, 4, 8, 4) };
            toolbar.Controls.Add(CreateAuthoringButton("SaveNpcContentButton", "保存", (_, _) => SaveDraftWithFeedback()));
            toolbar.Controls.Add(CreateAuthoringButton("ReloadNpcContentButton", "重载", (_, _) => ReloadDraftWithConfirmation()));
            toolbar.Controls.Add(CreateAuthoringButton("DiffNpcContentButton", "差异", (_, _) => ShowDraftDiff()));
            Controls.Add(toolbar);
            toolbar.BringToFront();

            var scriptTab = new TabPage { Name = "NpcScriptWorkflowTab", Text = "脚本闭环" };
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(4) };
            buttons.Controls.Add(CreateAuthoringButton("PreviewNpcScriptButton", "刷新预览", (_, _) => RefreshScriptPreview()));
            buttons.Controls.Add(CreateAuthoringButton("OpenNpcScriptButton", "跳转脚本", (_, _) => OpenSelectedScript()));
            buttons.Controls.Add(CreateAuthoringButton("OpenNpcResourceButton", "打开预览资源", (_, _) => OpenSelectedPreviewResource()));
            _scriptSourceLabel = new Label { Name = "NpcScriptSourceLabel", Dock = DockStyle.Top, Height = 26, TextAlign = ContentAlignment.MiddleLeft, Text = "脚本来源：(未选择)" };
            _scriptPageListBox = new ListBox { Name = "NpcScriptPageListBox", Dock = DockStyle.Fill };
            _scriptPageListBox.SelectedIndexChanged += (_, _) => RenderSelectedScriptPage();
            _scriptPreviewTextBox = new TextBox { Name = "NpcScriptPreviewTextBox", Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, Font = new Font("Consolas", 9F) };
            _scriptDiagnosticsListBox = new ListBox { Name = "NpcScriptDiagnosticsListBox", Dock = DockStyle.Fill };
            var right = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 210 };
            right.Panel1.Controls.Add(_scriptPreviewTextBox);
            right.Panel2.Controls.Add(_scriptDiagnosticsListBox);
            var main = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 170 };
            main.Panel1.Controls.Add(_scriptPageListBox);
            main.Panel2.Controls.Add(right);
            scriptTab.Controls.Add(main);
            scriptTab.Controls.Add(_scriptSourceLabel);
            scriptTab.Controls.Add(buttons);
            tabControl1.TabPages.Add(scriptTab);
            FormClosing += NPCInfoForm_FormClosingGate;
        }

        private static Button CreateAuthoringButton(string name, string text, EventHandler handler)
        {
            var button = new Button { Name = name, Text = text, AutoSize = true };
            button.Click += handler;
            return button;
        }

        public bool SelectNpc(int npcIndex)
        {
            for (int index = 0; index < NPCInfoListBox.Items.Count; index++)
            {
                if (NPCInfoListBox.Items[index] is not NPCInfo item || item.Index != npcIndex)
                    continue;
                NPCInfoListBox.ClearSelected();
                NPCInfoListBox.SelectedIndex = index;
                NPCInfoListBox.TopIndex = index;
                return true;
            }
            return false;
        }

        private void AddButton_Click(object sender, EventArgs e)
        {
            NPCInfo added = AddDraft();
            UpdateInterface();
            SelectNpc(added.Index);
        }

        public NPCInfo AddDraft() => _editingSession.Add();
        private void RemoveButton_Click(object sender, EventArgs e)
        {
            if (_selectedNPCInfos.Count == 0) return;

            if (MessageBox.Show("是否要删除选定的NPC", "删除NPC", MessageBoxButtons.YesNo) != DialogResult.Yes) return;

            for (int i = 0; i < _selectedNPCInfos.Count; i++) _editingSession.Remove(_selectedNPCInfos[i]);

            UpdateInterface();
        }

        private void UpdateInterface()
        {
            if (NPCInfoListBox.Items.Count != _editingSession.Drafts.Count)
            {
                NPCInfoListBox.Items.Clear();

                for (int i = 0; i < _editingSession.Drafts.Count; i++)
                    NPCInfoListBox.Items.Add(_editingSession.Drafts[i]);
            }

            _selectedNPCInfos = NPCInfoListBox.SelectedItems.Cast<NPCInfo>().ToList();

            if (_selectedNPCInfos.Count == 0)
            {
                tabPage1.Enabled = false;
                tabPage2.Enabled = false;
                NPCIndexTextBox.Text = string.Empty;
                NFileNameTextBox.Text = string.Empty;
                NNameTextBox.Text = string.Empty;
                NXTextBox.Text = string.Empty;
                NYTextBox.Text = string.Empty;
                NImageTextBox.Text = string.Empty;
                NRateTextBox.Text = string.Empty;
                MapComboBox.SelectedItem = null;
                MinLev_textbox.Text = string.Empty;
                MaxLev_textbox.Text = string.Empty;
                Class_combo.Text = string.Empty;
                ConquestHidden_combo.SelectedIndex = -1;
                Day_combo.Text = string.Empty;
                TimeVisible_checkbox.Checked = false;
                StartHour_combo.Text = string.Empty;
                EndHour_combo.Text = string.Empty;
                StartMin_num.Value = 0;
                EndMin_num.Value = 1;
                Flag_textbox.Text = string.Empty;
                ShowBigMapCheckBox.Checked = false;
                BigMapIconTextBox.Text = string.Empty;
                ConquestVisible_checkbox.Checked = true;
                return;
            }

            NPCInfo info = _selectedNPCInfos[0];

            tabPage1.Enabled = true;
            tabPage2.Enabled = true;

            NPCIndexTextBox.Text = info.Index.ToString();
            NFileNameTextBox.Text = info.FileName;
            NNameTextBox.Text = info.Name;
            NXTextBox.Text = info.Location.X.ToString();
            NYTextBox.Text = info.Location.Y.ToString();
            NImageTextBox.Text = info.Image.ToString();
            NRateTextBox.Text = info.Rate.ToString();
            MapComboBox.SelectedItem = Envir.MapInfoList.FirstOrDefault(x => x.Index == info.MapIndex);
            MinLev_textbox.Text = info.MinLev.ToString();
            MaxLev_textbox.Text = info.MaxLev.ToString();
            Class_combo.Text = info.ClassRequired;
            ConquestHidden_combo.SelectedItem = Envir.ConquestInfoList.FirstOrDefault(x => x.Index == info.Conquest);
            Day_combo.Text = info.DayofWeek;
            TimeVisible_checkbox.Checked = info.TimeVisible;
            StartHour_combo.Text = info.HourStart.ToString();
            EndHour_combo.Text = info.HourEnd.ToString();
            StartMin_num.Value = info.MinuteStart;
            EndMin_num.Value = info.MinuteEnd;
            Flag_textbox.Text = info.FlagNeeded.ToString();
            ShowBigMapCheckBox.Checked = info.ShowOnBigMap;
            BigMapIconTextBox.Text = info.BigMapIcon.ToString();
            TeleportToCheckBox.Checked = info.CanTeleportTo;
            ConquestVisible_checkbox.Checked = info.ConquestVisible;
            LoadImage(info.Image);


            for (int i = 1; i < _selectedNPCInfos.Count; i++)
            {
                info = _selectedNPCInfos[i];

                if (NFileNameTextBox.Text != info.FileName) NFileNameTextBox.Text = string.Empty;
                if (NNameTextBox.Text != info.Name) NNameTextBox.Text = string.Empty;
                if (NXTextBox.Text != info.Location.X.ToString()) NXTextBox.Text = string.Empty;

                if (NYTextBox.Text != info.Location.Y.ToString()) NYTextBox.Text = string.Empty;
                if (NImageTextBox.Text != info.Image.ToString()) NImageTextBox.Text = string.Empty;
                if (NRateTextBox.Text != info.Rate.ToString()) NRateTextBox.Text = string.Empty;
                if (BigMapIconTextBox.Text != info.BigMapIcon.ToString()) BigMapIconTextBox.Text = string.Empty;
            }
        }

        private void RefreshNPCList()
        {
            NPCInfoListBox.SelectedIndexChanged -= NPCInfoListBox_SelectedIndexChanged;

            List<bool> selected = new List<bool>();

            for (int i = 0; i < NPCInfoListBox.Items.Count; i++) selected.Add(NPCInfoListBox.GetSelected(i));
            NPCInfoListBox.Items.Clear();

            for (int i = 0; i < _editingSession.Drafts.Count; i++) NPCInfoListBox.Items.Add(_editingSession.Drafts[i]);
            for (int i = 0; i < selected.Count; i++) NPCInfoListBox.SetSelected(i, selected[i]);

            NPCInfoListBox.SelectedIndexChanged += NPCInfoListBox_SelectedIndexChanged;
        }

        private void NPCInfoListBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_selectedNPCInfos.Count > 0)
            {
                NPCInfo info = _selectedNPCInfos[0];
                LoadImage(info.Image);
            }
            else
            {
                LoadImage(0);
            }

            UpdateInterface();
            RefreshScriptPreview();

        }
        private void LoadImage(ushort imageValue)
        {
            string filename = $"{imageValue}.bmp";
            string imagePath = Path.Combine(Environment.CurrentDirectory, "Envir", "Previews", "NPC", filename);

            if (File.Exists(imagePath))
            {
                using (FileStream fs = new FileStream(imagePath, FileMode.Open, FileAccess.Read))
                {
                    NPCPreview.Image = Image.FromStream(fs);
                }
            }
            else
            {
                NPCPreview.Image = null;
            }
        }

        private void NFileNameTextBox_TextChanged(object sender, EventArgs e)
        {
            if (ActiveControl != sender) return;

            for (int i = 0; i < _selectedNPCInfos.Count; i++)
                _selectedNPCInfos[i].FileName = ActiveControl.Text;

            RefreshNPCList();
        }
        private void NNameTextBox_TextChanged(object sender, EventArgs e)
        {
            if (ActiveControl != sender) return;

            for (int i = 0; i < _selectedNPCInfos.Count; i++)
                _selectedNPCInfos[i].Name = ActiveControl.Text;
        }
        private void NXTextBox_TextChanged(object sender, EventArgs e)
        {
            if (ActiveControl != sender) return;

            int temp;

            if (!int.TryParse(ActiveControl.Text, out temp))
            {
                ActiveControl.BackColor = Color.Red;
                return;
            }
            ActiveControl.BackColor = SystemColors.Window;


            for (int i = 0; i < _selectedNPCInfos.Count; i++)
                _selectedNPCInfos[i].Location.X = temp;

            RefreshNPCList();
        }
        private void NYTextBox_TextChanged(object sender, EventArgs e)
        {
            if (ActiveControl != sender) return;

            int temp;

            if (!int.TryParse(ActiveControl.Text, out temp))
            {
                ActiveControl.BackColor = Color.Red;
                return;
            }
            ActiveControl.BackColor = SystemColors.Window;


            for (int i = 0; i < _selectedNPCInfos.Count; i++)
                _selectedNPCInfos[i].Location.Y = temp;

            RefreshNPCList();
        }
        private void NImageTextBox_TextChanged(object sender, EventArgs e)
        {
            if (ActiveControl != sender) return;

            ushort temp;

            if (!ushort.TryParse(ActiveControl.Text, out temp))
            {
                ActiveControl.BackColor = Color.Red;
                return;
            }
            ActiveControl.BackColor = SystemColors.Window;


            for (int i = 0; i < _selectedNPCInfos.Count; i++)
                _selectedNPCInfos[i].Image = temp;

            LoadImage(temp);
        }
        private void NRateTextBox_TextChanged(object sender, EventArgs e)
        {
            if (ActiveControl != sender) return;

            ushort temp;

            if (!ushort.TryParse(ActiveControl.Text, out temp))
            {
                ActiveControl.BackColor = Color.Red;
                return;
            }
            ActiveControl.BackColor = SystemColors.Window;


            for (int i = 0; i < _selectedNPCInfos.Count; i++)
                _selectedNPCInfos[i].Rate = temp;
        }

        private void MapComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (ActiveControl != sender) return;

            for (int i = 0; i < _selectedNPCInfos.Count; i++)
            {
                MapInfo temp = (MapInfo)MapComboBox.SelectedItem;
                _selectedNPCInfos[i].MapIndex = temp.Index;
            }

        }

        private void NPCInfoForm_FormClosed(object sender, FormClosedEventArgs e)
        {
        }

        private void NPCInfoForm_FormClosingGate(object sender, FormClosingEventArgs e)
        {
            if (!_editingSession.IsDirty) return;
            DialogResult decision = MessageBox.Show(this,
                "NPC 草稿尚未保存。是否保存后关闭？\r\n选择“否”将放弃草稿。",
                "未保存的 NPC 内容", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
            if (decision == DialogResult.Cancel)
            {
                e.Cancel = true;
                return;
            }
            if (decision == DialogResult.Yes && !TrySaveDraft(out string error))
            {
                MessageBox.Show(this, error, "保存失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                e.Cancel = true;
            }
        }

        public bool TrySaveDraft(out string error) => TrySaveDraft(Envir.SaveDB, out error);

        public bool TrySaveDraft(Action persist, out string error)
        {
            IReadOnlyList<ProjectPreflightDiagnostic> diagnostics = ValidateDraft();
            ProjectPreflightDiagnostic[] blocking = diagnostics.Where(value => value.Severity == ProjectPreflightSeverity.Error).ToArray();
            if (blocking.Length > 0)
            {
                error = "保存前校验未通过：\r\n" + string.Join("\r\n", blocking.Select(value => $"{value.Code} {value.Source} {value.Message}"));
                return false;
            }

            NpcScriptDiagnostic[] scriptBlocking = BuildScriptDiagnosticsForDrafts()
                .Where(value => value.Code.StartsWith("CONTENT03-LINK-", StringComparison.Ordinal)).ToArray();
            if (scriptBlocking.Length > 0)
            {
                error = "脚本链接校验未通过：\r\n" + string.Join("\r\n", scriptBlocking.Select(value => $"{value.Code} {value.PageKey} {value.Message}"));
                return false;
            }

            int previousHighWatermark = Envir.NPCIndex;
            int committedHighWatermark = Math.Max(previousHighWatermark, _editingSession.Drafts.Select(value => value.Index).DefaultIfEmpty().Max());
            Envir.NPCIndex = committedHighWatermark;
            NpcContentCommitResult result = _editingSession.TryCommit(persist);
            error = result.Error;
            if (!result.Success)
            {
                Envir.NPCIndex = previousHighWatermark;
                return false;
            }
            NPCInfoListBox.Items.Clear();
            UpdateInterface();
            return true;
        }

        public IReadOnlyList<ProjectPreflightDiagnostic> ValidateDraft()
        {
            return _editingSession.Validate(npcs => ProjectSemanticPreflight.Validate(new ProjectPreflightRequest
            {
                NpcDirectory = _npcDirectory,
                CSharpNpcDirectory = Settings.CSharpScriptsPath,
                Maps = Envir.MapInfoList,
                Npcs = npcs,
                Monsters = Envir.MonsterInfoList,
                Items = Envir.ItemInfoList,
                Scripts = Envir.CSharpScripts.CurrentRegistry,
            }).Diagnostics.Where(value => value.Source.StartsWith("NPCInfo[", StringComparison.Ordinal)).ToArray());
        }

        private IReadOnlyList<NpcScriptDiagnostic> BuildScriptDiagnosticsForDrafts()
        {
            var diagnostics = new List<NpcScriptDiagnostic>();
            foreach (NPCInfo draft in _editingSession.Drafts)
            {
                string key = $"NPCs/{draft.FileName}";
                TextFileDefinition definition = Envir.TextFileProvider?.GetByKey(key);
                string diskPath = Path.Combine(_npcDirectory, draft.FileName + ".txt");
                IEnumerable<string> lines = definition?.Lines ?? (File.Exists(diskPath) ? File.ReadLines(diskPath) : []);
                diagnostics.AddRange(NpcScriptAuthoring.BuildPreview(draft.FileName, lines, definition != null ? key : diskPath).Diagnostics);
            }
            return diagnostics;
        }

        public IReadOnlyList<NpcContentDiff> GetDraftDiff() => _editingSession.BuildDiff();

        public void ReloadDraft()
        {
            int? selected = SelectedNpcIndex;
            _editingSession.Reload();
            NPCInfoListBox.Items.Clear();
            UpdateInterface();
            if (selected.HasValue) SelectNpc(selected.Value);
        }

        private void SaveDraftWithFeedback()
        {
            if (TrySaveDraft(out string error))
                MessageBox.Show(this, "NPC 内容已保存。", "保存完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            else
                MessageBox.Show(this, error, "保存失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private void ReloadDraftWithConfirmation()
        {
            if (_editingSession.IsDirty && MessageBox.Show(this, "重载将放弃当前 NPC 草稿，是否继续？", "确认重载", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;
            ReloadDraft();
        }

        private void ShowDraftDiff()
        {
            IReadOnlyList<NpcContentDiff> diff = GetDraftDiff();
            MessageBox.Show(this, diff.Count == 0 ? "当前没有未保存差异。" : string.Join("\r\n", diff.Select(value => value.Summary)),
                "NPC 内容差异", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        public NpcScriptPreview RefreshScriptPreview()
        {
            NPCInfo draft = SelectedDraft;
            if (draft == null)
            {
                _scriptPreview = NpcScriptAuthoring.BuildPreview(string.Empty, [], string.Empty);
                RenderScriptPreview();
                return _scriptPreview;
            }

            string key = $"NPCs/{draft.FileName}";
            TextFileDefinition definition = Envir.TextFileProvider?.GetByKey(key);
            string diskPath = Path.Combine(_npcDirectory, draft.FileName + ".txt");
            IEnumerable<string> lines;
            string source;
            if (definition != null)
            {
                lines = definition.Lines;
                source = key + "（当前脚本注册表）";
            }
            else if (File.Exists(diskPath))
            {
                lines = File.ReadLines(diskPath);
                source = diskPath;
            }
            else
            {
                lines = [];
                source = key;
            }

            _scriptPreview = NpcScriptAuthoring.BuildPreview(draft.FileName, lines, source);
            RenderScriptPreview();
            return _scriptPreview;
        }

        private void RenderScriptPreview()
        {
            _scriptSourceLabel.Text = "脚本来源：" + (string.IsNullOrWhiteSpace(_scriptPreview.Source) ? "(未选择)" : _scriptPreview.Source);
            _scriptPageListBox.Items.Clear();
            _scriptPageListBox.Items.AddRange(_scriptPreview.Pages.Cast<object>().ToArray());
            _scriptDiagnosticsListBox.Items.Clear();
            _scriptDiagnosticsListBox.Items.AddRange(_scriptPreview.Diagnostics.Select(value => $"{value.Code} {value.PageKey} {value.Message}").Cast<object>().ToArray());
            if (_scriptPageListBox.Items.Count > 0) _scriptPageListBox.SelectedIndex = 0;
            else _scriptPreviewTextBox.Clear();
        }

        private void RenderSelectedScriptPage()
        {
            if (_scriptPageListBox.SelectedItem is not NpcScriptPagePreview page)
            {
                _scriptPreviewTextBox.Clear();
                return;
            }
            _scriptPreviewTextBox.Text = page.Key + "\r\n" + string.Join("\r\n", page.Lines) +
                (page.Links.Count == 0 ? string.Empty : "\r\n\r\n链接：" + string.Join("、", page.Links));
        }

        private void OpenSelectedScript()
        {
            NPCInfo draft = SelectedDraft;
            if (draft == null) return;
            string scriptPath = ResolveSelectedScriptPath();
            if (scriptPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                using var form = new ScriptDebugForm(scriptPath);
                form.ShowDialog(this);
                RefreshScriptPreview();
                return;
            }

            if (File.Exists(scriptPath)) Shared.Helpers.FileIO.OpenScript(scriptPath, true);
            else MessageBox.Show(this, "未找到对应的 C# 或 TXT 脚本。", "脚本跳转", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        public string ResolveSelectedScriptPath()
        {
            NPCInfo draft = SelectedDraft;
            if (draft == null) return string.Empty;
            string csharpPath = FindCSharpNpcScript(draft.FileName);
            return string.IsNullOrWhiteSpace(csharpPath) ? Path.Combine(_npcDirectory, draft.FileName + ".txt") : csharpPath;
        }

        public static string FindCSharpNpcScript(string npcFileName)
        {
            if (string.IsNullOrWhiteSpace(npcFileName) || !Directory.Exists(Settings.CSharpScriptsPath)) return string.Empty;
            string normalized = npcFileName.Replace('\\', '/');
            string leaf = Path.GetFileName(normalized);
            return Directory.EnumerateFiles(Settings.CSharpScriptsPath, "*.cs", SearchOption.AllDirectories)
                .FirstOrDefault(path => Path.GetFileNameWithoutExtension(path).Equals(leaf, StringComparison.OrdinalIgnoreCase)
                    || File.ReadLines(path).Take(200).Any(line => line.Contains(normalized, StringComparison.OrdinalIgnoreCase))) ?? string.Empty;
        }

        private void OpenSelectedPreviewResource()
        {
            string path = GetSelectedPreviewResourcePath();
            if (!File.Exists(path))
            {
                MessageBox.Show(this, $"NPC 预览资源不存在：{path}", "资源检查", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }

        public string GetSelectedPreviewResourcePath() => SelectedDraft == null
            ? string.Empty
            : Path.Combine(Environment.CurrentDirectory, "Envir", "Previews", "NPC", $"{SelectedDraft.Image}.bmp");




        private void PasteMButton_Click(object sender, EventArgs e)
        {
            string data = Clipboard.GetText();

            if (!data.StartsWith("NPC", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("无法粘贴，复制的数据不是NPC信息");
                return;
            }


            string[] npcs = data.Split(new[] { '\t' }, StringSplitOptions.RemoveEmptyEntries);


            //for (int i = 1; i < npcs.Length; i++)
            //    NPCInfo.FromText(npcs[i]);

            UpdateInterface();
        }

        private void ExportAllButton_Click(object sender, EventArgs e)
        {
            ExportNPCs(_editingSession.Drafts.ToList());
        }

        private void ExportSelected_Click(object sender, EventArgs e)
        {
            var list = NPCInfoListBox.SelectedItems.Cast<NPCInfo>().ToList();

            ExportNPCs(list);
        }

        public void ExportNPCs(List<NPCInfo> NPCs)
        {
            if (NPCs.Count == 0) return;

            SaveFileDialog sfd = new SaveFileDialog();
            sfd.InitialDirectory = Path.Combine(Application.StartupPath, "Exports");
            sfd.FileName = "4_NPC数据";
            sfd.Filter = "Text File|*.txt";
            sfd.ShowDialog();

            if (sfd.FileName == string.Empty) return;

            using (StreamWriter sw = File.AppendText(sfd.FileNames[0]))
            {
                for (int j = 0; j < NPCs.Count; j++)
                {
                    sw.WriteLine(NPCs[j].ToText());
                }
            }
            MessageBox.Show("NPC数据导出完成");
        }

        private void ImportButton_Click(object sender, EventArgs e)
        {
            string Path = string.Empty;

            OpenFileDialog ofd = new OpenFileDialog();
            ofd.Filter = "Text File|*.txt";
            ofd.ShowDialog();

            if (ofd.FileName == string.Empty) return;

            Path = ofd.FileName;

            string data;
            using (var sr = new StreamReader(Path))
            {
                data = sr.ReadToEnd();
            }

            var npcs = data.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var m in npcs)
            {
                try
                {
                    if (TryParseImportedNpc(m, out NPCInfo imported))
                    {
                        NPCInfo draft = _editingSession.Add();
                        imported.Index = draft.Index;
                        CopyImportedNpc(imported, draft);
                    }
                }
                catch { }
            }

            UpdateInterface();
            MessageBox.Show("NPC数据导入完成");
        }

        private bool TryParseImportedNpc(string text, out NPCInfo info)
        {
            info = null;
            string[] data = text.Split(',');
            if (data.Length < 18) return false;
            MapInfo map = Envir.MapInfoList.FirstOrDefault(value => value.FileName.Equals(data[1], StringComparison.OrdinalIgnoreCase));
            if (map == null || !int.TryParse(data[2], out int x) || !int.TryParse(data[3], out int y)
                || !ushort.TryParse(data[5], out ushort image) || !ushort.TryParse(data[6], out ushort rate)
                || !bool.TryParse(data[7], out bool showOnBigMap) || !int.TryParse(data[8], out int bigMapIcon)
                || !bool.TryParse(data[9], out bool canTeleportTo) || !bool.TryParse(data[10], out bool conquestVisible)
                || !short.TryParse(data[11], out short minLev) || !short.TryParse(data[12], out short maxLev)
                || !bool.TryParse(data[13], out bool timeVisible) || !byte.TryParse(data[14], out byte hourStart)
                || !byte.TryParse(data[15], out byte minuteStart) || !byte.TryParse(data[16], out byte hourEnd)
                || !byte.TryParse(data[17], out byte minuteEnd)) return false;
            info = new NPCInfo
            {
                FileName = data[0], MapIndex = map.Index, Location = new Point(x, y), Name = data[4], Image = image, Rate = rate,
                ShowOnBigMap = showOnBigMap, BigMapIcon = bigMapIcon, CanTeleportTo = canTeleportTo, ConquestVisible = conquestVisible,
                MinLev = minLev, MaxLev = maxLev, TimeVisible = timeVisible, HourStart = hourStart, MinuteStart = minuteStart,
                HourEnd = hourEnd, MinuteEnd = minuteEnd,
            };
            return true;
        }

        private static void CopyImportedNpc(NPCInfo source, NPCInfo target)
        {
            target.FileName = source.FileName; target.Name = source.Name; target.MapIndex = source.MapIndex; target.Location = source.Location;
            target.Image = source.Image; target.Rate = source.Rate; target.ShowOnBigMap = source.ShowOnBigMap; target.BigMapIcon = source.BigMapIcon;
            target.CanTeleportTo = source.CanTeleportTo; target.ConquestVisible = source.ConquestVisible; target.MinLev = source.MinLev;
            target.MaxLev = source.MaxLev; target.TimeVisible = source.TimeVisible; target.HourStart = source.HourStart;
            target.MinuteStart = source.MinuteStart; target.HourEnd = source.HourEnd; target.MinuteEnd = source.MinuteEnd;
        }

        private void OpenNButton_Click(object sender, EventArgs e)
        {
            OpenSelectedScript();
        }

        private void MinLev_textbox_TextChanged(object sender, EventArgs e)
        {
            if (ActiveControl != sender) return;

            short temp;

            if (!short.TryParse(ActiveControl.Text, out temp))
            {
                ActiveControl.BackColor = Color.Red;
                return;
            }
            ActiveControl.BackColor = SystemColors.Window;


            for (int i = 0; i < _selectedNPCInfos.Count; i++)
                _selectedNPCInfos[i].MinLev = temp;
        }

        private void HourShow_textbox_TextChanged(object sender, EventArgs e)
        {
            if (ActiveControl != sender) return;

            byte temp;

            if (!byte.TryParse(ActiveControl.Text, out temp))
            {
                ActiveControl.BackColor = Color.Red;
                return;
            }
            ActiveControl.BackColor = SystemColors.Window;


            for (int i = 0; i < _selectedNPCInfos.Count; i++)
                _selectedNPCInfos[i].HourStart = temp;
        }

        private void MinutesShow_textbox_TextChanged(object sender, EventArgs e)
        {
            if (ActiveControl != sender) return;

            byte temp;

            if (!byte.TryParse(ActiveControl.Text, out temp))
            {
                ActiveControl.BackColor = Color.Red;
                return;
            }
            ActiveControl.BackColor = SystemColors.Window;


            for (int i = 0; i < _selectedNPCInfos.Count; i++)
                _selectedNPCInfos[i].MinuteStart = temp;
        }

        private void Class_textbox_TextChanged(object sender, EventArgs e)
        {
            if (ActiveControl != sender) return;

            for (int i = 0; i < _selectedNPCInfos.Count; i++)
                _selectedNPCInfos[i].ClassRequired = ActiveControl.Text;
        }

        private void CopyMButton_Click(object sender, EventArgs e)
        {
            MessageBox.Show(Envir.Now.DayOfWeek.ToString());
        }

        private void MaxLev_textbox_TextChanged(object sender, EventArgs e)
        {
            if (ActiveControl != sender) return;

            short temp;

            if (!short.TryParse(ActiveControl.Text, out temp))
            {
                ActiveControl.BackColor = Color.Red;
                return;
            }
            ActiveControl.BackColor = SystemColors.Window;


            for (int i = 0; i < _selectedNPCInfos.Count; i++)
                _selectedNPCInfos[i].MaxLev = temp;
        }

        private void Class_combo_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (ActiveControl != sender) return;
            string temp = ActiveControl.Text;


            for (int i = 0; i < _selectedNPCInfos.Count; i++)
                _selectedNPCInfos[i].ClassRequired = temp;
        }

        private void Day_combo_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (ActiveControl != sender) return;
            string temp = ActiveControl.Text;


            for (int i = 0; i < _selectedNPCInfos.Count; i++)
                _selectedNPCInfos[i].DayofWeek = temp;
        }

        private void TimeVisible_checkbox_CheckedChanged(object sender, EventArgs e)
        {
            for (int i = 0; i < _selectedNPCInfos.Count; i++)
                _selectedNPCInfos[i].TimeVisible = TimeVisible_checkbox.Checked;
        }

        private void StartHour_combo_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (ActiveControl != sender) return;

            byte temp;

            if (!byte.TryParse(ActiveControl.Text, out temp))
            {
                ActiveControl.BackColor = Color.Red;
                return;
            }
            ActiveControl.BackColor = SystemColors.Window;

            for (int i = 0; i < _selectedNPCInfos.Count; i++)
                _selectedNPCInfos[i].HourStart = temp;
        }

        private void EndHour_combo_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (ActiveControl != sender) return;

            byte temp;

            if (!byte.TryParse(ActiveControl.Text, out temp))
            {
                ActiveControl.BackColor = Color.Red;
                return;
            }
            ActiveControl.BackColor = SystemColors.Window;

            for (int i = 0; i < _selectedNPCInfos.Count; i++)
                _selectedNPCInfos[i].HourEnd = temp;
        }

        private void StartMin_num_ValueChanged(object sender, EventArgs e)
        {
            for (int i = 0; i < _selectedNPCInfos.Count; i++)
                _selectedNPCInfos[i].MinuteStart = (byte)StartMin_num.Value;
        }

        private void EndMin_num_ValueChanged(object sender, EventArgs e)
        {
            for (int i = 0; i < _selectedNPCInfos.Count; i++)
                _selectedNPCInfos[i].MinuteEnd = (byte)EndMin_num.Value;
        }

        private void Flag_textbox_TextChanged(object sender, EventArgs e)
        {
            if (ActiveControl != sender) return;

            int temp;

            if (!int.TryParse(ActiveControl.Text, out temp))
            {
                ActiveControl.BackColor = Color.Red;
                return;
            }
            ActiveControl.BackColor = SystemColors.Window;

            for (int i = 0; i < _selectedNPCInfos.Count; i++)
                _selectedNPCInfos[i].FlagNeeded = temp;
        }

        private void dateTimePicker1_ValueChanged(object sender, EventArgs e)
        {
            MessageBox.Show(Envir.Now.TimeOfDay.ToString());
        }

        private void NPCInfoForm_Load(object sender, EventArgs e)
        {

        }

        private void ConquestHidden_combo_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (ActiveControl != sender) return;

            int conquestIndex = 0;

            if (ConquestHidden_combo.SelectedItem is ConquestInfo conquestInfo)
            {
                conquestIndex = conquestInfo.Index;
            }

            for (int i = 0; i < _selectedNPCInfos.Count; i++)
                _selectedNPCInfos[i].Conquest = conquestIndex;
        }

        private void ShowBigMapCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            if (ActiveControl != sender) return;

            for (int i = 0; i < _selectedNPCInfos.Count; i++)
                _selectedNPCInfos[i].ShowOnBigMap = ShowBigMapCheckBox.Checked;
        }

        private void BigMapIconTextBox_TextChanged(object sender, EventArgs e)
        {
            if (ActiveControl != sender) return;

            int temp;

            if (!int.TryParse(ActiveControl.Text, out temp))
            {
                ActiveControl.BackColor = Color.Red;
                return;
            }
            ActiveControl.BackColor = SystemColors.Window;


            for (int i = 0; i < _selectedNPCInfos.Count; i++)
                _selectedNPCInfos[i].BigMapIcon = temp;
        }

        private void TeleportToCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            if (ActiveControl != sender) return;

            for (int i = 0; i < _selectedNPCInfos.Count; i++)
                _selectedNPCInfos[i].CanTeleportTo = TeleportToCheckBox.Checked;
        }

        private void ConquestVisible_checkbox_CheckedChanged(object sender, EventArgs e)
        {
            if (ActiveControl != sender) return;

            for (int i = 0; i < _selectedNPCInfos.Count; i++)
                _selectedNPCInfos[i].ConquestVisible = ConquestVisible_checkbox.Checked;
        }
    }
}
