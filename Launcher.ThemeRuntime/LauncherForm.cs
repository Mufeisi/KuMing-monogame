namespace Launcher.ThemeRuntime;

internal sealed class LauncherForm : Form
{
    private readonly LoadedLauncherSnapshot _loaded;
    private readonly string _clientDirectory;
    private readonly Action<string, string, LauncherServer, MicroEndpoint, LauncherPlayerSettings> _launch;
    private readonly ComboBox _serverDropdown = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TreeView _serverSidebar = new() { BorderStyle = BorderStyle.None, HideSelection = false };
    private readonly Panel _announcements = new() { AutoScroll = true };
    private readonly FlowLayoutPanel _actionLinks = new() { AutoSize = true, BackColor = Color.Transparent };
    private readonly CancellationTokenSource _announcementCancellation = new();
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly LauncherProgressBar _overall = new() { FillColor = Color.LimeGreen };
    private readonly LauncherProgressBar _current = new() { FillColor = Color.DeepSkyBlue };
    private readonly Label _progressText = new() { AutoEllipsis = true };
    private readonly Label _sourceText = new() { AutoSize = true };
    private readonly Label _windowTitleText = new() { AutoSize = true, BackColor = Color.Transparent };
    private readonly ImageStateButton _launchButton = new() { Text = "进入游戏" };
    private readonly Dictionary<LauncherControlId, Control> _themeControls = new();
    private readonly Dictionary<Control, Color> _originalBackColors = new();
    private readonly Dictionary<Control, (string Family, float Size, FontStyle Style)> _originalFontSpecs = new();
    private readonly Dictionary<Control, Image> _derivedBackgrounds = new();
    private LauncherPlayerSettings _settings;
    private ClientSelectionResult _selectedClient;
    private bool _settingsDirty;
    private bool _launching;
    private readonly List<Image> _ownedImages = new();
    private readonly List<Font> _ownedFonts = new();
    private readonly List<Control> _clickTargets = new();
    private readonly System.Windows.Forms.Timer _progressTimer = new() { Interval = 300 };
    private readonly bool _builtInClassicSkin;
    private ImageStateButton? _classicCloseButton;
    private ImageStateButton? _classicSettingsButton;
    private Label? _classicServerLabel;
    private bool _autoStartTriggered;
    private bool _buttonImagesLoaded;
    private bool _entryUpdateBlocked;
    private bool _dpiLayoutPending;
    private bool _disposeStarted;

