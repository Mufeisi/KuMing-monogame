using Launcher.ThemeRuntime;

namespace LyoCrystal.LauncherEditor;

public static class EditorPreflightValidator
{
    public static IReadOnlyList<string> Validate(EditorProject project, string projectRoot)
    {
        ArgumentNullException.ThrowIfNull(project);
        LauncherSnapshotValidator.Validate(project.Snapshot);
        var issues = new List<string>();
        project.SynchronizeMicroIdentity();
        if (project.Gateway.MemoryCacheMb is < 16 or > 1024 || project.Gateway.DiskCacheMb is < 128 or > 32768) issues.Add("微端网关缓存容量超出允许范围");
        if (project.Snapshot.DefaultMicro.Enabled && (string.IsNullOrWhiteSpace(project.Snapshot.DefaultMicro.ResourceVersion) || string.IsNullOrWhiteSpace(project.Snapshot.DefaultMicro.SigningIdentity)))
            issues.Add("项目默认微端缺少资源版本或签名身份");
        foreach (LauncherServer server in project.Snapshot.Servers.Where(item => item.MicroOverride?.Enabled == true))
            if (!string.Equals(server.MicroOverride!.ResourceVersion, project.Snapshot.DefaultMicro.ResourceVersion, StringComparison.Ordinal) ||
                !string.Equals(server.MicroOverride.SigningIdentity, project.Snapshot.DefaultMicro.SigningIdentity, StringComparison.Ordinal))
                issues.Add($"区服 {server.Name} 的微端资源版本或签名身份与项目不一致");
        foreach (int dpi in new[] { 96, 120, 144, 192 })
        {
            LauncherDpiLayoutResult result = LauncherRuntimeHost.ValidatePerMonitorDpiForEvidence(project.Snapshot, projectRoot, dpi);
            if (!result.AllControlsInsideCanvas || !result.ClickTargetsMatch || !result.TextFits) issues.Add($"{dpi * 100 / 96}%：{result.Details}");
        }
        LauncherControlOverride[] controls = project.Snapshot.Theme.Controls.Where(item => item.Visible).ToArray();
        for (int i = 0; i < controls.Length; i++) for (int j = i + 1; j < controls.Length; j++)
        {
            Rectangle a = new(controls[i].X, controls[i].Y, controls[i].Width, controls[i].Height);
            Rectangle b = new(controls[j].X, controls[j].Y, controls[j].Width, controls[j].Height);
            Rectangle intersection = Rectangle.Intersect(a, b);
            if (intersection.Width > 0 && intersection.Height > 0) issues.Add($"控件点击区域重叠：{controls[i].Id} 与 {controls[j].Id}");
        }
        return issues;
    }

    public static void ThrowIfInvalid(EditorProject project, string projectRoot)
    {
        IReadOnlyList<string> issues = Validate(project, projectRoot);
        if (issues.Count > 0) throw new InvalidDataException("发布前检查未通过：\r\n" + string.Join("\r\n", issues));
    }
}
