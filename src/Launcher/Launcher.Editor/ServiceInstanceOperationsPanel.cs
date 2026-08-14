using LyoCrystal.InstanceManagement;

namespace LyoCrystal.LauncherEditor;

internal sealed record ServiceInstanceOperationsEvidence(int ProfileCount, string SelectedInstanceId, ServiceInstanceRuntimeState State, int ComponentCount, bool HasLogs, bool HasAudit);

internal sealed class ServiceInstanceOperationsPanel : UserControl
{
    private readonly ServiceInstanceProfileStore store;
    private readonly ComboBox profiles = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 240 };
    private readonly Button preflight = new() { Text = "运行预检", AutoSize = true };
    private readonly Button start = new() { Text = "启动实例", AutoSize = true };
    private readonly Button health = new() { Text = "刷新健康", AutoSize = true };
    private readonly Button stop = new() { Text = "正常停止", AutoSize = true };
    private readonly Button forceStop = new() { Text = "确认强制停止", AutoSize = true };
    private readonly ListView components = new() { Dock = DockStyle.Top, Height = 210, View = View.Details, FullRowSelect = true, GridLines = true };
    private readonly TextBox output = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, BackColor = Color.White, Font = new Font(FontFamily.GenericMonospace, 9) };
    private readonly Label state = new() { AutoSize = true, ForeColor = DesktopAuthoringTheme.TextSecondary, Margin = new Padding(8, 9, 12, 0) };
    private ServiceInstanceProfile? selectedProfile;
    private ServiceInstanceRuntime? runtime;
    private bool busy;

    internal ServiceInstanceOperationsPanel(string projectRoot)
    {
        store = new ServiceInstanceProfileStore(projectRoot);
        Dock = DockStyle.Fill;
        BackColor = Color.White;
        components.Columns.Add("组件", 150);
        components.Columns.Add("状态", 100);
        components.Columns.Add("PID", 80);
        components.Columns.Add("运行时长", 100);
        components.Columns.Add("版本", 180);
        components.Columns.Add("日志", 360);

        var header = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Dock = DockStyle.Top, Padding = new Padding(28, 22, 28, 12) };
        header.Controls.Add(new Label { Text = "服务实例运行", AutoSize = true, Font = DesktopAuthoringTheme.CreateBodyFont(20, FontStyle.Bold), ForeColor = Color.FromArgb(34, 70, 125) });
        header.Controls.Add(new Label { Text = "从项目 instances 目录选择声明式实例；正式环境只允许预检。启动、健康、日志和停止由实例运行模块负责。", AutoSize = true, MaximumSize = new Size(1050, 0), ForeColor = DesktopAuthoringTheme.TextSecondary, Margin = new Padding(0, 7, 0, 0) });
        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, Padding = new Padding(28, 6, 18, 10) };
        actions.Controls.Add(new Label { Text = "实例", AutoSize = true, Margin = new Padding(0, 9, 6, 0) });
        actions.Controls.Add(profiles);
        var reload = new Button { Text = "重新载入", AutoSize = true };
        actions.Controls.AddRange([reload, preflight, start, health, stop, forceStop, state]);
        var logActions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(28, 8, 18, 4) };
        logActions.Controls.Add(new Label { Text = "诊断、审计与日志", AutoSize = true, Font = DesktopAuthoringTheme.CreateBodyFont(11, FontStyle.Bold), Margin = new Padding(0, 7, 12, 0) });
        var viewLog = new Button { Text = "查看所选组件日志", AutoSize = true };
        logActions.Controls.Add(viewLog);

        Controls.Add(output);
        Controls.Add(logActions);
        Controls.Add(components);
        Controls.Add(actions);
        Controls.Add(header);

        reload.Click += (_, _) => ReloadProfiles();
        profiles.SelectedIndexChanged += (_, _) => LoadSelectedProfile();
        preflight.Click += (_, _) => RunUiAction(() => { RenderPreflight(); return Task.CompletedTask; });
        start.Click += (_, _) => RunUiAction(StartSelectedAsync);
        health.Click += (_, _) => RunUiAction(RefreshHealthAsync);
        stop.Click += (_, _) => RunUiAction(() => StopAsync(forceConfirmed: false));
        forceStop.Click += (_, _) => ConfirmForceStop();
        viewLog.Click += (_, _) => ShowSelectedLog();
        ReloadProfiles();
    }

    internal async Task<ServiceInstanceOperationsEvidence> RunLifecycleForEvidenceAsync()
    {
        if (profiles.Items.Count == 0) throw new InvalidOperationException("没有可用于验证的服务实例档案。");
        profiles.SelectedIndex = 0;
        RenderPreflight();
        await StartSelectedAsync().ConfigureAwait(false);
        await RefreshHealthAsync().ConfigureAwait(false);
        await StopAsync(forceConfirmed: false).ConfigureAwait(false);
        return CaptureRuntimeEvidence();
    }

    internal ServiceInstanceOperationsEvidence CaptureForEvidence()
        => CaptureRuntimeEvidence();

    private ServiceInstanceOperationsEvidence CaptureRuntimeEvidence()
    {
        ServiceInstanceRuntimeSnapshot? snapshot = runtime?.GetSnapshot();
        return new ServiceInstanceOperationsEvidence(
            store.ListInstanceIds().Count,
            selectedProfile?.InstanceId ?? string.Empty,
            snapshot?.State ?? ServiceInstanceRuntimeState.Stopped,
            snapshot?.Components.Count ?? selectedProfile?.Components.Count ?? 0,
            snapshot?.Components.Any(item => File.Exists(item.LogPath)) == true,
            snapshot?.AuditEvents.Count > 0);
    }

    private void ReloadProfiles()
    {
        string? selected = profiles.SelectedItem as string;
        profiles.Items.Clear();
        foreach (string id in store.ListInstanceIds()) profiles.Items.Add(id);
        if (selected is not null && profiles.Items.Contains(selected)) profiles.SelectedItem = selected;
        else if (profiles.Items.Count > 0) profiles.SelectedIndex = 0;
        else
        {
            selectedProfile = null;
            output.Text = "当前项目没有实例档案。请在项目 instances 目录添加经验证的 JSON 档案。";
            RenderState(null);
        }
    }

    private void LoadSelectedProfile()
    {
        if (profiles.SelectedItem is not string id) return;
        try
        {
            selectedProfile = store.Load(id);
            RenderPreflight();
        }
        catch (Exception error)
        {
            selectedProfile = null;
            output.Text = "档案加载失败：" + error.Message;
            RenderState(null);
        }
    }

    private void RenderPreflight()
    {
        if (selectedProfile is null) throw new InvalidOperationException("请先选择实例档案。");
        IReadOnlyList<InstanceDiagnostic> diagnostics = ServiceInstanceProfileValidator.Validate(selectedProfile, inspectFileSystem: true);
        output.Lines = diagnostics.Count == 0
            ? ["预检通过：档案、路径、端口、依赖和秘密边界有效。"]
            : diagnostics.Select(item => $"[{item.Severity}] {item.Code} {item.Source}：{item.Message}").ToArray();
        RenderState(runtime?.GetSnapshot());
    }

    private async Task StartSelectedAsync()
    {
        if (selectedProfile is null) throw new InvalidOperationException("请先选择实例档案。");
        if (runtime is not null && runtime.GetSnapshot().State != ServiceInstanceRuntimeState.Stopped)
            throw new InvalidOperationException("当前实例尚未停止。");
        if (runtime is not null) await runtime.DisposeAsync().ConfigureAwait(false);
        runtime = new ServiceInstanceRuntime(selectedProfile);
        await runtime.StartAsync().ConfigureAwait(false);
    }

    private async Task RefreshHealthAsync()
    {
        if (runtime is null) throw new InvalidOperationException("实例尚未启动。");
        await runtime.RefreshHealthAsync().ConfigureAwait(false);
    }

    private async Task StopAsync(bool forceConfirmed)
    {
        if (runtime is null) return;
        await runtime.StopAsync(forceConfirmed).ConfigureAwait(false);
    }

    private void ConfirmForceStop()
    {
        if (MessageBox.Show(this, "强制停止会终止由当前实例启动的进程树。是否继续第一步确认？", "强制停止确认 1/2", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        if (MessageBox.Show(this, "请再次确认：仅当正常停止超时且日志已保存时执行强制停止。", "强制停止确认 2/2", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        RunUiAction(() => StopAsync(forceConfirmed: true));
    }

    private void AppendAuditAndRender()
    {
        ServiceInstanceRuntimeSnapshot snapshot = runtime!.GetSnapshot();
        output.Lines = snapshot.AuditEvents.Select(item => $"{item.TimestampUtc:O} [{item.Action}] {item.ComponentId} {item.Message}").ToArray();
        RenderState(snapshot);
    }

    private void RenderState(ServiceInstanceRuntimeSnapshot? snapshot)
    {
        components.BeginUpdate();
        components.Items.Clear();
        IEnumerable<ServiceComponentRuntimeSnapshot> values = snapshot?.Components ?? selectedProfile?.Components.Select(component => new ServiceComponentRuntimeSnapshot(component.Id, ServiceComponentRuntimeState.Pending, null, null, TimeSpan.Zero, component.ExpectedVersion, component.LogPath, "待启动")) ?? [];
        foreach (ServiceComponentRuntimeSnapshot item in values)
        {
            var row = new ListViewItem(item.ComponentId);
            row.SubItems.Add(item.State.ToString());
            row.SubItems.Add(item.ProcessId?.ToString() ?? "—");
            row.SubItems.Add(item.Uptime.ToString(@"hh\:mm\:ss"));
            row.SubItems.Add(string.IsNullOrWhiteSpace(item.Version) ? "未知" : item.Version);
            row.SubItems.Add(item.LogPath);
            row.Tag = item;
            components.Items.Add(row);
        }
        components.EndUpdate();
        ServiceInstanceRuntimeState current = snapshot?.State ?? ServiceInstanceRuntimeState.Stopped;
        state.Text = "状态：" + current;
        bool hasProfile = selectedProfile is not null;
        preflight.Enabled = hasProfile && !busy;
        start.Enabled = hasProfile && current == ServiceInstanceRuntimeState.Stopped && selectedProfile!.Environment != ServiceEnvironmentKind.Production && !busy;
        health.Enabled = (current is ServiceInstanceRuntimeState.Healthy or ServiceInstanceRuntimeState.Failed) && !busy;
        stop.Enabled = current != ServiceInstanceRuntimeState.Stopped && !busy;
        forceStop.Enabled = stop.Enabled;
    }

    private void ShowSelectedLog()
    {
        if (components.SelectedItems.Count == 0 || components.SelectedItems[0].Tag is not ServiceComponentRuntimeSnapshot item) return;
        if (!File.Exists(item.LogPath)) { output.Text = "日志文件尚未生成：" + item.LogPath; return; }
        const int limit = 64 * 1024;
        using var stream = new FileStream(item.LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (stream.Length > limit) stream.Seek(-limit, SeekOrigin.End);
        using var reader = new StreamReader(stream);
        output.Text = reader.ReadToEnd();
    }

    private async void RunUiAction(Func<Task> action)
    {
        if (busy) return;
        busy = true;
        RenderState(runtime?.GetSnapshot());
        try
        {
            await action().ConfigureAwait(true);
            if (runtime is not null) AppendAuditAndRender();
        }
        catch (Exception error) { output.Text = "操作失败：" + error.Message; RenderState(runtime?.GetSnapshot()); }
        finally { busy = false; RenderState(runtime?.GetSnapshot()); }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (runtime is not null) runtime.DisposeAsync().AsTask().GetAwaiter().GetResult();
            output.Font.Dispose();
        }
        base.Dispose(disposing);
    }
}
