using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Shared.Extensions;
using LibraryEditor.Authoring;

namespace LibraryEditor
{
    public partial class LMain : Form
    {
        private readonly Dictionary<int, int> _indexList = new Dictionary<int, int>();
        private MLibraryV2 _library, _referenceLibrary, _shadowLibrary;
        private MLibraryV2.MImage _selectedImage, _exportImage;
        private Image _originalImage;
        public Bitmap _referenceImage;

        protected bool ImageTabActive = true;
        protected bool MaskTabActive = false;
        protected bool FrameTabActive = false;

        public bool ApplyOffsets => checkBox1.Checked;

        protected string ViewMode = "Image";

        private LibraryContentEditingSession _editingSession;
        private ResourceReferenceWorkspace _resourceWorkspace;
        private readonly Action<MLibraryV2> _persistLibrary;
        private readonly Func<string, bool> _confirmDiscard;
        private readonly Func<string, MLibraryV2> _loadLibrary;
        private Panel _resourceAnalysisPanel;
        private Panel _authoringHost;
        private TableLayoutPanel _authoringLayout;
        private TextBox _resourceAnalysisText;
        private TextBox _changeText;
        private Label _authoringStatusLabel;
        private Button _showAnalysisButton;
        private bool _allowClose;
        private bool _updatingControls;
        private bool _frameGridDirty;
        private bool _analysisExpanded = true;
        private bool _lastWideLayout = true;
        private int _workspaceFocusIndex = -1;

        [DllImport("user32.dll")]
        private static extern int SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

        public LMain()
            : this(null, null)
        {
        }

        public LMain(
            Action<MLibraryV2> persistLibrary,
            Func<string, bool> confirmDiscard,
            Func<string, MLibraryV2> loadLibrary = null)
        {
            _persistLibrary = persistLibrary ?? (library => library.Save());
            _confirmDiscard = confirmDiscard ?? (message => MessageBox.Show(
                message,
                "未保存修改",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) == DialogResult.Yes);
            _loadLibrary = loadLibrary ?? (fileName => new MLibraryV2(fileName));
            InitializeComponent();
            BuildAuthoringWorkspace();
            frameGridView.CellValueChanged += (_, _) => MarkFrameGridDirty();
            frameGridView.RowsAdded += (_, _) => MarkFrameGridDirty();
            frameGridView.RowsRemoved += (_, _) => MarkFrameGridDirty();

            this.FrameAction.ValueType = typeof(MirAction);
            this.FrameAction.DataSource = Enum.GetValues(typeof(MirAction));


            SendMessage(PreviewListView.Handle, 4149, 0, 5242946); //80 x 66

            this.AllowDrop = true;
            this.DragEnter += new DragEventHandler(Form1_DragEnter);
            this.DragDrop += new DragEventHandler(Form1_DragDrop);

            if (Program.openFileWith.Length > 0 && File.Exists(Program.openFileWith))
            {
                LoadLibraryForAuthoring(Program.openFileWith);
            }
        }

        public bool HasUnsavedChanges => _editingSession != null && (_editingSession.IsDirty || _frameGridDirty);

        public string CurrentLibraryPath => _library?.FileName ?? string.Empty;

        public string CurrentResourceOwnerPath { get; private set; } = string.Empty;

        public string GetDraftChanges()
        {
            if (_editingSession == null) return "未打开资源库";
            string changes = _editingSession.DescribeChanges();
            if (!_frameGridDirty) return changes;
            return changes == "无变更" ? "帧表：已修改" : changes + Environment.NewLine + "帧表：已修改";
        }

        public string GetResourceAnalysis() => _resourceAnalysisText?.Text ?? string.Empty;

        public string GetAuthoringStatus() => _authoringStatusLabel?.Text ?? string.Empty;

        public IReadOnlyList<LibraryContentDiagnostic> ValidateDraft()
            => _editingSession?.Validate() ?? Array.Empty<LibraryContentDiagnostic>();

        public IReadOnlyList<string> GetResourceOwners(string resourcePath)
            => _resourceWorkspace?.Report.GetOwners(resourcePath) ?? Array.Empty<string>();

        public bool SetImageOffsetForAuthoring(int imageIndex, short x, short y)
        {
            if (_library == null || imageIndex < 0 || imageIndex >= _library.Images.Count) return false;
            MLibraryV2.MImage image = _library.GetMImage(imageIndex);
            if (image == null) return false;
            image.X = x;
            image.Y = y;
            RefreshAuthoringState();
            return true;
        }

        public bool NavigateToResource(string resourcePath)
        {
            if (_resourceWorkspace == null) return false;
            ResourceAsset asset = _resourceWorkspace.Assets.FirstOrDefault(item =>
                string.Equals(item.ResourcePath, ResourceReferenceAnalyzer.NormalizePath(resourcePath),
                    StringComparison.OrdinalIgnoreCase));
            if (asset == null)
            {
                string owner = _resourceWorkspace.Report.GetOwners(resourcePath).FirstOrDefault();
                return !string.IsNullOrWhiteSpace(owner) && NavigateToResourceOwner(resourcePath, owner);
            }
            string fullPath = Path.Combine(_resourceWorkspace.ResourceRoot,
                asset.ResourcePath.Replace('/', Path.DirectorySeparatorChar));
            if (!string.Equals(Path.GetExtension(fullPath), ".Lib", StringComparison.OrdinalIgnoreCase)) return false;
            LoadLibraryForAuthoring(fullPath);
            return string.Equals(Path.GetFullPath(_library?.FileName ?? string.Empty), Path.GetFullPath(fullPath),
                StringComparison.OrdinalIgnoreCase);
        }

        public bool NavigateToResourceOwner(string resourcePath, string owner)
        {
            if (_resourceWorkspace == null) return false;
            ResourceReference reference = _resourceWorkspace.References.FirstOrDefault(item =>
                string.Equals(item.ResourcePath, ResourceReferenceAnalyzer.NormalizePath(resourcePath), StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.Owner, owner, StringComparison.OrdinalIgnoreCase));
            if (reference == null || string.IsNullOrWhiteSpace(reference.OwnerPath) || !File.Exists(reference.OwnerPath))
                return false;

            CurrentResourceOwnerPath = Path.GetFullPath(reference.OwnerPath);
            SetAnalysisPanelVisible(true);
            string marker = $"{reference.ResourcePath} ← {reference.Owner}";
            int position = _resourceAnalysisText.Text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (position >= 0)
            {
                _resourceAnalysisText.Select(position, marker.Length);
                _resourceAnalysisText.ScrollToCaret();
            }
            _resourceAnalysisText.Focus();
            SetAuthoringStatus($"已定位清单拥有记录：{Path.GetFileName(reference.OwnerPath)}", true);
            return true;
        }

        public void LoadLibraryForAuthoring(string filename)
        {
            if (!CanDiscardChanges()) return;
            OpenLibraryCore(filename);
        }

        public void LoadResourceWorkspace(string resourceRoot, string packageManifestPath)
        {
            _resourceWorkspace = ResourceReferenceWorkspace.Load(resourceRoot, packageManifestPath);
            RefreshResourceAnalysis();
        }

        public async Task<bool> LoadResourceWorkspaceAsync(string resourceRoot, string packageManifestPath)
        {
            SetAuthoringStatus("正在分析资源引用…", true);
            try
            {
                ResourceReferenceWorkspace workspace = await Task.Run(() =>
                    ResourceReferenceWorkspace.Load(resourceRoot, packageManifestPath));
                if (IsDisposed || Disposing) return false;
                _resourceWorkspace = workspace;
                RefreshResourceAnalysis();
                SetAuthoringStatus("资源分析完成", true);
                return true;
            }
            catch (Exception ex)
            {
                if (!IsDisposed && !Disposing) SetAuthoringStatus(ex.Message, false);
                return false;
            }
        }

        public bool TrySaveDraft(out string error)
        {
            if (_editingSession == null)
            {
                error = "未打开资源库。";
                return false;
            }
            UpdateFrameGridData();
            if (_resourceWorkspace?.Report.MissingReferences.Count > 0)
            {
                ResourceReferenceDiagnostic diagnostic = _resourceWorkspace.Report.MissingReferences[0];
                error = string.Join(Environment.NewLine, _resourceWorkspace.Report.MissingReferences
                    .Select(item => $"{item.Code} {item.Message}"));
                NavigateToResourceOwner(diagnostic.ResourcePath, diagnostic.Owner);
                SetAuthoringStatus(error, false);
                return false;
            }
            if (!_editingSession.TryValidateAndCommit(_persistLibrary, out IReadOnlyList<LibraryContentDiagnostic> diagnostics, out error))
            {
                SetAuthoringStatus(error, false);
                if (diagnostics.Count > 0) SelectDiagnostic(diagnostics[0]);
                RefreshAuthoringState();
                return false;
            }
            _library = _editingSession.Draft;
            _frameGridDirty = false;
            SetAuthoringStatus("已保存", true);
            RefreshAuthoringState();
            return true;
        }

