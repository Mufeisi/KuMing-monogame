using System.Text;
using Server;
using Server.MirDatabase;
using Server.MirEnvir;
using Server.MirObjects;
using Server.Scripting;
using Server.Scripting.Variables;
using Xunit;

namespace Base05.Tests;

[Collection(nameof(PhysicalTextFileProviderCollection))]
public sealed class LingFengTxtSyntaxGoldenTests
{
    [Fact]
    public void 参数Tokenizer支持中文空参数引号空格和安全转义()
    {
        Assert.True(TxtScriptTokenizer.TryTokenize(
            "GIVE \"回城 卷\" 1 \"\" \"含\\\"引号\" \"QuestDiary\\test dir\\new.txt\"",
            out string[] tokens,
            out string error), error);

        Assert.Equal(new[] { "GIVE", "回城 卷", "1", "", "含\"引号", @"QuestDiary\test dir\new.txt" }, tokens);
    }

    [Fact]
    public void 参数Tokenizer拒绝未闭合引号()
    {
        Assert.False(TxtScriptTokenizer.TryTokenize("MOV S1 \"未闭合", out _, out string error));
        Assert.Contains("右侧双引号", error, StringComparison.Ordinal);
    }

    [Fact]
    public void 参数Tokenizer忽略旧脚本行首DOS控制字符()
    {
        Assert.True(TxtScriptTokenizer.TryTokenize("\u001a", out string[] eofTokens, out string eofError), eofError);
        Assert.Empty(eofTokens);

        Assert.True(TxtScriptTokenizer.TryTokenize(
            "\u001f[@STDMODEFUNC209]", out string[] labelTokens, out string labelError), labelError);
        Assert.Equal(new[] { "[@STDMODEFUNC209]" }, labelTokens);
    }

    [Fact]
    public void 参数Tokenizer忽略参数边界后的行尾注释但保留正文分号()
    {
        Assert.True(TxtScriptTokenizer.TryTokenize(
            "SetOnTimer 1 1   ;1秒级定时器",
            out string[] tokens,
            out string error), error);
        Assert.Equal(new[] { "SetOnTimer", "1", "1" }, tokens);

        Assert.True(TxtScriptTokenizer.TryTokenize(
            "MOV S1 \"正文;仍保留\"",
            out tokens,
            out error), error);
        Assert.Equal(new[] { "MOV", "S1", "正文;仍保留" }, tokens);
    }

    [Fact]
    public void 参数Tokenizer保留引号外翎风反斜杠路径()
    {
        Assert.True(TxtScriptTokenizer.TryTokenize(
            @"CALL QuestDiary\test.txt @入口",
            out string[] tokens,
            out string error), error);

        Assert.Equal(new[] { "CALL", @"QuestDiary\test.txt", "@入口" }, tokens);
    }

    [Fact]
    public void 动作解析器使用Tokenizer结果而非再次截断带空格字符串()
    {
        var segment = new NPCSegment(
            new NPCPage("[@MAIN]"), new List<string>(), new List<string>(),
            new List<string>(), new List<string>(), new List<string>());

        segment.ParseAct(segment.ActList, "MOV S1 \"中文 参数且含\\\"引号\"");

        NPCActions action = Assert.Single(segment.ActList);
        Assert.Equal(ActionType.VariableMutate, action.Type);
        Assert.Equal(new[] { "S1", "MOV", "中文 参数且含\"引号" }, action.Params);
    }

