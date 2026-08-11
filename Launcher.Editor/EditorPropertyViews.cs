using System.ComponentModel;
using Launcher.ThemeRuntime;

namespace LyoCrystal.LauncherEditor;

internal sealed class ProjectBrandPropertyView
{
    private readonly EditorProject _project;
    public ProjectBrandPropertyView(EditorProject project) => _project = project;
    [Category("项目"), DisplayName("项目标识"), ReadOnly(true)] public string ProjectId => _project.Snapshot.ProjectId;
    [Category("项目"), DisplayName("项目名称")] public string ProjectName { get => _project.Snapshot.ProjectName; set => _project.Snapshot.ProjectName = value; }
    [Category("项目"), DisplayName("远程发布地址"), Description("支持 HTTP 或 HTTPS；留空时使用内置和上次有效快照。")] public string RemoteReleaseBaseUrl { get => _project.Snapshot.RemoteReleaseBaseUrl; set => _project.Snapshot.RemoteReleaseBaseUrl = value; }
    [Category("品牌"), DisplayName("输出文件名")] public string OutputFileName { get => _project.Brand.OutputFileName; set => _project.Brand.OutputFileName = value; }
    [Category("品牌"), DisplayName("产品名称")] public string ProductName { get => _project.Brand.ProductName; set => _project.Brand.ProductName = value; }
    [Category("品牌"), DisplayName("文件说明")] public string FileDescription { get => _project.Brand.FileDescription; set => _project.Brand.FileDescription = value; }
    [Category("品牌"), DisplayName("公司名称")] public string CompanyName { get => _project.Brand.CompanyName; set => _project.Brand.CompanyName = value; }
    [Category("品牌"), DisplayName("版权")] public string Copyright { get => _project.Brand.Copyright; set => _project.Brand.Copyright = value; }
    [Category("品牌"), DisplayName("文件版本")] public string FileVersion { get => _project.Brand.FileVersion; set => _project.Brand.FileVersion = value; }
    [Category("品牌"), DisplayName("产品版本")] public string ProductVersion { get => _project.Brand.ProductVersion; set => _project.Brand.ProductVersion = value; }
    [Category("品牌"), DisplayName("窗口标题")] public string WindowTitle { get => _project.Brand.WindowTitle; set => _project.Brand.WindowTitle = value; }
    [Category("品牌"), DisplayName("任务栏名称"), Description("写入玩家入口的产品标识，供 Windows 任务栏和进程界面识别。")]
    public string TaskbarName { get => _project.Brand.TaskbarName; set => _project.Brand.TaskbarName = value; }
    [Category("品牌"), DisplayName("图标路径")] public string IconPath { get => _project.Brand.IconPath; set => _project.Brand.IconPath = value; }
}

internal sealed class ThemePropertyView
{
    private readonly LauncherTheme _theme;
    public ThemePropertyView(LauncherTheme theme) => _theme = theme;
    [DisplayName("模板")] public LauncherTemplateKind Template { get => _theme.Template; set => _theme.Template = value; }
    [DisplayName("区服列表模式")] public ServerListMode ServerListMode { get => _theme.ServerListMode; set => _theme.ServerListMode = value; }
    [DisplayName("画布宽度")] public int CanvasWidth { get => _theme.CanvasWidth; set => _theme.CanvasWidth = value; }
    [DisplayName("画布高度")] public int CanvasHeight { get => _theme.CanvasHeight; set => _theme.CanvasHeight = value; }
    [DisplayName("强调色")] public string AccentColor { get => _theme.AccentColor; set => _theme.AccentColor = value; }
    [DisplayName("背景图片")] public string BackgroundImage { get => _theme.BackgroundImage; set => _theme.BackgroundImage = value; }
    [DisplayName("开始按钮图片")] public string LaunchButtonImage { get => _theme.LaunchButtonImage; set => _theme.LaunchButtonImage = value; }
    [DisplayName("悬停图片（可选）")] public string LaunchButtonHoverImage { get => _theme.LaunchButtonHoverImage; set => _theme.LaunchButtonHoverImage = value; }
    [DisplayName("按下图片（可选）")] public string LaunchButtonPressedImage { get => _theme.LaunchButtonPressedImage; set => _theme.LaunchButtonPressedImage = value; }
    [DisplayName("禁用图片（可选）")] public string LaunchButtonDisabledImage { get => _theme.LaunchButtonDisabledImage; set => _theme.LaunchButtonDisabledImage = value; }
}

internal sealed class GatewayPropertyView
{
    private readonly GatewayDeploymentSettings _value;
    public GatewayPropertyView(GatewayDeploymentSettings value) => _value = value;
    [DisplayName("监听 IP")] public string ListenAddress { get => _value.ListenAddress; set => _value.ListenAddress = value; }
    [DisplayName("端口")] public int Port { get => _value.Port; set => _value.Port = value; }
    [DisplayName("完整客户端目录提示")] public string ResourceDirectory { get => _value.ResourceDirectory; set => _value.ResourceDirectory = value; }
}

internal sealed class DefaultMicroPropertyView
{
    private readonly MicroEndpoint _value;
    public DefaultMicroPropertyView(MicroEndpoint value) => _value = value;
    [DisplayName("启用微端")] public bool Enabled { get => _value.Enabled; set => _value.Enabled = value; }
    [DisplayName("主入口地址")] public string Address { get => _value.Address; set => _value.Address = value; }
    [DisplayName("主入口端口")] public int Port { get => _value.Port; set => _value.Port = value; }
    [DisplayName("备用地址")] public string BackupAddress { get => _value.BackupAddress; set => _value.BackupAddress = value; }
    [DisplayName("备用端口")] public int BackupPort { get => _value.BackupPort; set => _value.BackupPort = value; }
    [DisplayName("访问用户")] public string User { get => _value.User; set => _value.User = value; }
}
