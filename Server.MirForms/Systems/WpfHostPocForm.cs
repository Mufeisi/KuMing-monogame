using System.Text;
using System.Windows.Forms.Integration;
using Microsoft.CodeAnalysis;
using RoslynPad.Editor;
using RoslynPad.Roslyn;
using WpfFontFamily = System.Windows.Media.FontFamily;

namespace Server.MirForms.Systems
{
    public sealed class WpfHostPocForm : Form
    {
        private readonly RoslynHost _roslynHost;
        private readonly RoslynCodeEditor _editor;
        private readonly ToolStripButton _diagnosticsButton;

        private DocumentId? _documentId;
        private string? _currentFilePath;
        private Encoding _currentEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        private static string ScriptsRootPath => Path.GetFullPath(global::Server.Settings.CSharpScriptsPath);

        public WpfHostPocForm()
        {
            Text = "RoslynPad 宿主 PoC";
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(900, 600);

            var toolStrip = new ToolStrip
            {
                GripStyle = ToolStripGripStyle.Hidden,
                Dock = DockStyle.Top,
            };

            var openButton = new ToolStripButton("打开...") { DisplayStyle = ToolStripItemDisplayStyle.Text };
            var saveButton = new ToolStripButton("保存") { DisplayStyle = ToolStripItemDisplayStyle.Text };
            var saveAsButton = new ToolStripButton("另存为...") { DisplayStyle = ToolStripItemDisplayStyle.Text };
            _diagnosticsButton = new ToolStripButton("诊断") { DisplayStyle = ToolStripItemDisplayStyle.Text, Enabled = false };

            openButton.Click += (_, _) => OpenFile();
            saveButton.Click += (_, _) => SaveFile();
            saveAsButton.Click += (_, _) => SaveFileAs();
            _diagnosticsButton.Click += (_, _) => _ = ShowDiagnosticsSafeAsync();

            toolStrip.Items.Add(openButton);
            toolStrip.Items.Add(saveButton);
            toolStrip.Items.Add(saveAsButton);
            toolStrip.Items.Add(new ToolStripSeparator());
            toolStrip.Items.Add(_diagnosticsButton);

            var elementHost = new ElementHost { Dock = DockStyle.Fill };

            _roslynHost = RoslynPadScriptHost.CreateHost(debugBuild: true);

            _editor = new RoslynCodeEditor
            {
                FontFamily = new WpfFontFamily("Consolas"),
                FontSize = 13,
            };

            _editor.Loaded += OnEditorLoaded;
            elementHost.Child = _editor;

            Controls.Add(elementHost);
            Controls.Add(toolStrip);
        }

        private void OnEditorLoaded(object sender, EventArgs e)
        {
            _editor.Loaded -= OnEditorLoaded;
            _ = InitializeEditorAsync();
        }

