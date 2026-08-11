using System.Drawing.Imaging;
using Launcher.ThemeRuntime;

namespace LyoCrystal.LauncherEditor;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Length == 2 && args[0] == "--editor-smoke") return RunSmoke(args[1]);
        string workspace = args.Length == 2 && args[0] == "--workspace" ? args[1] : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "LyoCrystal 启动器项目");
        Application.Run(new MainForm(new EditorProjectStore(workspace)));
        return 0;
    }

    private static int RunSmoke(string outputDirectory)
    {
        try
        {
            string output = Path.GetFullPath(outputDirectory);
            Directory.CreateDirectory(output);
            var store = new EditorProjectStore(Path.Combine(output, "workspace"));
            EditorProject project = store.ListProjectIds().Contains("smoke-project", StringComparer.OrdinalIgnoreCase)
                ? store.Load("smoke-project")
                : store.Create("smoke-project", "GM 离线编辑器验收", LauncherTemplateKind.Widescreen);
            project.Snapshot.Theme.ServerListMode = ServerListMode.Sidebar;
            project.Snapshot.Servers[0].Name = "编辑器验收一区";
            project.Snapshot.Announcements = new List<LauncherAnnouncement> { new() { Title = "离线公告", Summary = "断网状态也可以保存、预览和生成部署包。", Date = DateTime.Today.ToString("yyyy-MM-dd") } };
            store.Save(project);
            using Bitmap preview = LauncherRuntimeHost.RenderTemplateForEvidence(project.Snapshot, store.GetProjectDirectory(project.Snapshot.ProjectId), 1f);
            preview.Save(Path.Combine(output, "editor-preview.png"), ImageFormat.Png);
            DeploymentPackageBuilder.CreateGatewayPackage(project, Path.Combine(output, "smoke-project-微端网关.zip"), "smoke-code");
            PlayerArtifactBuilder.Create(project, store.GetProjectDirectory(project.Snapshot.ProjectId), Path.Combine(output, "smoke-project-玩家入口.exe"), "smoke-code");
            using (var form = new MainForm(store) { WindowState = FormWindowState.Normal, Size = new Size(1400, 850), StartPosition = FormStartPosition.Manual, Location = new Point(-32000, -32000) })
            {
                form.Show(); Application.DoEvents();
                using var screenshot = new Bitmap(form.Width, form.Height);
                form.DrawToBitmap(screenshot, new Rectangle(Point.Empty, screenshot.Size));
                screenshot.Save(Path.Combine(output, "editor-ui.png"), ImageFormat.Png);
                form.Hide();
            }
            return File.Exists(Path.Combine(output, "editor-preview.png")) && File.Exists(Path.Combine(output, "editor-ui.png")) && File.Exists(Path.Combine(output, "smoke-project-微端网关.zip")) && File.Exists(Path.Combine(output, "smoke-project-玩家入口.exe")) ? 0 : 2;
        }
        catch { return 1; }
    }
}
