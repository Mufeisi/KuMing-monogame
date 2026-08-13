namespace LyoCrystal.LauncherEditor;

internal sealed class DistributionOverviewPanel : UserControl
{
    private readonly EditorProject _project;
    private readonly Action<DistributionFixTarget> _navigate;
    private readonly TableLayoutPanel _facts = new() { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Top, Padding = new Padding(18, 12, 18, 12) };
    private readonly FlowLayoutPanel _issues = new() { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Dock = DockStyle.Top, Padding = new Padding(18, 10, 18, 16) };
    private readonly FlowLayoutPanel _endpointResults = new() { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Dock = DockStyle.Top, Padding = new Padding(18, 8, 18, 16) };
    private readonly Button _probeEndpoints = new() { Text = "检查主/备用入口", AutoSize = true };
    private readonly Label _scanState = new() { AutoSize = true, ForeColor = DesktopAuthoringTheme.TextSecondary, Text = "正在扫描资源目录……" };
    private CancellationTokenSource? _scanCancellation;
    private CancellationTokenSource? _endpointCancellation;

    internal DistributionOverviewSnapshot? Snapshot { get; private set; }

    internal DistributionOverviewPanel(EditorProject project, Action<DistributionFixTarget> navigate)
    {
        _project = project;
        _navigate = navigate;
        Dock = DockStyle.Fill;
        BackColor = Color.White;
        AutoScroll = true;
        _facts.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        _facts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var header = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Dock = DockStyle.Top, Padding = new Padding(28, 24, 28, 12) };
        header.Controls.Add(new Label { Text = "发行体概览", AutoSize = true, Font = DesktopAuthoringTheme.CreateBodyFont(20, FontStyle.Bold), ForeColor = Color.FromArgb(34, 70, 125) });
        header.Controls.Add(new Label
        {
            Text = "玩家入口交付客户端核心；资源包由独立微端服务提供。资源版本与签名身份绑定一次发布，区服默认继承项目入口，只在确有需要时覆盖。",
            AutoSize = true, MaximumSize = new Size(1000, 0), ForeColor = DesktopAuthoringTheme.TextSecondary, Margin = new Padding(0, 7, 0, 0)
        });
        var refresh = new Button { Text = "重新扫描", AutoSize = true, Margin = new Padding(28, 0, 0, 8) };
        refresh.Click += (_, _) => BeginRefresh();
        var issueTitle = new Label { Text = "发布前待处理", AutoSize = true, Font = DesktopAuthoringTheme.CreateBodyFont(12, FontStyle.Bold), Margin = new Padding(28, 16, 0, 0) };
        var endpointHeader = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Dock = DockStyle.Top, Padding = new Padding(28, 16, 18, 0) };
        endpointHeader.Controls.Add(new Label { Text = "入口连通性与远端身份", AutoSize = true, Font = DesktopAuthoringTheme.CreateBodyFont(12, FontStyle.Bold), Margin = new Padding(0, 7, 16, 0) });
        _probeEndpoints.Click += async (_, _) => await RunEndpointPreflightAsync();
        endpointHeader.Controls.Add(_probeEndpoints);
        Controls.Add(_endpointResults);
        Controls.Add(endpointHeader);
        Controls.Add(_issues);
        Controls.Add(issueTitle);
        Controls.Add(_facts);
        Controls.Add(refresh);
        Controls.Add(header);
        BeginRefresh();
    }

    internal async Task<IReadOnlyList<DistributionEndpointResult>> RunEndpointPreflightAsync()
    {
        _endpointCancellation?.Cancel();
        _endpointCancellation?.Dispose();
        _endpointCancellation = new CancellationTokenSource();
        CancellationToken cancellationToken = _endpointCancellation.Token;
        _probeEndpoints.Enabled = false;
        _probeEndpoints.Text = "正在检查……";
        _endpointResults.Controls.Clear();
        _endpointResults.Controls.Add(new Label { Text = "正在并行检查所有已启用入口；每个入口最多等待 3 秒。", AutoSize = true, ForeColor = DesktopAuthoringTheme.TextSecondary });
        try
        {
            IReadOnlyList<DistributionEndpointResult> results = await DistributionEndpointPreflight.RunAsync(_project, cancellationToken);
            RenderEndpointResults(results);
            return results;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Array.Empty<DistributionEndpointResult>();
        }
        catch (Exception error)
        {
            _endpointResults.Controls.Clear();
            _endpointResults.Controls.Add(new Label { Text = "入口预检失败：" + error.Message, AutoSize = true, ForeColor = Color.Firebrick });
            return Array.Empty<DistributionEndpointResult>();
        }
        finally
        {
            if (!IsDisposed) { _probeEndpoints.Text = "重新检查入口"; _probeEndpoints.Enabled = true; }
        }
    }

    private void RenderEndpointResults(IReadOnlyList<DistributionEndpointResult> results)
    {
        _endpointResults.SuspendLayout(); _endpointResults.Controls.Clear();
        if (results.Count == 0)
        {
            _endpointResults.Controls.Add(new Label { Text = "当前项目没有已启用的微端入口。", AutoSize = true, ForeColor = DesktopAuthoringTheme.TextSecondary });
        }
        foreach (DistributionEndpointResult result in results)
        {
            string role = result.Role == DistributionEndpointRole.Primary ? "主入口" : "备用入口";
            string state = result.Status switch
            {
                DistributionEndpointStatus.Passed => "通过",
                DistributionEndpointStatus.TimedOut => "超时",
                DistributionEndpointStatus.IdentityMismatch => "身份不一致",
                DistributionEndpointStatus.InvalidResponse => "响应无效",
                _ => "不可达",
            };
            var row = new TableLayoutPanel { AutoSize = true, ColumnCount = 3, Margin = new Padding(0, 3, 0, 3), Width = 980 };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 275));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            row.Controls.Add(new Label { Text = $"{result.Scope} · {role} · {result.Address}:{result.Port}", AutoSize = true, Margin = new Padding(0, 6, 8, 0) }, 0, 0);
            row.Controls.Add(new Label { Text = state, AutoSize = true, ForeColor = result.Passed ? Color.FromArgb(20, 105, 45) : Color.Firebrick, Margin = new Padding(0, 6, 8, 0) }, 1, 0);
            row.Controls.Add(new Label { Text = result.Message, AutoSize = true, MaximumSize = new Size(580, 0), ForeColor = DesktopAuthoringTheme.TextSecondary, Margin = new Padding(0, 6, 0, 0) }, 2, 0);
            _endpointResults.Controls.Add(row);
        }
        _endpointResults.ResumeLayout();
    }

    internal void BeginRefresh()
    {
        _scanCancellation?.Cancel();
        _scanCancellation?.Dispose();
        _scanCancellation = new CancellationTokenSource();
        CancellationToken token = _scanCancellation.Token;
        RenderLoading();
        _ = InspectAsync(token);
    }

    private async Task InspectAsync(CancellationToken token)
    {
        try
        {
            DistributionOverviewSnapshot snapshot = await Task.Run(() => DistributionOverview.Inspect(_project, token), token);
            if (!token.IsCancellationRequested && !IsDisposed) Render(snapshot);
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (!token.IsCancellationRequested && !IsDisposed) RenderFailure(error.Message);
        }
    }

    private void RenderLoading()
    {
        Snapshot = null;
        _facts.SuspendLayout(); _facts.Controls.Clear(); _facts.RowStyles.Clear(); _facts.RowCount = 0;
        AddFact("资源扫描", _scanState);
        _facts.ResumeLayout();
        _issues.Controls.Clear();
    }

    private void Render(DistributionOverviewSnapshot snapshot)
    {
        Snapshot = snapshot;
        _facts.SuspendLayout(); _facts.Controls.Clear(); _facts.RowStyles.Clear(); _facts.RowCount = 0;
        AddFact("交付方式", snapshot.DeliveryMode);
        AddFact("客户端核心", snapshot.ClientCore);
        AddFact("资源包", snapshot.ResourcePackage);
        AddFact("资源版本", snapshot.ResourceVersion);
        AddFact("签名身份", snapshot.SigningIdentity);
        AddFact("默认主入口", snapshot.DefaultEndpoint);
        AddFact("默认备用入口", snapshot.BackupEndpoint);
        AddFact("区服覆盖", snapshot.ServerOverrides);
        _facts.ResumeLayout();

        _issues.SuspendLayout(); _issues.Controls.Clear();
        if (snapshot.Issues.Count == 0)
        {
            _issues.Controls.Add(new Label { Text = "✓ 本地发行体检查通过，可以继续执行入口连通性预检。", AutoSize = true, ForeColor = Color.FromArgb(20, 105, 45), Padding = new Padding(0, 7, 0, 7) });
        }
        else
        {
            foreach (DistributionIssue issue in snapshot.Issues)
            {
                var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0, 3, 0, 3) };
                row.Controls.Add(new Label { Text = "! " + issue.Message, AutoSize = true, ForeColor = Color.FromArgb(150, 70, 0), Margin = new Padding(0, 8, 12, 0) });
                var fix = new Button { Text = FixText(issue.Target), AutoSize = true, Tag = issue.Target };
                fix.Click += (_, _) => _navigate((DistributionFixTarget)fix.Tag!);
                row.Controls.Add(fix); _issues.Controls.Add(row);
            }
        }
        _issues.ResumeLayout();
    }

    private void RenderFailure(string message)
    {
        Snapshot = null; _facts.Controls.Clear(); _facts.RowStyles.Clear(); _facts.RowCount = 0; AddFact("资源扫描", "失败：" + message);
    }

    private void AddFact(string name, string value) => AddFact(name, new Label { Text = value, AutoSize = true, MaximumSize = new Size(900, 0), ForeColor = DesktopAuthoringTheme.TextPrimary });
    private void AddFact(string name, Control value)
    {
        int row = _facts.RowCount++;
        _facts.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _facts.Controls.Add(new Label { Text = name, AutoSize = true, ForeColor = DesktopAuthoringTheme.TextSecondary, Margin = new Padding(0, 7, 12, 7) }, 0, row);
        value.Margin = new Padding(0, 7, 0, 7); _facts.Controls.Add(value, 1, row);
    }

    private static string FixText(DistributionFixTarget target) => target switch
    {
        DistributionFixTarget.ResourceDirectory => "选择资源目录",
        DistributionFixTarget.DefaultEndpoint => "配置默认入口",
        DistributionFixTarget.ServerOverrides => "检查区服覆盖",
        DistributionFixTarget.Signing => "检查签名与版本",
        _ => "运行完整预检",
    };

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _scanCancellation?.Cancel(); _scanCancellation?.Dispose(); _endpointCancellation?.Cancel(); _endpointCancellation?.Dispose(); }
        base.Dispose(disposing);
    }
}