        private async Task InitializeEditorAsync()
        {
            var workingDirectory = Directory.Exists(ScriptsRootPath) ? ScriptsRootPath : Directory.GetCurrentDirectory();

            try
            {
                _documentId = await _editor.InitializeAsync(
                    _roslynHost,
                    new ClassificationHighlightColors(),
                    workingDirectory,
                    string.Empty,
                    SourceCodeKind.Regular).ConfigureAwait(true);

                _editor.Text = "// RoslynPad 宿主 PoC（WinForms + ElementHost）\r\n\r\npublic sealed class Example { }\r\n";
                _diagnosticsButton.Enabled = true;
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show(
                    this,
                    ex.ToString(),
                    "RoslynPad 初始化失败",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private async Task ShowDiagnosticsSafeAsync()
        {
            try
            {
                await ShowDiagnosticsAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show(
                    this,
                    ex.ToString(),
                    "诊断失败",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void OpenFile()
        {
            using var dialog = new OpenFileDialog
            {
                Filter = "C# 文件 (*.cs)|*.cs|所有文件 (*.*)|*.*",
                InitialDirectory = Directory.Exists(ScriptsRootPath) ? ScriptsRootPath : Directory.GetCurrentDirectory(),
                CheckFileExists = true,
                Multiselect = false,
            };

            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            try
            {
                var text = ReadAllTextPreserveEncoding(dialog.FileName, out var encoding);

                _currentFilePath = dialog.FileName;
                _currentEncoding = encoding;

                Text = $"RoslynPad 宿主 PoC - {Path.GetFileName(_currentFilePath)}";
                _editor.Text = text;
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show(
                    this,
                    ex.ToString(),
                    "打开失败",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void SaveFile()
        {
            if (string.IsNullOrWhiteSpace(_currentFilePath))
            {
                SaveFileAs();
                return;
            }

            try
            {
                WriteAllTextPreserveEncoding(_currentFilePath, _editor.Text, _currentEncoding);
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show(
                    this,
                    ex.ToString(),
                    "保存失败",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void SaveFileAs()
        {
            using var dialog = new SaveFileDialog
            {
                Filter = "C# 文件 (*.cs)|*.cs|所有文件 (*.*)|*.*",
                InitialDirectory = Directory.Exists(ScriptsRootPath) ? ScriptsRootPath : Directory.GetCurrentDirectory(),
                FileName = string.IsNullOrWhiteSpace(_currentFilePath) ? "Script.cs" : Path.GetFileName(_currentFilePath),
            };

            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            _currentFilePath = dialog.FileName;
            Text = $"RoslynPad 宿主 PoC - {Path.GetFileName(_currentFilePath)}";

            SaveFile();
        }

        private async Task ShowDiagnosticsAsync()
        {
            if (_documentId == null)
            {
                return;
            }

            var document = _roslynHost.GetDocument(_documentId);
            if (document == null)
            {
                System.Windows.Forms.MessageBox.Show(
                    this,
                    "未找到当前文档（初始化可能未完成）。",
                    "诊断",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            var semanticModel = await document.GetSemanticModelAsync().ConfigureAwait(true);
            if (semanticModel == null)
            {
                System.Windows.Forms.MessageBox.Show(
                    this,
                    "无法获取语义模型。",
                    "诊断",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            var diagnostics = semanticModel.GetDiagnostics()
                .Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)
                .OrderBy(d => d.Location.GetLineSpan().StartLinePosition.Line)
                .ThenBy(d => d.Location.GetLineSpan().StartLinePosition.Character)
                .ToArray();

            var message = diagnostics.Length == 0
                ? "没有错误/警告。"
                : string.Join(Environment.NewLine, diagnostics.Select(FormatDiagnostic));

            System.Windows.Forms.MessageBox.Show(
                this,
                message,
                $"诊断（{diagnostics.Length}）",
                MessageBoxButtons.OK,
                diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error) ? MessageBoxIcon.Error : MessageBoxIcon.Warning);
        }

        private static string FormatDiagnostic(Diagnostic diagnostic)
        {
            var span = diagnostic.Location.GetLineSpan();
            var line = span.StartLinePosition.Line + 1;
            var column = span.StartLinePosition.Character + 1;

            return $"[{diagnostic.Severity}] {diagnostic.Id}: {diagnostic.GetMessage()} ({line},{column})";
        }

        private static string ReadAllTextPreserveEncoding(string filePath, out Encoding encoding)
        {
            var bytes = File.ReadAllBytes(filePath);

            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
                return encoding.GetString(bytes, 3, bytes.Length - 3);
            }

            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            {
                encoding = Encoding.Unicode;
                return encoding.GetString(bytes, 2, bytes.Length - 2);
            }

            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            {
                encoding = Encoding.BigEndianUnicode;
                return encoding.GetString(bytes, 2, bytes.Length - 2);
            }

            encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            return encoding.GetString(bytes);
        }

        private static void WriteAllTextPreserveEncoding(string filePath, string text, Encoding encoding)
        {
            if (encoding is UTF8Encoding)
            {
                var emitBom = encoding.GetPreamble().Length != 0;
                encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: emitBom);
            }

            File.WriteAllText(filePath, text, encoding);
        }

    }
}
