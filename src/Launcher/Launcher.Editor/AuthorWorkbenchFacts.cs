using System.Reflection;
using Launcher.ThemeRuntime;
using LyoCrystal.InstanceManagement;
using LyoCrystal.Workbench;
using Shared.Security;

namespace LyoCrystal.LauncherEditor;

internal static class AuthorWorkbenchFacts
{
    internal static IReadOnlyList<IWorkbenchFactProvider> Summary(EditorProject project, string projectRoot) =>
    [
        new DelegateProvider("启动器项目", token => Task.FromResult<IReadOnlyList<WorkbenchFact>>(ProjectVersions(project))),
        new DelegateProvider("作者能力", token => Task.FromResult<IReadOnlyList<WorkbenchFact>>(Capabilities(project, projectRoot))),
        new DelegateProvider("服务实例", token => Task.FromResult<IReadOnlyList<WorkbenchFact>>(InstanceVersions(projectRoot)))
    ];

    internal static IReadOnlyList<IWorkbenchFactProvider> FullPreflight(EditorProject project, string projectRoot) =>
    [
        .. Summary(project, projectRoot),
        new DelegateProvider("项目发布预检", token => Task.Run(() => ProjectPreflight(project, projectRoot), token)),
        new DelegateProvider("发行体预检", token => Task.Run(() => DistributionPreflight(project, token), token)),
        new DelegateProvider("入口连通性", async token => EndpointPreflight(await DistributionEndpointPreflight.RunAsync(project, token).ConfigureAwait(false))),
        new DelegateProvider("实例档案预检", token => Task.FromResult<IReadOnlyList<WorkbenchFact>>(InstancePreflight(projectRoot)))
    ];

    private static IReadOnlyList<WorkbenchFact> ProjectVersions(EditorProject project)
    {
        ProjectReleaseHistoryItem? currentRelease = project.Release.History.Count == 0 ? null : project.Release.History.MaxBy(item => item.Sequence);
        string editorVersion = typeof(MainForm).Assembly.GetName().Version?.ToString() ?? "未知";
        string runtimeVersion = typeof(LauncherSnapshot).Assembly.GetName().Version?.ToString() ?? "未知";
        return
        [
            Version("author-editor", "作者工作台", editorVersion, "Launcher.Editor 程序集"),
            Version("player-entry", "玩家入口", project.Brand.ProductVersion, "项目品牌元数据"),
            Version("launcher-runtime", "启动器运行时", runtimeVersion, "Launcher.ThemeRuntime 程序集"),
            Version("distribution-resource", "发行资源", Display(project.Snapshot.DefaultMicro.ResourceVersion), "项目默认微端"),
            Version("gui-document", "GUI 文档", $"{project.Format} / 文档 {project.GameGuiDocuments.Count}", "编辑器项目"),
            Version("server-schema", "服务端 Schema", "由实例档案提供", "服务实例档案", WorkbenchFactStatus.Warning),
            Version("server-scripts", "服务端脚本", "由实例档案提供", "服务实例档案", WorkbenchFactStatus.Warning),
            Version("release-current", "当前测试/正式发布", currentRelease is null ? "尚未发布" : $"序列 {currentRelease.Sequence} / {currentRelease.VersionName}", "发布历史", currentRelease is null ? WorkbenchFactStatus.Warning : WorkbenchFactStatus.Passed),
            Version("client-compatibility", "客户端兼容基线", BootstrapManifestTrustConfiguration.CurrentClientCompatibilityVersion.ToString(), "签名清单策略")
        ];
    }

    private static IReadOnlyList<WorkbenchFact> Capabilities(EditorProject project, string projectRoot)
    {
        int instanceCount = new ServiceInstanceProfileStore(projectRoot).ListInstanceIds().Count;
        var facts = new List<WorkbenchFact>
        {
            Capability("launcher-authoring", "启动器可视化编辑", true, "Launcher.Editor"),
            Capability("custom-gui", "游戏 GUI 编辑", project.GameGuiDocuments.Count > 0, "Shared.CustomGui"),
            Capability("micro-distribution", "微端按需发行", project.Snapshot.DefaultMicro.Enabled, "Launcher.ThemeRuntime"),
            Capability("signed-release", "签名发布与回滚", !string.IsNullOrWhiteSpace(project.Release.CurrentKeyId), "ProjectReleasePublisher"),
            Capability("service-instances", "服务实例运行", instanceCount > 0, "Launcher.InstanceManagement", $"档案 {instanceCount} 个"),
            new WorkbenchFact("multi-region-merger", WorkbenchFactKind.Capability, "跨区合服候选", "关闭", "LEG-10 候选门禁", WorkbenchFactStatus.Warning, "真实运营区、角色模型和脱敏演练条件尚未满足。")
        };
        var reviews = new WorkbenchReviewStore(projectRoot);
        string? latestId = reviews.ListTestReleaseIds().LastOrDefault();
        if (latestId is not null)
        {
            WorkbenchTestReleaseReview latest = reviews.LoadTestRelease(latestId);
            facts.Add(new WorkbenchFact("test-release-latest", WorkbenchFactKind.Capability, "最近测试发布", latest.ResourceVersion, "TestResourceReleasePublisher", WorkbenchFactStatus.Passed, $"序列 {latest.Sequence}｜包 {latest.PackageCount}｜签名 {latest.KeyId}"));
        }
        else facts.Add(new WorkbenchFact("test-release-latest", WorkbenchFactKind.Capability, "最近测试发布", "尚未生成", "TestResourceReleasePublisher", WorkbenchFactStatus.Warning));
        return facts;
    }

