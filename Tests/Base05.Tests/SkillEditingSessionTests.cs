using Server.Authoring;
using Server.MirDatabase;
using Xunit;

namespace Base05.Tests;

public sealed class SkillEditingSessionTests
{
    [Fact]
    public void 草稿差异与取消_不会修改原始技能()
    {
        var source = CreateSource();
        var session = new SkillEditingSession(source);

        session.Observe(new SkillSafeDraft("火球术·新版", 9));
        SkillEditReview review = session.Review();

        Assert.True(session.IsDirty);
        Assert.Equal(2, review.Differences.Count);
        Assert.Equal("火球术", source.Name);
        Assert.Equal((byte)1, source.Icon);

        Assert.Equal(new SkillSafeDraft("火球术", 1), session.Cancel());
        Assert.False(session.IsDirty);
        Assert.Equal((ushort)20, source.PowerBase);
    }

    [Fact]
    public void 非法名称_在保存前被稳定诊断阻断()
    {
        var source = CreateSource();
        var session = new SkillEditingSession(source);
        session.Observe(new SkillSafeDraft("坏\n名称", 2));

        SkillEditCommitResult result = session.TryCommit(() => throw new InvalidOperationException("不应调用"));

        Assert.False(result.Completed);
        Assert.Contains(result.Review.Diagnostics, item => item.Code == "LEG08-SKILL-NAME-003");
        Assert.Equal("火球术", source.Name);
    }

    [Fact]
    public void 显式保存_只更新名称和图标且调用一次持久化()
    {
        var source = CreateSource();
        var session = new SkillEditingSession(source);
        session.Observe(new SkillSafeDraft("新火球术", 8));
        int persisted = 0;

        SkillEditCommitResult result = session.TryCommit(() => persisted++);

        Assert.True(result.Completed);
        Assert.Equal(1, persisted);
        Assert.Equal("新火球术", source.Name);
        Assert.Equal((byte)8, source.Icon);
        Assert.Equal((ushort)20, source.PowerBase);
        Assert.False(session.IsDirty);
    }

    [Fact]
    public void 持久化失败_恢复全部白名单字段并保留草稿重试()
    {
        var source = CreateSource();
        var session = new SkillEditingSession(source);
        session.Observe(new SkillSafeDraft("待重试", 7));

        SkillEditCommitResult result = session.TryCommit(() => throw new IOException("磁盘失败"));

        Assert.False(result.Completed);
        Assert.Contains("已恢复", result.Error);
        Assert.Equal("火球术", source.Name);
        Assert.Equal((byte)1, source.Icon);
        Assert.Equal(new SkillSafeDraft("待重试", 7), session.Draft);
        Assert.True(session.IsDirty);
    }

    [Fact]
    public void 并发修改事实对象_提交失败并要求重载()
    {
        var source = CreateSource();
        var session = new SkillEditingSession(source);
        session.Observe(new SkillSafeDraft("我的草稿", 6));
        source.Icon = 5;

        SkillEditCommitResult result = session.TryCommit(() => throw new InvalidOperationException("不应调用"));

        Assert.False(result.Completed);
        Assert.Contains(result.Review.Diagnostics, item => item.Code == "LEG08-SKILL-CONFLICT-001");
        Assert.Equal((byte)5, source.Icon);
    }

    private static MagicInfo CreateSource() => new()
    {
        Name = "火球术",
        Spell = Spell.FireBall,
        Icon = 1,
        PowerBase = 20,
        DelayBase = 1800,
        Range = 9
    };
}