    public LauncherForm(LoadedLauncherSnapshot loaded, string clientDirectory, Action<string, string, LauncherServer, MicroEndpoint, LauncherPlayerSettings> launch)
    {
        _loaded = loaded;
        _clientDirectory = clientDirectory;
        _launch = launch;
        _builtInClassicSkin = loaded.Snapshot.Theme.Template == LauncherTemplateKind.Classic
            && string.IsNullOrWhiteSpace(loaded.Snapshot.Theme.BackgroundImage)
            && string.IsNullOrWhiteSpace(loaded.Snapshot.Theme.LaunchButtonImage);
        _selectedClient = ClientSelection.GetPreferred(loaded.Snapshot.ProjectId, clientDirectory, loaded.Snapshot.LoginCoreResources);
        _settings = ClientSettingsWriter.Read(_selectedClient.ResourceDirectory, CloneSettings(loaded.Snapshot.Defaults));
        string windowTitle = string.IsNullOrWhiteSpace(loaded.Snapshot.WindowTitle) ? loaded.Snapshot.ProjectName : loaded.Snapshot.WindowTitle;
        Text = string.IsNullOrWhiteSpace(loaded.Snapshot.TaskbarName) ? windowTitle : loaded.Snapshot.TaskbarName;
        _windowTitleText.Text = windowTitle;
        ApplyTaskbarIdentity(loaded.Snapshot.ProjectId, loaded.Snapshot.TaskbarName);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(loaded.Snapshot.Theme.CanvasWidth, loaded.Snapshot.Theme.CanvasHeight);
        MinimumSize = Size;
        BackColor = Color.FromArgb(18, 20, 28);
        ForeColor = Color.WhiteSmoke;
        DoubleBuffered = true;
        if (_builtInClassicSkin) FormBorderStyle = FormBorderStyle.None;
        BuildUi();
        string background = LauncherSnapshotValidator.ResolveAsset(_loaded.Root, _loaded.Snapshot.Theme.BackgroundImage);
        if (!string.IsNullOrEmpty(background)) { BackgroundImage = Own(SafeLoadImage(background)); BackgroundImageLayout = ImageLayout.Stretch; }
        else if (_loaded.Snapshot.Theme.Template == LauncherTemplateKind.Classic) { BackgroundImage = Own(BuildClassicBackground(ClientSize)); BackgroundImageLayout = ImageLayout.Stretch; }
        ApplyTemplate();
        DpiChanged += (_, _) => QueueDpiLayout();
        UpdateProgress(new LauncherProgressState("启动核心已就绪，可进入游戏", string.Empty, 0, 0, 0, 0, 0));
        _progressTimer.Tick += (_, _) => PollProgress();
        _progressTimer.Start();
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (_settings.AutoStart && !_autoStartTriggered && !_entryUpdateBlocked)
        {
            _autoStartTriggered = true;
            BeginInvoke(async () => await LaunchSelectedAsync());
        }
        if (_loaded.Snapshot.AnnouncementMode == AnnouncementDisplayMode.ExternalPage)
        {
            AnnouncementPresentationResolver.Presentation presentation = await AnnouncementPresentationResolver.LoadAsync(_loaded.Snapshot, cancellationToken: _announcementCancellation.Token);
            if (IsDisposed || Disposing || !Visible || _announcementCancellation.IsCancellationRequested) return;
            if (presentation.Mode == AnnouncementDisplayMode.ExternalPage)
            {
                FlowLayoutPanel? browser = null;
                try
                {
                    if (!Uri.TryCreate(_loaded.Snapshot.ExternalAnnouncementUrl, UriKind.Absolute, out Uri? documentUri)) return;
                    IReadOnlyList<ExternalAnnouncementElement> elements = SafeExternalAnnouncementDocument.Parse(presentation.Html, documentUri);
                    if (elements.Count == 0) return;
                    browser = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Color.White, Padding = new Padding(10) };
                    using HttpClient imageClient = ExternalAnnouncementHttp.CreateClient(TimeSpan.FromSeconds(5));
                    long remainingImageBytes = 8L * 1024 * 1024;
                    long remainingImagePixels = 16_000_000;
                    foreach (ExternalAnnouncementElement element in elements)
                    {
                        if (element.Kind == ExternalAnnouncementElementKind.Image)
                        {
                            var picture = new PictureBox { Width = Math.Max(240, _announcements.Width - 45), Height = 170, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(238, 238, 238) };
                            (Image? image, long bytes, long pixels) = await LoadExternalAnnouncementImageAsync(imageClient, element.Url, Math.Min(2L * 1024 * 1024, remainingImageBytes), remainingImagePixels, _announcementCancellation.Token);
                            if (image is null) throw new InvalidDataException("外部公告图片加载失败");
                            remainingImageBytes -= bytes; remainingImagePixels -= pixels;
                            picture.Image = image; picture.Disposed += (_, _) => image.Dispose(); browser.Controls.Add(picture); continue;
                        }
                        Control line;
                        if (element.Kind == ExternalAnnouncementElementKind.Link)
                        {
                            var link = new LinkLabel { Text = element.Text, AutoSize = true, MaximumSize = new Size(Math.Max(220, _announcements.Width - 50), 0) };
                            link.Links.Add(0, link.Text.Length, element.Url); link.LinkClicked += (_, args) => { if (args.Link?.LinkData is string url) new LauncherActionDispatcher().Execute(LauncherAction.OpenAnnouncementLink, url); }; line = link;
                        }
                        else line = new Label { Text = element.Text, AutoSize = true, MaximumSize = new Size(Math.Max(220, _announcements.Width - 50), 0) };
                        Font? ownedFont = element.Kind == ExternalAnnouncementElementKind.Heading ? new Font(line.Font.FontFamily, line.Font.Size + 3, FontStyle.Bold) : element.Bold ? new Font(line.Font, FontStyle.Bold) : null;
                        if (ownedFont is not null) { line.Font = ownedFont; line.Disposed += (_, _) => ownedFont.Dispose(); }
                        if (!string.IsNullOrEmpty(element.Color)) line.ForeColor = ColorTranslator.FromHtml(element.Color);
                        browser.Controls.Add(line);
                    }
                    foreach (Control control in _announcements.Controls.Cast<Control>().ToArray()) control.Dispose();
                    _announcements.Controls.Clear(); _announcements.Controls.Add(browser);
                    browser = null;
                }
                catch
                {
                    browser?.Dispose();
                    if (!IsDisposed && !Disposing && !_announcementCancellation.IsCancellationRequested) ShowNativeAnnouncements();
                }
            }
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _announcementCancellation.Cancel();
        _lifetimeCancellation.Cancel();
        base.OnFormClosing(e);
    }

    internal void SetEntryUpdateChecking()
    {
        _entryUpdateBlocked = true;
        _launchButton.Enabled = false;
        UpdateProgress(new LauncherProgressState("正在检查玩家入口更新…", string.Empty, 0, 0, 0, 0, 0));
    }

    internal void ReleaseEntryUpdateGate(string message)
    {
        _entryUpdateBlocked = false;
        _launchButton.Enabled = true;
        UpdateProgress(new LauncherProgressState(message, string.Empty, 0, 0, 0, 0, 0));
        if (_settings.AutoStart && !_autoStartTriggered && Visible)
        {
            _autoStartTriggered = true;
            BeginInvoke(async () => await LaunchSelectedAsync());
        }
    }

    internal void BlockForRequiredEntryUpdate(string message)
    {
        _entryUpdateBlocked = true;
        _launchButton.Enabled = false;
        UpdateProgress(new LauncherProgressState(message, string.Empty, 0, 0, 0, 0, 0));
    }

