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
        if (args.Length == 2 && args[0] == "--editor-ui-smoke") return RunUiSmoke(args[1]);
        string workspace = args.Length == 2 && args[0] == "--workspace" ? args[1] : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "传奇启动器项目");
        Application.Run(new MainForm(new EditorProjectStore(workspace)));
        return 0;
    }

    private static int RunUiSmoke(string outputDirectory)
    {
        string output = Path.GetFullPath(outputDirectory);
        try
        {
            Directory.CreateDirectory(output);
            var store = new EditorProjectStore(Path.Combine(output, "项目"));
            if (store.ListProjectIds().Count == 0) store.Create("ui-project", "中文傻瓜启动器", LauncherTemplateKind.Classic);
            using var form = new MainForm(store) { StartPosition = FormStartPosition.Manual, Location = new Point(-32000, -32000) };
            form.Show(); form.WindowState = FormWindowState.Normal; form.Size = new Size(1280, 800); Application.DoEvents();
            using var screenshot = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(screenshot, new Rectangle(Point.Empty, screenshot.Size));
            screenshot.Save(Path.Combine(output, "中文傻瓜配置器.png"), ImageFormat.Png);
            ToolStripDropDownButton advanced = form.Controls.OfType<ToolStrip>().SelectMany(strip => strip.Items.OfType<ToolStripDropDownButton>()).Single(item => item.Text == "高级工具");
            ((ToolStripMenuItem)advanced.DropDownItems[0]).PerformClick(); Application.DoEvents();
            using var advancedScreenshot = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(advancedScreenshot, new Rectangle(Point.Empty, advancedScreenshot.Size));
            advancedScreenshot.Save(Path.Combine(output, "中文高级设置.png"), ImageFormat.Png);
            form.Hide();
            return 0;
        }
        catch (Exception ex)
        {
            try { Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output, "界面验证错误.txt"), ex.ToString()); } catch { }
            return 1;
        }
    }

    private static int RunSmoke(string outputDirectory)
    {
        string output = Path.GetFullPath(outputDirectory);
        try
        {
            Directory.CreateDirectory(output);
            var store = new EditorProjectStore(Path.Combine(output, "workspace"));
            EditorProject project = store.ListProjectIds().Contains("smoke-project", StringComparer.OrdinalIgnoreCase)
                ? store.Load("smoke-project")
                : store.Create("smoke-project", "管理员离线配置器验收", LauncherTemplateKind.Widescreen);
            project.Snapshot.Theme.ServerListMode = ServerListMode.Sidebar;
            project.Snapshot.RemoteReleaseBaseUrl = "http://127.0.0.1:8080/launcher/";
            project.Snapshot.Servers[0].Name = "编辑器验收一区";
            project.Snapshot.Announcements = new List<LauncherAnnouncement> { new() { Title = "离线公告", Summary = "断网状态也可以保存、预览和生成部署包。", Date = DateTime.Today.ToString("yyyy-MM-dd") } };
            if (string.IsNullOrWhiteSpace(project.ImportedClientDirectory))
            {
                project.ImportedClientDirectory = Path.Combine(output, "smoke-client");
                foreach (string relative in new[] { "Data/Title.Lib", "Data/ChrSel.Lib", "Data/Prguse.Lib" })
                {
                    string path = Path.Combine(project.ImportedClientDirectory, relative.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    if (!File.Exists(path)) File.WriteAllBytes(path, System.Text.Encoding.ASCII.GetBytes("editor-smoke-" + relative));
                }
            }
            store.Save(project);
            using Bitmap preview = LauncherRuntimeHost.RenderTemplateForEvidence(project.Snapshot, store.GetProjectDirectory(project.Snapshot.ProjectId), 1f);
            preview.Save(Path.Combine(output, "editor-preview.png"), ImageFormat.Png);
            string player = Path.Combine(output, "smoke-project-玩家入口.exe");
            PlayerArtifactBuilder.Create(project, store.GetProjectDirectory(project.Snapshot.ProjectId), player, "smoke-code");
            project.Release.PlayerUpdateMode = PlayerUpdateMode.Normal;
            project.Release.PlayerUpdateFile = player;
            project.Release.PlayerUpdateVersion = project.Brand.FileVersion;
            string publish = Path.Combine(output, "signed-publish");
            ProjectReleaseResult first = ProjectReleasePublisher.Publish(project, store.GetProjectDirectory(project.Snapshot.ProjectId), publish, "离线冒烟首发");
            project.Snapshot.Announcements[0].Summary = "第二个不可变版本，用于验证更高序列回滚。";
            ProjectReleasePublisher.Publish(project, store.GetProjectDirectory(project.Snapshot.ProjectId), publish, "离线冒烟第二版");
            ProjectReleasePublisher.Rollback(project, store.GetProjectDirectory(project.Snapshot.ProjectId), publish, first.VersionName, "离线冒烟回滚");
            project.Release.LastPublishRoot = publish;
            store.Save(project);
            ProjectReleasePublisher.CreateOfflineDeploymentPackage(publish, Path.Combine(output, "smoke-project-离线发布.zip"));
            ProjectReleaseKeyStore.ExportRecovery(project, store.GetProjectDirectory(project.Snapshot.ProjectId), "Smoke-Recovery-Password-2026", Path.Combine(output, "smoke-project-密钥恢复包.lyorecovery"));
            DeploymentPackageBuilder.CreateGatewayPackage(project, Path.Combine(output, "smoke-project-微端网关.zip"), "smoke-code");
            using (var form = new MainForm(store) { WindowState = FormWindowState.Normal, Size = new Size(1400, 850), StartPosition = FormStartPosition.Manual, Location = new Point(-32000, -32000) })
            {
                form.Show(); Application.DoEvents();
                using var screenshot = new Bitmap(form.Width, form.Height);
                form.DrawToBitmap(screenshot, new Rectangle(Point.Empty, screenshot.Size));
                screenshot.Save(Path.Combine(output, "editor-ui.png"), ImageFormat.Png);
                form.Hide();
            }
            return File.Exists(Path.Combine(output, "editor-preview.png")) && File.Exists(Path.Combine(output, "editor-ui.png")) && File.Exists(Path.Combine(output, "smoke-project-微端网关.zip")) && File.Exists(player) && File.Exists(Path.Combine(output, "smoke-project-离线发布.zip")) && File.Exists(Path.Combine(output, "smoke-project-密钥恢复包.lyorecovery")) ? 0 : 2;
        }
        catch (Exception ex)
        {
            try { Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output, "smoke-error.txt"), ex.ToString()); } catch { }
            return 1;
        }
    }
}
