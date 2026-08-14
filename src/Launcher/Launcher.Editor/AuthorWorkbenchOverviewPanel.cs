using LyoCrystal.Workbench;

namespace LyoCrystal.LauncherEditor;

internal sealed record AuthorWorkbenchEvidence(int VersionCount, int CapabilityCount, int PreflightCount, int OwnerCount, bool HasMergerClosure, bool Passed);

internal sealed class AuthorWorkbenchOverviewPanel : UserControl
{
    private readonly EditorProject project;
    private readonly string projectRoot;
    private readonly ListView facts = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true };
    private readonly Label state = new() { AutoSize = true, ForeColor = DesktopAuthoringTheme.TextSecondary, Margin = new Padding(12, 9, 0, 0) };
    private readonly Button refresh = new() { Text = "刷新版本与能力", AutoSize = true };
    private readonly Button preflight = new() { Text = "运行统一预检", AutoSize = true };
    private readonly TextBox review = new() { Dock = DockStyle.Bottom, Height = 125, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = Color.White };
    private CancellationTokenSource? cancellation;

    internal WorkbenchOverviewSnapshot? Snapshot { get; private set; }

    internal AuthorWorkbenchOverviewPanel(EditorProject project, string projectRoot)
    {
        this.project = project;
        this.projectRoot = Path.GetFullPath(projectRoot);
        Dock = DockStyle.Fill;
        BackColor = Color.White;
        facts.Columns.Add("类型", 90);
        facts.Columns.Add("名称", 190);
        facts.Columns.Add("值", 260);
        facts.Columns.Add("状态", 90);
        facts.Columns.Add("事实所有者", 190);
        facts.Columns.Add("说明", 440);
        var header = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Dock = DockStyle.Top, Padding = new Padding(28, 22, 28, 10) };
        header.Controls.Add(new Label { Text = "作者工作台总览", AutoSize = true, Font = DesktopAuthoringTheme.CreateBodyFont(20, FontStyle.Bold), ForeColor = Color.FromArgb(34, 70, 125) });
        header.Controls.Add(new Label { Text = "统一查看启动器、发行体、GUI、服务实例、Schema、脚本版本与能力；每条事实保留原模块所有者。", AutoSize = true, MaximumSize = new Size(1050, 0), ForeColor = DesktopAuthoringTheme.TextSecondary, Margin = new Padding(0, 7, 0, 0) });
        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(28, 4, 18, 10) };
        var saveSnapshot = new Button { Text = "保存版本快照", AutoSize = true };
        var compare = new Button { Text = "比较最近两次", AutoSize = true };
        var testRelease = new Button { Text = "生成并记录测试发布", AutoSize = true };
        actions.Controls.AddRange([refresh, preflight, saveSnapshot, compare, testRelease, state]);
        Controls.Add(facts);
        Controls.Add(review);
        Controls.Add(actions);
        Controls.Add(header);
        refresh.Click += async (_, _) => await CollectAsync(fullPreflight: false);
        preflight.Click += async (_, _) => await CollectAsync(fullPreflight: true);
        saveSnapshot.Click += (_, _) =>
        {
            try { SaveCurrentSnapshot("manual"); }
            catch (Exception error) { review.Text = "版本快照保存失败：" + error.Message; }
        };
        compare.Click += (_, _) => RenderLatestDiff();
        testRelease.Click += (_, _) => PublishTestRelease();
        _ = CollectAsync(fullPreflight: false);
    }

    internal Task<WorkbenchOverviewSnapshot> RunFullPreflightForEvidenceAsync()
    {
        cancellation?.Cancel();
        return CollectCoreAsync(fullPreflight: true, CancellationToken.None);
    }

    internal WorkbenchStoredSnapshot SaveSnapshotForEvidence(string label) => SaveCurrentSnapshot(label);

    internal IReadOnlyList<WorkbenchVersionChange> CompareLatestSnapshotsForEvidence() => CompareLatestSnapshots();

    internal WorkbenchTestReleaseReview PublishTestReleaseForEvidence(string outputRoot) => PublishTestReleaseCore(outputRoot);

    internal AuthorWorkbenchEvidence CaptureForEvidence()
    {
        WorkbenchOverviewSnapshot snapshot = Snapshot ?? throw new InvalidOperationException("工作台总览尚未完成采集。");
        return new AuthorWorkbenchEvidence(
            snapshot.Facts.Count(item => item.Kind == WorkbenchFactKind.Version),
            snapshot.Facts.Count(item => item.Kind == WorkbenchFactKind.Capability),
            snapshot.Facts.Count(item => item.Kind == WorkbenchFactKind.Preflight),
            snapshot.Facts.Select(item => item.Owner).Distinct(StringComparer.Ordinal).Count(),
            snapshot.Facts.Any(item => item.Id == "multi-region-merger" && item.Value == "关闭"),
            snapshot.Passed);
    }

    private async Task CollectAsync(bool fullPreflight)
    {
        cancellation?.Cancel();
        cancellation?.Dispose();
        cancellation = new CancellationTokenSource();
        refresh.Enabled = preflight.Enabled = false;
        state.Text = fullPreflight ? "正在运行统一预检……" : "正在刷新……";
        try
        {
            WorkbenchOverviewSnapshot snapshot = await CollectCoreAsync(fullPreflight, cancellation.Token).ConfigureAwait(true);
            if (!IsDisposed) Render(snapshot);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        finally
        {
            if (!IsDisposed) { refresh.Enabled = preflight.Enabled = true; }
        }
    }

    private async Task<WorkbenchOverviewSnapshot> CollectCoreAsync(bool fullPreflight, CancellationToken token)
    {
        IReadOnlyList<IWorkbenchFactProvider> providers = fullPreflight ? AuthorWorkbenchFacts.FullPreflight(project, projectRoot) : AuthorWorkbenchFacts.Summary(project, projectRoot);
        WorkbenchOverviewSnapshot snapshot = await new WorkbenchOverviewService(providers).CollectAsync(token).ConfigureAwait(false);
        Snapshot = snapshot;
        return snapshot;
    }

    private void Render(WorkbenchOverviewSnapshot snapshot)
    {
        facts.BeginUpdate();
        facts.Items.Clear();
        foreach (WorkbenchFact fact in snapshot.Facts)
        {
            var row = new ListViewItem(fact.Kind switch { WorkbenchFactKind.Version => "版本", WorkbenchFactKind.Capability => "能力", _ => "预检" });
            row.SubItems.Add(fact.Name);
            row.SubItems.Add(fact.Value);
            row.SubItems.Add(fact.Status switch { WorkbenchFactStatus.Passed => "通过", WorkbenchFactStatus.Warning => "注意", WorkbenchFactStatus.Failed => "失败", _ => "不可用" });
            row.SubItems.Add(fact.Owner);
            row.SubItems.Add(fact.Details);
            row.ForeColor = fact.Status == WorkbenchFactStatus.Failed ? Color.Firebrick : fact.Status is WorkbenchFactStatus.Warning or WorkbenchFactStatus.Unavailable ? Color.FromArgb(150, 70, 0) : DesktopAuthoringTheme.TextPrimary;
            facts.Items.Add(row);
        }
        facts.EndUpdate();
        state.Text = $"采集 {snapshot.Facts.Count} 项｜{snapshot.CapturedAtUtc:yyyy-MM-dd HH:mm:ss} UTC｜" + (snapshot.Passed ? "通过" : "存在失败项");
    }

    private WorkbenchStoredSnapshot SaveCurrentSnapshot(string label)
    {
        WorkbenchOverviewSnapshot snapshot = Snapshot ?? throw new InvalidOperationException("请先刷新工作台总览。");
        WorkbenchStoredSnapshot stored = new WorkbenchReviewStore(projectRoot).SaveSnapshot(snapshot, label);
        review.Text = $"版本快照已保存：{stored.Id}\r\n事实项：{stored.Snapshot.Facts.Count}\r\n时间：{stored.SavedAtUtc:O}";
        return stored;
    }

    private IReadOnlyList<WorkbenchVersionChange> CompareLatestSnapshots()
    {
        var store = new WorkbenchReviewStore(projectRoot);
        string[] ids = store.ListSnapshotIds().TakeLast(2).ToArray();
        if (ids.Length < 2) throw new InvalidOperationException("至少需要两个已保存快照才能比较。");
        return WorkbenchVersionDiff.Compare(store.LoadSnapshot(ids[0]).Snapshot, store.LoadSnapshot(ids[1]).Snapshot);
    }

    private void RenderLatestDiff()
    {
        try
        {
            IReadOnlyList<WorkbenchVersionChange> changes = CompareLatestSnapshots();
            review.Lines = changes.Select(item => $"[{item.Change}] {item.Name}｜{item.Owner}｜{item.Before} → {item.After}").ToArray();
        }
        catch (Exception error) { review.Text = "版本差异读取失败：" + error.Message; }
    }

    private void PublishTestRelease()
    {
        using var folder = new FolderBrowserDialog { Description = "选择空目录保存测试资源发布并记录审查结果" };
        if (folder.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            WorkbenchTestReleaseReview result = PublishTestReleaseCore(folder.SelectedPath);
            review.Text = $"测试发布已验签并记录。\r\n版本：{result.ResourceVersion}\r\n序列：{result.Sequence}\r\n资源包：{result.PackageCount}\r\n签名：{result.KeyId}";
            _ = CollectAsync(fullPreflight: false);
        }
        catch (Exception error) { review.Text = "测试发布失败：" + error.Message; }
    }

    private WorkbenchTestReleaseReview PublishTestReleaseCore(string outputRoot)
    {
        TestResourceReleaseResult result = TestResourceReleasePublisher.Publish(project, projectRoot, outputRoot);
        var record = new WorkbenchTestReleaseReview("release-" + result.Sequence, DateTimeOffset.UtcNow, result.ResourceVersion, result.KeyId, result.Sequence, result.PackageCount, result.OutputRoot, SignatureVerified: true);
        return new WorkbenchReviewStore(projectRoot).SaveTestRelease(record);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { cancellation?.Cancel(); cancellation?.Dispose(); }
        base.Dispose(disposing);
    }
}