        public bool ReloadDraft()
        {
            if (_editingSession == null || !CanDiscardChanges()) return false;
            try
            {
                _editingSession.Reload();
                _library = _editingSession.Draft;
                _frameGridDirty = false;
                ResetLibrarySurface();
                SetAuthoringStatus("已从磁盘重载", true);
                return true;
            }
            catch (Exception ex)
            {
                RefreshAuthoringState();
                SetAuthoringStatus($"重载失败：{ex.Message}；已保留当前草稿。", false);
                return false;
            }
        }

        public bool NavigateToImage(int imageIndex)
        {
            if (_library == null || imageIndex < 0 || imageIndex >= _library.Images.Count) return false;
            PreviewListView.SelectedIndices.Clear();
            PreviewListView.SelectedIndices.Add(imageIndex);
            PreviewListView.EnsureVisible(imageIndex);
            return true;
        }

        public void SetAnalysisPanelVisible(bool visible)
        {
            _analysisExpanded = visible;
            ApplyAnalysisPanelLayout();
        }

        public string MoveWorkspaceFocusForAuthoring(bool reverse)
        {
            Control[] surfaces =
            [
                AddButton,
                PreviewListView,
                _resourceAnalysisPanel.Visible ? _resourceAnalysisText : _showAnalysisButton
            ];
            _workspaceFocusIndex = reverse
                ? (_workspaceFocusIndex - 1 + surfaces.Length) % surfaces.Length
                : (_workspaceFocusIndex + 1) % surfaces.Length;
            surfaces[_workspaceFocusIndex].Focus();
            return surfaces[_workspaceFocusIndex].Name;
        }

        private void Form1_DragDrop(object sender, DragEventArgs e)
        {
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);

            if (Path.GetExtension(files[0]).ToUpper() == ".WIL" ||
                Path.GetExtension(files[0]).ToUpper() == ".WZL" ||
                Path.GetExtension(files[0]).ToUpper() == ".MIZ")
            {
                toolStripProgressBar.Maximum = files.Length;
                toolStripProgressBar.Value = 0;

                new Action(() =>
                {
                    try
                    {
                        ParallelOptions options = new ParallelOptions { MaxDegreeOfParallelism = 8 };
                        Parallel.For(0, files.Length, options, i =>
                        {
                            if (Path.GetExtension(files[i]) == ".wtl")
                            {
                                WTLLibrary WTLlib = new WTLLibrary(files[i]);
                                WTLlib.ToMLibrary();
                            }
                            else
                            {
                                WeMadeLibrary WILlib = new WeMadeLibrary(files[i]);
                                WILlib.ToMLibrary();
                            }

                            Invoke(new Action(() =>
                            {
                                toolStripProgressBar.Value++;
                            }));

                        });
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(ex.ToString());
                    }

                    Invoke(new Action(() =>
                    {
                        toolStripProgressBar.Value = 0;
                    }));

                    MessageBox.Show(
                        string.Format("已成功转换 {0} {1}",
                            (files.Length).ToString(),
                            (files.Length > 1) ? "libraries" : "library"));
                }).BeginInvoke(null, null);
            }
            else if (Path.GetExtension(files[0]).ToUpper() == ".LIB")
            {
                LoadLibraryForAuthoring(files[0]);
            }
            else
            {
                return;
            }
        }

