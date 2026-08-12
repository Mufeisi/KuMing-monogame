using System.ComponentModel;
using Launcher.ThemeRuntime;

namespace LyoCrystal.LauncherEditor;

internal sealed class ProjectBrandPropertyView
{
    private readonly EditorProject _project;
    public ProjectBrandPropertyView(EditorProject project) => _project = project;
    [Category("项目"), DisplayName("项目名称")] public string ProjectName { get => _project.Snapshot.ProjectName; set => _project.Snapshot.ProjectName = value; }
    [Category("项目"), DisplayName("玩家下载方式"), Description("微端按需是默认方式；完整客户端方式由管理员生成独立完整包，玩家不能自行切换。")]
    [TypeConverter(typeof(DeliveryModeChineseConverter))]
    public ClientDeliveryMode DeliveryMode { get => _project.DeliveryMode; set => _project.DeliveryMode = value; }
    [Category("项目"), DisplayName("远程发布地址"), Description("填写网页发布地址；留空时使用内置和上次有效配置。")] public string RemoteReleaseBaseUrl { get => _project.Snapshot.RemoteReleaseBaseUrl; set => _project.Snapshot.RemoteReleaseBaseUrl = value; }
    [Category("公告"), DisplayName("公告显示模式"), TypeConverter(typeof(AnnouncementModeChineseConverter))] public AnnouncementDisplayMode AnnouncementMode { get => _project.Snapshot.AnnouncementMode; set => _project.Snapshot.AnnouncementMode = value; }
    [Category("公告"), DisplayName("外部网页地址"), Description("仅允许网页地址；加载失败自动回退已签名内置公告。")] public string ExternalAnnouncementUrl { get => _project.Snapshot.ExternalAnnouncementUrl; set => _project.Snapshot.ExternalAnnouncementUrl = value; }
    [Category("品牌"), DisplayName("输出文件名")] public string OutputFileName { get => _project.Brand.OutputFileName; set => _project.Brand.OutputFileName = value; }
    [Category("品牌"), DisplayName("产品名称")] public string ProductName { get => _project.Brand.ProductName; set => _project.Brand.ProductName = value; }
    [Category("品牌"), DisplayName("文件说明")] public string FileDescription { get => _project.Brand.FileDescription; set => _project.Brand.FileDescription = value; }
    [Category("品牌"), DisplayName("公司名称")] public string CompanyName { get => _project.Brand.CompanyName; set => _project.Brand.CompanyName = value; }
    [Category("品牌"), DisplayName("版权")] public string Copyright { get => _project.Brand.Copyright; set => _project.Brand.Copyright = value; }
    [Category("品牌"), DisplayName("文件版本")] public string FileVersion { get => _project.Brand.FileVersion; set => _project.Brand.FileVersion = value; }
    [Category("品牌"), DisplayName("产品版本")] public string ProductVersion { get => _project.Brand.ProductVersion; set => _project.Brand.ProductVersion = value; }
    [Category("品牌"), DisplayName("窗口标题")] public string WindowTitle { get => _project.Brand.WindowTitle; set => _project.Brand.WindowTitle = value; }
    [Category("品牌"), DisplayName("任务栏名称"), Description("写入玩家入口的产品标识，供系统任务栏和进程界面识别。")]
    public string TaskbarName { get => _project.Brand.TaskbarName; set => _project.Brand.TaskbarName = value; }
    [Category("品牌"), DisplayName("图标路径")] public string IconPath { get => _project.Brand.IconPath; set => _project.Brand.IconPath = value; }
}

