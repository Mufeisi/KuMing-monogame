using System.Text;
using System.Text.Json;

namespace LyoCrystal.InstanceManagement;

public sealed class ServiceInstanceProfileStore
{
    public ServiceInstanceProfileStore(string projectRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        ProjectRoot = Path.GetFullPath(projectRoot);
        ProfilesRoot = Path.Combine(ProjectRoot, "instances");
    }

    public string ProjectRoot { get; }
    public string ProfilesRoot { get; }

    public IReadOnlyList<string> ListInstanceIds()
    {
        if (!Directory.Exists(ProfilesRoot)) return Array.Empty<string>();
        RejectReparsePath(ProfilesRoot);
        return Directory.EnumerateFiles(ProfilesRoot, "*.json", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileNameWithoutExtension)
            .Where(value => value is not null)
            .Cast<string>()
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
    }

    public ServiceInstanceProfile Load(string instanceId)
    {
        string path = ResolveProfilePath(instanceId);
        RejectReparsePath(ProfilesRoot);
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        ServiceInstanceProfile profile = JsonSerializer.Deserialize(stream, ServiceInstanceProfileJsonContext.Default.ServiceInstanceProfile)
            ?? throw new InvalidDataException("实例档案为空。");
        if (!string.Equals(profile.InstanceId, instanceId, StringComparison.Ordinal))
            throw new InvalidDataException("实例档案标识与文件名不一致。");
        return profile;
    }

    public void Save(ServiceInstanceProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        IReadOnlyList<InstanceDiagnostic> diagnostics = ServiceInstanceProfileValidator.Validate(profile, inspectFileSystem: false);
        if (diagnostics.Any(item => item.Severity == InstanceDiagnosticSeverity.Error))
            throw new InvalidDataException(string.Join("；", diagnostics.Select(item => $"{item.Code} {item.Message}")));

        Directory.CreateDirectory(ProjectRoot);
        RejectReparsePath(ProjectRoot);
        Directory.CreateDirectory(ProfilesRoot);
        RejectReparsePath(ProfilesRoot);
        string path = ResolveProfilePath(profile.InstanceId);
        string temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, profile, ServiceInstanceProfileJsonContext.Default.ServiceInstanceProfile);
                stream.Write(Encoding.UTF8.GetBytes(Environment.NewLine));
                stream.Flush(true);
            }
            if (File.Exists(path)) File.Replace(temp, path, null, true);
            else File.Move(temp, path);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    private string ResolveProfilePath(string instanceId)
    {
        if (!ServiceInstanceProfileValidator.IsValidIdentifier(instanceId))
            throw new InvalidDataException("实例标识格式无效。");
        return Path.Combine(ProfilesRoot, instanceId + ".json");
    }

    private static void RejectReparsePath(string path)
    {
        if (Directory.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("实例档案目录不得使用重解析点。");
    }
}
