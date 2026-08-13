using System.Drawing;
using System.Text;
using Server.Diagnostics;
using Server.MirDatabase;

namespace Server.Authoring;

public sealed record NpcContentDiff(int EntityIndex, string Summary);

public sealed record NpcContentCommitResult(bool Success, string Error)
{
    public static NpcContentCommitResult Completed { get; } = new(true, string.Empty);
}

/// <summary>NPC 作者编辑会话。事实对象仅在显式提交时更新。</summary>
public sealed class NpcContentEditingSession
{
    private readonly IList<NPCInfo> _source;
    private readonly List<OriginalEntry> _originals;
    private readonly List<NPCInfo> _drafts;
    private int _nextIndex;

    public IReadOnlyList<NPCInfo> Drafts => _drafts;
    public bool IsDirty => BuildDiff().Count > 0;

    public NpcContentEditingSession(IList<NPCInfo> source, int indexHighWatermark = 0)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _originals = source.Select(value => new OriginalEntry(value, Clone(value))).ToList();
        _drafts = source.Select(Clone).ToList();
        _nextIndex = Math.Max(indexHighWatermark, source.Select(value => value.Index).DefaultIfEmpty().Max());
    }

    public NPCInfo Add()
    {
        var draft = new NPCInfo { Index = ++_nextIndex };
        _drafts.Add(draft);
        return draft;
    }

    public bool Remove(NPCInfo draft) => draft is not null && _drafts.Remove(draft);

    public void Reload()
    {
        _drafts.Clear();
        _drafts.AddRange(_source.Select(Clone));
    }

    public IReadOnlyList<NpcContentDiff> BuildDiff()
    {
        var result = new List<NpcContentDiff>();
        var originalByIndex = _originals.ToDictionary(value => value.Snapshot.Index, value => value.Snapshot);
        var draftByIndex = _drafts.ToDictionary(value => value.Index);

        foreach (NPCInfo draft in _drafts.OrderBy(value => value.Index))
        {
            if (!originalByIndex.TryGetValue(draft.Index, out NPCInfo original))
                result.Add(new NpcContentDiff(draft.Index, $"新增 NPC：{draft.FileName} ({draft.Location.X},{draft.Location.Y})"));
            else if (!Equivalent(original, draft))
                result.Add(new NpcContentDiff(draft.Index, DescribeChanges(original, draft)));
        }

        foreach (NPCInfo original in _originals.Select(value => value.Snapshot).OrderBy(value => value.Index))
            if (!draftByIndex.ContainsKey(original.Index))
                result.Add(new NpcContentDiff(original.Index, $"删除 NPC：{original.FileName}"));

        return result;
    }

    public IReadOnlyList<ProjectPreflightDiagnostic> Validate(
        Func<IReadOnlyCollection<NPCInfo>, IReadOnlyList<ProjectPreflightDiagnostic>> validator)
    {
        if (validator is null) throw new ArgumentNullException(nameof(validator));
        return validator(_drafts);
    }

    public NpcContentCommitResult TryCommit(Action persist)
    {
        if (persist is null) throw new ArgumentNullException(nameof(persist));
        List<NPCInfo> previousOrder = _source.ToList();
        var previousValues = previousOrder.Select(value => new OriginalEntry(value, Clone(value))).ToList();

        try
        {
            var existing = _originals.ToDictionary(value => value.Snapshot.Index, value => value.Instance);
            _source.Clear();
            foreach (NPCInfo draft in _drafts)
            {
                NPCInfo target = existing.TryGetValue(draft.Index, out NPCInfo value) ? value : new NPCInfo();
                Copy(draft, target);
                _source.Add(target);
            }

            persist();
            ResetBaseline();
            return NpcContentCommitResult.Completed;
        }
        catch (Exception ex)
        {
            _source.Clear();
            foreach (OriginalEntry entry in previousValues)
            {
                Copy(entry.Snapshot, entry.Instance);
                _source.Add(entry.Instance);
            }
            return new NpcContentCommitResult(false, ex.Message);
        }
    }

    private void ResetBaseline()
    {
        _originals.Clear();
        _originals.AddRange(_source.Select(value => new OriginalEntry(value, Clone(value))));
        Reload();
    }

    public static NPCInfo Clone(NPCInfo value)
    {
        var clone = new NPCInfo();
        Copy(value, clone);
        return clone;
    }

    private static void Copy(NPCInfo source, NPCInfo target)
    {
        target.Index = source.Index;
        target.FileName = source.FileName;
        target.Name = source.Name;
        target.MapIndex = source.MapIndex;
        target.Location = new Point(source.Location.X, source.Location.Y);
        target.Rate = source.Rate;
        target.Image = source.Image;
        target.Colour = source.Colour;
        target.TimeVisible = source.TimeVisible;
        target.HourStart = source.HourStart;
        target.MinuteStart = source.MinuteStart;
        target.HourEnd = source.HourEnd;
        target.MinuteEnd = source.MinuteEnd;
        target.MinLev = source.MinLev;
        target.MaxLev = source.MaxLev;
        target.DayofWeek = source.DayofWeek;
        target.ClassRequired = source.ClassRequired;
        target.Sabuk = source.Sabuk;
        target.FlagNeeded = source.FlagNeeded;
        target.Conquest = source.Conquest;
        target.ShowOnBigMap = source.ShowOnBigMap;
        target.BigMapIcon = source.BigMapIcon;
        target.CanTeleportTo = source.CanTeleportTo;
        target.ConquestVisible = source.ConquestVisible;
        target.CollectQuestIndexes = source.CollectQuestIndexes.ToList();
        target.FinishQuestIndexes = source.FinishQuestIndexes.ToList();
    }

    private static bool Equivalent(NPCInfo left, NPCInfo right) => DescribeChanges(left, right).Length == 0;

    private static string DescribeChanges(NPCInfo left, NPCInfo right)
    {
        var changes = new List<string>();
        Add(nameof(NPCInfo.FileName), left.FileName, right.FileName);
        Add(nameof(NPCInfo.Name), left.Name, right.Name);
        Add(nameof(NPCInfo.MapIndex), left.MapIndex, right.MapIndex);
        Add(nameof(NPCInfo.Location), left.Location, right.Location);
        Add(nameof(NPCInfo.Image), left.Image, right.Image);
        Add(nameof(NPCInfo.Rate), left.Rate, right.Rate);
        Add(nameof(NPCInfo.MinLev), left.MinLev, right.MinLev);
        Add(nameof(NPCInfo.MaxLev), left.MaxLev, right.MaxLev);
        Add(nameof(NPCInfo.TimeVisible), left.TimeVisible, right.TimeVisible);
        Add(nameof(NPCInfo.HourStart), left.HourStart, right.HourStart);
        Add(nameof(NPCInfo.MinuteStart), left.MinuteStart, right.MinuteStart);
        Add(nameof(NPCInfo.HourEnd), left.HourEnd, right.HourEnd);
        Add(nameof(NPCInfo.MinuteEnd), left.MinuteEnd, right.MinuteEnd);
        Add(nameof(NPCInfo.DayofWeek), left.DayofWeek, right.DayofWeek);
        Add(nameof(NPCInfo.ClassRequired), left.ClassRequired, right.ClassRequired);
        Add(nameof(NPCInfo.Sabuk), left.Sabuk, right.Sabuk);
        Add(nameof(NPCInfo.FlagNeeded), left.FlagNeeded, right.FlagNeeded);
        Add(nameof(NPCInfo.Conquest), left.Conquest, right.Conquest);
        Add(nameof(NPCInfo.ShowOnBigMap), left.ShowOnBigMap, right.ShowOnBigMap);
        Add(nameof(NPCInfo.BigMapIcon), left.BigMapIcon, right.BigMapIcon);
        Add(nameof(NPCInfo.CanTeleportTo), left.CanTeleportTo, right.CanTeleportTo);
        Add(nameof(NPCInfo.ConquestVisible), left.ConquestVisible, right.ConquestVisible);
        Add(nameof(NPCInfo.Colour), left.Colour, right.Colour);
        if (!left.CollectQuestIndexes.SequenceEqual(right.CollectQuestIndexes)) changes.Add("CollectQuestIndexes");
        if (!left.FinishQuestIndexes.SequenceEqual(right.FinishQuestIndexes)) changes.Add("FinishQuestIndexes");
        return changes.Count == 0 ? string.Empty : $"修改 NPC {right.Index}：{string.Join("、", changes)}";

        void Add<T>(string name, T oldValue, T newValue)
        {
            if (!EqualityComparer<T>.Default.Equals(oldValue, newValue)) changes.Add(name);
        }
    }

    private sealed record OriginalEntry(NPCInfo Instance, NPCInfo Snapshot);
}
