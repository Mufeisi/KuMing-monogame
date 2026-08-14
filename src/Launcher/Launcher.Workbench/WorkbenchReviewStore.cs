using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;

namespace LyoCrystal.Workbench;

public sealed record WorkbenchStoredSnapshot(string Id, DateTimeOffset SavedAtUtc, WorkbenchOverviewSnapshot Snapshot);
public sealed record WorkbenchTestReleaseReview(string Id, DateTimeOffset RecordedAtUtc, string ResourceVersion, string KeyId, long Sequence, int PackageCount, string OutputRoot, bool SignatureVerified);

public sealed partial class WorkbenchReviewStore
{
    private readonly string projectRoot;
    private readonly string root;
    private readonly string snapshotsRoot;
    private readonly string releasesRoot;

    public WorkbenchReviewStore(string projectRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        this.projectRoot = Path.GetFullPath(projectRoot);
        root = Path.Combine(this.projectRoot, "workbench-reviews");
        snapshotsRoot = Path.Combine(root, "snapshots");
        releasesRoot = Path.Combine(root, "test-releases");
    }

    public WorkbenchStoredSnapshot SaveSnapshot(WorkbenchOverviewSnapshot snapshot, string label)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        PrepareRoots(create: true);
        string id = BuildId(label);
        var stored = new WorkbenchStoredSnapshot(id, DateTimeOffset.UtcNow, snapshot);
        SaveAtomic(snapshotsRoot, id, stored, WorkbenchReviewJsonContext.Default.WorkbenchStoredSnapshot);
        return stored;
    }

    public IReadOnlyList<string> ListSnapshotIds() { PrepareRoots(create: false); return ListIds(snapshotsRoot); }
    public WorkbenchStoredSnapshot LoadSnapshot(string id) { PrepareRoots(create: false); return Load(snapshotsRoot, id, WorkbenchReviewJsonContext.Default.WorkbenchStoredSnapshot); }

    public WorkbenchTestReleaseReview SaveTestRelease(WorkbenchTestReleaseReview review)
    {
        ArgumentNullException.ThrowIfNull(review);
        PrepareRoots(create: true);
        ValidateId(review.Id);
        if (!review.SignatureVerified || review.Sequence < 1 || review.PackageCount < 1)
            throw new InvalidDataException("只允许记录已验签且包含有效序列和资源包的测试发布结果。");
        SaveAtomic(releasesRoot, review.Id, review, WorkbenchReviewJsonContext.Default.WorkbenchTestReleaseReview);
        return review;
    }

    public IReadOnlyList<string> ListTestReleaseIds() { PrepareRoots(create: false); return ListIds(releasesRoot); }
    public WorkbenchTestReleaseReview LoadTestRelease(string id) { PrepareRoots(create: false); return Load(releasesRoot, id, WorkbenchReviewJsonContext.Default.WorkbenchTestReleaseReview); }

    private void PrepareRoots(bool create)
    {
        if (create) Directory.CreateDirectory(projectRoot);
        if (!Directory.Exists(projectRoot)) throw new DirectoryNotFoundException("工作台项目目录不存在。");
        RejectReparse(projectRoot);
        if (create) Directory.CreateDirectory(root);
        if (Directory.Exists(root)) RejectReparse(root);
    }

    private static string BuildId(string label)
    {
        string normalized = new(label.Trim().ToLowerInvariant().Select(character => char.IsAsciiLetterOrDigit(character) || character == '-' ? character : '-').ToArray());
        normalized = normalized.Trim('-');
        if (normalized.Length > 32) normalized = normalized[..32].TrimEnd('-');
        if (normalized.Length < 3) normalized = "snapshot";
        return DateTimeOffset.UtcNow.ToString("yyyyMMdd't'HHmmssfffffff'z'") + "-" + normalized;
    }

    private static IReadOnlyList<string> ListIds(string directory)
    {
        if (!Directory.Exists(directory)) return Array.Empty<string>();
        RejectReparse(directory);
        return Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly).Select(Path.GetFileNameWithoutExtension).OfType<string>().OrderBy(value => value, StringComparer.Ordinal).ToArray();
    }

    private static T Load<T>(string directory, string id, JsonTypeInfo<T> typeInfo)
    {
        ValidateId(id);
        RejectReparse(directory);
        string path = Path.Combine(directory, id + ".json");
        if (!File.Exists(path) || new FileInfo(path).Length > 4 * 1024 * 1024) throw new InvalidDataException("工作台审查工件不存在或超过大小限制。");
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return JsonSerializer.Deserialize(stream, typeInfo) ?? throw new InvalidDataException("工作台审查工件为空。");
    }

    private static void SaveAtomic<T>(string directory, string id, T value, JsonTypeInfo<T> typeInfo)
    {
        ValidateId(id);
        Directory.CreateDirectory(Path.GetDirectoryName(directory)!);
        RejectReparse(Path.GetDirectoryName(directory)!);
        Directory.CreateDirectory(directory);
        RejectReparse(directory);
        string path = Path.Combine(directory, id + ".json");
        string temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, value, typeInfo);
                stream.Flush(true);
            }
            if (File.Exists(path)) File.Replace(temporary, path, null, true);
            else File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static void ValidateId(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || !IdPattern().IsMatch(id)) throw new InvalidDataException("工作台审查工件标识无效。");
    }

    private static void RejectReparse(string path)
    {
        if (Directory.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("工作台审查目录不得为重解析点。");
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{2,95}$")]
    private static partial Regex IdPattern();
}

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(WorkbenchStoredSnapshot))]
[JsonSerializable(typeof(WorkbenchTestReleaseReview))]
internal sealed partial class WorkbenchReviewJsonContext : JsonSerializerContext;
