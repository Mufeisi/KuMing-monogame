using System.Net;
using System.Text.RegularExpressions;

namespace LyoCrystal.InstanceManagement;

public enum InstanceDiagnosticSeverity
{
    Information,
    Warning,
    Error
}

public sealed record InstanceDiagnostic(string Code, InstanceDiagnosticSeverity Severity, string Source, string Message);

public static partial class ServiceInstanceProfileValidator
{
    public static IReadOnlyList<InstanceDiagnostic> Validate(ServiceInstanceProfile profile, bool inspectFileSystem = true)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var diagnostics = new List<InstanceDiagnostic>();

        if (!string.Equals(profile.Format, ServiceInstanceProfile.CurrentFormat, StringComparison.Ordinal))
            diagnostics.Add(Error("LEG09-PROFILE-FORMAT-001", "Format", "实例档案格式不受支持。"));
        if (!IsValidIdentifier(profile.InstanceId))
            diagnostics.Add(Error("LEG09-PROFILE-ID-001", "InstanceId", "实例标识只能包含小写字母、数字和连字符，长度为 3～48。"));
        if (!IsValidIdentifier(profile.ServerId))
            diagnostics.Add(Error("LEG09-PROFILE-SERVER-001", "ServerId", "区服标识只能包含小写字母、数字和连字符，长度为 3～48。"));
        if (!IPAddress.TryParse(profile.LoginAddress, out _))
            diagnostics.Add(Error("LEG09-PROFILE-LOGIN-001", "LoginAddress", "登录入口必须是明确的 IP 地址，不在运行时隐式解析主机名。"));
        if (profile.Environment == ServiceEnvironmentKind.Production && !IsSecretReference(profile.SecretReference))
            diagnostics.Add(Error("LEG09-PROFILE-SECRET-001", "SecretReference", "正式环境必须使用 secret:// 引用，档案不得包含秘密值。"));
        else if (profile.SecretReference.Length > 0 && !IsSecretReference(profile.SecretReference))
            diagnostics.Add(Error("LEG09-PROFILE-SECRET-002", "SecretReference", "秘密引用必须使用 secret:// 名称，不得写入秘密值。"));
        if (profile.ExpectedSchemaVersion < 0)
            diagnostics.Add(Error("LEG09-PROFILE-VERSION-001", "ExpectedSchemaVersion", "目标数据库 Schema 版本不得为负数。"));
        if (profile.ExpectedScriptRevision.Length > 128 || profile.ExpectedScriptRevision.Any(char.IsControl))
            diagnostics.Add(Error("LEG09-PROFILE-VERSION-002", "ExpectedScriptRevision", "目标脚本修订标识不得超过 128 个字符或包含控制字符。"));

        string? root = TryResolveRoot(profile.RootDirectory, diagnostics, inspectFileSystem);
        var componentIds = new HashSet<string>(StringComparer.Ordinal);
        var ports = new Dictionary<int, string>();
        AddPort(profile.LoginBasePort, profile.PortOffset, "LoginBasePort", "登录入口", ports, diagnostics);

        if (profile.Components.Count == 0)
            diagnostics.Add(Error("LEG09-PROFILE-COMPONENT-001", "Components", "实例至少需要一个可运行组件。"));

        foreach (ServiceComponentProfile component in profile.Components)
        {
            string source = $"Components[{component.Id}]";
            if (!IsValidIdentifier(component.Id))
                diagnostics.Add(Error("LEG09-PROFILE-COMPONENT-002", source, "组件标识格式无效。"));
            else if (!componentIds.Add(component.Id))
                diagnostics.Add(Error("LEG09-PROFILE-COMPONENT-003", source, "组件标识重复。"));
            if (component.DependencyMode == ServiceDependencyMode.Shared && !IsValidIdentifier(component.SharedGroup))
                diagnostics.Add(Error("LEG09-PROFILE-SHARED-001", source, "共享组件必须声明有效的共享组。"));
            if (component.DependencyMode == ServiceDependencyMode.Exclusive && component.SharedGroup.Length > 0)
                diagnostics.Add(Error("LEG09-PROFILE-SHARED-002", source, "独占组件不得声明共享组。"));
            if (component.StartTimeoutSeconds is < 1 or > 300 || component.StopTimeoutSeconds is < 1 or > 300)
                diagnostics.Add(Error("LEG09-PROFILE-TIMEOUT-001", source, "启动和停止超时必须在 1～300 秒之间。"));
            if (component.HealthProbe == ServiceHealthProbeKind.Http && !component.HealthPath.StartsWith('/'))
                diagnostics.Add(Error("LEG09-PROFILE-HEALTH-001", source, "HTTP 健康路径必须以 / 开头。"));
            if (component.StopPath.Length > 0 && !component.StopPath.StartsWith('/'))
                diagnostics.Add(Error("LEG09-PROFILE-STOP-001", source, "HTTP 停止路径必须以 / 开头。"));
            if (component.Arguments.Any(argument => SecretArgumentPattern().IsMatch(argument)))
                diagnostics.Add(Error("LEG09-PROFILE-SECRET-003", source, "组件参数疑似包含秘密值；请改用秘密引用和受控注入入口。"));
            AddPort(component.BasePort, profile.PortOffset, source + ".BasePort", component.Id, ports, diagnostics);

            if (root is not null)
            {
                ValidateContainedPath(root, component.ExecutablePath, source + ".ExecutablePath", true, inspectFileSystem, diagnostics);
                ValidateContainedPath(root, component.WorkingDirectory, source + ".WorkingDirectory", false, inspectFileSystem, diagnostics);
                if (component.LogPath.Length > 0)
                    ValidateContainedPath(root, component.LogPath, source + ".LogPath", false, false, diagnostics);
            }
        }