    private static IReadOnlyList<WorkbenchFact> InstanceVersions(string projectRoot)
    {
        var store = new ServiceInstanceProfileStore(projectRoot);
        var facts = new List<WorkbenchFact>();
        foreach (string id in store.ListInstanceIds())
        {
            ServiceInstanceProfile profile = store.Load(id);
            string executableVersions = string.Join("；", profile.Components.Select(component => component.Id + "=" + Display(component.ExpectedVersion)));
            facts.Add(Version("instance/" + id + "/executables", id + " 组件", executableVersions, "服务实例"));
            facts.Add(Version("instance/" + id + "/schema", id + " Schema", profile.ExpectedSchemaVersion == 0 ? "未声明" : profile.ExpectedSchemaVersion.ToString(), "服务实例", profile.ExpectedSchemaVersion == 0 ? WorkbenchFactStatus.Warning : WorkbenchFactStatus.Passed));
            facts.Add(Version("instance/" + id + "/scripts", id + " 脚本", Display(profile.ExpectedScriptRevision), "服务实例", string.IsNullOrWhiteSpace(profile.ExpectedScriptRevision) ? WorkbenchFactStatus.Warning : WorkbenchFactStatus.Passed));
        }
        if (facts.Count == 0)
            facts.Add(new WorkbenchFact("instance/none", WorkbenchFactKind.Version, "服务实例版本", "无实例档案", "服务实例", WorkbenchFactStatus.Unavailable));
        return facts;
    }

    private static IReadOnlyList<WorkbenchFact> ProjectPreflight(EditorProject project, string projectRoot)
    {
        IReadOnlyList<string> issues = EditorPreflightValidator.Validate(project, projectRoot);
        return ToPreflightFacts("project", "项目发布", "项目发布预检", issues);
    }

    private static IReadOnlyList<WorkbenchFact> DistributionPreflight(EditorProject project, CancellationToken token)
    {
        DistributionOverviewSnapshot snapshot = DistributionOverview.Inspect(project, token);
        return ToPreflightFacts("distribution", "发行体", "发行体预检", snapshot.Issues.Select(item => item.Message).ToArray());
    }

    private static IReadOnlyList<WorkbenchFact> EndpointPreflight(IReadOnlyList<DistributionEndpointResult> results)
    {
        if (results.Count == 0)
            return [new WorkbenchFact("endpoint/none", WorkbenchFactKind.Preflight, "入口连通性", "无已启用入口", "入口连通性", WorkbenchFactStatus.Warning)];
        return results.Select((result, index) => new WorkbenchFact(
            "endpoint/" + index,
            WorkbenchFactKind.Preflight,
            result.Scope + (result.Role == DistributionEndpointRole.Primary ? "主入口" : "备用入口"),
            result.Passed ? "通过" : "失败",
            "入口连通性",
            result.Passed ? WorkbenchFactStatus.Passed : WorkbenchFactStatus.Failed,
            result.Message)).ToArray();
    }

    private static IReadOnlyList<WorkbenchFact> InstancePreflight(string projectRoot)
    {
        var store = new ServiceInstanceProfileStore(projectRoot);
        var facts = new List<WorkbenchFact>();
        foreach (string id in store.ListInstanceIds())
        {
            ServiceInstanceProfile profile = store.Load(id);
            InstanceDiagnostic[] errors = ServiceInstanceProfileValidator.Validate(profile, inspectFileSystem: true).Where(item => item.Severity == InstanceDiagnosticSeverity.Error).ToArray();
            facts.Add(new WorkbenchFact("instance-preflight/" + id, WorkbenchFactKind.Preflight, id, errors.Length == 0 ? "通过" : "失败", "实例档案预检", errors.Length == 0 ? WorkbenchFactStatus.Passed : WorkbenchFactStatus.Failed, string.Join("；", errors.Select(item => item.Code + " " + item.Message))));
        }
        if (facts.Count == 0) facts.Add(new WorkbenchFact("instance-preflight/none", WorkbenchFactKind.Preflight, "实例档案", "未配置", "实例档案预检", WorkbenchFactStatus.Warning));
        return facts;
    }

    private static IReadOnlyList<WorkbenchFact> ToPreflightFacts(string id, string name, string owner, IReadOnlyList<string> issues)
        => issues.Count == 0
            ? [new WorkbenchFact(id + "/passed", WorkbenchFactKind.Preflight, name, "通过", owner, WorkbenchFactStatus.Passed)]
            : issues.Select((issue, index) => new WorkbenchFact(id + "/" + index, WorkbenchFactKind.Preflight, name, "失败", owner, WorkbenchFactStatus.Failed, issue)).ToArray();

    private static WorkbenchFact Version(string id, string name, string value, string owner, WorkbenchFactStatus status = WorkbenchFactStatus.Passed)
        => new(id, WorkbenchFactKind.Version, name, value, owner, status);
    private static WorkbenchFact Capability(string id, string name, bool enabled, string owner, string details = "")
        => new(id, WorkbenchFactKind.Capability, name, enabled ? "可用" : "未启用", owner, enabled ? WorkbenchFactStatus.Passed : WorkbenchFactStatus.Warning, details);
    private static string Display(string? value) => string.IsNullOrWhiteSpace(value) ? "未声明" : value;

    private sealed class DelegateProvider(string owner, Func<CancellationToken, Task<IReadOnlyList<WorkbenchFact>>> collect) : IWorkbenchFactProvider
    {
        public string Owner => owner;
        public Task<IReadOnlyList<WorkbenchFact>> CollectAsync(CancellationToken cancellationToken) => collect(cancellationToken);
    }
}
