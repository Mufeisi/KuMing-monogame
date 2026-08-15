using System.Text;
using Server;
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
