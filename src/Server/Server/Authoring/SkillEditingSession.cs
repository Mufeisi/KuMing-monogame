using Server.Diagnostics;
using Server.MirDatabase;

namespace Server.Authoring;

public sealed record SkillSafeDraft(string Name, byte Icon);

public sealed record SkillSafeDifference(string Field, string Before, string After);

public sealed record SkillEditReview(
    IReadOnlyList<ProjectPreflightDiagnostic> Diagnostics,
    IReadOnlyList<SkillSafeDifference> Differences)
{
    public bool HasErrors => Diagnostics.Any(item => item.Severity == ProjectPreflightSeverity.Error);
    public bool HasChanges => Differences.Count > 0;
}

public sealed record SkillEditCommitResult(bool Completed, SkillEditReview Review, string Error);

/// <summary>
/// 技能安全编辑会话。首版白名单只允许名称和图标；战斗、等级、消耗、冷却与范围字段没有写入入口。
/// </summary>
public sealed class SkillEditingSession
{
    private readonly MagicInfo _source;
    private SkillSafeDraft _baseline;

    public SkillEditingSession(MagicInfo source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _baseline = Capture(source);
        Draft = _baseline;
    }

    public SkillSafeDraft Draft { get; private set; }
    public bool IsDirty => !Equals(Draft, _baseline);

    public void Observe(SkillSafeDraft draft)
    {
        Draft = draft ?? throw new ArgumentNullException(nameof(draft));
    }

    public SkillSafeDraft Cancel()
    {
        Draft = _baseline;
        return Draft;
    }

    public SkillSafeDraft ReloadFromSource()
    {
        _baseline = Capture(_source);
        Draft = _baseline;
        return Draft;
    }

    public SkillEditReview Review()
    {
        var diagnostics = new List<ProjectPreflightDiagnostic>();
        string normalizedName = Draft.Name?.Trim() ?? string.Empty;
        if (normalizedName.Length == 0)
            diagnostics.Add(Error("LEG08-SKILL-NAME-001", "MagicInfo.Name", "技能名称不能为空。"));
        if (normalizedName.Length > 64)
            diagnostics.Add(Error("LEG08-SKILL-NAME-002", "MagicInfo.Name", "技能名称不得超过 64 个字符。"));
        if (normalizedName.Any(char.IsControl))
            diagnostics.Add(Error("LEG08-SKILL-NAME-003", "MagicInfo.Name", "技能名称不得包含控制字符。"));
        if (!Equals(Capture(_source), _baseline))
            diagnostics.Add(Error("LEG08-SKILL-CONFLICT-001", "MagicInfo", "事实对象已被其他编辑修改，请重载后重新提交。"));

        var differences = new List<SkillSafeDifference>();
        if (!string.Equals(_baseline.Name, Draft.Name, StringComparison.Ordinal))
            differences.Add(new SkillSafeDifference(nameof(MagicInfo.Name), _baseline.Name, Draft.Name ?? string.Empty));
        if (_baseline.Icon != Draft.Icon)
            differences.Add(new SkillSafeDifference(nameof(MagicInfo.Icon), _baseline.Icon.ToString(), Draft.Icon.ToString()));

        return new SkillEditReview(diagnostics.AsReadOnly(), differences.AsReadOnly());
    }

    public SkillEditCommitResult TryCommit(Action persist)
    {
        ArgumentNullException.ThrowIfNull(persist);
        SkillEditReview review = Review();
        if (review.HasErrors)
            return new SkillEditCommitResult(false, review, "保存前校验未通过。");
        if (!review.HasChanges)
            return new SkillEditCommitResult(false, review, "没有需要保存的白名单字段变更。");

        SkillSafeDraft beforeCommit = Capture(_source);
        try
        {
            Apply(_source, Draft);
            persist();
            _baseline = Capture(_source);
            Draft = _baseline;
            return new SkillEditCommitResult(true, review, string.Empty);
        }
        catch (Exception ex)
        {
            Apply(_source, beforeCommit);
            return new SkillEditCommitResult(false, review, $"持久化失败，已恢复保存前内容：{ex.Message}");
        }
    }

    private static SkillSafeDraft Capture(MagicInfo source) => new(source.Name, source.Icon);

    private static void Apply(MagicInfo target, SkillSafeDraft draft)
    {
        target.Name = draft.Name.Trim();
        target.Icon = draft.Icon;
    }

    private static ProjectPreflightDiagnostic Error(string code, string source, string message) =>
        new(code, ProjectPreflightSeverity.Error, source, message);
}