internal sealed class ThemePropertyView
{
    private readonly EditorProject _project;
    private readonly LauncherTheme _theme;
    public ThemePropertyView(EditorProject project) { _project = project; _theme = project.Snapshot.Theme; }
    [DisplayName("模板"), TypeConverter(typeof(TemplateKindChineseConverter))] public LauncherTemplateKind Template { get => _theme.Template; set => _theme.Template = value; }
    [DisplayName("区服列表模式"), TypeConverter(typeof(ServerListModeChineseConverter))] public ServerListMode ServerListMode { get => _theme.ServerListMode; set => _theme.ServerListMode = value; }
    [DisplayName("画布宽度")] public int CanvasWidth { get => _theme.CanvasWidth; set => _theme.CanvasWidth = value; }
    [DisplayName("画布高度")] public int CanvasHeight { get => _theme.CanvasHeight; set => _theme.CanvasHeight = value; }
    [DisplayName("强调色")] public string AccentColor { get => _theme.AccentColor; set => _theme.AccentColor = value; }
    [DisplayName("背景图片")] public string BackgroundImage { get => _theme.BackgroundImage; set => _theme.BackgroundImage = value; }
    [DisplayName("开始按钮图片")] public string LaunchButtonImage { get => _theme.LaunchButtonImage; set => _theme.LaunchButtonImage = value; }
    [DisplayName("悬停图片（可选）")] public string LaunchButtonHoverImage { get => _theme.LaunchButtonHoverImage; set => _theme.LaunchButtonHoverImage = value; }
    [DisplayName("按下图片（可选）")] public string LaunchButtonPressedImage { get => _theme.LaunchButtonPressedImage; set => _theme.LaunchButtonPressedImage = value; }
    [DisplayName("禁用图片（可选）")] public string LaunchButtonDisabledImage { get => _theme.LaunchButtonDisabledImage; set => _theme.LaunchButtonDisabledImage = value; }
    [DisplayName("导入时优化图片"), Description("启用时 BMP 默认无损转换为 PNG；关闭时保留原始格式。")]
    [TypeConverter(typeof(ChineseBooleanConverter))]
    public bool OptimizeImportedImages { get => _project.OptimizeImportedImages; set => _project.OptimizeImportedImages = value; }
}

internal sealed class GatewayPropertyView
{
    private readonly GatewayDeploymentSettings _value;
    public GatewayPropertyView(GatewayDeploymentSettings value) => _value = value;
    [DisplayName("监听 IP")] public string ListenAddress { get => _value.ListenAddress; set => _value.ListenAddress = value; }
    [DisplayName("端口")] public int Port { get => _value.Port; set => _value.Port = value; }
    [DisplayName("完整客户端目录提示")] public string ResourceDirectory { get => _value.ResourceDirectory; set => _value.ResourceDirectory = value; }
    [DisplayName("缓存目录")] public string CacheDirectory { get => _value.CacheDirectory; set => _value.CacheDirectory = value; }
    [DisplayName("内存缓存（兆字节）")] public int MemoryCacheMb { get => _value.MemoryCacheMb; set => _value.MemoryCacheMb = value; }
    [DisplayName("磁盘缓存（兆字节）")] public int DiskCacheMb { get => _value.DiskCacheMb; set => _value.DiskCacheMb = value; }
}

internal sealed class DefaultMicroPropertyView
{
    private readonly MicroEndpoint _value;
    public DefaultMicroPropertyView(MicroEndpoint value) => _value = value;
    [DisplayName("启用微端"), TypeConverter(typeof(ChineseBooleanConverter))] public bool Enabled { get => _value.Enabled; set => _value.Enabled = value; }
    [DisplayName("主入口地址")] public string Address { get => _value.Address; set => _value.Address = value; }
    [DisplayName("主入口端口")] public int Port { get => _value.Port; set => _value.Port = value; }
    [DisplayName("备用地址")] public string BackupAddress { get => _value.BackupAddress; set => _value.BackupAddress = value; }
    [DisplayName("备用端口")] public int BackupPort { get => _value.BackupPort; set => _value.BackupPort = value; }
}

internal sealed class ReleasePropertyView
{
    private readonly ProjectReleaseMetadata _value;
    public ReleasePropertyView(ProjectReleaseMetadata value) => _value = value;
    [DisplayName("下一发布序列"), ReadOnly(true)] public long NextSequence => _value.NextSequence;
    [DisplayName("入口更新模式"), TypeConverter(typeof(UpdateModeChineseConverter))] public PlayerUpdateMode PlayerUpdateMode { get => _value.PlayerUpdateMode; set => _value.PlayerUpdateMode = value; }
    [DisplayName("新版玩家启动器")] public string PlayerUpdateFile { get => _value.PlayerUpdateFile; set => _value.PlayerUpdateFile = value; }
    [DisplayName("新版入口版本")] public string PlayerUpdateVersion { get => _value.PlayerUpdateVersion; set => _value.PlayerUpdateVersion = value; }
    [DisplayName("最近发布目录"), ReadOnly(true)] public string LastPublishRoot => _value.LastPublishRoot;
}
