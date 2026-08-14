namespace LyoCrystal.Workbench;

public enum WorkbenchFactKind { Version, Capability, Preflight }
public enum WorkbenchFactStatus { Passed, Warning, Failed, Unavailable }

public sealed record WorkbenchFact(string Id, WorkbenchFactKind Kind, string Name, string Value, string Owner, WorkbenchFactStatus Status, string Details = "");

public interface IWorkbenchFactProvider
{
    string Owner { get; }
    Task<IReadOnlyList<WorkbenchFact>> CollectAsync(CancellationToken cancellationToken);
}

public sealed record WorkbenchOverviewSnapshot(DateTimeOffset CapturedAtUtc, IReadOnlyList<WorkbenchFact> Facts)
{
    public bool Passed => Facts.All(item => item.Status is WorkbenchFactStatus.Passed or WorkbenchFactStatus.Warning);
}

public sealed class WorkbenchOverviewService(IEnumerable<IWorkbenchFactProvider> providers)
{
    private readonly IWorkbenchFactProvider[] providers = providers?.ToArray() ?? throw new ArgumentNullException(nameof(providers));

    public async Task<WorkbenchOverviewSnapshot> CollectAsync(CancellationToken cancellationToken = default)
    {
        Task<IReadOnlyList<WorkbenchFact>>[] tasks = providers.Select(provider => CollectProviderAsync(provider, cancellationToken)).ToArray();
        IReadOnlyList<WorkbenchFact>[] results = await Task.WhenAll(tasks).ConfigureAwait(false);
        WorkbenchFact[] facts = results.SelectMany(value => value)
            .OrderBy(item => item.Kind)
            .ThenBy(item => item.Owner, StringComparer.Ordinal)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .ToArray();
        return new WorkbenchOverviewSnapshot(DateTimeOffset.UtcNow, facts);
    }

    private static async Task<IReadOnlyList<WorkbenchFact>> CollectProviderAsync(IWorkbenchFactProvider provider, CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<WorkbenchFact> facts = await provider.CollectAsync(cancellationToken).ConfigureAwait(false);
            if (facts.Any(item => string.IsNullOrWhiteSpace(item.Owner)))
                throw new InvalidDataException("事实必须声明原模块 Owner。");
            return facts;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            return [new WorkbenchFact("provider-failure/" + provider.Owner, WorkbenchFactKind.Preflight, provider.Owner + "采集", "失败", provider.Owner, WorkbenchFactStatus.Failed, error.Message)];
        }
    }
}

public enum WorkbenchVersionChangeKind { Added, Removed, Changed, Unchanged }
public sealed record WorkbenchVersionChange(string Id, string Name, string Owner, string Before, string After, WorkbenchVersionChangeKind Change);

public static class WorkbenchVersionDiff
{
    public static IReadOnlyList<WorkbenchVersionChange> Compare(WorkbenchOverviewSnapshot before, WorkbenchOverviewSnapshot after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        var left = before.Facts.Where(item => item.Kind == WorkbenchFactKind.Version).ToDictionary(item => item.Id, StringComparer.Ordinal);
        var right = after.Facts.Where(item => item.Kind == WorkbenchFactKind.Version).ToDictionary(item => item.Id, StringComparer.Ordinal);
        return left.Keys.Union(right.Keys, StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).Select(id =>
        {
            left.TryGetValue(id, out WorkbenchFact? oldValue);
            right.TryGetValue(id, out WorkbenchFact? newValue);
            WorkbenchVersionChangeKind change = oldValue is null ? WorkbenchVersionChangeKind.Added
                : newValue is null ? WorkbenchVersionChangeKind.Removed
                : string.Equals(oldValue.Value, newValue.Value, StringComparison.Ordinal) ? WorkbenchVersionChangeKind.Unchanged
                : WorkbenchVersionChangeKind.Changed;
            WorkbenchFact display = newValue ?? oldValue!;
            return new WorkbenchVersionChange(id, display.Name, display.Owner, oldValue?.Value ?? string.Empty, newValue?.Value ?? string.Empty, change);
        }).ToArray();
    }
}
