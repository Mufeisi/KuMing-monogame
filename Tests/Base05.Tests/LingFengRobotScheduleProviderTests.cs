using Microsoft.VisualBasic.FileIO;
using Server;
using Server.MirEnvir;
using Server.MirObjects;
using Server.Scripting;
using System.Text;
using Xunit;

namespace Base05.Tests;

[Collection(nameof(PhysicalTextFileProviderCollection))]
public sealed class LingFengRobotScheduleProviderTests
{
    [Fact]
    public void 解析秒分时每日每周并拒绝非法调度()
    {
        var definition = new TextFileDefinition("SystemScripts/AutoRunRobot")
            .AddLines(new[]
            {
                "#AutoRun NPC SEC 2 @秒任务",
                "#AutoRun\tNpc\tMIN\t3\t@分任务",
                "#AutoRun NPC HOUR 4 @时任务",
                "#AutoRun NPC RUNONDAY 12:34:56 @每日任务",
                "#AutoRun NPC RUNONWEEK 5:19:55 @每周任务"
            });

        Assert.True(LingFengRobotScheduleProvider.TryCreate(
            definition, out LingFengRobotScheduleSnapshot snapshot, out IReadOnlyList<string> errors));
        Assert.Empty(errors);
        Assert.Equal(5, snapshot.Entries.Count);
        Assert.Equal(new[] { "[@秒任务]", "[@分任务]", "[@时任务]", "[@每日任务]", "[@每周任务]" },
            snapshot.Entries.Select(entry => entry.Page));

        var invalid = new TextFileDefinition("SystemScripts/AutoRunRobot")
            .AddLines(new[]
            {
                "#AutoRun NPC SEC 0 @零",
                "#AutoRun NPC HOUR 2147483647 @溢出",
                "#AutoRun NPC RUNONDAY 25:00 @越界",
                "#AutoRun NPC UNKNOWN 1 @未知"
            });
        Assert.False(LingFengRobotScheduleProvider.TryCreate(
            invalid, out _, out IReadOnlyList<string> invalidErrors));
        Assert.Equal(4, invalidErrors.Count);
    }

    [Fact]
    public void 酷明旧Robot标签按唯一同义页解析且已知外部页登记后不进入E1调度()
    {
        var definition = new TextFileDefinition("SystemScripts/AutoRunRobot")
            .AddLines(new[]
            {
                "#AutoRun NPC RUNONDAY 12:00 @Mir2_轮回开启Rm",
                "#AutoRun NPC RUNONDAY 18:00 @03战场开放",
                "#AutoRun NPC RUNONDAY 22:02 @Mir2_沙城奖励Rm"
            });
        Assert.True(LingFengRobotScheduleProvider.TryCreate(
            definition, out LingFengRobotScheduleSnapshot snapshot, out _));

        Assert.True(LingFengRobotScheduleProvider.TryResolvePages(
            snapshot, new[] { "[@03轮回开启]", "[@12战场开放]" },
            out LingFengRobotScheduleSnapshot resolved,
            out IReadOnlyList<string> errors));
        Assert.Equal(new[] { "[@03轮回开启]", "[@12战场开放]" },
            resolved.Entries.Select(entry => entry.Page));
        Assert.Empty(errors);
    }

    [Fact]
    public void 调度器按到期时点执行并阻止重入限制单次预算()
    {
        DateTime start = new(2026, 8, 17, 12, 0, 0, DateTimeKind.Local);
        var definition = new TextFileDefinition("SystemScripts/AutoRunRobot")
            .AddLines(new[]
            {
                "#AutoRun NPC SEC 2 @秒任务",
                "#AutoRun NPC RUNONDAY 12:00:03 @每日任务",
                "#AutoRun NPC RUNONDAY 12:00:02 @待预算任务",
                "#AutoRun NPC RUNONWEEK 1:12:00:01 @每周任务"
            });
        Assert.True(LingFengRobotScheduleProvider.TryCreate(definition, out var snapshot, out _));
        var scheduler = new LingFengRobotScheduler(maxExecutionsPerTick: 2);
        scheduler.Publish(snapshot, start);
        var calls = new List<string>();

        scheduler.Process(start.AddSeconds(1), page =>
        {
            calls.Add(page);
            scheduler.Process(start.AddSeconds(1), nested => calls.Add("重入:" + nested));
        });
        Assert.Equal(new[] { "[@每周任务]" }, calls);

        scheduler.Process(start.AddSeconds(3), calls.Add);
        Assert.Equal(new[] { "[@每周任务]", "[@秒任务]", "[@每日任务]" }, calls);
        Assert.Equal(1, scheduler.ReentryRejectedCount);
        Assert.Equal(1, scheduler.BudgetExceededCount);

        scheduler.Process(start.AddSeconds(3), calls.Add);
        Assert.Equal(new[] { "[@每周任务]", "[@秒任务]", "[@每日任务]", "[@待预算任务]" }, calls);
    }

