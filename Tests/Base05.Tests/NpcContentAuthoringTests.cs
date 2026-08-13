using System.Drawing;
using Server.Authoring;
using Server.MirDatabase;
using Xunit;

namespace Base05.Tests;

public sealed class NpcContentAuthoringTests
{
    [Fact]
    public void 草稿修改取消不会触碰事实对象()
    {
        var source = new List<NPCInfo> { new() { Index = 7, FileName = "merchant", Name = "商人", MapIndex = 1, Location = new Point(5, 6) } };
        var session = new NpcContentEditingSession(source);

        session.Drafts[0].Name = "草稿商人";
        session.Drafts[0].Location = new Point(8, 9);
        session.Drafts[0].HourStart = 12;
        session.Drafts[0].CollectQuestIndexes.Add(42);

        Assert.True(session.IsDirty);
        Assert.Equal("商人", source[0].Name);
        Assert.Equal(new Point(5, 6), source[0].Location);
        Assert.Equal((byte)0, source[0].HourStart);
        Assert.Empty(source[0].CollectQuestIndexes);
        session.Reload();
        Assert.False(session.IsDirty);
    }

    [Fact]
    public void 显式提交保留已有实体身份并保存新增删除和修改()
    {
        var kept = new NPCInfo { Index = 2, FileName = "keeper", Name = "旧名称" };
        var removed = new NPCInfo { Index = 5, FileName = "removed" };
        var source = new List<NPCInfo> { kept, removed };
        var session = new NpcContentEditingSession(source);
        session.Drafts[0].Name = "新名称";
        session.Remove(session.Drafts[1]);
        session.Add().FileName = "added";
        int persistCount = 0;

        NpcContentCommitResult result = session.TryCommit(() => persistCount++);

        Assert.True(result.Success);
        Assert.Equal(1, persistCount);
        Assert.Same(kept, source[0]);
        Assert.Equal("新名称", kept.Name);
        Assert.Equal([2, 6], source.Select(value => value.Index));
        Assert.False(session.IsDirty);
    }

    [Fact]
    public void 新增索引从持久化高水位单调递增而不复用已删除编号()
    {
        var source = new List<NPCInfo> { new() { Index = 2 } };
        var session = new NpcContentEditingSession(source, indexHighWatermark: 99);

        Assert.Equal(100, session.Add().Index);
        session.Remove(session.Drafts.Single(value => value.Index == 100));
        Assert.Equal(101, session.Add().Index);
    }

    [Fact]
    public void 保存失败恢复原列表顺序和值且保留可重试草稿()
    {
        var first = new NPCInfo { Index = 2, FileName = "keeper", Name = "旧名称" };
        var second = new NPCInfo { Index = 5, FileName = "second" };
        var source = new List<NPCInfo> { first, second };
        var session = new NpcContentEditingSession(source);
        session.Drafts[0].Name = "待保存名称";
        session.Remove(session.Drafts[1]);

        NpcContentCommitResult result = session.TryCommit(() => throw new IOException("磁盘不可写"));

        Assert.False(result.Success);
        Assert.Equal("磁盘不可写", result.Error);
        Assert.Equal([first, second], source);
        Assert.Equal("旧名称", first.Name);
        Assert.Equal("待保存名称", session.Drafts[0].Name);
        Assert.True(session.IsDirty);
    }

    [Fact]
    public void 对话预览提取页面链接并定位悬空目标()
    {
        string[] lines =
        [
            "[@MAIN]",
            "欢迎。<购买/@BUY> <离开/@EXIT>",
            "[@BUY]",
            "GOTO @MISSING",
            "TIMERECALLGROUP 10 @GROUPMISSING",
            "ROLLDIE @ROLLMISSING",
        ];

        NpcScriptPreview preview = NpcScriptAuthoring.BuildPreview("merchant", lines, "NPCs/merchant");

        Assert.Equal(["[@MAIN]", "[@BUY]"], preview.Pages.Select(value => value.Key));
        Assert.Contains("[@BUY]", preview.Pages[0].Links);
        Assert.Equal(3, preview.Diagnostics.Count);
        Assert.All(preview.Diagnostics, diagnostic => Assert.Equal("CONTENT03-LINK-001", diagnostic.Code));
        Assert.Contains(preview.Diagnostics, diagnostic => diagnostic.Message.Contains("[@MISSING]"));
        Assert.Contains(preview.Diagnostics, diagnostic => diagnostic.Message.Contains("[@GROUPMISSING]"));
        Assert.Contains(preview.Diagnostics, diagnostic => diagnostic.Message.Contains("[@ROLLMISSING]"));
    }

    [Fact]
    public void 缺少入口页和空脚本产生稳定诊断码()
    {
        NpcScriptPreview missingMain = NpcScriptAuthoring.BuildPreview("merchant", ["[@OTHER]", "内容"], "NPCs/merchant");
        NpcScriptPreview empty = NpcScriptAuthoring.BuildPreview("merchant", [], "NPCs/merchant");

        Assert.Contains(missingMain.Diagnostics, value => value.Code == "CONTENT03-LINK-002");
        Assert.Contains(empty.Diagnostics, value => value.Code == "CONTENT03-NPC-002");
    }
}
