using System.Text;
using Server;
using Server.Scripting;
using Xunit;

namespace Base05.Tests;

[Collection(nameof(PhysicalTextFileProviderCollection))]
public sealed class TxtScriptReloadCoordinatorTests
{
    [Fact]
    public void 严格预检在数据段边界停止把商品正文识别为动作命令()
    {
        bool oldStrict = Settings.TxtScriptsStrictCompatibility;
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        try
        {
            Settings.TxtScriptsStrictCompatibility = true;
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            var definition = new TextFileDefinition("NPCs/商人", "商人.txt", "CP936", "CRLF")
                .AddLines([
                    "[@MAIN]", "#ACT", "GIVEGOLD 1", "[GOODS]", "古铜戒指", "[TRADE]", "轻型盔甲(男)"
                ]);

            Assert.DoesNotContain(TxtScriptSnapshotValidator.Validate(new CSharpTextFileProvider(
                    new Dictionary<string, TextFileDefinition> { [definition.Key] = definition })),
                value => value.StartsWith("TXT-SNAPSHOT-014", StringComparison.Ordinal));
        }
        finally
        {
            Settings.TxtScriptsStrictCompatibility = oldStrict;
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 成功重载原子发布新快照并保留旧定义对象供既有对话使用()
    {
        string root = CreateRoot();
        try
        {
            Write(root, "NPCs/老兵.txt", "旧内容");
            ITextFileProvider published = null;
            using var coordinator = Create(root, provider => { published = provider; return true; });

            TxtScriptReloadResult first = coordinator.ReloadNow();
            TextFileDefinition oldDefinition = published.GetByKey("NPCs/老兵");
            Write(root, "NPCs/老兵.txt", "新内容");
            TxtScriptReloadResult second = coordinator.ReloadNow();

            Assert.True(first.Published);
            Assert.True(second.Published);
            Assert.Equal(2, second.Snapshot.Version);
            Assert.NotEqual(first.Snapshot.Digest, second.Snapshot.Digest);
            Assert.Equal(new[] { "npcs/老兵" }, second.Snapshot.ChangedKeys);
            Assert.Equal("旧内容", Assert.Single(oldDefinition.Lines));
            Assert.Equal("新内容", Assert.Single(published.GetByKey("NPCs/老兵").Lines));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void 编码失败时不发布且旧快照继续服务()
    {
        string root = CreateRoot();
        try
        {
            Write(root, "NPCs/老兵.txt", "可用内容");
            ITextFileProvider published = null;
            int publishCount = 0;
            using var coordinator = Create(root, provider =>
            {
                published = provider;
                publishCount++;
                return true;
            });
            Assert.True(coordinator.ReloadNow().Published);
            File.WriteAllBytes(Path.Combine(root, "NPCs", "老兵.txt"), new byte[] { 0x81 });

            TxtScriptReloadResult failed = coordinator.ReloadNow();

            Assert.False(failed.Published);
            Assert.Single(failed.Errors);
            Assert.Equal(1, publishCount);
            Assert.Equal(1, coordinator.Current.Version);
            Assert.Equal("可用内容", Assert.Single(published.GetByKey("NPCs/老兵").Lines));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void 声明或引用验证失败时整批回滚()
    {
        string root = CreateRoot();
        try
        {
            Write(root, "NPCs/老兵.txt", "旧内容");
            ITextFileProvider published = null;
            bool reject = false;
            using var coordinator = new TxtScriptReloadCoordinator(
                Options(root), 10,
                provider => { published = provider; return true; },
                _ => reject ? new[] { "变量声明冲突：GLOBAL X 类型由整数变为字符串。" } : Array.Empty<string>());
            Assert.True(coordinator.ReloadNow().Published);
            Write(root, "NPCs/老兵.txt", "候选内容");
            reject = true;

            TxtScriptReloadResult failed = coordinator.ReloadNow();

            Assert.False(failed.Published);
            Assert.Contains("变量声明冲突", Assert.Single(failed.Errors), StringComparison.Ordinal);
            Assert.Equal("旧内容", Assert.Single(published.GetByKey("NPCs/老兵").Lines));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void 生产快照验证器拒绝重复标签并报告源文件行号()
    {
        string root = CreateRoot();
        try
        {
            Write(root, "NPCs/老兵.txt", "[@MAIN]\n#SAY\n可用");
            ITextFileProvider published = null;
            using var coordinator = new TxtScriptReloadCoordinator(
                Options(root), 10,
                provider => { published = provider; return true; },
                TxtScriptSnapshotValidator.Validate);
            Assert.True(coordinator.ReloadNow().Published);
            Write(root, "NPCs/老兵.txt", "[@MAIN]\n#SAY\n候选\n[@main]");

            TxtScriptReloadResult failed = coordinator.ReloadNow();

            Assert.False(failed.Published);
            Assert.Contains(failed.Errors, error => error.Contains("TXT-SNAPSHOT-002", StringComparison.Ordinal));
            Assert.Contains(failed.Errors, error => error.Contains("老兵.txt:4", StringComparison.OrdinalIgnoreCase));
            Assert.Equal("可用", published.GetByKey("NPCs/老兵").Lines[2]);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void 生产快照验证器拒绝缺失Include文件和标签并保留旧快照()
    {
        string root = CreateRoot();
        try
        {
            Write(root, "QuestDiary/公共.txt", "[@存在]\n#SAY\n公共");
            Write(root, "NPCs/老兵.txt", "[@MAIN]\n#INCLUDE [QuestDiary/公共.txt] @存在");
            ITextFileProvider published = null;
            using var coordinator = new TxtScriptReloadCoordinator(
                Options(root), 10,
                provider => { published = provider; return true; },
                TxtScriptSnapshotValidator.Validate);
            Assert.True(coordinator.ReloadNow().Published);
            Write(root, "NPCs/老兵.txt",
                "[@MAIN]\n#INCLUDE [QuestDiary/不存在.txt] @入口\n#INCLUDE [QuestDiary/公共.txt] @缺失");

            TxtScriptReloadResult failed = coordinator.ReloadNow();

            Assert.False(failed.Published);
            Assert.Contains(failed.Errors, error => error.Contains("TXT-SNAPSHOT-004", StringComparison.Ordinal));
            Assert.Contains(failed.Errors, error => error.Contains("TXT-SNAPSHOT-005", StringComparison.Ordinal));
            Assert.Equal("#INCLUDE [QuestDiary/公共.txt] @存在",
                published.GetByKey("NPCs/老兵").Lines[1]);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void 生产快照验证器拒绝Include循环并保留旧快照()
    {
        string root = CreateRoot();
        try
        {
            Write(root, "QuestDiary/A.txt", "[@MAIN]\n#SAY\n旧内容");
            ITextFileProvider published = null;
            using var coordinator = new TxtScriptReloadCoordinator(
                Options(root), 10,
                provider => { published = provider; return true; },
                TxtScriptSnapshotValidator.Validate);
            Assert.True(coordinator.ReloadNow().Published);
            Write(root, "QuestDiary/A.txt", "[@MAIN]\n#INCLUDE [QuestDiary/B.txt] @MAIN");
            Write(root, "QuestDiary/B.txt", "[@MAIN]\n#INCLUDE [QuestDiary/A.txt] @MAIN");

            TxtScriptReloadResult failed = coordinator.ReloadNow();

            Assert.False(failed.Published);
            Assert.Contains(failed.Errors, error => error.Contains("TXT-SNAPSHOT-008", StringComparison.Ordinal));
            Assert.Equal("旧内容", published.GetByKey("QuestDiary/A").Lines[2]);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Include引用深度十六层可发布而十七层被拒绝()
    {
        string root = CreateRoot();
        try
        {
            for (int index = 0; index <= 16; index++)
            {
                string next = index == 16
                    ? "#SAY\n终点"
                    : $"#INCLUDE [QuestDiary/{index + 1}.txt] @MAIN";
                Write(root, $"QuestDiary/{index}.txt", $"[@MAIN]\n{next}");
            }
            using var coordinator = new TxtScriptReloadCoordinator(
                Options(root), 10, _ => true, TxtScriptSnapshotValidator.Validate);

            Assert.True(coordinator.ReloadNow().Published);

            Write(root, "QuestDiary/17.txt", "[@MAIN]\n#SAY\n超限");
            Write(root, "QuestDiary/16.txt", "[@MAIN]\n#INCLUDE [QuestDiary/17.txt] @MAIN");
            TxtScriptReloadResult failed = coordinator.ReloadNow();

            Assert.False(failed.Published);
            Assert.Contains(failed.Errors, error => error.Contains("TXT-SNAPSHOT-009", StringComparison.Ordinal));
            Assert.Equal(1, coordinator.Current.Version);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void 生产快照验证器拒绝缺失的本地跳转标签和跨目录Call()
    {
        string root = CreateRoot();
        try
        {
            Write(root, "NPCs/老兵.txt", "[@MAIN]\n#ACT\nGOTO @不存在\nCALL \"QuestDiary/不存在.txt\"");
            using var coordinator = new TxtScriptReloadCoordinator(
                Options(root), 10, _ => true, TxtScriptSnapshotValidator.Validate);

            TxtScriptReloadResult failed = coordinator.ReloadNow();

            Assert.False(failed.Published);
            Assert.Contains(failed.Errors, error => error.Contains("TXT-SNAPSHOT-010", StringComparison.Ordinal));
            Assert.Contains(failed.Errors, error => error.Contains("TXT-SNAPSHOT-004", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void 翎风带参和动态本地标签通过预检而静态缺页仍失败关闭()
    {
        string root = CreateRoot();
        try
        {
            Write(root, "NPCs/带参跳转.txt",
                "[@MAIN]\n#ACT\nGOTO @目标(1,测试)\nGOTO @<$STR(S$动态页)>\n" +
                "[@目标]\n#SAY\n已到达");
            using var coordinator = new TxtScriptReloadCoordinator(
                Options(root), 10, _ => true, TxtScriptSnapshotValidator.Validate);

            Assert.True(coordinator.ReloadNow().Published);

            Write(root, "NPCs/带参跳转.txt",
                "[@MAIN]\n#ACT\nGOTO @确实不存在\n[@目标]\n#SAY\n已到达");
            TxtScriptReloadResult failed = coordinator.ReloadNow();
            Assert.False(failed.Published);
            Assert.Contains(failed.Errors, error =>
                error.Contains("TXT-SNAPSHOT-010", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void 生产快照验证器拒绝无效变量声明并报告原始行号()
    {
        string root = CreateRoot();
        try
        {
            Write(root, "Variables/Declarations.txt", "; 可用声明\nVAR Decimal P Rate DEFAULT 1.5");
            ITextFileProvider published = null;
            using var coordinator = new TxtScriptReloadCoordinator(
                Options(root), 10,
                provider => { published = provider; return true; },
                TxtScriptSnapshotValidator.Validate);
            Assert.True(coordinator.ReloadNow().Published);
            Write(root, "Variables/Declarations.txt", "; 错误候选\nVAR Decimal P Rate DEFAULT 非数字");

            TxtScriptReloadResult failed = coordinator.ReloadNow();

            Assert.False(failed.Published);
            Assert.Contains(failed.Errors, error =>
                error.Contains("TXT-SNAPSHOT-011", StringComparison.Ordinal) &&
                error.Contains("variables/declarations:2", StringComparison.OrdinalIgnoreCase));
            Assert.Equal("VAR Decimal P Rate DEFAULT 1.5",
                published.GetByKey("Variables/Declarations").Lines[1]);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void 连续保存经过防抖只发布一次最终内容()
    {
        string root = CreateRoot();
        try
        {
            Write(root, "NPCs/老兵.txt", "初始");
            ITextFileProvider published = null;
            int publishCount = 0;
            using var coordinator = new TxtScriptReloadCoordinator(
                Options(root), 80,
                provider => { published = provider; Interlocked.Increment(ref publishCount); return true; });

            Write(root, "NPCs/老兵.txt", "第一次");
            coordinator.NotifyChangeForTest();
            Write(root, "NPCs/老兵.txt", "第二次");
            coordinator.NotifyChangeForTest();
            Write(root, "NPCs/老兵.txt", "最终内容");
            coordinator.NotifyChangeForTest();

            Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref publishCount) == 1, TimeSpan.FromSeconds(5)));
            Thread.Sleep(150);
            Assert.Equal(1, publishCount);
            Assert.Equal("最终内容", Assert.Single(published.GetByKey("NPCs/老兵").Lines));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void 停止并释放后保存不会再次发布且手动重载明确拒绝()
    {
        string root = CreateRoot();
        try
        {
            Write(root, "NPCs/老兵.txt", "初始");
            int publishCount = 0;
            var coordinator = Create(root, _ => { publishCount++; return true; });
            coordinator.Start();
            coordinator.Dispose();
            Write(root, "NPCs/老兵.txt", "关闭后修改");
            Thread.Sleep(150);

            Assert.Equal(0, publishCount);
            Assert.Throws<ObjectDisposedException>(() => coordinator.ReloadNow());
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static TxtScriptReloadCoordinator Create(string root, Func<ITextFileProvider, bool> publisher) =>
        new(Options(root), 20, publisher);

    private static PhysicalTextFileProviderOptions Options(string root) =>
        new(root, TxtScriptLayout.LyoCrystal) { MaxFileBytes = 4096 };

    private static string CreateRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "LyoCrystal-TxtReload-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "NPCs"));
        return root;
    }

    private static void Write(string root, string relativePath, string content)
    {
        string path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, new UTF8Encoding(false, true));
    }
}