    [Fact]
    public void 缩进大小写中文和五种段落边界形成稳定解析结果()
    {
        using ParsedNpc fixture = Parse(
            "   [@main]   \n" +
            "   ; 缩进注释不会进入正文\n" +
            "   #if\n" +
            "   LEVEL > 1\n" +
            "   #act\n" +
            "   SET [1] 1\n" +
            "   #say\n" +
            "欢迎，勇士。<下一页/@next>\n" +
            "   #elseact\n" +
            "   SET [2] 1\n" +
            "   #elsesay\n" +
            "等级不足。\n" +
            "   [@next]   \n" +
            "#SAY\n" +
            "结束。\n");

        NPCPage main = fixture.Script.NPCPages.Single(page =>
            page.Key.Equals("[@MAIN]", StringComparison.OrdinalIgnoreCase));
        NPCSegment segment = Assert.Single(main.SegmentList);
        Assert.Single(segment.CheckList);
        Assert.Single(segment.ActList);
        Assert.Single(segment.ElseActList);
        Assert.Equal(new[] { "欢迎，勇士。<下一页/@next>" }, segment.Say);
        Assert.Equal(new[] { "等级不足。" }, segment.ElseSay);
        Assert.Contains("[@next]", segment.Buttons, StringComparer.OrdinalIgnoreCase);

        NPCPage next = fixture.Script.NPCPages.Single(page =>
            page.Key.Equals("[@NEXT]", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("结束。", Assert.Single(Assert.Single(next.SegmentList).Say));
    }

    [Fact]
    public void 对话正文保留前导空格而命令行仅规范化结构空白()
    {
        using ParsedNpc fixture = Parse(
            "[@MAIN]\n" +
            "  #SAY  \n" +
            "  正文缩进保留  \n" +
            "  #ACT\n" +
            "  SET [3] 1  \n");

        NPCSegment segment = Assert.Single(Assert.Single(fixture.Script.NPCPages).SegmentList);
        Assert.Equal("  正文缩进保留", Assert.Single(segment.Say));
        Assert.Single(segment.ActList);
    }

    [Fact]
    public void 未知段落指令在发布前产生稳定文件行号诊断()
    {
        var definition = new TextFileDefinition("NPCs/错误", "D:/受控/NPCs/错误.txt", "UTF-8", "LF")
            .AddLines(new[] { "[@MAIN]", "#SAY", "可用", "#UNKNOWN", "不应发布" });
        ITextFileProvider provider = new CSharpTextFileProvider(
            new Dictionary<string, TextFileDefinition>(StringComparer.Ordinal)
            {
                [definition.Key] = definition
            });

        IReadOnlyList<string> errors = TxtScriptSnapshotValidator.Validate(provider);

        string error = Assert.Single(errors);
        Assert.Contains("TXT-SNAPSHOT-006", error, StringComparison.Ordinal);
        Assert.Contains("错误.txt:4", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void 翎风While结构缺失配对时严格候选失败关闭()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        bool oldStrict = Settings.TxtScriptsStrictCompatibility;
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            Settings.TxtScriptsStrictCompatibility = true;
            var definition = new TextFileDefinition(
                    "NPCs/循环错误", "D:/受控/NPCs/循环错误.txt", "UTF-8", "LF")
                .AddLines(new[]
                {
                    "[@MAIN]", "#ACT", "ENDWHILE", "WHILE N$次数 < 3", "MOV N$次数 1"
                });
            ITextFileProvider provider = new CSharpTextFileProvider(
                new Dictionary<string, TextFileDefinition>(StringComparer.Ordinal)
                {
                    [definition.Key] = definition
                });

            IReadOnlyList<string> errors = TxtScriptSnapshotValidator.Validate(provider);

            Assert.Equal(2, errors.Count(error =>
                error.StartsWith("TXT-SNAPSHOT-018", StringComparison.Ordinal)));
        }
        finally
        {
            Settings.TxtScriptsStrictCompatibility = oldStrict;
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void Call支持带空格引号和QuestDiary跨目录脚本()
    {
        using ParsedNpc fixture = Parse(
            "[@MAIN]\n#ACT\nCALL \"QuestDiary/公共 库.txt\"\n",
            new Dictionary<string, string>
            {
                ["QuestDiary/公共 库.txt"] = "[@MAIN]\n#SAY\n跨目录调用成功"
            });

        NPCSegment segment = Assert.Single(Assert.Single(fixture.Script.NPCPages).SegmentList);
        NPCActions action = Assert.Single(segment.ActList);
        Assert.Equal(ActionType.Call, action.Type);
        Assert.True(int.TryParse(Assert.Single(action.Params), out int calledScriptId));
        NPCScript called = NPCScript.Get(calledScriptId);
        Assert.Equal("questdiary/公共 库", called.FileName);
        Assert.Equal("跨目录调用成功",
            Assert.Single(Assert.Single(called.NPCPages).SegmentList).Say.Single());
    }

    [Fact]
    public void 翎风根相对井号Call保留目标脚本标签()
    {
        using ParsedNpc fixture = Parse(
            "[@MAIN]\n#ACT\n#CALL [\\命格系统\\事件.txt] @命格入口\n",
            new Dictionary<string, string>
            {
                ["QuestDiary/命格系统/事件.txt"] = "[@命格入口]\n#SAY\n命格调用成功"
            });

        NPCSegment segment = Assert.Single(Assert.Single(fixture.Script.NPCPages).SegmentList);
        NPCActions action = Assert.Single(segment.ActList);
        Assert.Equal(ActionType.Call, action.Type);
        Assert.Equal("@命格入口", action.Params[1]);

        var player = new PlayerObject
        {
            Info = new CharacterInfo { Name = "命格调用人物" },
            Account = new AccountInfo(),
            NPCObjectID = 123
        };
        Assert.True(segment.Check(player));
        DelayedAction delayed = Assert.Single(player.ActionList);
        Assert.Equal(DelayedType.NPC, delayed.Type);
        Assert.Equal("[@命格入口]", delayed.Params[2]);
    }

    [Fact]
    public void 翎风Or条件任一命中即执行动作()
    {
        using ParsedNpc fixture = Parse(
            "[@MAIN]\n#OR\nCHECKLEVEL == 1\nCHECKLEVEL == 2\n#ACT\nGIVEGOLD 7\n");
        NPCSegment segment = Assert.Single(Assert.Single(fixture.Script.NPCPages).SegmentList);
        Assert.True(segment.MatchAnyCheck);
        Assert.Equal(2, segment.CheckList.Count);
        Assert.Single(segment.ActList);
        var player = new PlayerObject
        {
            Info = new CharacterInfo { Name = "命格条件人物", Level = 2 },
            Account = new AccountInfo()
        };

        Assert.True(segment.Check(player));
        Assert.Equal(7u, player.Account.Gold);
    }

    [Fact]
    public void 翎风If数量条件满足指定个数才进入成功分支()
    {
        using ParsedNpc fixture = Parse(
            "[@MAIN]\n#IF(2)\nCHECKLEVEL == 2\nCHECKLEVEL >= 2\nCHECKLEVEL == 3\n" +
            "#ACT\nGIVEGOLD 11\n#ELSEACT\nGIVEGOLD 1\n");
        NPCSegment segment = Assert.Single(Assert.Single(fixture.Script.NPCPages).SegmentList);
        Assert.Equal(2, segment.RequiredCheckMatches);
        Assert.False(segment.MatchAnyCheck);

        var matching = new PlayerObject
        {
            Info = new CharacterInfo { Name = "多条件命中人物", Level = 2 },
            Account = new AccountInfo()
        };
        Assert.True(segment.Check(matching));
        Assert.Equal(11u, matching.Account.Gold);

        var failing = new PlayerObject
        {
            Info = new CharacterInfo { Name = "多条件失败人物", Level = 1 },
            Account = new AccountInfo()
        };
        Assert.False(segment.Check(failing));
        Assert.Equal(1u, failing.Account.Gold);
    }

    [Fact]
    public void 物理Txt的命名变量声明在CSharp关闭时可供Npc动作使用()
    {
        using ParsedNpc fixture = Parse(
            "[@MAIN]\n#ACT\nMOV P.Rate 2.5\n#SAY\n完成\n",
            new Dictionary<string, string>
            {
                ["Variables/Declarations.txt"] = "VAR Decimal P Rate DEFAULT 1.25"
            });

        ScriptVariableDeclaration declaration = Envir.Main.CSharpScripts
            .EffectiveVariableDeclarations.GetRequired(ScriptVariableScope.P, "Rate");
        Assert.Equal(ScriptVariableKind.Decimal, declaration.Kind);
        Assert.Equal(1.25m, declaration.DefaultValue.Decimal);
        NPCActions action = Assert.Single(
            Assert.Single(Assert.Single(fixture.Script.NPCPages).SegmentList).ActList);
        Assert.Equal(ActionType.VariableMutate, action.Type);
        Assert.Equal(new[] { "P.Rate", "MOV", "2.5" }, action.Params);
    }

    [Fact]
    public void 跳转CallBreak延迟和GotoLabel参数形成稳定动作()
    {
        using ParsedNpc fixture = Parse(
            "[@MAIN]\n#ACT\n" +
            "GOTO @NEXT\n" +
            "DELAYGOTO 3 @NEXT\n" +
            "GOTOLABEL 3 @NEXT 12\n" +
            "GOTOLABEL 8 @NEXT 100 120 5 1 S1 S2\n" +
            "BREAK\n" +
            "CALL \"QuestDiary/公共.txt\"\n" +
            "[@NEXT]\n#SAY\n完成\n",
            new Dictionary<string, string>
            {
                ["QuestDiary/公共.txt"] = "[@MAIN]\n#SAY\n公共"
            });

        NPCSegment segment = fixture.Script.NPCPages
            .Single(page => page.Key.Equals("[@MAIN]", StringComparison.OrdinalIgnoreCase))
            .SegmentList.Single();

        Assert.Equal(
            new[] { ActionType.Goto, ActionType.DelayGoto, ActionType.GotoLabel, ActionType.GotoLabel, ActionType.Break, ActionType.Call },
            segment.ActList.Select(action => action.Type));
        Assert.Equal(new[] { "3", "@NEXT", "12" }, segment.ActList[2].Params);
        Assert.Equal(new[] { "8", "@NEXT", "100", "120", "5", "1", "S1", "S2" },
            segment.ActList[3].Params);
    }

    [Fact]
    public void 物理文件显式行延续合并为逻辑行且诊断仍指向起始原行()
    {
        string root = Path.Combine(Path.GetTempPath(), "LyoCrystal-TxtContinuation-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "NPCs"));
            File.WriteAllText(
                Path.Combine(root, "NPCs", "延续.txt"),
                "[@MAIN]\n#ACT\nMOV S1 \"中文 \\\n续行\"\n#SAY\n完成",
                new UTF8Encoding(false, true));
            ITextFileProvider provider = new PhysicalTextFileProvider(
                new PhysicalTextFileProviderOptions(root, TxtScriptLayout.LyoCrystal));

            TextFileDefinition definition = provider.GetByKey("NPCs/延续");
            Assert.Equal("MOV S1 \"中文  续行\"", definition.Lines[2]);
            Assert.Equal(3, definition.GetSourceLineNumber(2));
            Assert.Equal(5, definition.GetSourceLineNumber(3));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static ParsedNpc Parse(string content, IReadOnlyDictionary<string, string> additionalFiles = null)
    {
        string root = Path.Combine(Path.GetTempPath(), "LyoCrystal-TxtSyntax-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "NPCs"));
        string fileName = "语法黄金_" + Guid.NewGuid().ToString("N");
        File.WriteAllText(Path.Combine(root, "NPCs", fileName + ".txt"), content, new UTF8Encoding(false, true));
        if (additionalFiles != null)
        {
            foreach ((string relativePath, string fileContent) in additionalFiles)
            {
                string path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, fileContent, new UTF8Encoding(false, true));
            }
        }
        return new ParsedNpc(root, fileName);
    }

    private sealed class ParsedNpc : IDisposable
    {
        private readonly bool _oldTxtEnabled = Settings.TxtScriptsEnabled;
        private readonly bool _oldCSharpEnabled = Settings.CSharpScriptsEnabled;
        private readonly string _oldPath = Settings.TxtScriptsPath;
        private readonly TxtScriptLayout _oldLayout = Settings.TxtScriptsLayout;
        private readonly long _oldMaxBytes = Settings.TxtScriptsMaxFileBytes;
        private readonly string _root;
        private readonly HashSet<int> _existingScriptIds;

        public ParsedNpc(string root, string fileName)
        {
            _root = root;
            _existingScriptIds = Envir.Main.Scripts.Keys.ToHashSet();
            Settings.CSharpScriptsEnabled = false;
            Settings.TxtScriptsEnabled = true;
            Settings.TxtScriptsPath = root;
            Settings.TxtScriptsLayout = TxtScriptLayout.LyoCrystal;
            Settings.TxtScriptsMaxFileBytes = 4096;
            Envir.Main.ApplyPhysicalTextFileDefinitions();
            Script = NPCScript.GetOrAdd(uint.MaxValue - 1, fileName, NPCScriptType.Normal);
        }

        public NPCScript Script { get; }

        public void Dispose()
        {
            foreach (int scriptId in Envir.Main.Scripts.Keys.Where(id => !_existingScriptIds.Contains(id)).ToArray())
                Envir.Main.Scripts.Remove(scriptId);
            Settings.TxtScriptsEnabled = false;
            Envir.Main.ApplyPhysicalTextFileDefinitions();
            Settings.TxtScriptsEnabled = _oldTxtEnabled;
            Settings.CSharpScriptsEnabled = _oldCSharpEnabled;
            Settings.TxtScriptsPath = _oldPath;
            Settings.TxtScriptsLayout = _oldLayout;
            Settings.TxtScriptsMaxFileBytes = _oldMaxBytes;
            Directory.Delete(_root, true);
        }
    }
}