        ValidateDependencyGraph(profile.Components, componentIds, diagnostics);
        return diagnostics.AsReadOnly();
    }

    private static void ValidateDependencyGraph(IReadOnlyList<ServiceComponentProfile> components, HashSet<string> ids, List<InstanceDiagnostic> diagnostics)
    {
        foreach (ServiceComponentProfile component in components)
            foreach (string dependency in component.DependsOn)
                if (!ids.Contains(dependency))
                    diagnostics.Add(Error("LEG09-PROFILE-DEPENDENCY-001", component.Id, $"依赖组件不存在：{dependency}。"));

        var byId = components.Where(item => ids.Contains(item.Id)).ToDictionary(item => item.Id, StringComparer.Ordinal);
        var states = new Dictionary<string, byte>(StringComparer.Ordinal);
        bool Visit(string id)
        {
            if (states.TryGetValue(id, out byte state)) return state == 1;
            states[id] = 1;
            if (byId.TryGetValue(id, out ServiceComponentProfile? component))
                foreach (string dependency in component.DependsOn)
                    if (byId.ContainsKey(dependency) && Visit(dependency)) return true;
            states[id] = 2;
            return false;
        }
        if (byId.Keys.Any(Visit))
            diagnostics.Add(Error("LEG09-PROFILE-DEPENDENCY-002", "Components", "组件依赖存在环，无法确定启动顺序。"));
    }

    private static string? TryResolveRoot(string path, List<InstanceDiagnostic> diagnostics, bool inspectFileSystem)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            diagnostics.Add(Error("LEG09-PROFILE-ROOT-001", "RootDirectory", "实例根目录必须是绝对路径。"));
            return null;
        }
        string root;
        try { root = Path.GetFullPath(path); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            diagnostics.Add(Error("LEG09-PROFILE-ROOT-002", "RootDirectory", "实例根目录格式无效。"));
            return null;
        }
        if (inspectFileSystem && !Directory.Exists(root))
            diagnostics.Add(Error("LEG09-PROFILE-ROOT-003", "RootDirectory", "实例根目录不存在。"));
        return root;
    }

    private static void ValidateContainedPath(string root, string relativePath, string source, bool requireFile, bool inspectFileSystem, List<InstanceDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathFullyQualified(relativePath))
        {
            diagnostics.Add(Error("LEG09-PROFILE-PATH-001", source, "组件路径必须是实例根目录内的相对路径。"));
            return;
        }
        string resolved;
        try { resolved = Path.GetFullPath(Path.Combine(root, relativePath)); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            diagnostics.Add(Error("LEG09-PROFILE-PATH-002", source, "组件路径格式无效。"));
            return;
        }
        string prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!resolved.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add(Error("LEG09-PROFILE-PATH-003", source, "组件路径越出实例根目录。"));
            return;
        }
        if (inspectFileSystem && requireFile && !File.Exists(resolved))
            diagnostics.Add(Error("LEG09-PROFILE-PATH-004", source, "组件可执行文件不存在。"));
        if (inspectFileSystem && !requireFile && !Directory.Exists(resolved))
            diagnostics.Add(Error("LEG09-PROFILE-PATH-005", source, "组件工作目录不存在。"));
    }

    private static void AddPort(int basePort, int offset, string source, string owner, Dictionary<int, string> ports, List<InstanceDiagnostic> diagnostics)
    {
        long effective = (long)basePort + offset;
        if (effective is < 1 or > 65535)
        {
            diagnostics.Add(Error("LEG09-PROFILE-PORT-001", source, "端口偏移后的有效端口必须在 1～65535。"));
            return;
        }
        int port = (int)effective;
        if (ports.TryGetValue(port, out string? existing))
            diagnostics.Add(Error("LEG09-PROFILE-PORT-002", source, $"端口 {port} 与 {existing} 冲突。"));
        else ports.Add(port, owner);
    }

    internal static bool IsValidIdentifier(string value) => value is not null && IdentifierPattern().IsMatch(value);
    private static bool IsSecretReference(string value) => value.StartsWith("secret://", StringComparison.Ordinal) && IsValidIdentifier(value[9..]);
    private static InstanceDiagnostic Error(string code, string source, string message) => new(code, InstanceDiagnosticSeverity.Error, source, message);

    [GeneratedRegex("^[a-z0-9](?:[a-z0-9-]{1,46}[a-z0-9])$")]
    private static partial Regex IdentifierPattern();

    [GeneratedRegex("(?i)(password|passwd|token|secret|api[-_]?key)\\s*=")]
    private static partial Regex SecretArgumentPattern();
}
