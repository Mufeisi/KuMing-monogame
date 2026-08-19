using System.Text;
using Server;
using Server.MirDatabase;
using Server.MirEnvir;
using Server.MirObjects;
using Server.Scripting;
using Xunit;

namespace Base05.Tests;

[CollectionDefinition(nameof(PhysicalTextFileProviderCollection), DisableParallelization = true)]
public sealed class PhysicalTextFileProviderCollection;

[Collection(nameof(PhysicalTextFileProviderCollection))]
public sealed class PhysicalTextFileProviderTests
{
    [Theory]
    [InlineData(TxtScriptLayout.LyoCrystal)]
    [InlineData(TxtScriptLayout.LingFeng)]
    public void 空目录在两种布局下均发布空快照(TxtScriptLayout layout)
    {
        string root = CreateTemporaryRoot();
        try
        {
            ITextFileProvider provider = new PhysicalTextFileProvider(
                new PhysicalTextFileProviderOptions(root, layout));

            Assert.Empty(provider.GetAll());
            Assert.Null(provider.GetByKey("NPCs/不存在"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void 根目录外候选文件拒绝进入快照()
    {
        string root = CreateTemporaryRoot();
        string outsideRoot = CreateTemporaryRoot();
        try
        {
            string outsideFile = Path.Combine(outsideRoot, "NPCs", "越界.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(outsideFile)!);
            File.WriteAllText(outsideFile, "[@MAIN]", new UTF8Encoding(false, true));

            InvalidDataException error = Assert.Throws<InvalidDataException>(() =>
                new PhysicalTextFileProvider(
                    new PhysicalTextFileProviderOptions(root, TxtScriptLayout.LyoCrystal),
                    new[] { outsideFile }));

            Assert.Contains(Path.GetFullPath(outsideFile), error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(Path.GetFullPath(root), error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            Directory.Delete(outsideRoot, recursive: true);
        }
    }

    [Fact]
    public void 重复Key拒绝发布并报告冲突来源()
    {
        string root = CreateTemporaryRoot();
        try
        {
            string file = Path.Combine(root, "NPCs", "重复.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, "[@MAIN]", new UTF8Encoding(false, true));
            var options = new PhysicalTextFileProviderOptions(root, TxtScriptLayout.LyoCrystal);

            InvalidDataException error = Assert.Throws<InvalidDataException>(() =>
                new PhysicalTextFileProvider(options, new[] { file, file }));

            Assert.Contains("npcs/重复", error.Message, StringComparison.Ordinal);
            Assert.Contains(Path.GetFullPath(file), error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(LogicKey.NormalizeOrThrow("NPCs/重复"), LogicKey.NormalizeOrThrow("npcs/重复"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void UTF8BOM正文损坏时禁止回退CP936()
    {
        string root = CreateTemporaryRoot();
        try
        {
            string npcDirectory = Path.Combine(root, "NPCs");
            Directory.CreateDirectory(npcDirectory);
            string file = Path.Combine(npcDirectory, "损坏BOM.txt");
            File.WriteAllBytes(file, new byte[] { 0xEF, 0xBB, 0xBF, 0x81, 0x40 });

            InvalidDataException error = Assert.Throws<InvalidDataException>(() =>
                new PhysicalTextFileProvider(
                    new PhysicalTextFileProviderOptions(root, TxtScriptLayout.LyoCrystal)));

            Assert.Contains(Path.GetFullPath(file), error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("UTF-8 BOM", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void 旧脚本行首控制字符仅在内存规范化且不修改CP936源文件()
    {
        string root = CreateTemporaryRoot();
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            string npcDirectory = Path.Combine(root, "NPCs");
            Directory.CreateDirectory(npcDirectory);
            string file = Path.Combine(npcDirectory, "控制字符.txt");
            byte[] original = Encoding.GetEncoding(936).GetBytes(
                "\u001a\r\n\u001f[@STDMODEFUNC209]\r\n#SAY\r\n中文正文\r\n");
            File.WriteAllBytes(file, original);

            var provider = new PhysicalTextFileProvider(
                new PhysicalTextFileProviderOptions(root, TxtScriptLayout.LyoCrystal));
            TextFileDefinition definition = Assert.Single(provider.GetAll());

            Assert.Equal(string.Empty, definition.Lines[0]);
            Assert.Equal("[@STDMODEFUNC209]", definition.Lines[1]);
            Assert.Equal("中文正文", definition.Lines[3]);
            Assert.Equal(original, File.ReadAllBytes(file));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void 原生Txt无需Cs包装即可进入NPC解析器并生成中文对话()
    {
        bool oldTxtEnabled = Settings.TxtScriptsEnabled;
        string oldTxtPath = Settings.TxtScriptsPath;
        TxtScriptLayout oldLayout = Settings.TxtScriptsLayout;
        long oldMaxFileBytes = Settings.TxtScriptsMaxFileBytes;
        bool oldCSharpEnabled = Settings.CSharpScriptsEnabled;
        string root = CreateTemporaryRoot();
        NPCScript script = null;
        try
        {
            string fileName = "物理来源示范_" + Guid.NewGuid().ToString("N");
            WriteUtf8(root, "QuestDiary/示范/公共对话.txt", "[@公共]\n{\n#SAY\n跨目录包含已加载\n}");
            WriteUtf8(root, $"NPCs/{fileName}.txt",
                "[@MAIN]\n#SAY\n原生TXT已加载\n#INCLUDE [QuestDiary/示范/公共对话.txt] @公共");
            Settings.CSharpScriptsEnabled = false;
            Settings.TxtScriptsEnabled = true;
            Settings.TxtScriptsPath = root;
            Settings.TxtScriptsLayout = TxtScriptLayout.LyoCrystal;
            Settings.TxtScriptsMaxFileBytes = 4096;
            Envir.Main.ApplyPhysicalTextFileDefinitions();

            script = NPCScript.GetOrAdd(uint.MaxValue, fileName, NPCScriptType.Normal);

            string[] speech = script.NPCPages
                .SelectMany(page => page.SegmentList)
                .SelectMany(segment => segment.Say)
                .ToArray();
            Assert.Contains("原生TXT已加载", speech);
            Assert.Contains("跨目录包含已加载", speech);
        }
        finally
        {
            if (script != null) Envir.Main.Scripts.Remove(script.ScriptID);
            Settings.CSharpScriptsEnabled = oldCSharpEnabled;
            Settings.TxtScriptsEnabled = false;
            Envir.Main.ApplyPhysicalTextFileDefinitions();
            Settings.TxtScriptsEnabled = oldTxtEnabled;
            Settings.TxtScriptsPath = oldTxtPath;
            Settings.TxtScriptsLayout = oldLayout;
            Settings.TxtScriptsMaxFileBytes = oldMaxFileBytes;
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void 物理快照发布后已缓存NPC实例重新解析新页面()
    {
        bool oldTxtEnabled = Settings.TxtScriptsEnabled;
        string oldTxtPath = Settings.TxtScriptsPath;
        TxtScriptLayout oldLayout = Settings.TxtScriptsLayout;
        long oldMaxFileBytes = Settings.TxtScriptsMaxFileBytes;
        bool oldCSharpEnabled = Settings.CSharpScriptsEnabled;
        string root = CreateTemporaryRoot();
        NPCScript script = null;
        PlayerObject player = null;
        try
        {
            string fileName = "缓存重载_" + Guid.NewGuid().ToString("N");
            WriteUtf8(root, $"NPCs/{fileName}.txt", "[@MAIN]\n#SAY\n旧快照");
            Settings.CSharpScriptsEnabled = false;
            Settings.TxtScriptsEnabled = true;
            Settings.TxtScriptsPath = root;
            Settings.TxtScriptsLayout = TxtScriptLayout.LyoCrystal;
            Settings.TxtScriptsMaxFileBytes = 4096;
            Envir.Main.ApplyPhysicalTextFileDefinitions();
            script = NPCScript.GetOrAdd(uint.MaxValue - 2, fileName, NPCScriptType.Normal);
            Assert.Contains("旧快照", Speech(script));

            player = new PlayerObject
            {
                Info = new CharacterInfo { Name = "热重载会话玩家" },
                Account = new AccountInfo(),
                NPCObjectID = 123,
                NPCScriptID = script.ScriptID,
                NPCPage = script.NPCPages.Single(page => page.Key == NPCScript.MainKey),
            };
            player.ActionList.Add(new DelayedAction(DelayedType.NPC, -1, player.NPCObjectID, "[@MAIN]"));
            Envir.Main.Players.Add(player);

            WriteUtf8(root, $"NPCs/{fileName}.txt", "[@MAIN]\n#SAY\n新快照");
            Envir.Main.ApplyPhysicalTextFileDefinitions();

            Assert.Same(script, NPCScript.GetOrAdd(uint.MaxValue - 2, fileName, NPCScriptType.Normal));
            Assert.DoesNotContain("旧快照", Speech(script));
            Assert.Contains("新快照", Speech(script));
            Assert.Equal(0u, player.NPCObjectID);
            Assert.Null(player.NPCPage);
            Assert.DoesNotContain(player.ActionList, action => action.Type == DelayedType.NPC);
        }
        finally
        {
            if (player != null) Envir.Main.Players.Remove(player);
            if (script != null) Envir.Main.Scripts.Remove(script.ScriptID);
            Settings.CSharpScriptsEnabled = oldCSharpEnabled;
            Settings.TxtScriptsEnabled = false;
            Envir.Main.ApplyPhysicalTextFileDefinitions();
            Settings.TxtScriptsEnabled = oldTxtEnabled;
            Settings.TxtScriptsPath = oldTxtPath;
            Settings.TxtScriptsLayout = oldLayout;
            Settings.TxtScriptsMaxFileBytes = oldMaxFileBytes;
            Directory.Delete(root, recursive: true);
        }
    }

    private static string[] Speech(NPCScript script) => script.NPCPages
        .SelectMany(page => page.SegmentList)
        .SelectMany(segment => segment.Say)
        .ToArray();

    [Fact]
    public void Envir仅在物理Txt开启时发布磁盘快照()
    {
        bool oldEnabled = Settings.TxtScriptsEnabled;
        string oldPath = Settings.TxtScriptsPath;
        TxtScriptLayout oldLayout = Settings.TxtScriptsLayout;
        long oldMaxFileBytes = Settings.TxtScriptsMaxFileBytes;
        string root = CreateTemporaryRoot();
        try
        {
            var envir = new Envir();
            Settings.TxtScriptsEnabled = false;
            Settings.TxtScriptsPath = Path.Combine(root, "不存在");
            envir.ApplyPhysicalTextFileDefinitions();
            Assert.Null(envir.TextFileProvider);

            WriteUtf8(root, "NPCs/示范老人.txt", "[@MAIN]");
            Settings.TxtScriptsEnabled = true;
            Settings.TxtScriptsPath = root;
            Settings.TxtScriptsLayout = TxtScriptLayout.LyoCrystal;
            Settings.TxtScriptsMaxFileBytes = 4096;

            envir.ApplyPhysicalTextFileDefinitions();

            TextFileDefinition definition = Assert.IsType<TextFileDefinition>(
                envir.TextFileProvider.GetByKey("NPCs/示范老人"));
            Assert.Equal("[@MAIN]", Assert.Single(definition.Lines));
        }
        finally
        {
            Settings.TxtScriptsEnabled = oldEnabled;
            Settings.TxtScriptsPath = oldPath;
            Settings.TxtScriptsLayout = oldLayout;
            Settings.TxtScriptsMaxFileBytes = oldMaxFileBytes;
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void 物理Txt来源采用关闭和受限的安全默认值()
    {
        Assert.False(Settings.TxtScriptsEnabled);
        Assert.Equal(Settings.EnvirPath, Settings.TxtScriptsPath);
        Assert.Equal(TxtScriptLayout.LyoCrystal, Settings.TxtScriptsLayout);
        Assert.Equal(1024 * 1024, Settings.TxtScriptsMaxFileBytes);
        Assert.Equal(TextFileSourcePriority.CSharpFirst, Settings.TxtScriptsSourcePriority);
        Assert.True(Settings.TxtScriptsHotReloadEnabled);
        Assert.Equal(500, Settings.TxtScriptsDebounceMs);
        Assert.Equal(64, Settings.TxtScriptsMaxImmediateTransitions);
    }

    [Fact]
    public void 组合来源默认由CSharp遮蔽同Key物理Txt并报告双方来源()
    {
        TextFileDefinition csharp = new TextFileDefinition("NPCs/老兵", "注册表.cs", "C#", "NONE")
            .AddLine("C#内容");
        TextFileDefinition txt = new TextFileDefinition("NPCs/老兵", "物理/老兵.txt", "UTF-8", "LF")
            .AddLine("TXT内容");
        var provider = new CompositeTextFileProvider(
            CreateCSharpProvider(csharp),
            CreateCSharpProvider(txt),
            TextFileSourcePriority.CSharpFirst);

        Assert.Equal("C#内容", Assert.Single(provider.GetByKey("npcs/老兵").Lines));
        TextFileSourceConflict conflict = Assert.Single(provider.Conflicts);
        Assert.Equal("npcs/老兵", conflict.Key);
        Assert.Contains("C#:注册表.cs", conflict.SelectedSource, StringComparison.Ordinal);
        Assert.Contains("TXT:物理/老兵.txt", conflict.ShadowedSource, StringComparison.Ordinal);
    }

    [Fact]
    public void 组合来源可显式选择物理Txt优先且每个Key只发布一次()
    {
        TextFileDefinition csharp = new TextFileDefinition("NPCs/老兵", "注册表.cs", "C#", "NONE")
            .AddLine("C#内容");
        TextFileDefinition txt = new TextFileDefinition("NPCs/老兵", "物理/老兵.txt", "UTF-8", "LF")
            .AddLine("TXT内容");
        var provider = new CompositeTextFileProvider(
            CreateCSharpProvider(csharp),
            CreateCSharpProvider(txt),
            TextFileSourcePriority.TxtFirst);

        Assert.Equal("TXT内容", Assert.Single(provider.GetByKey("NPCs/老兵").Lines));
        Assert.Single(provider.GetAll());
        Assert.Equal(TextFileSourcePriority.TxtFirst, Assert.Single(provider.Conflicts).Priority);
    }

    [Fact]
    public void 组合来源覆盖两种开关的四种组合且不重复执行()
    {
        TextFileDefinition csharp = new TextFileDefinition("Defines/CSharp").AddLine("C#");
        TextFileDefinition txt = new TextFileDefinition("Defines/Txt", "Txt.txt", "UTF-8", "LF").AddLine("TXT");
        ITextFileProvider csharpProvider = CreateCSharpProvider(csharp);
        ITextFileProvider txtProvider = CreateCSharpProvider(txt);

        Assert.Empty(new CompositeTextFileProvider(null, null, TextFileSourcePriority.CSharpFirst).GetAll());
        Assert.Equal(new[] { "defines/csharp" },
            new CompositeTextFileProvider(csharpProvider, null, TextFileSourcePriority.CSharpFirst)
                .GetAll().Select(item => item.Key));
        Assert.Equal(new[] { "defines/txt" },
            new CompositeTextFileProvider(null, txtProvider, TextFileSourcePriority.CSharpFirst)
                .GetAll().Select(item => item.Key));
        Assert.Equal(new[] { "defines/csharp", "defines/txt" },
            new CompositeTextFileProvider(csharpProvider, txtProvider, TextFileSourcePriority.CSharpFirst)
                .GetAll().Select(item => item.Key));
    }

    [Fact]
    public void 查询拒绝路径穿越和绝对路径形式的Key()
    {
        string root = CreateTemporaryRoot();
        try
        {
            WriteUtf8(root, "NPCs/安全脚本.txt", "安全内容");
            ITextFileProvider provider = new PhysicalTextFileProvider(
                new PhysicalTextFileProviderOptions(root, TxtScriptLayout.LyoCrystal));

            Assert.NotNull(provider.GetByKey("NPCs/安全脚本"));
            Assert.Null(provider.GetByKey("../NPCs/安全脚本"));
            Assert.Null(provider.GetByKey("/NPCs/安全脚本"));
            Assert.Null(provider.GetByKey("\\NPCs\\安全脚本"));
            Assert.Null(provider.GetByKey("C:\\NPCs\\安全脚本"));
            Assert.Null(provider.GetByKey("\\\\server\\share\\安全脚本"));
            Assert.Null(provider.GetByKey("\\\\?\\C:\\NPCs\\安全脚本"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LingFeng六类目录映射为互不冲突的逻辑Key()
    {
        string root = CreateTemporaryRoot();
        try
        {
            WriteUtf8(root, "Market_Def/比奇/老兵.txt", "市场");
            WriteUtf8(root, "Npc_def/比奇/老兵.txt", "功能");
            WriteUtf8(root, "QuestDiary/任务/主线.TXT", "任务");
            WriteUtf8(root, "MapQuest_def/QManage.txt", "登录入口");
            WriteUtf8(root, "Robot_def/ROBOTMANAGE.TXT", "机器人入口");
            WriteUtf8(root, "DeFines/公共/变量.txt", "定义");

            ITextFileProvider provider = new PhysicalTextFileProvider(
                new PhysicalTextFileProviderOptions(root, TxtScriptLayout.LingFeng));

            Assert.Equal("市场", Assert.Single(provider.GetByKey("NPCs/比奇/老兵").Lines));
            Assert.Equal("功能", Assert.Single(provider.GetByKey("NpcDefs/比奇/老兵").Lines));
            Assert.Equal("任务", Assert.Single(provider.GetByKey("QuestDiary/任务/主线").Lines));
            Assert.Equal("登录入口", Assert.Single(provider.GetByKey("SystemScripts/QManage").Lines));
            Assert.Equal("机器人入口", Assert.Single(provider.GetByKey("SystemScripts/RobotManage").Lines));
            Assert.Equal("定义", Assert.Single(provider.GetByKey("Defines/公共/变量").Lines));
            Assert.Equal(6, provider.GetAll().Count);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LingFengDefine展开命令名并通过真实元宝检测扣除链()
    {
        string root = CreateTemporaryRoot();
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        try
        {
            WriteUtf8(root, "QuestDiary/配置/常量.txt",
                "[@配置]\n{\n#Define $(货币变量) GameGold\n}");
            WriteUtf8(root, "Defines/Constant.ini",
                "#Define #原版逻辑标志# 552");
            WriteUtf8(root, "QuestDiary/玩法/消费.txt",
                "[@消费]\n{\n#IF\nCheck$(货币变量) ? 30\n#ACT\n$(货币变量) - 30\nSET [#原版逻辑标志#] 1\n}");
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";

            var provider = new PhysicalTextFileProvider(
                new PhysicalTextFileProviderOptions(root, TxtScriptLayout.LingFeng));
            TextFileDefinition definition = provider.GetByKey("QuestDiary/玩法/消费");
            Assert.Contains("CheckGameGold ? 30", definition.Lines);
            Assert.Contains("GameGold - 30", definition.Lines);
            Assert.Contains("SET [552] 1", definition.Lines);
            Assert.StartsWith("; #Define", provider.GetByKey("QuestDiary/配置/常量").Lines[2]);
            Assert.StartsWith("; #Define", Assert.Single(
                provider.GetByKey("Defines/Constant").Lines));

            var player = new PlayerObject
            {
                Info = new CharacterInfo { Name = "宏展开命格人物", PearlCount = 100 }
            };
            var page = new NPCPage("[@消费]");
            var segment = new NPCSegment(page, new List<string>(), new List<string>(),
                new List<string>(), new List<string>(), new List<string>(), "define-test");
            segment.ParseCheck("CheckGameGold ? 30");
            segment.ParseAct(segment.ActList, "GameGold - 30");
            Assert.True(segment.Check(player));
            Assert.Equal(70, player.Info.PearlCount);
        }
        finally
        {
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LingFengDefine冲突与循环均拒绝整个候选()
    {
        string conflictRoot = CreateTemporaryRoot();
        string cycleRoot = CreateTemporaryRoot();
        try
        {
            WriteUtf8(conflictRoot, "QuestDiary/A.txt", "#Define $(货币) GameGold");
            WriteUtf8(conflictRoot, "QuestDiary/B.txt", "#Define $(货币) GoldCount");
            InvalidDataException conflict = Assert.Throws<InvalidDataException>(() =>
                new PhysicalTextFileProvider(
                    new PhysicalTextFileProviderOptions(conflictRoot, TxtScriptLayout.LingFeng)));
            Assert.Contains("LFENV16-DEFINE-002", conflict.Message, StringComparison.Ordinal);

            WriteUtf8(cycleRoot, "QuestDiary/A.txt",
                "#Define $(甲) $(乙)\n#Define $(乙) $(甲)");
            InvalidDataException cycle = Assert.Throws<InvalidDataException>(() =>
                new PhysicalTextFileProvider(
                    new PhysicalTextFileProviderOptions(cycleRoot, TxtScriptLayout.LingFeng)));
            Assert.Contains("LFENV16-DEFINE-003", cycle.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(conflictRoot, recursive: true);
            Directory.Delete(cycleRoot, recursive: true);
        }
    }

    [Fact]
    public void 空文件可加载且非Txt扩展名不进入脚本源()
    {
        string root = CreateTemporaryRoot();
        try
        {
            string npcDirectory = Path.Combine(root, "NPCs");
            Directory.CreateDirectory(npcDirectory);
            string emptyFile = Path.Combine(npcDirectory, "空脚本.txt");
            File.WriteAllBytes(emptyFile, Array.Empty<byte>());
            File.WriteAllText(Path.Combine(root, "忽略.cs"), "[@MAIN]", Encoding.UTF8);
            string unlistedDirectory = Path.Combine(root, "Logs");
            Directory.CreateDirectory(unlistedDirectory);
            File.WriteAllText(Path.Combine(unlistedDirectory, "不应加载.txt"), "[@MAIN]", Encoding.UTF8);

            ITextFileProvider provider = new PhysicalTextFileProvider(
                new PhysicalTextFileProviderOptions(root, TxtScriptLayout.LyoCrystal));

            TextFileDefinition definition = Assert.IsType<TextFileDefinition>(provider.GetByKey("NPCs/空脚本"));
            Assert.Equal(new[] { string.Empty }, definition.Lines);
            Assert.Equal("UTF-8", definition.SourceEncoding);
            Assert.Equal("NONE", definition.SourceNewLine);
            Assert.Null(provider.GetByKey("忽略"));
            Assert.Null(provider.GetByKey("Logs/不应加载"));
            Assert.Single(provider.GetAll());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void 超过配置大小的文件在读取前拒绝并报告限制()
    {
        string root = CreateTemporaryRoot();
        try
        {
            string npcDirectory = Path.Combine(root, "NPCs");
            Directory.CreateDirectory(npcDirectory);
            string file = Path.Combine(npcDirectory, "oversized.txt");
            File.WriteAllBytes(file, Encoding.UTF8.GetBytes("12345"));

            InvalidDataException error = Assert.Throws<InvalidDataException>(() =>
                new PhysicalTextFileProvider(
                    new PhysicalTextFileProviderOptions(root, TxtScriptLayout.LyoCrystal)
                    {
                        MaxFileBytes = 4
                    }));

            Assert.Contains(Path.GetFullPath(file), error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("4 字节", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void 损坏字节拒绝发布并报告源文件()
    {
        string root = CreateTemporaryRoot();
        try
        {
            string npcDirectory = Path.Combine(root, "NPCs");
            Directory.CreateDirectory(npcDirectory);
            string file = Path.Combine(npcDirectory, "损坏.txt");
            File.WriteAllBytes(file, new byte[] { 0x81 });

            InvalidDataException error = Assert.Throws<InvalidDataException>(() =>
                new PhysicalTextFileProvider(
                    new PhysicalTextFileProviderOptions(root, TxtScriptLayout.LyoCrystal)));

            Assert.Contains(Path.GetFullPath(file), error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("UTF-8 或 CP936", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LyoCrystal布局移除UTF8BOM并保留LF来源信息()
    {
        string root = CreateTemporaryRoot();
        try
        {
            string npcDirectory = Path.Combine(root, "NPCs");
            Directory.CreateDirectory(npcDirectory);
            string file = Path.Combine(npcDirectory, "utf8.txt");
            byte[] preamble = Encoding.UTF8.GetPreamble();
            byte[] body = Encoding.UTF8.GetBytes("[@MAIN]\n#SAY\n你好\n");
            File.WriteAllBytes(file, preamble.Concat(body).ToArray());

            ITextFileProvider provider = new PhysicalTextFileProvider(
                new PhysicalTextFileProviderOptions(root, TxtScriptLayout.LyoCrystal));

            TextFileDefinition definition = Assert.IsType<TextFileDefinition>(provider.GetByKey("NPCs/utf8"));
            Assert.Equal(new[] { "[@MAIN]", "#SAY", "你好", string.Empty }, definition.Lines);
            Assert.Equal("UTF-8 BOM", definition.SourceEncoding);
            Assert.Equal("LF", definition.SourceNewLine);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LyoCrystal布局读取CP936中文并保留CRLF来源信息()
    {
        string root = CreateTemporaryRoot();
        try
        {
            string npcDirectory = Path.Combine(root, "NPCs");
            Directory.CreateDirectory(npcDirectory);
            string file = Path.Combine(npcDirectory, "测试老人.txt");
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            Encoding cp936 = Encoding.GetEncoding(936,
                EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
            File.WriteAllBytes(file, cp936.GetBytes("[@MAIN]\r\n#SAY\r\n欢迎回来\r\n"));

            ITextFileProvider provider = new PhysicalTextFileProvider(
                new PhysicalTextFileProviderOptions(root, TxtScriptLayout.LyoCrystal));

            TextFileDefinition definition = Assert.IsType<TextFileDefinition>(
                provider.GetByKey("NPCs/测试老人"));
            Assert.Equal(new[] { "[@MAIN]", "#SAY", "欢迎回来", string.Empty }, definition.Lines);
            Assert.Equal(Path.GetFullPath(file), definition.SourcePath);
            Assert.Equal("CP936", definition.SourceEncoding);
            Assert.Equal("CRLF", definition.SourceNewLine);
            Assert.Equal(3, definition.GetSourceLineNumber(2));
            Assert.Equal($"{Path.GetFullPath(file)}:3", definition.GetSourceLocation(2));
            Assert.Throws<ArgumentOutOfRangeException>(() => definition.GetSourceLineNumber(4));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTemporaryRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "LyoCrystal-PhysicalTxt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void WriteUtf8(string root, string relativePath, string content)
    {
        string file = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, content, new UTF8Encoding(false, true));
    }

    private static ITextFileProvider CreateCSharpProvider(params TextFileDefinition[] definitions) =>
        new CSharpTextFileProvider(definitions.ToDictionary(item => item.Key, StringComparer.Ordinal));
}
