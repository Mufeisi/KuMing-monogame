using Launcher.ThemeRuntime;

namespace LyoCrystal.LauncherEditor;

public static class EditorPreflightValidator
{
    public static IReadOnlyList<string> Validate(EditorProject project, string projectRoot)
    {
        ArgumentNullException.ThrowIfNull(project);
        LauncherSnapshotValidator.Validate(project.Snapshot);
        var issues = new List<string>();
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