    private void BuildUi()
    {
        Controls.AddRange(new Control[] { _announcements, _serverDropdown, _serverSidebar, _launchButton, _overall, _current, _progressText, _sourceText, _windowTitleText, _actionLinks });
        var actionDispatcher = new LauncherActionDispatcher();
        foreach (LauncherActionLink link in _loaded.Snapshot.ActionLinks)
        {
            var item = new LinkLabel { Text = link.Text, AutoSize = true, LinkColor = Color.LightSkyBlue, Margin = new Padding(6) };
            item.Click += (_, _) => actionDispatcher.Execute(link.Action, link.Url);
            _actionLinks.Controls.Add(item);
        }
        _actionLinks.Location = new Point(18, 20);
        foreach (LauncherServer server in _loaded.Snapshot.Servers.Where(server => server.Status != ServerOperatingStatus.Hidden).OrderBy(server => server.SortOrder)) _serverDropdown.Items.Add(server);
        _serverDropdown.DisplayMember = nameof(LauncherServer.Name);
        if (_serverDropdown.Items.Count > 0) _serverDropdown.SelectedIndex = 0;
        foreach (IGrouping<string, LauncherServer> group in _loaded.Snapshot.Servers.Where(server => server.Status != ServerOperatingStatus.Hidden).OrderBy(server => server.SortOrder).GroupBy(x => x.Group))
        {
            var node = new TreeNode(group.Key);
            foreach (LauncherServer server in group) node.Nodes.Add(new TreeNode($"{server.Name}  [{StatusText(server.Status)}]") { Tag = server });
            _serverSidebar.Nodes.Add(node);
            node.Expand();
        }
        if (_serverSidebar.Nodes.Count > 0 && _serverSidebar.Nodes[0].Nodes.Count > 0) _serverSidebar.SelectedNode = _serverSidebar.Nodes[0].Nodes[0];
        ShowNativeAnnouncements();
        var settings = CreateTopButton("游戏设置", 145);
        settings.Click += (_, _) => { using var dialog = new PlayerSettingsForm(_settings, _selectedClient.ResourceDirectory); if (dialog.ShowDialog(this) == DialogResult.OK) { _settings = dialog.Value; _settingsDirty = true; } };
        var diagnose = CreateTopButton("连通诊断", 265);
        diagnose.Click += async (_, _) => await DiagnoseAsync();
        var chooseClient = CreateTopButton("更换客户端", 385);
        chooseClient.Click += (_, _) =>
        {
            ClientSelectionResult? selected = ClientSelection.SelectManually(this, _loaded.Snapshot.ProjectId, _clientDirectory, _loaded.Snapshot.LoginCoreResources);
            if (selected is null) return;
            _selectedClient = selected;
            _settings = ClientSettingsWriter.Read(selected.ResourceDirectory, CloneSettings(_loaded.Snapshot.Defaults));
            _settingsDirty = false;
        };
        Controls.AddRange(new Control[] { settings, diagnose, chooseClient });
        if (_builtInClassicSkin)
        {
            _classicCloseButton = new ImageStateButton { Text = string.Empty, TabStop = false };
            _classicCloseButton.Click += (_, _) => Close();
            _classicSettingsButton = new ImageStateButton { Text = string.Empty, TabStop = false };
            _classicSettingsButton.Click += (_, _) => settings.PerformClick();
            _classicServerLabel = new Label { Text = "选择服务器：", AutoSize = true, BackColor = Color.Transparent, ForeColor = Color.White };
            Controls.AddRange(new Control[] { _classicCloseButton, _classicSettingsButton, _classicServerLabel });
        }
        _themeControls[LauncherControlId.ServerList] = _loaded.Snapshot.Theme.ServerListMode == ServerListMode.Sidebar ? _serverSidebar : _serverDropdown;
        _themeControls[LauncherControlId.Announcements] = _announcements;
        _themeControls[LauncherControlId.LaunchButton] = _launchButton;
        _themeControls[LauncherControlId.OverallProgress] = _overall;
        _themeControls[LauncherControlId.CurrentProgress] = _current;
        _themeControls[LauncherControlId.ProgressText] = _progressText;
        _themeControls[LauncherControlId.SettingsButton] = settings;
        _themeControls[LauncherControlId.DiagnoseButton] = diagnose;
        _themeControls[LauncherControlId.ChooseClientButton] = chooseClient;
        _clickTargets.AddRange(new Control[] { _launchButton, settings, diagnose, chooseClient, _serverDropdown, _serverSidebar });
        _launchButton.Click += async (_, _) => await LaunchSelectedAsync();
        _sourceText.Text = "配置来源：" + (_loaded.Source switch { SnapshotSource.Remote => "有效远程版本", SnapshotSource.Cache => "上次有效快照", _ => "内置快照" });
    }

    private void ShowNativeAnnouncements()
    {
        foreach (Control control in _announcements.Controls.Cast<Control>().ToArray()) control.Dispose();
        _announcements.Controls.Clear();
        foreach (LauncherAnnouncement item in _loaded.Snapshot.Announcements.OrderByDescending(item => item.Pinned).ThenByDescending(item => item.Date, StringComparer.Ordinal).Take(12))
        {
            var card = new AnnouncementCard(item, _loaded.Root) { Dock = DockStyle.Top, Height = 78 };
            _announcements.Controls.Add(card); card.BringToFront();
        }
    }

