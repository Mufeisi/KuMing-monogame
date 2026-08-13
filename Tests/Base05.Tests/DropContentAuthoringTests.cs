using Server.Authoring;
using Server.Diagnostics;
using Server.Scripting;
using Xunit;

namespace Base05.Tests;

public sealed class DropContentAuthoringTests
{
    [Fact]
    public void 草稿差异取消和保存失败均不改变原文本()
    {
        const string source = ";武器\n1/10 木剑";
        var session = new DropContentEditingSession(source);
        session.SetDraft(";武器\n1/5 木剑");

        Assert.True(session.IsDirty);
        DropContentDiff diff = Assert.Single(session.BuildDiff());
        Assert.Equal(2, diff.LineNumber);
        Assert.Equal("1/10 木剑", diff.Before);
        Assert.Equal("1/5 木剑", diff.After);

        DropContentCommitResult failed = session.TryCommit(_ => throw new IOException("磁盘不可写"));
        Assert.False(failed.Success);
        Assert.True(session.IsDirty);
        session.Reload(source);
        Assert.False(session.IsDirty);
    }

    [Fact]
    public void 文件提交成功后可重载且不会遗留临时文件()
    {
        string root = Path.Combine(Path.GetTempPath(), "LyoCrystalDropSession", Guid.NewGuid().ToString("N"));
        try
        {
            string file = Path.Combine(root, "drop.txt");
            var session = new DropContentEditingSession("1/5 木剑");

            DropContentCommitResult result = session.TryCommitFile(file);

            Assert.True(result.Success, result.Error);
            Assert.Equal("1/5 木剑", File.ReadAllText(file));
            Assert.False(session.IsDirty);
            Assert.Empty(Directory.GetFiles(root, "*.tmp"));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void 文本草稿复用LEG02掉落校验并提供稳定格式诊断()
    {
        var session = new DropContentEditingSession("1/0 木剑\n1/5 不存在物品\n坏行");

        IReadOnlyList<ProjectPreflightDiagnostic> diagnostics = session.Validate(
            "Drops/test", name => name == "木剑", out DropTableDefinition table);

        Assert.NotNull(table);
        Assert.Contains(diagnostics, value => value.Code == "CONTENT04-DROP-001" && value.Source.EndsWith("line[1]"));
        Assert.Contains(diagnostics, value => value.Code == "CONTENT04-DROP-001" && value.Source.EndsWith("line[3]"));
        Assert.Contains(diagnostics, value => value.Code == "LEG02-DROP-005" && value.Message.Contains("不存在物品"));
    }

    [Fact]
    public void 概率展开计算物品数量金币均值和顺序组期望()
    {
        var table = new DropTableDefinition("Drops/test");
        table.Drops.Add(DropEntryDefinition.Item(4, "木剑", 2));
        table.Drops.Add(DropEntryDefinition.GoldDrop(2, 100));
        var group = new DropGroupDefinition { First = true };
        group.Drops.Add(DropEntryDefinition.Item(2, "药水"));
        group.Drops.Add(DropEntryDefinition.Item(2, "卷轴"));
        table.Drops.Add(DropEntryDefinition.GroupDrop(1, group));

        IReadOnlyList<DropAnalysisRow> rows = DropContentAnalyzer.Expand(table);

        Assert.Equal(0.5, rows.Single(value => value.Target == "木剑").ExpectedAmount, 6);
        Assert.Equal(49.75, rows.Single(value => value.Target == "Gold").ExpectedAmount, 6);
        Assert.Equal(0.5, rows.Single(value => value.Target == "药水").ExpectedAmount, 6);
        Assert.Equal(0.25, rows.Single(value => value.Target == "卷轴").ExpectedAmount, 6);
    }

    [Fact]
    public void 固定种子模拟结果可复现并接近理论期望()
    {
        var table = new DropTableDefinition("Drops/test");
        table.Drops.Add(DropEntryDefinition.Item(4, "木剑", 2));

        DropSimulationResult first = DropContentAnalyzer.Simulate(table, 100000, 42);
        DropSimulationResult second = DropContentAnalyzer.Simulate(table, 100000, 42);

        Assert.Equal(first.Rows, second.Rows);
        Assert.InRange(Assert.Single(first.Rows).AverageAmount, 0.48, 0.52);
    }

    [Fact]
    public void CSharp分组定义可以直接分析且不建立第二事实源()
    {
        var table = new DropTableDefinition("Drops/scripted");
        var group = new DropGroupDefinition { Random = true };
        group.Drops.Add(DropEntryDefinition.Item(1, "木剑", 1, 1));
        group.Drops.Add(DropEntryDefinition.Item(1, "铁剑", 1, 3));
        table.Drops.Add(DropEntryDefinition.GroupDrop(2, group));

        string report = DropContentAnalyzer.FormatAnalysis(table, 1000);

        Assert.Contains("drops/scripted", report);
        Assert.Contains("木剑", report);
        Assert.Contains("铁剑", report);
        Assert.Contains("固定种子模拟", report);
    }

    [Fact]
    public void CSharp定义快照可审查期望产出前后差异()
    {
        var table = new DropTableDefinition("Drops/script-diff");
        table.Drops.Add(DropEntryDefinition.Item(10, "木剑"));
        DropAnalysisSnapshot snapshot = DropContentAnalyzer.Capture(table);
        table.Drops[0].Chance = 5;

        DropAnalysisDiff diff = Assert.Single(DropContentAnalyzer.Compare(snapshot, table));

        Assert.Equal("木剑", diff.Target);
        Assert.Equal(0.1, diff.BeforeExpected, 6);
        Assert.Equal(0.2, diff.AfterExpected, 6);
    }

    [Fact]
    public void 随机组全命中候选按权重精确展开()
    {
        var table = new DropTableDefinition("Drops/random");
        var group = new DropGroupDefinition { Random = true };
        group.Drops.Add(DropEntryDefinition.Item(1, "木剑", 1, 1));
        group.Drops.Add(DropEntryDefinition.Item(1, "铁剑", 1, 3));
        table.Drops.Add(DropEntryDefinition.GroupDrop(2, group));

        IReadOnlyList<DropAnalysisRow> rows = DropContentAnalyzer.Expand(table);

        Assert.Equal(0.125, rows.Single(value => value.Target == "木剑").ExpectedAmount, 6);
        Assert.Equal(0.375, rows.Single(value => value.Target == "铁剑").ExpectedAmount, 6);
        Assert.All(rows, value => Assert.Equal("随机组权重精确展开", value.Note));
    }

    [Fact]
    public void 随机组金币仍按真实运行时语义全部累计()
    {
        var randomTable = new DropTableDefinition("Drops/random-gold");
        var randomGroup = new DropGroupDefinition { Random = true };
        randomGroup.Drops.Add(DropEntryDefinition.GoldDrop(1, 100));
        randomGroup.Drops.Add(DropEntryDefinition.Item(1, "木剑"));
        randomTable.Drops.Add(DropEntryDefinition.GroupDrop(1, randomGroup));

        DropSimulationResult randomResult = DropContentAnalyzer.Simulate(randomTable, 10000, 7);
        Assert.InRange(randomResult.Rows.Single(value => value.Target == "Gold").AverageAmount, 98, 101);
        Assert.Equal(1, randomResult.Rows.Single(value => value.Target == "木剑").AverageAmount, 6);

    }

    [Fact]
    public void 条件掉落缺少上下文时不伪造模拟结果()
    {
        var table = new DropTableDefinition("Drops/conditional");
        DropEntryDefinition entry = DropEntryDefinition.GoldDrop(1, 100);
        entry.Condition = _ => false;
        table.Drops.Add(entry);

        string report = DropContentAnalyzer.FormatAnalysis(table, 100);
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => DropContentAnalyzer.Simulate(table, 100));
        DropSimulationResult withContext = DropContentAnalyzer.Simulate(
            table, 100, 7, new DropAttemptContext("作者模拟", null, null, table.Key));

        Assert.Contains("跳过数值模拟", report);
        Assert.Contains("期望=不可计算", report);
        Assert.Contains("必须提供 DropAttemptContext", error.Message);
        Assert.Empty(withContext.Rows);
    }

    [Fact]
    public void 随机组嵌套条件后代在结构展开中标记不可计算()
    {
        var table = new DropTableDefinition("Drops/nested-condition");
        var nested = new DropGroupDefinition();
        DropEntryDefinition conditional = DropEntryDefinition.GoldDrop(1, 100);
        conditional.Condition = _ => true;
        nested.Drops.Add(conditional);
        var random = new DropGroupDefinition { Random = true };
        random.Drops.Add(DropEntryDefinition.GroupDrop(1, nested));
        table.Drops.Add(DropEntryDefinition.GroupDrop(1, random));

        DropAnalysisRow row = Assert.Single(DropContentAnalyzer.Expand(table));

        Assert.True(row.Conditional);
        Assert.Equal("不可计算", row.Probability);
        Assert.True(double.IsNaN(row.ExpectedAmount));
    }
}