        private void Form1_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy;
        }

        private void ClearInterface()
        {
            _selectedImage = null;
            ImageBox.Image = null;
            ZoomTrackBar.Value = 1;

            WidthLabel.Text = "<空>";
            HeightLabel.Text = "<空>";
            numericUpDownX.Value = 0;
            numericUpDownY.Value = 0;
        }

        public static Bitmap AddPaddingToBitmap(Bitmap originalBitmap, int padding)
        {
            int newWidth = originalBitmap.Width + 2 * padding;
            int newHeight = originalBitmap.Height + 2 * padding;

            Bitmap paddedBitmap = new Bitmap(newWidth, newHeight);

            using (Graphics g = Graphics.FromImage(paddedBitmap))
            {
                g.Clear(Color.Transparent);

                int x = (paddedBitmap.Width - originalBitmap.Width) / 2;
                int y = (paddedBitmap.Height - originalBitmap.Height) / 2;

                g.DrawImage(originalBitmap, x, y);
            }

            return paddedBitmap;
        }

        private void PreviewListView_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (PreviewListView.SelectedIndices.Count == 0)
            {
                ClearInterface();
                return;
            }

            _selectedImage = _library.GetMImage(PreviewListView.SelectedIndices[0]);

            if (_selectedImage == null)
            {
                ClearInterface();
                return;
            }

            WidthLabel.Text = _selectedImage.Width.ToString();
            HeightLabel.Text = _selectedImage.Height.ToString();

            numericUpDownX.Value = _selectedImage.X;
            numericUpDownY.Value = _selectedImage.Y;

            Bitmap referenceImage = null;
            MLibraryV2.MImage referenceMImage = null;
            if (_referenceLibrary != null)
            {
                referenceMImage = _referenceLibrary.GetMImage(PreviewListView.SelectedIndices[0]);
                if (referenceMImage != null)
                {
                    referenceImage = referenceMImage.Image;
                }
            }

            Bitmap image = null;
            if (ViewMode == "Image")
                image = _selectedImage.Image;
            else
                image = _selectedImage.MaskImage;

            if (image == null)
            {
                ImageBox.Image = null;
                return;
            }

            Bitmap newImage = null;
            if (!ApplyOffsets)
            {
                newImage = new Bitmap(Math.Max(_referenceImage?.Width ?? 0, Math.Max(image.Width, referenceImage?.Width ?? 0)), Math.Max(_referenceImage?.Height ?? 0, Math.Max(image.Height, referenceImage?.Height ?? 0)));
                using (var g = Graphics.FromImage(newImage))
                {
                    if (_referenceImage != null)
                        g.DrawImage(_referenceImage, Point.Empty);
                    if (referenceImage != null)
                        g.DrawImage(referenceImage, Point.Empty);
                    g.DrawImage(image, Point.Empty);
                }
            }
            else
            {
                var maxWidth = Math.Max(image.Width, referenceImage?.Width ?? 0);
                var maxHeight = Math.Max(image.Height, referenceImage?.Height ?? 0);

                int offsetX = 0;
                int offsetY = 0;
                if (referenceImage != null)
                {
                    offsetX = -_selectedImage.X + referenceMImage.X;
                    offsetY = -_selectedImage.Y + referenceMImage.Y;
                }
                maxWidth += Math.Abs(offsetX);
                maxHeight += Math.Abs(offsetY);

                newImage = new Bitmap(maxWidth, maxHeight);
                using (var g = Graphics.FromImage(newImage))
                {
                    if (referenceImage != null)
                        g.DrawImage(referenceImage, new Point(offsetX > 0 ? offsetX : 0, offsetY > 0 ? offsetY : 0));
                    g.DrawImage(image, new Point(offsetX < 0 ? Math.Abs(offsetX) : 0, offsetY < 0 ? Math.Abs(offsetY) : 0));
                }

                if (_referenceImage != null)
                {
                    var newMaxWidth = Math.Max(_referenceImage.Width, newImage.Width + Math.Abs(_selectedImage.X));
                    var newMaxHeight = Math.Max(_referenceImage.Height, newImage.Height + Math.Abs(_selectedImage.Y));

                    var anotherNewBitmap = new Bitmap(newMaxWidth, newMaxHeight);
                    using (var g = Graphics.FromImage(anotherNewBitmap))
                    {
                        g.DrawImage(_referenceImage, new Point(_selectedImage.X < 0 ? Math.Abs(_selectedImage.X) : 0, _selectedImage.Y < 0 ? Math.Abs(_selectedImage.Y) : 0));
                        g.DrawImage(image, new Point(_selectedImage.X > 0 ? _selectedImage.X : 0, _selectedImage.Y > 0 ? _selectedImage.Y : 0));
                    }
                    newImage = anotherNewBitmap;
                }
            }

            ImageBox.Image = newImage;
            int globalOffsetX = _referenceImage != null ? 0 : referenceMImage?.X ?? _selectedImage.X;
            int globalOffsetY = _referenceImage != null ? 0 : referenceMImage?.Y ?? _selectedImage.Y;

            ImageBox.Location = ApplyOffsets ? new Point(100 + globalOffsetX, 100 + globalOffsetY) : Point.Empty;

            // Keep track of what image/s are selected.
            if (PreviewListView.SelectedIndices.Count > 1)
            {
                toolStripStatusLabel.ForeColor = Color.Red;
                toolStripStatusLabel.Text = "选择多个图像";
            }
            else
            {
                toolStripStatusLabel.ForeColor = SystemColors.ControlText;
                toolStripStatusLabel.Text = "选定的图像: " + string.Format("{0} / {1}",
                PreviewListView.SelectedIndices[0].ToString(),
                (PreviewListView.Items.Count - 1).ToString());
            }

            nudJump.Value = PreviewListView.SelectedIndices[0];
        }

        private void PreviewListView_RetrieveVirtualItem(object sender, RetrieveVirtualItemEventArgs e)
        {
            int index;

            if (_indexList.TryGetValue(e.ItemIndex, out index))
            {
                e.Item = new ListViewItem { ImageIndex = index, Text = e.ItemIndex.ToString() };
                return;
            }

            _indexList.Add(e.ItemIndex, ImageList.Images.Count);
            ImageList.Images.Add(_library.GetPreview(e.ItemIndex));
            e.Item = new ListViewItem { ImageIndex = index, Text = e.ItemIndex.ToString() };
        }

        private void AddButton_Click(object sender, EventArgs e)
        {
            if (_library == null) return;
            if (_library.FileName == null) return;

            if (ImportImageDialog.ShowDialog() != DialogResult.OK) return;

            List<string> fileNames = new List<string>(ImportImageDialog.FileNames);

            //fileNames.Sort();
            toolStripProgressBar.Value = 0;
            toolStripProgressBar.Maximum = fileNames.Count;

            for (int i = 0; i < fileNames.Count; i++)
            {
                string fileName = fileNames[i];
                Bitmap image;

                try
                {
                    image = new Bitmap(fileName);
                }
                catch
                {
                    continue;
                }

                fileName = Path.Combine(Path.GetDirectoryName(fileName), "Placements", Path.GetFileNameWithoutExtension(fileName));
                fileName = Path.ChangeExtension(fileName, ".txt");

                short x = 0;
                short y = 0;

                if (File.Exists(fileName))
                {
                    string[] placements = File.ReadAllLines(fileName);

                    if (placements.Length > 0)
                        short.TryParse(placements[0], out x);
                    if (placements.Length > 1)
                        short.TryParse(placements[1], out y);
                }

                _library.AddImage(image, x, y, checkboxRemoveBlackOnImport.Checked);
                toolStripProgressBar.Value++;
                //image.Dispose();
            }

            PreviewListView.VirtualListSize = _library.Images.Count;
            toolStripProgressBar.Value = 0;
            RefreshAuthoringState();
        }

        private void newToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (SaveLibraryDialog.ShowDialog() != DialogResult.OK) return;

            var newLibrary = new MLibraryV2(SaveLibraryDialog.FileName);
            PreviewListView.VirtualListSize = 0;
            newLibrary.Save();
            var newSession = new LibraryContentEditingSession(newLibrary);
            LibraryContentEditingSession oldSession = _editingSession;
            _editingSession = newSession;
            _library = newSession.Draft;
            oldSession?.Dispose();
            ResetLibrarySurface();
        }

        private void openToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (OpenLibraryDialog.ShowDialog() != DialogResult.OK) return;

            _referenceLibrary?.Dispose();
            _referenceLibrary = null;
            _referenceImage?.Dispose();
            _referenceImage = null;
            LoadLibraryForAuthoring(OpenLibraryDialog.FileName);
        }

        private void OpenLibraryCore(string filename)
        {
            var newLibrary = _loadLibrary(filename);
            var newSession = new LibraryContentEditingSession(newLibrary, _loadLibrary);
            ClearInterface();
            ImageList.Images.Clear();
            PreviewListView.Items.Clear();
            _indexList.Clear();

            LibraryContentEditingSession oldSession = _editingSession;
            _editingSession = newSession;
            _library = newSession.Draft;
            oldSession?.Dispose();
            PreviewListView.VirtualListSize = _library.Images.Count;

            // Show .Lib path in application title.
            this.Text = filename;

            PreviewListView.SelectedIndices.Clear();

            if (PreviewListView.Items.Count > 0)
                PreviewListView.Items[0].Selected = true;

            UpdateFrameGridView();
            TryLoadAssociatedWorkspace(filename);
            RefreshAuthoringState();
        }

        private void OpenReferenceLibrary(string filename)
        {
            var replacement = new MLibraryV2(filename);
            MLibraryV2 old = _referenceLibrary;
            _referenceLibrary = replacement;
            _referenceImage?.Dispose();
            _referenceImage = null;
            old?.Dispose();
        }

        private void OpenShadowLibraryAndImport(string filename)
        {
            if (_library == null) return;

            var replacement = new MLibraryV2(filename);
            MLibraryV2 old = _shadowLibrary;
            _shadowLibrary = replacement;
            old?.Dispose();

            ImageList.Images.Clear();
            _indexList.Clear();

            for (int i = 0; i < _library.Images.Count; i++)
            {
                var mImage = _library.GetMImage(i);
                if (mImage == null || mImage.Image == null) continue;

                var shadowImage = _shadowLibrary.GetMImage(i);
                if (shadowImage == null || shadowImage.Image == null) continue;

                var offSetX = -mImage.X + shadowImage.X;
                var offSetY = -mImage.Y + shadowImage.Y;

                var maxWidth = Math.Max(mImage.Width, shadowImage.Width + Math.Abs(offSetX));
                var maxHeight = Math.Max(mImage.Height, shadowImage.Height + Math.Abs(offSetY));

                var newBitmap = new Bitmap(maxWidth, maxHeight);
                using (var g = Graphics.FromImage(newBitmap))
                {
                    g.DrawImage(mImage.Image, new Point(offSetX < 0 ? Math.Abs(offSetX) : 0, offSetY < 0 ? Math.Abs(offSetY) : 0));
                    g.DrawImage(shadowImage.Image, new Point(offSetX > 0 ? offSetX : 0, offSetY > 0 ? offSetY : 0));
                }

                _library.ReplaceImage(i, newBitmap, mImage.X, mImage.Y, checkboxRemoveBlackOnImport.Checked);
            }

            PreviewListView.VirtualListSize = _library.Images.Count;

            try
            {
                PreviewListView.RedrawItems(0, PreviewListView.Items.Count - 1, true);

                if (ViewMode == "Image")
                {
                    ImageBox.Image = _library.Images[PreviewListView.SelectedIndices[0]].Image;
                }
                else
                {
                    ImageBox.Image = _library.Images[PreviewListView.SelectedIndices[0]].MaskImage;
                }
            }
            catch (Exception)
            {
                return;
            }
        }

        private void saveToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!TrySaveDraft(out string error))
                MessageBox.Show(error, "保存失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private void saveAsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (_library == null) return;
            if (SaveLibraryDialog.ShowDialog() != DialogResult.OK) return;

            string previousPath = _library.FileName;
            _library.FileName = SaveLibraryDialog.FileName;
            if (!TrySaveDraft(out string error))
            {
                _library.FileName = previousPath;
                MessageBox.Show(error, "另存失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void closeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        private void DeleteButton_Click(object sender, EventArgs e)
        {
            if (_library == null) return;
            if (_library.FileName == null) return;
            if (PreviewListView.SelectedIndices.Count == 0) return;

            if (MessageBox.Show("确定要删除所选图像？",
                "删除所选内容",
                MessageBoxButtons.YesNoCancel) != DialogResult.Yes) return;

            List<int> removeList = new List<int>();

            for (int i = 0; i < PreviewListView.SelectedIndices.Count; i++)
                removeList.Add(PreviewListView.SelectedIndices[i]);

            removeList.Sort();

            for (int i = removeList.Count - 1; i >= 0; i--)
                _library.RemoveImage(removeList[i]);

            ImageList.Images.Clear();
            _indexList.Clear();
            PreviewListView.VirtualListSize -= removeList.Count;
            RefreshAuthoringState();
        }

        private void convertToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (OpenWeMadeDialog.ShowDialog() != DialogResult.OK) return;

            toolStripProgressBar.Maximum = OpenWeMadeDialog.FileNames.Length;
            toolStripProgressBar.Value = 0;

            try
            {
                Task.Factory.StartNew(() =>
                {
                    ParallelOptions options = new ParallelOptions { MaxDegreeOfParallelism = 8 };
                    Parallel.For(0, OpenWeMadeDialog.FileNames.Length, options, i =>
                            {
                                var fileName = OpenWeMadeDialog.FileNames[i];
                                var ext = Path.GetExtension(fileName).ToUpper();
                                if (ext == ".WTL")
                                {
                                    WTLLibrary WTLlib = new WTLLibrary(fileName);
                                    WTLlib.ToMLibrary();
                                }
                                else if (ext == ".LIB")
                                {
                                    MLibraryV1 v1Lib = new MLibraryV1(fileName);
                                    v1Lib.ToMLibrary();
                                }
                                else
                                {
                                    WeMadeLibrary WILlib = new WeMadeLibrary(fileName);
                                    WILlib.ToMLibrary();
                                }
                                Invoke(new Action(() => { toolStripProgressBar.Value++; }));
                            });
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString());
            }

            toolStripProgressBar.Value = 0;

            MessageBox.Show(string.Format("已成功转换 {0} {1}",
                (OpenWeMadeDialog.FileNames.Length).ToString(),
                (OpenWeMadeDialog.FileNames.Length > 1) ? "libraries" : "library"));
        }

        private void copyToToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (PreviewListView.SelectedIndices.Count == 0) return;
            if (SaveLibraryDialog.ShowDialog() != DialogResult.OK) return;

            MLibraryV2 tempLibrary = new MLibraryV2(SaveLibraryDialog.FileName);

            List<int> copyList = new List<int>();

            for (int i = 0; i < PreviewListView.SelectedIndices.Count; i++)
                copyList.Add(PreviewListView.SelectedIndices[i]);

            copyList.Sort();

            for (int i = 0; i < copyList.Count; i++)
            {
                MLibraryV2.MImage image = _library.GetMImage(copyList[i]);
                tempLibrary.AddImage(image.Image, image.MaskImage, image.X, image.Y, checkboxRemoveBlackOnImport.Checked);
            }

            tempLibrary.Save();
        }

        private void removeBlanksToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show("确定要删除空白图像？",
                "删除空白图像",
                MessageBoxButtons.YesNo) != DialogResult.Yes) return;

            _library.RemoveBlanks();
            ImageList.Images.Clear();
            _indexList.Clear();
            PreviewListView.VirtualListSize = _library.Count;
            RefreshAuthoringState();
        }

        private void countBlanksToolStripMenuItem_Click(object sender, EventArgs e)
        {
            OpenLibraryDialog.Multiselect = true;

            if (OpenLibraryDialog.ShowDialog() != DialogResult.OK)
            {
                OpenLibraryDialog.Multiselect = false;
                return;
            }

            OpenLibraryDialog.Multiselect = false;

            MLibraryV2.Load = false;

            int count = 0;

            for (int i = 0; i < OpenLibraryDialog.FileNames.Length; i++)
            {
                MLibraryV2 library = new MLibraryV2(OpenLibraryDialog.FileNames[i]);

                for (int x = 0; x < library.Count; x++)
                {
                    if (library.Images[x].Length <= 8)
                        count++;
                }

                library.Close();
            }

            MLibraryV2.Load = true;
            MessageBox.Show(count.ToString());
        }

        private void InsertImageButton_Click(object sender, EventArgs e)
        {
            if (_library == null) return;
            if (_library.FileName == null) return;
            if (PreviewListView.SelectedIndices.Count == 0) return;
            if (ImportImageDialog.ShowDialog() != DialogResult.OK) return;

            List<string> fileNames = new List<string>(ImportImageDialog.FileNames);

            //fileNames.Sort();

            int index = PreviewListView.SelectedIndices[0];

            toolStripProgressBar.Value = 0;
            toolStripProgressBar.Maximum = fileNames.Count;

            for (int i = fileNames.Count - 1; i >= 0; i--)
            {
                string fileName = fileNames[i];

                Bitmap image;

                try
                {
                    image = new Bitmap(fileName);
                }
                catch
                {
                    continue;
                }

                fileName = Path.Combine(Path.GetDirectoryName(fileName), "Placements", Path.GetFileNameWithoutExtension(fileName));
                fileName = Path.ChangeExtension(fileName, ".txt");

                short x = 0;
                short y = 0;

                if (File.Exists(fileName))
                {
                    string[] placements = File.ReadAllLines(fileName);

                    if (placements.Length > 0)
                        short.TryParse(placements[0], out x);
                    if (placements.Length > 1)
                        short.TryParse(placements[1], out y);
                }

                _library.InsertImage(index, image, x, y, checkboxRemoveBlackOnImport.Checked);

                toolStripProgressBar.Value++;
            }

            ImageList.Images.Clear();
            _indexList.Clear();
            PreviewListView.VirtualListSize = _library.Images.Count;
            toolStripProgressBar.Value = 0;
            RefreshAuthoringState();
        }

        private void safeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show("确定要删除空白图像？",
                "删除空白图像", MessageBoxButtons.YesNo) != DialogResult.Yes) return;

            _library.RemoveBlanks(true);
            ImageList.Images.Clear();
            _indexList.Clear();
            PreviewListView.VirtualListSize = _library.Count;
            RefreshAuthoringState();
        }

        private const int HowDeepToScan = 6;

        public static void ProcessDir(string sourceDir, int recursionLvl, string outputDir)
        {
            if (recursionLvl <= HowDeepToScan)
            {
                // Process the list of files found in the directory.
                string[] fileEntries = Directory.GetFiles(sourceDir);
                foreach (string fileName in fileEntries)
                {
                    if (Directory.Exists(outputDir) != true) Directory.CreateDirectory(outputDir);
                    MLibraryV0 OldLibrary = new MLibraryV0(fileName);
                    MLibraryV2 NewLibrary = new MLibraryV2(outputDir + Path.GetFileName(fileName)) { Images = new List<MLibraryV2.MImage>(), IndexList = new List<int>(), Count = OldLibrary.Images.Count }; ;
                    for (int i = 0; i < OldLibrary.Images.Count; i++)
                        NewLibrary.Images.Add(null);
                    for (int j = 0; j < OldLibrary.Images.Count; j++)
                    {
                        MLibraryV0.MImage oldimage = OldLibrary.GetMImage(j);
                        NewLibrary.Images[j] = new MLibraryV2.MImage(oldimage.FBytes, oldimage.Width, oldimage.Height) { X = oldimage.X, Y = oldimage.Y };
                    }
                    NewLibrary.Save();
                    for (int i = 0; i < NewLibrary.Images.Count; i++)
                    {
                        if (NewLibrary.Images[i].Preview != null)
                            NewLibrary.Images[i].Preview.Dispose();
                        if (NewLibrary.Images[i].Image != null)
                            NewLibrary.Images[i].Image.Dispose();
                        if (NewLibrary.Images[i].MaskImage != null)
                            NewLibrary.Images[i].MaskImage.Dispose();
                    }
                    for (int i = 0; i < OldLibrary.Images.Count; i++)
                    {
                        if (OldLibrary.Images[i].Preview != null)
                            OldLibrary.Images[i].Preview.Dispose();
                        if (OldLibrary.Images[i].Image != null)
                            OldLibrary.Images[i].Image.Dispose();
                    }
                    NewLibrary.Images.Clear();
                    NewLibrary.IndexList.Clear();
                    OldLibrary.Images.Clear();
                    OldLibrary.IndexList.Clear();
                    NewLibrary.Close();
                    OldLibrary.Close();
                    NewLibrary = null;
                    OldLibrary = null;
                }

                // Recurse into subdirectories of this directory.
                string[] subdirEntries = Directory.GetDirectories(sourceDir);
                foreach (string subdir in subdirEntries)
                {
                    // Do not iterate through re-parse points.
                    if (Path.GetFileName(Path.GetFullPath(subdir).TrimEnd(Path.DirectorySeparatorChar)) == Path.GetFileName(Path.GetFullPath(outputDir).TrimEnd(Path.DirectorySeparatorChar))) continue;
                    if ((File.GetAttributes(subdir) &
                         FileAttributes.ReparsePoint) !=
                             FileAttributes.ReparsePoint)
                        ProcessDir(subdir, recursionLvl + 1, outputDir + " \\" + Path.GetFileName(Path.GetFullPath(subdir).TrimEnd(Path.DirectorySeparatorChar)) + "\\");
                }
            }
        }

        // Export a single image.
        private void ExportButton_Click(object sender, EventArgs e)
        {
            if (_library == null || _library.FileName == null || PreviewListView.SelectedIndices.Count == 0)
                return;

            string _fileName = Path.GetFileName(OpenLibraryDialog.FileName);
            string _newName = _fileName.Remove(_fileName.IndexOf('.'));
            string _folder = Application.StartupPath + "\\Exported\\" + _newName + "\\";

            Bitmap blank = new Bitmap(1, 1);

            // Create the folder if it doesn't exist.
            (new FileInfo(_folder)).Directory.Create();

            ListView.SelectedIndexCollection _col = PreviewListView.SelectedIndices;

            toolStripProgressBar.Value = 0;
            toolStripProgressBar.Maximum = _col.Count;

            DialogResult result = MessageBox.Show("是保存为BMP格式、否保存PNG格式？或取消操作", "选择保存格式", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (result == DialogResult.Cancel)
            {
                toolStripProgressBar.Value = 0;
                return;
            }
            string fileExtension = (result == DialogResult.Yes) ? ".bmp" : ".png";
            ImageFormat imageFormat = (result == DialogResult.Yes) ? ImageFormat.Bmp : ImageFormat.Png;

            for (int i = _col[0]; i < (_col[0] + _col.Count); i++)
            {
                _exportImage = _library.GetMImage(i);
                if (_exportImage.Image == null)
                {
                    blank.Save(_folder + i.ToString() + fileExtension, imageFormat);
                }
                else
                {
                    _exportImage.Image.Save(_folder + i.ToString() + fileExtension, imageFormat);
                }

                toolStripProgressBar.Value++;

                if (!Directory.Exists(_folder + "/Placements/"))
                    Directory.CreateDirectory(_folder + "/Placements/");

                File.WriteAllLines(_folder + "/Placements/" + i.ToString() + ".txt", new string[] { _exportImage.X.ToString(), _exportImage.Y.ToString() });
            }

            toolStripProgressBar.Value = 0;
            MessageBox.Show("图像保存到 " + _folder + "...", "已完成", MessageBoxButtons.OK);
        }

        // Don't let the splitter go out of sight on resizing.
        private void LMain_Resize(object sender, EventArgs e)
        {
            if (splitContainer1.SplitterDistance <= this.Height - 150) return;
            if (this.Height - 150 > 0)
            {
                splitContainer1.SplitterDistance = this.Height - 150;
            }
        }

        // Resize the image(Zoom).
        private Image ImageBoxZoom(Image image, Size size)
        {
            _originalImage = _selectedImage.Image;
            Bitmap _bmp = new Bitmap(_originalImage, Convert.ToInt32(_originalImage.Width * size.Width), Convert.ToInt32(_originalImage.Height * size.Height));
            Graphics _gfx = Graphics.FromImage(_bmp);
            return _bmp;
        }

        // Zoom in and out.
        private void ZoomTrackBar_Scroll(object sender, EventArgs e)
        {
            if (ImageBox.Image == null)
            {
                ZoomTrackBar.Value = 1;
            }
            if (ZoomTrackBar.Value > 0)
            {
                try
                {
                    PreviewListView.Items[(int)nudJump.Value].EnsureVisible();

                    Bitmap _newBMP = new Bitmap(_selectedImage.Width * ZoomTrackBar.Value, _selectedImage.Height * ZoomTrackBar.Value);
                    using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(_newBMP))
                    {
                        if (checkBoxPreventAntiAliasing.Checked == true)
                        {
                            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            g.CompositingMode = CompositingMode.SourceCopy;
                        }

                        if (checkBoxQuality.Checked == true)
                        {
                            g.InterpolationMode = InterpolationMode.NearestNeighbor;
                        }

                        g.DrawImage(_selectedImage.Image, new Rectangle(0, 0, _newBMP.Width, _newBMP.Height));
                    }
                    ImageBox.Image = _newBMP;

                    toolStripStatusLabel.ForeColor = SystemColors.ControlText;
                    toolStripStatusLabel.Text = "选定的图像: " + string.Format("{0} / {1}",
                        PreviewListView.SelectedIndices[0].ToString(),
                        (PreviewListView.Items.Count - 1).ToString());
                }
                catch
                {
                    return;
                }
            }
        }

        // Swap the image panel background colour Black/White.
        private void pictureBox_Click(object sender, EventArgs e)
        {
            if (panel.BackColor == Color.Black)
            {
                panel.BackColor = Color.GhostWhite;
            }
            else
            {
                panel.BackColor = Color.Black;
            }
        }

        private void PreviewListView_VirtualItemsSelectionRangeChanged(object sender, ListViewVirtualItemsSelectionRangeChangedEventArgs e)
        {
            // Keep track of what image/s are selected.
            ListView.SelectedIndexCollection _col = PreviewListView.SelectedIndices;

            if (_col.Count > 1)
            {
                toolStripStatusLabel.ForeColor = Color.Red;
                toolStripStatusLabel.Text = "选择了多个图像";
            }
        }

        private void buttonReplace_Click(object sender, EventArgs e)
        {
            if (_library == null) return;
            if (_library.FileName == null) return;
            if (PreviewListView.SelectedIndices.Count == 0) return;

            OpenFileDialog ofd = new OpenFileDialog();
            ofd.ShowDialog();

            if (ofd.FileName == "") return;

            Bitmap newBmp = new Bitmap(ofd.FileName);

            ImageList.Images.Clear();
            _indexList.Clear();
            _library.ReplaceImage(PreviewListView.SelectedIndices[0], newBmp, 0, 0, checkboxRemoveBlackOnImport.Checked);
            PreviewListView.VirtualListSize = _library.Images.Count;
            RefreshAuthoringState();

            try
            {
                PreviewListView.RedrawItems(0, PreviewListView.Items.Count - 1, true);

                if (ViewMode == "Image")
                {
                    ImageBox.Image = _library.Images[PreviewListView.SelectedIndices[0]].Image;
                }
                else
                {
                    ImageBox.Image = _library.Images[PreviewListView.SelectedIndices[0]].MaskImage;
                }
            }
            catch (Exception)
            {
                return;
            }
        }

        private void previousImageToolStripMenuItem_Click(object sender, EventArgs e)
        {
            try
            {
                if (PreviewListView.Visible && PreviewListView.Items.Count > 0)
                {
                    int index = PreviewListView.SelectedIndices[0];
                    index = index - 1;
                    PreviewListView.SelectedIndices.Clear();
                    this.PreviewListView.Items[index].Selected = true;
                    PreviewListView.Items[index].EnsureVisible();

                    if (_selectedImage.Height == 1 && _selectedImage.Width == 1 && PreviewListView.SelectedIndices[0] != 0)
                    {
                        previousImageToolStripMenuItem_Click(null, null);
                    }
                }
            }
            catch (Exception)
            {
                PreviewListView.SelectedIndices.Clear();
                this.PreviewListView.Items[PreviewListView.Items.Count - 1].Selected = true;
            }
        }

        private void nextImageToolStripMenuItem_Click(object sender, EventArgs e)
        {
            try
            {
                if (PreviewListView.Visible && PreviewListView.Items.Count > 0)
                {
                    int index = PreviewListView.SelectedIndices[0];
                    index = index + 1;
                    PreviewListView.SelectedIndices.Clear();
                    this.PreviewListView.Items[index].Selected = true;
                    PreviewListView.Items[index].EnsureVisible();

                    if (_selectedImage.Height == 1 && _selectedImage.Width == 1 && PreviewListView.SelectedIndices[0] != 0)
                    {
                        nextImageToolStripMenuItem_Click(null, null);
                    }
                }
            }
            catch (Exception)
            {
                PreviewListView.SelectedIndices.Clear();
                this.PreviewListView.Items[0].Selected = true;
            }
        }

        // Move Left and Right through images.
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (!ImageTabActive) return false;

            if (keyData == Keys.Left)
            {
                previousImageToolStripMenuItem_Click(null, null);
                return true;
            }

            if (keyData == Keys.Right)
            {
                nextImageToolStripMenuItem_Click(null, null);
                return true;
            }

            if (keyData == Keys.Up) //Not 100% accurate but works for now.
            {
                double d = Math.Floor((double)(PreviewListView.Width / 67));
                int index = PreviewListView.SelectedIndices[0] - (int)d;

                PreviewListView.SelectedIndices.Clear();
                if (index < 0)
                    index = 0;

                this.PreviewListView.Items[index].Selected = true;

                return true;
            }

            if (keyData == Keys.Down) //Not 100% accurate but works for now.
            {
                double d = Math.Floor((double)(PreviewListView.Width / 67));
                int index = PreviewListView.SelectedIndices[0] + (int)d;

                PreviewListView.SelectedIndices.Clear();
                if (index > PreviewListView.Items.Count - 1)
                    index = PreviewListView.Items.Count - 1;

                this.PreviewListView.Items[index].Selected = true;

                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void buttonSkipNext_Click(object sender, EventArgs e)
        {
            nextImageToolStripMenuItem_Click(null, null);
        }

        private void buttonSkipPrevious_Click(object sender, EventArgs e)
        {
            previousImageToolStripMenuItem_Click(null, null);
        }

        private void checkBoxQuality_CheckedChanged(object sender, EventArgs e)
        {
            ZoomTrackBar_Scroll(null, null);
        }

        private void checkBoxPreventAntiAliasing_CheckedChanged(object sender, EventArgs e)
        {
            ZoomTrackBar_Scroll(null, null);
        }

        private void nudJump_ValueChanged(object sender, EventArgs e)
        {
            if (PreviewListView.Items.Count - 1 >= nudJump.Value)
            {
                PreviewListView.SelectedIndices.Clear();
                PreviewListView.Items[(int)nudJump.Value].Selected = true;
                PreviewListView.Items[(int)nudJump.Value].EnsureVisible();
            }
        }

        private void nudJump_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                //Enter key is down.
                if (PreviewListView.Items.Count - 1 >= nudJump.Value)
                {
                    PreviewListView.SelectedIndices.Clear();
                    PreviewListView.Items[(int)nudJump.Value].Selected = true;
                    PreviewListView.Items[(int)nudJump.Value].EnsureVisible();
                }
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        #region Frames

        private void defaultMonsterFramesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            _library.Frames.Clear();
            _library.Frames = new FrameSet(FrameSet.DefaultMonsterFrameSet);

            UpdateFrameGridView();
            RefreshAuthoringState();
        }

        private void defaultNPCFramesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            _library.Frames.Clear();
            _library.Frames = new FrameSet(FrameSet.DefaultNPCFrameSet);

            UpdateFrameGridView();
            RefreshAuthoringState();
        }

        private void defaultPlayerFramesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            _library.Frames.Clear();

            UpdateFrameGridView();
            RefreshAuthoringState();
        }

        private void tabControl_SelectedIndexChanged(object sender, EventArgs e)
        {
            switch (tabControl.SelectedIndex)
            {
                case 0: //Images
                    ImageTabActive = true;
                    MaskTabActive = false;
                    FrameTabActive = false;
                    ImageBox.Location = new Point(0, 0);
                    FrameAnimTimer.Stop();
                    break;
                case 1: //Masks
                    ImageTabActive = false;
                    MaskTabActive = true;
                    FrameTabActive = false;
                    ImageBox.Location = new Point(0, 0);
                    FrameAnimTimer.Stop();
                    break;
                case 2: //Frames
                    ImageTabActive = false;
                    MaskTabActive = false;
                    FrameTabActive = true;
                    break;
            }
        }

        private void autofillNpcFramesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (FolderLibraryDialog.ShowDialog() != DialogResult.OK) return;

            var path = FolderLibraryDialog.SelectedPath;

            var files = Directory.GetFiles(path, "*.Lib");

            if (MessageBox.Show($"确定要用 {files.Count()} Libs及其匹配的框架集",
                "自动填充 Libs.",
                MessageBoxButtons.YesNo) != DialogResult.Yes) return;

            foreach (var file in files)
            {
                var name = Path.GetFileNameWithoutExtension(file);

                if (!int.TryParse(name, out int imageNumber)) continue;

                using var library = new MLibraryV2(file);
                library.Frames = GetFrameSetByImage((Monster)imageNumber);
                library.Save();
            }
        }

        private void frameGridView_CellValidating(object sender, DataGridViewCellValidatingEventArgs e)
        {
            frameGridView.Rows[e.RowIndex].ErrorText = "";

            if (frameGridView.Rows[e.RowIndex].IsNewRow) { return; }

            if (e.ColumnIndex >= 1 && e.ColumnIndex <= 8)
            {
                if (!int.TryParse(e.FormattedValue.ToString(), out _))
                {
                    e.Cancel = true;
                    frameGridView.Rows[e.RowIndex].ErrorText = "该值必须是整数";
                }
            }
        }

        private void frameGridView_DefaultValuesNeeded(object sender, DataGridViewRowEventArgs e)
        {
            e.Row.Cells["FrameStart"].Value = 0;
            e.Row.Cells["FrameCount"].Value = 0;
            e.Row.Cells["FrameSkip"].Value = 0;
            e.Row.Cells["FrameInterval"].Value = 0;
            e.Row.Cells["FrameEffectStart"].Value = 0;
            e.Row.Cells["FrameEffectCount"].Value = 0;
            e.Row.Cells["FrameEffectSkip"].Value = 0;
            e.Row.Cells["FrameEffectInterval"].Value = 0;
            e.Row.Cells["FrameReverse"].Value = false;
            e.Row.Cells["FrameBlend"].Value = false;
        }


        private void UpdateFrameGridView()
        {
            bool previousUpdateState = _updatingControls;
            _updatingControls = true;
            try
            {
                frameGridView.Rows.Clear();
                foreach (var action in _library.Frames.Keys)
                {
                    var frame = _library.Frames[action];
                    int rowIndex = frameGridView.Rows.Add();
                    var row = frameGridView.Rows[rowIndex];
                    row.Cells["FrameAction"].Value = action;
                    row.Cells["FrameStart"].Value = frame.Start;
                    row.Cells["FrameCount"].Value = frame.Count;
                    row.Cells["FrameSkip"].Value = frame.Skip;
                    row.Cells["FrameInterval"].Value = frame.Interval;
                    row.Cells["FrameEffectStart"].Value = frame.EffectStart;
                    row.Cells["FrameEffectCount"].Value = frame.EffectCount;
                    row.Cells["FrameEffectSkip"].Value = frame.EffectSkip;
                    row.Cells["FrameEffectInterval"].Value = frame.EffectInterval;
                    row.Cells["FrameReverse"].Value = frame.Reverse;
                    row.Cells["FrameBlend"].Value = frame.Blend;
                }
            }
            finally
            {
                _updatingControls = previousUpdateState;
            }
        }

        private void UpdateFrameGridData()
        {
            if (_library == null) return;

            _library.Frames.Clear();

            foreach (DataGridViewRow row in frameGridView.Rows)
            {
                var cells = row.Cells;

                if (cells["FrameAction"].Value == null) continue;

                var action = (MirAction)row.Cells["FrameAction"].Value;

                if (_library.Frames.ContainsKey(action))
                {
                    MessageBox.Show(string.Format($"操作的 '{action}' 存在多次，因此将不会保存"));
                    continue;
                }

                var frame = new Frame(cells["FrameStart"].Value.ValueOrDefault<int>(),
                                        cells["FrameCount"].Value.ValueOrDefault<int>(),
                                        cells["FrameSkip"].Value.ValueOrDefault<int>(),
                                        cells["FrameInterval"].Value.ValueOrDefault<int>(),
                                        cells["FrameEffectStart"].Value.ValueOrDefault<int>(),
                                        cells["FrameEffectCount"].Value.ValueOrDefault<int>(),
                                        cells["FrameEffectSkip"].Value.ValueOrDefault<int>(),
                                        cells["FrameEffectInterval"].Value.ValueOrDefault<int>())
                {
                    Reverse = cells["FrameReverse"].Value.ValueOrDefault<bool>(),
                    Blend = cells["FrameBlend"].Value.ValueOrDefault<bool>()
                };

                _library.Frames.Add(action, frame);
            }
        }

        private void frameGridView_RowEnter(object sender, DataGridViewCellEventArgs e)
        {
            var row = frameGridView.Rows[e.RowIndex];

            if (row == null) return;

            var cells = row.Cells;

            if (cells["FrameAction"].Value == null) return;

            var frame = new Frame(cells["FrameStart"].Value.ValueOrDefault<int>(),
                                        cells["FrameCount"].Value.ValueOrDefault<int>(),
                                        cells["FrameSkip"].Value.ValueOrDefault<int>(),
                                        cells["FrameInterval"].Value.ValueOrDefault<int>(),
                                        cells["FrameEffectStart"].Value.ValueOrDefault<int>(),
                                        cells["FrameEffectCount"].Value.ValueOrDefault<int>(),
                                        cells["FrameEffectSkip"].Value.ValueOrDefault<int>(),
                                        cells["FrameEffectInterval"].Value.ValueOrDefault<int>())
            {
                Reverse = cells["FrameReverse"].Value.ValueOrDefault<bool>(),
                Blend = cells["FrameBlend"].Value.ValueOrDefault<bool>()
            };

            if (frame.Interval == 0) return;

            _drawFrame = frame;

            FrameAnimTimer.Interval = frame.Interval;
            FrameAnimTimer.Start();
        }

        private Frame _drawFrame;
        private int _currentFrame;
        private MirDirection _currentDirection;

        private void FrameAnimTimer_Tick(object sender, EventArgs e)
        {
            if (_drawFrame == null) return;

            try
            {
                if (_currentFrame >= _drawFrame.Count - 1)
                {
                    _currentFrame = 0;
                    MirDirection[] arr = (MirDirection[])Enum.GetValues(typeof(MirDirection));
                    int j = Array.IndexOf<MirDirection>(arr, _currentDirection) + 1;
                    _currentDirection = (arr.Length == j) ? arr[0] : arr[j];
                }

                var drawFrame = _drawFrame.Start + (_drawFrame.OffSet * (byte)_currentDirection) + _currentFrame;

                _selectedImage = _library.GetMImage(drawFrame);

                if (ViewMode == "Image")
                {
                    ImageBox.Location = new Point(250 + _selectedImage.X, 250 + _selectedImage.Y);
                    ImageBox.Image = _selectedImage.Image;
                }
                else
                {
                    ImageBox.Location = new Point(250 + _selectedImage.X, 250 + _selectedImage.Y);
                    ImageBox.Image = _selectedImage.MaskImage;
                }

                _currentFrame++;
            }
            catch { }
        }

        private void PreviewListViewMask_SelectedIndexChanged(object sender, EventArgs e)
        {

        }

        private void RButtonViewMode_CheckedChanged(object sender, EventArgs e)
        {
            if (RButtonImage.Checked)
            {
                ViewMode = "Image";
            }
            else if (RButtonOverlay.Checked)
            {
                ViewMode = "Overlay";
            }

            if (_selectedImage != null)
            {
                if (ViewMode == "Image")
                {
                    ImageBox.Image = _selectedImage.Image;
                }
                else
                {
                    ImageBox.Image = _selectedImage.MaskImage;
                }
            }
        }

        /// <summary>
        /// List of monsters and matching frames
        /// Method MUST be edited before use. The existing code is only here as an example.
        /// READ THE COMMENTS WITHIN THIS METHOD BEFORE USE
        /// </summary>
        /// <param name="image"></param>
        /// <returns></returns>
        private FrameSet GetFrameSetByImage(Monster image)
        {
            //REMOVE THE BELOW EXCEPTION ONCE THE DESIRED CODE HAS BEEN ADDED          
            throw new NotImplementedException("必须先更新 'GetFrameSetByImage' 后才能使用此函数");

            //UNCOMMENT THE CODE BELOW, IT SERVES AS AN EXAMPLE OF HOW TO MATCH IMAGES UP TO THE CORRECT FRAMES
            //List<FrameSet> FrameList = new List<FrameSet>();
            //FrameSet frame;

            ////ADD LIST OF FRAMES (CAN BE COPIED FROM THE CLIENTS FRAME.CS)
            //FrameList.Add(frame = new FrameSet());
            //frame.Add(MirAction.Standing, new Frame(0, 4, 0, 450));
            //frame.Add(MirAction.Harvest, new Frame(12, 10, 0, 200));

            ////ADD SWITCH OF IMAGE TO CORRECT FRAME (CAN BE COPIED FROM THE MONSTEROBJECT.CS FRAME LIST)
            //FrameSet matchingFrame = new FrameSet();
            //switch (image)
            //{
            //    case Monster.Hen:
            //        matchingFrame = FrameList[0];
            //        break;
            //}

            //return matchingFrame;
        }
        #endregion

        private void openReferenceFileToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (OpenLibraryDialog.ShowDialog() != DialogResult.OK) return;

            OpenReferenceLibrary(OpenLibraryDialog.FileName);
            PreviewListView.Invoke(new EventHandler(PreviewListView_SelectedIndexChanged), EventArgs.Empty);
        }

        private void checkBox1_CheckedChanged(object sender, EventArgs e)
        {
            PreviewListView.Invoke(new EventHandler(PreviewListView_SelectedIndexChanged), EventArgs.Empty);
        }

        private void importShadowsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (OpenLibraryDialog.ShowDialog() != DialogResult.OK) return;
            OpenShadowLibraryAndImport(OpenLibraryDialog.FileName);
        }

        private void openReferenceImageToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (_library == null) return;
            if (_library.FileName == null) return;

            if (ImportImageDialog.ShowDialog() != DialogResult.OK) return;

            string fileName = ImportImageDialog.FileNames[0];
            var replacement = new Bitmap(fileName);
            Bitmap old = _referenceImage;
            _referenceImage = replacement;
            old?.Dispose();
        }

        private void BulkButton_Click(object sender, EventArgs e)
        {
            // Create an instance of the InputDialog class
            InputDialog dlg = new InputDialog();

            // Show the dialog as a modal dialog
            DialogResult result = dlg.ShowDialog();

            // If the user clicked the Ok button, retrieve the values entered by the user
            if (result == DialogResult.OK)
            {
                for (int i = 0; i < PreviewListView.SelectedIndices.Count; i++)
                {
                    MLibraryV2.MImage image = _library.GetMImage(PreviewListView.SelectedIndices[i]);
                    if (image == null || image.Image == null) continue;
                    image.X += (short)dlg.Value1;
                    image.Y += (short)dlg.Value2;
                }
                RefreshAuthoringState();
            }
        }

        private void numericUpDownX_ValueChanged(object sender, EventArgs e)
        {
            if (_updatingControls) return;
            for (int i = 0; i < PreviewListView.SelectedIndices.Count; i++)
            {
                MLibraryV2.MImage image = _library.GetMImage(PreviewListView.SelectedIndices[i]);
                image.X = (short)numericUpDownX.Value;
            }
            PreviewListView.Invoke(new EventHandler(PreviewListView_SelectedIndexChanged), EventArgs.Empty);
            RefreshAuthoringState();
        }

        private void numericUpDownY_ValueChanged(object sender, EventArgs e)
        {
            if (_updatingControls) return;
            for (int i = 0; i < PreviewListView.SelectedIndices.Count; i++)
            {
                MLibraryV2.MImage image = _library.GetMImage(PreviewListView.SelectedIndices[i]);
                image.Y = (short)numericUpDownY.Value;
            }
            PreviewListView.Invoke(new EventHandler(PreviewListView_SelectedIndexChanged), EventArgs.Empty);
            RefreshAuthoringState();
        }

        private void frameGridView_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {

        }

        private void BuildAuthoringWorkspace()
        {
            AutoScaleMode = AutoScaleMode.Dpi;
            MinimumSize = new Size(1100, 700);
            ClientSize = new Size(1280, 800);
            KeyPreview = true;

            var toolbar = new FlowLayoutPanel
            {
                Name = "ResourceAuthoringToolbar",
                Dock = DockStyle.Fill,
                Padding = new Padding(8, 7, 8, 4),
                WrapContents = false,
                BackColor = Color.FromArgb(245, 247, 250)
            };
            var title = new Label
            {
                Text = "资源引用与库编辑",
                AutoSize = true,
                Font = new Font(Font.FontFamily, 12F, FontStyle.Bold),
                Margin = new Padding(0, 6, 18, 0)
            };
            _authoringStatusLabel = new Label
            {
                Name = "ResourceAuthoringStatus",
                Text = "未打开资源库",
                AutoSize = true,
                Font = new Font(Font.FontFamily, 12F),
                Margin = new Padding(0, 6, 18, 0)
            };
            Button save = CreateAuthoringButton("保存", (_, _) => saveToolStripMenuItem_Click(this, EventArgs.Empty));
            save.Name = "ResourceSaveButton";
            Button reload = CreateAuthoringButton("重载", (_, _) => ReloadDraft());
            reload.Name = "ResourceReloadButton";
            Button differences = CreateAuthoringButton("差异", (_, _) => ShowChanges());
            differences.Name = "ResourceDiffButton";
            Button analyze = CreateAuthoringButton("分析", (_, _) => RefreshResourceAnalysis());
            analyze.Name = "ResourceAnalyzeButton";
            Button loadManifest = CreateAuthoringButton("加载清单", (_, _) => LoadManifestFromDialog());
            loadManifest.Name = "ResourceLoadManifestButton";
            _showAnalysisButton = CreateAuthoringButton("显示分析", (_, _) => SetAnalysisPanelVisible(true));
            _showAnalysisButton.Name = "ResourceShowAnalysisButton";
            _showAnalysisButton.Visible = false;
            toolbar.Controls.AddRange([title, _authoringStatusLabel, save, reload, differences, analyze, loadManifest, _showAnalysisButton]);

            _resourceAnalysisText = new TextBox
            {
                Name = "ResourceAnalysisText",
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                WordWrap = true,
                Font = new Font(Font.FontFamily, 12F),
                BackColor = SystemColors.Window
            };
            _changeText = new TextBox
            {
                Name = "ResourceChangeText",
                Dock = DockStyle.Bottom,
                Height = 150,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Font = new Font(Font.FontFamily, 12F),
                BackColor = SystemColors.Window
            };
            var analysisTitle = new Label
            {
                Text = "资源引用分析（只读候选）",
                Dock = DockStyle.Top,
                Height = 36,
                Padding = new Padding(8, 7, 0, 0),
                Font = new Font(Font.FontFamily, 12F, FontStyle.Bold)
            };
            Button collapse = CreateAuthoringButton("收起", (_, _) => SetAnalysisPanelVisible(false));
            collapse.Name = "ResourceCollapseAnalysisButton";
            collapse.Dock = DockStyle.Bottom;
            _resourceAnalysisPanel = new Panel
            {
                Name = "ResourceAnalysisPanel",
                Dock = DockStyle.Fill,
                Padding = new Padding(8),
                BackColor = Color.FromArgb(248, 249, 251)
            };
            _resourceAnalysisPanel.Controls.Add(_resourceAnalysisText);
            _resourceAnalysisPanel.Controls.Add(_changeText);
            _resourceAnalysisPanel.Controls.Add(collapse);
            _resourceAnalysisPanel.Controls.Add(analysisTitle);

            int originalIndex = Controls.GetChildIndex(splitContainer1);
            Controls.Remove(splitContainer1);
            _authoringHost = new Panel
            {
                Name = "ResourceAuthoringHost",
                Dock = DockStyle.Fill
            };
            _authoringLayout = new TableLayoutPanel
            {
                Name = "ResourceAuthoringLayout",
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 2,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            _authoringLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _authoringLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300F));
            _authoringLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52F));
            _authoringLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            splitContainer1.Dock = DockStyle.Fill;
            _authoringLayout.Controls.Add(toolbar, 0, 0);
            _authoringLayout.SetColumnSpan(toolbar, 2);
            _authoringLayout.Controls.Add(splitContainer1, 0, 1);
            _authoringLayout.Controls.Add(_resourceAnalysisPanel, 1, 1);
            _authoringHost.Controls.Add(_authoringLayout);
            Controls.Add(_authoringHost);
            Controls.SetChildIndex(_authoringHost, originalIndex);
            FormClosing += LMain_FormClosing;
            KeyDown += LMain_KeyDown;
            Resize += (_, _) => UpdateAuthoringLayout();
            UpdateAuthoringLayout();
        }

        private Button CreateAuthoringButton(string text, EventHandler click)
        {
            var button = new Button
            {
                Text = text,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                MinimumSize = new Size(76, 36),
                Font = new Font(Font.FontFamily, 12F),
                Margin = new Padding(4, 0, 0, 0),
                UseVisualStyleBackColor = true
            };
            button.Click += click;
            return button;
        }

        private void UpdateAuthoringLayout()
        {
            if (_resourceAnalysisPanel == null) return;
            bool showFull = ClientSize.Width >= 1280;
            if (showFull != _lastWideLayout)
            {
                _analysisExpanded = showFull;
                _lastWideLayout = showFull;
            }
            ApplyAnalysisPanelLayout();
        }

        private void ApplyAnalysisPanelLayout()
        {
            if (_resourceAnalysisPanel == null || _authoringLayout == null) return;
            _authoringLayout.ColumnStyles[1].Width = _analysisExpanded ? 300F : 0F;
            _resourceAnalysisPanel.Visible = _analysisExpanded;
            _showAnalysisButton.Visible = !_analysisExpanded;
        }

        private void ResetLibrarySurface()
        {
            _updatingControls = true;
            try
            {
                PreviewListView.SelectedIndices.Clear();
                ClearInterface();
                ImageList.Images.Clear();
                PreviewListView.Items.Clear();
                _indexList.Clear();
                PreviewListView.VirtualListSize = _library?.Images.Count ?? 0;
                UpdateFrameGridView();
                _frameGridDirty = false;
                RefreshAuthoringState();
            }
            finally
            {
                _updatingControls = false;
            }
        }

        private void MarkFrameGridDirty()
        {
            if (_updatingControls || _editingSession == null) return;
            _frameGridDirty = true;
            RefreshAuthoringState();
        }

        private bool CanDiscardChanges()
        {
            if (!HasUnsavedChanges) return true;
            return _confirmDiscard("当前资源库有未保存修改，确定放弃并继续吗？");
        }

        private void RefreshAuthoringState()
        {
            if (_authoringStatusLabel == null) return;
            _authoringStatusLabel.Text = _editingSession == null
                ? "未打开资源库"
                : HasUnsavedChanges ? "有未保存修改" : "已保存";
            _authoringStatusLabel.ForeColor = HasUnsavedChanges ? Color.DarkOrange : Color.DarkGreen;
            if (_changeText != null) _changeText.Text = GetDraftChanges();
        }

        private void RefreshResourceAnalysis()
        {
            if (_resourceAnalysisText == null) return;
            if (_resourceWorkspace == null)
            {
                _resourceAnalysisText.Text = "尚未加载 bootstrap-packages.json。\r\n分析只读，不会自动删除任何资源。";
                return;
            }
            ResourceReferenceReport report = _resourceWorkspace.Report;
            var lines = new List<string>
            {
                $"资产：{_resourceWorkspace.Assets.Count}  引用：{_resourceWorkspace.References.Count}",
                $"缺失：{report.MissingReferences.Count}  重复候选：{report.DuplicateCandidates.Count}  未使用候选：{report.UnusedCandidates.Count}",
                ""
            };
            lines.Add("【缺失引用】");
            lines.AddRange(report.MissingReferences.Select(item => $"{item.Code} {item.ResourcePath} ← {item.Owner}"));
            lines.Add("【反向引用】");
            lines.AddRange(_resourceWorkspace.Assets
                .Select(asset => (asset.ResourcePath, Owners: report.GetOwners(asset.ResourcePath)))
                .Where(item => item.Owners.Count > 0)
                .Select(item => $"{item.ResourcePath} ← {string.Join(", ", item.Owners)}"));
            lines.Add("【重复候选】");
            lines.AddRange(report.DuplicateCandidates.Select(item => string.Join(" = ", item.ResourcePaths)));
            lines.Add("【未使用候选（不会自动删除）】");
            lines.AddRange(report.UnusedCandidates);
            _resourceAnalysisText.Text = string.Join(Environment.NewLine, lines);
        }

        private void TryLoadAssociatedWorkspace(string libraryPath)
        {
            string directory = Path.GetDirectoryName(Path.GetFullPath(libraryPath));
            while (!string.IsNullOrWhiteSpace(directory))
            {
                string manifest = Path.Combine(directory, "bootstrap-packages.json");
                if (File.Exists(manifest))
                {
                    _ = LoadResourceWorkspaceAsync(directory, manifest);
                    return;
                }
                directory = Directory.GetParent(directory)?.FullName;
            }
            _resourceWorkspace = null;
            RefreshResourceAnalysis();
        }

        private void LoadManifestFromDialog()
        {
            using var dialog = new OpenFileDialog
            {
                Filter = "资源包清单 (bootstrap-packages.json)|bootstrap-packages.json|JSON 文件 (*.json)|*.json",
                FileName = "bootstrap-packages.json",
                CheckFileExists = true
            };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            string root = Path.GetDirectoryName(dialog.FileName) ?? Directory.GetCurrentDirectory();
            _ = LoadResourceWorkspaceAsync(root, dialog.FileName);
        }

        private void ShowChanges()
        {
            RefreshAuthoringState();
            SetAnalysisPanelVisible(true);
            _changeText.Focus();
        }

        private void SetAuthoringStatus(string message, bool success)
        {
            if (_authoringStatusLabel == null) return;
            _authoringStatusLabel.Text = message;
            _authoringStatusLabel.ForeColor = success ? Color.DarkGreen : Color.DarkRed;
        }

        private void SelectDiagnostic(LibraryContentDiagnostic diagnostic)
        {
            if (diagnostic.ImageIndex.HasValue) NavigateToImage(diagnostic.ImageIndex.Value);
        }

        private void LMain_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (_allowClose) return;
            if (!CanDiscardChanges())
            {
                e.Cancel = true;
                return;
            }
            _allowClose = true;
            _editingSession?.Dispose();
            _editingSession = null;
            _library = null;
            _referenceLibrary?.Dispose();
            _referenceLibrary = null;
            _referenceImage?.Dispose();
            _referenceImage = null;
            _shadowLibrary?.Dispose();
            _shadowLibrary = null;
        }

        private void LMain_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.S)
            {
                saveToolStripMenuItem_Click(sender, EventArgs.Empty);
                e.SuppressKeyPress = true;
            }
            else if (e.Control && e.KeyCode == Keys.R)
            {
                ReloadDraft();
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.F6)
            {
                MoveWorkspaceFocusForAuthoring(e.Shift);
                e.SuppressKeyPress = true;
            }
        }
    }
}