    [Fact]
    public void 物理Envir经Robot页面调度执行并在停止后不再执行()
    {
        bool oldTxtEnabled = Settings.TxtScriptsEnabled;
        string oldTxtPath = Settings.TxtScriptsPath;
        TxtScriptLayout oldLayout = Settings.TxtScriptsLayout;
        long oldMaxFileBytes = Settings.TxtScriptsMaxFileBytes;
        bool oldCSharpEnabled = Settings.CSharpScriptsEnabled;
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        string root = Path.Combine(Path.GetTempPath(), "LyoCrystal-LFENV10-" + Guid.NewGuid().ToString("N"));
        NPCScript script = null;
        try
        {
            Write(root, "Robot_def/ROBOTMANAGE.TXT",
                "[@定时任务]\n#ACT\nPARAM1 测试地图 7\nPARAM2 123\nPARAM3 456");
            Write(root, "Robot_def/AUTORUNROBOT.TXT", "#AutoRun NPC SEC 1 @定时任务");
            Settings.CSharpScriptsEnabled = false;
            Settings.TxtScriptsEnabled = true;
            Settings.TxtScriptsPath = root;
            Settings.TxtScriptsLayout = TxtScriptLayout.LingFeng;
            Settings.TxtScriptsMaxFileBytes = 4096;
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026.08";
            Envir.Main.ApplyPhysicalTextFileDefinitions();
            DateTime publishedAt = Envir.Main.Now;
            script = NPCScript.GetOrAdd(uint.MaxValue - 810, "00Robot", NPCScriptType.Robot);
            NPCSegment segment = Assert.Single(Assert.Single(script.NPCPages).SegmentList);

            Robot.ProcessAt(script, publishedAt.AddSeconds(1));

            Assert.Equal("测试地图", segment.Param1);
            Assert.Equal(7, segment.Param1Instance);
            Assert.Equal(123, segment.Param2);
            Assert.Equal(456, segment.Param3);

            segment.Param2 = 0;
            Robot.Clear();
            Robot.ProcessAt(script, publishedAt.AddSeconds(2));
            Assert.Equal(0, segment.Param2);
        }
        finally
        {
            Robot.Clear();
            if (script != null) Envir.Main.Scripts.Remove(script.ScriptID);
            Settings.CSharpScriptsEnabled = oldCSharpEnabled;
            Settings.TxtScriptsEnabled = false;
            Envir.Main.ApplyPhysicalTextFileDefinitions();
            Settings.TxtScriptsEnabled = oldTxtEnabled;
            Settings.TxtScriptsPath = oldTxtPath;
            Settings.TxtScriptsLayout = oldLayout;
            Settings.TxtScriptsMaxFileBytes = oldMaxFileBytes;
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void 热更新替换旧快照且停止后不再派发()
    {
        DateTime start = new(2026, 8, 17, 0, 0, 0, DateTimeKind.Local);
        var scheduler = new LingFengRobotScheduler();
        scheduler.Publish(Snapshot("#AutoRun NPC SEC 1 @旧任务"), start);
        scheduler.Publish(Snapshot("#AutoRun NPC SEC 1 @新任务"), start);
        var calls = new List<string>();

        scheduler.Process(start.AddSeconds(1), calls.Add);
        Assert.Equal(new[] { "[@新任务]" }, calls);

        scheduler.Stop();
        scheduler.Process(start.AddSeconds(2), calls.Add);
        Assert.Single(calls);
        Assert.False(scheduler.IsRunning);
    }

    [Fact]
    public void 固定时刻在启动当秒执行且单页异常不阻断其他到期页()
    {
        DateTime start = new DateTime(2026, 8, 17, 8, 30, 0, DateTimeKind.Local).AddMilliseconds(123);
        var scheduler = new LingFengRobotScheduler();
        scheduler.Publish(Snapshot(
            "#AutoRun NPC RUNONDAY 08:30 @异常页",
            "#AutoRun NPC RUNONWEEK 1:08:30 @正常页"), start);
        var calls = new List<string>();
        var faults = new List<string>();

        scheduler.Process(start, page =>
        {
            if (page == "[@异常页]") throw new InvalidOperationException("测试异常");
            calls.Add(page);
        }, (page, error) => faults.Add(page + ":" + error.GetType().Name));

        Assert.Equal(new[] { "[@正常页]" }, calls);
        Assert.Equal(new[] { "[@异常页]:InvalidOperationException" }, faults);
        Assert.Equal(1, scheduler.FaultedExecutionCount);
    }

    [Fact]
    public void 无效AutoRun热更新拒绝发布并保留旧快照()
    {
        string root = Path.Combine(Path.GetTempPath(), "LyoCrystal-LFENV10-Reload-" + Guid.NewGuid().ToString("N"));
        try
        {
            Write(root, "Robot_def/ROBOTMANAGE.TXT", "[@可用]\n#ACT\nPARAM2 1");
            Write(root, "Robot_def/AUTORUNROBOT.TXT", "#AutoRun NPC SEC 1 @可用");
            ITextFileProvider published = null;
            int publishCount = 0;
            using var coordinator = new TxtScriptReloadCoordinator(
                new PhysicalTextFileProviderOptions(root, TxtScriptLayout.LingFeng) { MaxFileBytes = 4096 },
                10,
                provider =>
                {
                    published = provider;
                    publishCount++;
                    return true;
                },
                TxtScriptSnapshotValidator.Validate);
            Assert.True(coordinator.ReloadNow().Published);

            Write(root, "Robot_def/AUTORUNROBOT.TXT", "#AutoRun NPC SEC 1 @不存在");
            TxtScriptReloadResult missingPage = coordinator.ReloadNow();
            Assert.False(missingPage.Published);
            Assert.Contains(missingPage.Errors, error => error.Contains("LFENV10-ROBOT-009", StringComparison.Ordinal));

            File.Delete(Path.Combine(root, "Robot_def", "ROBOTMANAGE.TXT"));
            TxtScriptReloadResult missingScript = coordinator.ReloadNow();
            Assert.False(missingScript.Published);
            Assert.Contains(missingScript.Errors, error => error.Contains("LFENV10-ROBOT-008", StringComparison.Ordinal));

            Write(root, "Robot_def/AUTORUNROBOT.TXT", "#AutoRun NPC SEC 0 @坏候选");

            TxtScriptReloadResult failed = coordinator.ReloadNow();

            Assert.False(failed.Published);
            Assert.Contains(failed.Errors, error => error.Contains("LFENV10-ROBOT-005", StringComparison.Ordinal));
            Assert.Equal(1, publishCount);
            Assert.Equal(1, coordinator.Current.Version);
            Assert.Equal("#AutoRun NPC SEC 1 @可用",
                Assert.Single(published.GetByKey("SystemScripts/AutoRunRobot").Lines));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [ExternalResourceFact(
        @"外部资源阻塞：本机未挂载 D:\ChuanQi\服务端 权威语料。",
        @"D:\ChuanQi\服务端")]
    public void 本机代表Robot定义全部通过严格调度解析()
    {
        const string corpusRoot = @"D:\ChuanQi\服务端";
        if (!Directory.Exists(corpusRoot))
            throw Xunit.Sdk.SkipException.ForSkip("本机未挂载 D:\\ChuanQi\\服务端 权威语料。");
        int verified = 0;
        foreach (IReadOnlyDictionary<string, string> row in RepresentativeRoots())
        {
            string root = Path.Combine(corpusRoot, row["相对路径"]);
            string file = Path.Combine(root, "Robot_def", "AUTORUNROBOT.TXT");
            if (!File.Exists(file)) continue;
            var provider = new PhysicalTextFileProvider(
                new PhysicalTextFileProviderOptions(root, TxtScriptLayout.LingFeng), new[] { file });
            TextFileDefinition definition = Assert.IsType<TextFileDefinition>(
                provider.GetByKey("SystemScripts/AutoRunRobot"));
            Assert.True(LingFengRobotScheduleProvider.TryCreate(
                definition, out LingFengRobotScheduleSnapshot snapshot, out IReadOnlyList<string> errors),
                $"{row["根ID"]}:{string.Join(";", errors)}");
            Assert.NotEmpty(snapshot.Entries);
            verified++;
        }
        Assert.True(verified >= 20, $"仅验证 {verified} 个代表 Robot 定义。");
    }

    private static LingFengRobotScheduleSnapshot Snapshot(params string[] lines)
    {
        var definition = new TextFileDefinition("SystemScripts/AutoRunRobot").AddLines(lines);
        Assert.True(LingFengRobotScheduleProvider.TryCreate(definition, out var snapshot, out var errors),
            string.Join(";", errors));
        return snapshot;
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, string>> RepresentativeRoots()
    {
        string path = Path.Combine(RepositoryRoot(), "Docs", "generated", "scripting", "lingfeng-envir-roots.csv");
        using var parser = new TextFieldParser(path) { TextFieldType = FieldType.Delimited, HasFieldsEnclosedInQuotes = true };
        parser.SetDelimiters(",");
        string[] headers = parser.ReadFields()!;
        var rows = new List<IReadOnlyDictionary<string, string>>();
        while (!parser.EndOfData)
        {
            string[] fields = parser.ReadFields()!;
            var row = headers.Select((header, index) => (header, fields[index]))
                .ToDictionary(item => item.header, item => item.Item2, StringComparer.Ordinal);
            if (row["代表样本"] == "是") rows.Add(row);
        }
        return rows;
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "global.json"))) return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("无法定位仓库根目录。");
    }

    private static void Write(string root, string relativePath, string content)
    {
        string path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, new UTF8Encoding(false, true));
    }
}