    private static async Task<(Image? Image, long Bytes, long Pixels)> LoadExternalAnnouncementImageAsync(HttpClient client, string url, long maximumBytes, long maximumPixels, CancellationToken cancellationToken)
    {
        try
        {
            if (maximumBytes <= 0 || maximumPixels <= 0) return (null, 0, 0);
            using HttpResponseMessage response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > maximumBytes) return (null, 0, 0);
            await using Stream input = await response.Content.ReadAsStreamAsync(cancellationToken); using var bytes = new MemoryStream(); byte[] buffer = new byte[16 * 1024]; int read;
            while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0) { if (bytes.Length + read > maximumBytes) return (null, 0, 0); bytes.Write(buffer, 0, read); }
            if (!SafeRasterImageMetadata.TryGetDimensions(bytes.GetBuffer().AsSpan(0, checked((int)bytes.Length)), out int width, out int height)) return (null, 0, 0);
            long pixels = (long)width * height;
            if (width > 4096 || height > 4096 || pixels > 8_000_000 || pixels > maximumPixels) return (null, 0, 0);
            bytes.Position = 0; using Image decoded = Image.FromStream(bytes, useEmbeddedColorManagement: false, validateImageData: true);
            if (decoded.Width != width || decoded.Height != height) return (null, 0, 0);
            return (new Bitmap(decoded), bytes.Length, pixels);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException or ArgumentException or OutOfMemoryException) { return (null, 0, 0); }
    }

    private Button CreateTopButton(string text, int rightOffset) => new() { Text = text, FlatStyle = FlatStyle.Flat, Size = new Size(110, 34), Location = new Point(Width - rightOffset, 20), Anchor = AnchorStyles.Top | AnchorStyles.Right };

    private void QueueDpiLayout()
    {
        if (_dpiLayoutPending || _disposeStarted || IsDisposed || Disposing) return;
        _dpiLayoutPending = true;
        try
        {
            BeginInvoke(() =>
            {
                try { if (!_disposeStarted && !IsDisposed && !Disposing) ApplyTemplate(initial: false); }
                finally { _dpiLayoutPending = false; }
            });
        }
        catch (InvalidOperationException) { _dpiLayoutPending = false; }
    }

    private void ApplyTemplate(bool initial = true)
    {
        int S(int value) => (int)Math.Round(value * DeviceDpi / 96d);
        bool sidebar = _loaded.Snapshot.Theme.ServerListMode == ServerListMode.Sidebar;
        _serverSidebar.Visible = sidebar;
        _serverDropdown.Visible = !sidebar;
        switch (_loaded.Snapshot.Theme.Template)
        {
            case LauncherTemplateKind.Widescreen:
                if (initial) ClientSize = new Size(Math.Max(1100, _loaded.Snapshot.Theme.CanvasWidth), Math.Max(650, _loaded.Snapshot.Theme.CanvasHeight));
                _serverSidebar.SetBounds(S(18), S(86), S(250), Math.Max(S(180), ClientSize.Height - S(180)));
                _serverDropdown.SetBounds(S(18), S(86), S(250), S(34));
                _announcements.SetBounds(S(290), S(86), Math.Max(S(200), ClientSize.Width - S(310)), Math.Max(S(120), ClientSize.Height - S(240)));
                break;
            case LauncherTemplateKind.Compact:
                if (initial) ClientSize = new Size(760, 520);
                _serverDropdown.SetBounds(S(24), Math.Min(S(300), ClientSize.Height - S(180)), Math.Min(S(330), ClientSize.Width - S(48)), S(34));
                _serverSidebar.SetBounds(S(24), S(80), S(230), Math.Min(S(210), ClientSize.Height - S(220)));
                _announcements.SetBounds(S(24), S(70), Math.Max(S(200), ClientSize.Width - S(48)), Math.Min(S(205), ClientSize.Height - S(250)));
                break;
            default:
                if (_builtInClassicSkin)
                {
                    ApplyBuiltInClassicLayout(S);
                    break;
                }
                _serverDropdown.SetBounds(S(30), Math.Min(S(350), ClientSize.Height - S(180)), Math.Min(S(360), ClientSize.Width - S(60)), S(34));
                _serverSidebar.SetBounds(S(30), S(80), S(245), Math.Min(S(255), ClientSize.Height - S(220)));
                _announcements.SetBounds(S(sidebar ? 295 : 30), S(80), Math.Max(S(200), sidebar ? ClientSize.Width - S(325) : ClientSize.Width - S(60)), Math.Min(S(245), ClientSize.Height - S(250)));
                break;
        }
        if (!_builtInClassicSkin)
        {
            _launchButton.SetBounds(ClientSize.Width - S(220), ClientSize.Height - S(125), S(180), S(54));
            _overall.SetBounds(S(30), ClientSize.Height - S(68), ClientSize.Width - S(60), S(12));
            _current.SetBounds(S(30), ClientSize.Height - S(48), ClientSize.Width - S(60), S(8));
            _progressText.SetBounds(S(30), ClientSize.Height - S(92), Math.Max(S(120), ClientSize.Width - S(280)), S(22));
            _sourceText.Location = new Point(S(30), ClientSize.Height - S(28));
            _windowTitleText.Location = new Point(S(24), S(24));
            Button[] topButtons = Controls.OfType<Button>().Where(button => button != _launchButton).ToArray();
            for (int i = 0; i < topButtons.Length; i++) topButtons[i].SetBounds(ClientSize.Width - S(145 + i * 120), S(20), S(110), S(34));
        }
        if (!_buttonImagesLoaded)
        {
            _buttonImagesLoaded = true;
            string image = LauncherSnapshotValidator.ResolveAsset(_loaded.Root, _loaded.Snapshot.Theme.LaunchButtonImage);
            if (!string.IsNullOrEmpty(image)) _launchButton.BaseImage = SafeLoadImage(image);
            string hover = LauncherSnapshotValidator.ResolveAsset(_loaded.Root, _loaded.Snapshot.Theme.LaunchButtonHoverImage);
            if (!string.IsNullOrEmpty(hover)) _launchButton.HoverImage = SafeLoadImage(hover);
            string pressed = LauncherSnapshotValidator.ResolveAsset(_loaded.Root, _loaded.Snapshot.Theme.LaunchButtonPressedImage);
            if (!string.IsNullOrEmpty(pressed)) _launchButton.PressedImage = SafeLoadImage(pressed);
            string disabled = LauncherSnapshotValidator.ResolveAsset(_loaded.Root, _loaded.Snapshot.Theme.LaunchButtonDisabledImage);
            if (!string.IsNullOrEmpty(disabled)) _launchButton.DisabledImage = SafeLoadImage(disabled);
            if (_launchButton.BaseImage is null && _builtInClassicSkin) ApplyBuiltInClassicImages();
            else if (_launchButton.BaseImage is null && _loaded.Snapshot.Theme.Template == LauncherTemplateKind.Classic) ApplyClassicLaunchButtonStyle();
        }
        ApplyControlOverrides(S);
    }

    internal static Bitmap BuildClassicBackground(Size size)
    {
        using Bitmap source = LoadClassicBitmap("pfffft.png");
        var image = new Bitmap(Math.Max(1, size.Width), Math.Max(1, size.Height), System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using Graphics graphics = Graphics.FromImage(image);
        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        graphics.DrawImage(source, new Rectangle(Point.Empty, image.Size));
        return image;
    }

    private void ApplyBuiltInClassicLayout(Func<int, int> scale)
    {
        Size classicSize = new(scale(801), scale(554));
        if (ClientSize != classicSize) ClientSize = classicSize;
        _announcements.Visible = false;
        _serverSidebar.Visible = false;
        _serverDropdown.Visible = true;
        _serverDropdown.SetBounds(scale(67), scale(428), scale(150), scale(24));
        _classicServerLabel?.SetBounds(scale(67), scale(407), scale(120), scale(20));
        _launchButton.SetBounds(scale(661), scale(471), scale(114), scale(57));
        _overall.SetBounds(scale(59), scale(491), scale(553), scale(12));
        _current.SetBounds(scale(59), scale(509), scale(553), scale(12));
        _progressText.SetBounds(scale(220), scale(466), scale(390), scale(20));
        _progressText.ForeColor = Color.LimeGreen;
        _sourceText.Visible = false;
        _windowTitleText.Visible = false;
        _actionLinks.Visible = false;
        Control settings = _themeControls[LauncherControlId.SettingsButton];
        settings.Visible = false;
        _themeControls[LauncherControlId.DiagnoseButton].Visible = false;
        _themeControls[LauncherControlId.ChooseClientButton].Visible = false;
        _classicSettingsButton?.SetBounds(scale(743), scale(15), scale(22), scale(26));
        _classicCloseButton?.SetBounds(scale(767), scale(15), scale(22), scale(26));
    }

    private void ApplyBuiltInClassicImages()
    {
        _launchButton.BaseImage = LoadClassicBitmap("Launch_Base.png");
        _launchButton.HoverImage = LoadClassicBitmap("Launch_Hover.png");
        _launchButton.PressedImage = LoadClassicBitmap("Launch_Pressed.png");
        _launchButton.Text = string.Empty;
        if (_classicSettingsButton is not null)
        {
            _classicSettingsButton.BaseImage = LoadClassicBitmap("Config_Base.png");
            _classicSettingsButton.HoverImage = LoadClassicBitmap("Config_Hover.png");
            _classicSettingsButton.PressedImage = LoadClassicBitmap("Config_Pressed.png");
        }
        if (_classicCloseButton is not null)
        {
            _classicCloseButton.BaseImage = LoadClassicBitmap("Cross_Base.png");
            _classicCloseButton.HoverImage = LoadClassicBitmap("Cross_Hover.png");
            _classicCloseButton.PressedImage = LoadClassicBitmap("Cross_Pressed.png");
        }
    }

    private static Bitmap LoadClassicBitmap(string name)
    {
        using Stream stream = typeof(LauncherForm).Assembly.GetManifestResourceStream("Launcher.ThemeRuntime.Classic." + name)
            ?? throw new InvalidOperationException("内置经典皮肤资源缺失：" + name);
        using var image = new Bitmap(stream);
        return new Bitmap(image);
    }

    private void ApplyClassicLaunchButtonStyle()
    {
        _launchButton.FlatStyle = FlatStyle.Flat;
        _launchButton.FlatAppearance.BorderSize = 1;
        _launchButton.FlatAppearance.BorderColor = Color.FromArgb(235, 190, 76);
        _launchButton.BackColor = Color.FromArgb(132, 82, 20);
        _launchButton.ForeColor = Color.FromArgb(255, 241, 190);
    }

    private void ApplyControlOverrides(Func<int, int> scale)
    {
        Font[] oldFonts = _ownedFonts.ToArray();
        _ownedFonts.Clear();
        foreach (LauncherControlOverride style in _loaded.Snapshot.Theme.Controls)
        {
            if (!_themeControls.TryGetValue(style.Id, out Control? control)) continue;
            if (!_originalBackColors.ContainsKey(control)) _originalBackColors[control] = control.BackColor;
            if (!_originalFontSpecs.ContainsKey(control)) _originalFontSpecs[control] = (control.Font.FontFamily.Name, control.Font.Size, control.Font.Style);
            control.SetBounds(scale(style.X), scale(style.Y), scale(style.Width), scale(style.Height));
            control.Visible = style.Visible;
            if (!string.IsNullOrWhiteSpace(style.ForeColor)) control.ForeColor = ColorTranslator.FromHtml(style.ForeColor);
            Color tint = string.IsNullOrWhiteSpace(style.BackColor) ? _originalBackColors[control] : ColorTranslator.FromHtml(style.BackColor);
            control.BackColor = tint;
            string background = LauncherSnapshotValidator.ResolveAsset(_loaded.Root, style.BackgroundImage);
            if (style.OpacityPercent < 100)
            {
                if (_derivedBackgrounds.Remove(control, out Image? previous)) { control.BackgroundImage = null; previous.Dispose(); }
                Image derived = BuildOpacityBackground(control, background, tint, style.OpacityPercent);
                _derivedBackgrounds[control] = derived;
                control.BackgroundImage = derived;
                control.BackgroundImageLayout = ImageLayout.Stretch;
                try { control.BackColor = Color.Transparent; } catch (ArgumentException) { control.BackColor = BackColor; }
                if (control is Button button) button.FlatStyle = FlatStyle.Flat;
            }
            else if (!string.IsNullOrEmpty(background) && control.BackgroundImage is null) { control.BackgroundImage = Own(SafeLoadImage(background)); control.BackgroundImageLayout = ImageLayout.Stretch; }
            if (style.FontSize > 0 || !string.IsNullOrWhiteSpace(style.FontName) || style.Bold)
            {
                (string originalFamily, float originalSize, FontStyle originalStyle) = _originalFontSpecs[control];
                string family = string.IsNullOrWhiteSpace(style.FontName) ? originalFamily : style.FontName;
                float size = style.FontSize > 0 ? style.FontSize : originalSize;
                var font = new Font(family, size, style.Bold ? FontStyle.Bold : originalStyle);
                _ownedFonts.Add(font); control.Font = font;
            }
        }
        foreach (Font font in oldFonts) font.Dispose();
    }

    private Bitmap BuildOpacityBackground(Control control, string explicitBackground, Color tint, int opacityPercent)
    {
        var result = new Bitmap(Math.Max(1, control.Width), Math.Max(1, control.Height), System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using Graphics graphics = Graphics.FromImage(result);
        graphics.Clear(BackColor);
        if (BackgroundImage is not null && ClientSize.Width > 0 && ClientSize.Height > 0)
        {
            var source = new Rectangle(
                control.Left * BackgroundImage.Width / ClientSize.Width,
                control.Top * BackgroundImage.Height / ClientSize.Height,
                Math.Max(1, control.Width * BackgroundImage.Width / ClientSize.Width),
                Math.Max(1, control.Height * BackgroundImage.Height / ClientSize.Height));
            source.Intersect(new Rectangle(Point.Empty, BackgroundImage.Size));
            if (source.Width > 0 && source.Height > 0) graphics.DrawImage(BackgroundImage, new Rectangle(Point.Empty, result.Size), source, GraphicsUnit.Pixel);
        }
        float alpha = opacityPercent / 100f;
        if (!string.IsNullOrEmpty(explicitBackground))
        {
            using Image image = SafeLoadImage(explicitBackground);
            using var attributes = new System.Drawing.Imaging.ImageAttributes();
            var matrix = new System.Drawing.Imaging.ColorMatrix { Matrix33 = alpha };
            attributes.SetColorMatrix(matrix);
            graphics.DrawImage(image, new Rectangle(Point.Empty, result.Size), 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, attributes);
        }
        else using (var brush = new SolidBrush(Color.FromArgb((int)Math.Round(255 * alpha), tint))) graphics.FillRectangle(brush, new Rectangle(Point.Empty, result.Size));
        return result;
    }

    private async Task LaunchSelectedAsync()
    {
        if (_launching) return;
        _launching = true;
        _launchButton.Enabled = false;
        try
        {
        LauncherServer? server = _loaded.Snapshot.Theme.ServerListMode == ServerListMode.Sidebar ? _serverSidebar.SelectedNode?.Tag as LauncherServer : _serverDropdown.SelectedItem as LauncherServer;
        if (server is null) { MessageBox.Show(this, "请先选择区服。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        if (server.Status is ServerOperatingStatus.Maintenance or ServerOperatingStatus.ComingSoon or ServerOperatingStatus.Hidden)
        {
            string message = server.Status == ServerOperatingStatus.ComingSoon ? "该区服尚未开放。" : "该区服由 GM 标记为维护中。";
            MessageBox.Show(this, message, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        MicroEndpoint micro = server.MicroOverride ?? _loaded.Snapshot.DefaultMicro;
        ClientSelectionResult? selectedClient = ClientSelection.Resolve(this, _loaded.Snapshot.ProjectId, _clientDirectory, _loaded.Snapshot.LoginCoreResources);
        if (selectedClient is null) return;
        var readinessProgress = new Progress<LauncherProgressState>(state => { if (!_disposeStarted && !IsDisposed && !Disposing) UpdateProgress(state); });
        if (micro.Enabled && !await MicroGatewayReadiness.EnsureCoreLibrariesAsync(micro, _loaded.Snapshot.ProjectId, selectedClient.ResourceDirectory, _loaded.Snapshot.LoginCoreResources, readinessProgress, _lifetimeCancellation.Token))
        {
            if (_lifetimeCancellation.IsCancellationRequested || IsDisposed || Disposing) return;
            MessageBox.Show(this, $"微端服务器 {micro.Address}:{micro.Port} 尚未启动，或缺少登录核心资源。\r\n\r\n请先运行“一键生成全部成品”得到的独立微端部署包，并确认资源目录中包含 Title.Lib、ChrSel.Lib、Prguse.Lib。", "登录资源尚未就绪", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (!string.Equals(selectedClient.ResourceDirectory, _selectedClient.ResourceDirectory, StringComparison.OrdinalIgnoreCase))
        {
            _selectedClient = selectedClient;
            if (!_settingsDirty) _settings = ClientSettingsWriter.Read(selectedClient.ResourceDirectory, CloneSettings(_loaded.Snapshot.Defaults));
        }
        LauncherProgressChannel.Clear(_loaded.Snapshot.ProjectId);
        ClientSettingsWriter.Write(selectedClient.ResourceDirectory, _settings);
        ClientSettingsWriter.WriteMicroIdentity(selectedClient.ResourceDirectory, _loaded.Snapshot.ProjectId, micro.User);
        ClientSettingsWriter.ValidateWritableDirectory(selectedClient.ResourceDirectory);
        _launch(selectedClient.ExecutableDirectory, selectedClient.ResourceDirectory, server, micro, _settings);
        UpdateProgress(new LauncherProgressState("游戏已启动；普通资源继续按需下载", string.Empty, 0, 0, 0, 0, 0));
        await Task.Delay(1500);
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
            return;
        }
        finally
        {
            _launching = false;
            if (!IsDisposed) _launchButton.Enabled = true;
        }
    }

    private async Task DiagnoseAsync()
    {
        LauncherServer? server = _serverDropdown.SelectedItem as LauncherServer ?? _serverSidebar.SelectedNode?.Tag as LauncherServer;
        if (server is null) return;
        _progressText.Text = "正在执行三秒连通性诊断……";
        TimeSpan? elapsed = await ServerConnectivityDiagnostic.ProbeAsync(server.Address, server.Port, CancellationToken.None);
        _progressText.Text = elapsed is null ? "诊断结果：无法在三秒内建立 TCP 连接（不改变 GM 区服状态）" : $"诊断结果：连接成功，用时 {elapsed.Value.TotalMilliseconds:F0} ms（不代表在线人数）";
    }

    public void UpdateProgress(LauncherProgressState state)
    {
        _overall.Value = (int)Math.Round(state.OverallFraction * 100);
        _current.Value = (int)Math.Round(state.CurrentFraction * 100);
        _progressText.Text = state.Stage + (string.IsNullOrEmpty(state.CurrentFile) ? string.Empty : $" · {state.CurrentFile}") + (state.BytesPerSecond <= 0 ? string.Empty : $" · {FormatBytes((long)state.BytesPerSecond)}/s · 剩余 {FormatBytes(state.RemainingBytes)}");
    }

    private void PollProgress()
    {
        if (LauncherProgressChannel.TryRead(_loaded.Snapshot.ProjectId, out LauncherProgressSnapshot? snapshot) && snapshot is not null && DateTimeOffset.UtcNow - snapshot.UpdatedUtc < TimeSpan.FromMinutes(2)) UpdateProgress(snapshot.State);
    }

    internal LauncherDpiLayoutResult ValidateDpiMessage(int dpi)
    {
        const int WmDpiChanged = 0x02E0;
        Rectangle bounds = Bounds;
        int width = (int)Math.Round(bounds.Width * dpi / (double)Math.Max(1, DeviceDpi));
        int height = (int)Math.Round(bounds.Height * dpi / (double)Math.Max(1, DeviceDpi));
        var suggested = new NativeRect(bounds.Left, bounds.Top, bounds.Left + width, bounds.Top + height);
        nint memory = System.Runtime.InteropServices.Marshal.AllocHGlobal(System.Runtime.InteropServices.Marshal.SizeOf<NativeRect>());
        try
        {
            System.Runtime.InteropServices.Marshal.StructureToPtr(suggested, memory, false);
            nint packedDpi = (nint)(dpi | (dpi << 16));
            SendMessage(Handle, WmDpiChanged, packedDpi, memory);
            Application.DoEvents();
            PerformLayout();
            Application.DoEvents();
        }
        finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(memory); }

        Rectangle canvas = new(Point.Empty, ClientSize);
        bool sidebarMode = _loaded.Snapshot.Theme.ServerListMode == ServerListMode.Sidebar;
        Control hidden = sidebarMode ? _serverDropdown : _serverSidebar;
        Control[] active = Controls.Cast<Control>().Where(item => item.Visible && item != hidden).ToArray();
        Control[] outside = active.Where(control => !canvas.Contains(control.Bounds)).ToArray();
        bool inside = outside.Length == 0;
        var missed = new List<string>();
        bool hits = _clickTargets.Where(item => item.Visible && item != hidden).All(control =>
        {
            Point center = new(control.Left + control.Width / 2, control.Top + control.Height / 2);
            Control? hit = GetChildAtPoint(center, GetChildAtPointSkip.Invisible | GetChildAtPointSkip.Disabled | GetChildAtPointSkip.Transparent);
            if (hit != control) missed.Add($"{control.Text}/{control.GetType().Name}->{hit?.Text}/{hit?.GetType().Name}");
            return hit == control;
        });
        string[] truncated = Descendants(this).Where(control => control.Visible && !string.IsNullOrWhiteSpace(control.Text) && control is Label or Button)
            .Where(control => TextRenderer.MeasureText(control.Text, control.Font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width > control.ClientSize.Width + 2)
            .Select(control => $"{control.Text}/{control.GetType().Name}").ToArray();
        string[] serverTruncation = sidebarMode
            ? _serverSidebar.Nodes.Cast<TreeNode>().SelectMany(group => group.Nodes.Cast<TreeNode>())
                .Where(node => TextRenderer.MeasureText(node.Text, _serverSidebar.Font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width > _serverSidebar.ClientSize.Width - 44)
                .Select(node => node.Text + "/TreeView").ToArray()
            : _serverDropdown.Items.Cast<LauncherServer>()
                .Where(server => TextRenderer.MeasureText(server.Name, _serverDropdown.Font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width > _serverDropdown.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 4)
                .Select(server => server.Name + "/ComboBox").ToArray();
        string[] allTruncation = truncated.Concat(serverTruncation).Distinct(StringComparer.Ordinal).ToArray();
        string details = string.Join("; ", outside.Select(item => $"越界:{item.Text}/{item.GetType().Name}={item.Bounds},画布={canvas}").Concat(missed).Concat(allTruncation.Select(item => $"文字截断:{item}")));
        return new LauncherDpiLayoutResult(inside && DeviceDpi == dpi, hits, active.Length, DeviceDpi, details, allTruncation.Length == 0);
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls) { yield return child; foreach (Control nested in Descendants(child)) yield return nested; }
    }

    private static void ApplyTaskbarIdentity(string projectId, string taskbarName)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(taskbarName)) return;
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(taskbarName);
        string digest = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant()[..16];
        SetCurrentProcessExplicitAppUserModelID($"LyoCrystal.{projectId}.{digest}");
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private readonly record struct NativeRect(int Left, int Top, int Right, int Bottom);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern nint SendMessage(nint window, int message, nint wParam, nint lParam);

    [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

    private static Image SafeLoadImage(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using Image source = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: true);
        if (source.Width > 4096 || source.Height > 4096) throw new InvalidDataException("主题图片像素尺寸超过 4096");
        return new Bitmap(source);
    }
    private T Own<T>(T image) where T : Image { _ownedImages.Add(image); return image; }
    protected override void Dispose(bool disposing) { if (disposing) { _disposeStarted = true; _lifetimeCancellation.Cancel(); _lifetimeCancellation.Dispose(); _announcementCancellation.Dispose(); _progressTimer.Dispose(); foreach (Image image in _derivedBackgrounds.Values) image.Dispose(); _derivedBackgrounds.Clear(); foreach (Image image in _ownedImages) image.Dispose(); _ownedImages.Clear(); foreach (Font font in _ownedFonts) font.Dispose(); _ownedFonts.Clear(); } base.Dispose(disposing); }
    private static LauncherPlayerSettings CloneSettings(LauncherPlayerSettings value) => new() { Resolution = value.Resolution, FullScreen = value.FullScreen, Borderless = value.Borderless, FpsCap = value.FpsCap, MaxFps = value.MaxFps, Volume = value.Volume, MusicVolume = value.MusicVolume, TopMost = value.TopMost, AutoStart = value.AutoStart, AdvancedLogs = value.AdvancedLogs, MicroCacheLimitMb = value.MicroCacheLimitMb };
    private static string StatusText(ServerOperatingStatus value) => value switch
    {
        ServerOperatingStatus.Busy => "火爆",
        ServerOperatingStatus.Recommended => "推荐",
        ServerOperatingStatus.NewServer => "新区",
        ServerOperatingStatus.Maintenance => "维护",
        ServerOperatingStatus.ComingSoon => "即将开放",
        ServerOperatingStatus.Hidden => "隐藏",
        _ => "正常",
    };
    private static string FormatBytes(long value) => value >= 1024 * 1024 ? $"{value / 1024d / 1024d:F1} 兆字节" : value >= 1024 ? $"{value / 1024d:F1} 千字节" : $"{value} 字节";
}
