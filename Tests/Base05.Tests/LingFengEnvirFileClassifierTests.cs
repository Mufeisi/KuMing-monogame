using Microsoft.VisualBasic.FileIO;
using Server.Scripting;
using System.Text;
using Xunit;

namespace Base05.Tests;

[Collection(nameof(PhysicalTextFileProviderCollection))]
public sealed class LingFengEnvirFileClassifierTests
{
    [Fact]
    public void 所有权规则清单完整且只有脚本允许发布()
    {
        IReadOnlyList<IReadOnlyDictionary<string, string>> rows =
            ReadCsv("lingfeng-envir-file-ownership.csv");

        Assert.Equal(11, rows.Count);
        Assert.Equal(rows.Count, rows.Select(row => row["规则ID"]).Distinct(StringComparer.Ordinal).Count());
        int[] priorities = rows
            .Select(row => int.Parse(row["优先级"], System.Globalization.CultureInfo.InvariantCulture))
            .ToArray();
        Assert.Equal(priorities.Order(), priorities);
        Assert.Equal(priorities.Length, priorities.Distinct().Count());
        Assert.Single(rows, row => row["允许脚本发布"] == "是" && row["所有者"] == "脚本");
        Assert.All(rows.Where(row => row["所有者"] != "脚本"), row =>
            Assert.Equal("否", row["允许脚本发布"]));
        Assert.All(rows, row =>
        {
            Assert.Equal("1", row["Schema版本"]);
            Assert.Equal("LFENV09-CLASSIFIER", row["测试编号"]);
            Assert.False(string.IsNullOrWhiteSpace(row["未匹配行为"]));
        });
    }

    [Theory]
    [InlineData("Market_Def/比奇/老兵.txt", LingFengEnvirFileOwner.Script, true, "npcs/比奇/老兵")]
    [InlineData("Market_Def/QFunction-0.txt", LingFengEnvirFileOwner.Script, true, "systemscripts/qfunction-0")]
    [InlineData("QFunction-0.txt", LingFengEnvirFileOwner.Script, true, "systemscripts/qfunction-0")]
    [InlineData("Npc_def/比奇/老兵.txt", LingFengEnvirFileOwner.Script, true, "npcdefs/比奇/老兵")]
    [InlineData("QuestDiary/主线/第一章.txt", LingFengEnvirFileOwner.Script, true, "questdiary/主线/第一章")]
    [InlineData("MapQuest_def/QManage.txt", LingFengEnvirFileOwner.Script, true, "systemscripts/qmanage")]
    [InlineData("Robot_def/ROBOTMANAGE.TXT", LingFengEnvirFileOwner.Script, true, "systemscripts/robotmanage")]
    [InlineData("Robot_def/AUTORUNROBOT.TXT", LingFengEnvirFileOwner.Script, true, "systemscripts/autorunrobot")]
    [InlineData("DeFines/公共/变量.txt", LingFengEnvirFileOwner.Script, true, "defines/公共/变量")]
    [InlineData("UserData/UserData.dat", LingFengEnvirFileOwner.RuntimeData, false, null)]
    [InlineData("Market_Saved/摆摊记录.txt", LingFengEnvirFileOwner.RuntimeData, false, null)]
    [InlineData("MonItems/稻草人.txt", LingFengEnvirFileOwner.DomainConfiguration, false, null)]
    [InlineData("MapInfo.txt", LingFengEnvirFileOwner.DomainConfiguration, false, null)]
    [InlineData("MonIcons/怪物图标.txt", LingFengEnvirFileOwner.ClientContract, false, null)]
    [InlineData("QuestDiary/旧脚本.bak", LingFengEnvirFileOwner.BackupOrArchive, false, null)]
    [InlineData("QuestDiary/清理数据.bat", LingFengEnvirFileOwner.ExecutableArtifact, false, null)]
    [InlineData("QuestDiary/说明.xlsx", LingFengEnvirFileOwner.Documentation, false, null)]
    [InlineData("未知组件.dll", LingFengEnvirFileOwner.ExecutableArtifact, false, null)]
    [InlineData("未知组件.xyz", LingFengEnvirFileOwner.Unassigned, false, null)]
    public void 分类结果唯一且发布策略由所有权决定(
        string relativePath,
        LingFengEnvirFileOwner owner,
        bool mayPublishAsScript,
        string? logicKey)
    {
        LingFengEnvirFileClassification result = LingFengEnvirFileClassifier.Classify(relativePath);

        Assert.Equal(owner, result.Owner);
        Assert.Equal(mayPublishAsScript, result.MayPublishAsScript);
        Assert.Equal(logicKey, result.LogicKey);
        Assert.False(string.IsNullOrWhiteSpace(result.RuleId));
    }

    [Fact]
    public void 物理Provider只发布脚本所有权且未知文件阻断候选()
    {
        string root = TemporaryRoot();
        try
        {
            Write(root, "Market_Def/比奇/老兵.txt", "脚本");
            Write(root, "QFunction-0.txt", "根级回退");
            Write(root, "Market_Def/QFunction-0.txt", "标准系统入口");
            Write(root, "UserData/运行数据.txt", "不可覆盖");
            Write(root, "MonItems/稻草人.txt", "1/1 金币 1");
            Write(root, "QuestDiary/旧脚本.bak", "备份");

            var provider = new PhysicalTextFileProvider(
                new PhysicalTextFileProviderOptions(root, TxtScriptLayout.LingFeng));

            Assert.Equal("脚本", Assert.Single(provider.GetByKey("NPCs/比奇/老兵")!.Lines));
            Assert.Equal("标准系统入口", Assert.Single(
                provider.GetByKey("SystemScripts/QFunction-0")!.Lines));
            Assert.Null(provider.GetByKey("NPCs/QFunction-0"));
            Assert.Equal(2, provider.GetAll().Count);

            File.Delete(Path.Combine(root, "Market_Def", "QFunction-0.txt"));
            provider = new PhysicalTextFileProvider(
                new PhysicalTextFileProviderOptions(root, TxtScriptLayout.LingFeng));
            Assert.Equal("根级回退", Assert.Single(
                provider.GetByKey("SystemScripts/QFunction-0")!.Lines));

            Write(root, "未知组件.xyz", "不可识别");
            InvalidDataException error = Assert.Throws<InvalidDataException>(() =>
                new PhysicalTextFileProvider(
                    new PhysicalTextFileProviderOptions(root, TxtScriptLayout.LingFeng)));
            Assert.Contains("未归属", error.Message, StringComparison.Ordinal);
            Assert.Contains("未知组件.xyz", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void 本机代表Envir每个文件都有唯一所有权且非脚本不可发布()
    {
        const string corpusRoot = @"D:\ChuanQi\服务端";
        if (!Directory.Exists(corpusRoot))
            throw Xunit.Sdk.SkipException.ForSkip("本机未挂载 D:\\ChuanQi\\服务端 权威语料。");

        foreach (IReadOnlyDictionary<string, string> row in ReadRepresentativeRoots())
        {
            string root = Path.Combine(corpusRoot, row["相对路径"]);
            foreach (string file in Directory.EnumerateFiles(root, "*", new EnumerationOptions
                     {
                         RecurseSubdirectories = true,
                         IgnoreInaccessible = false,
                         AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint
                     }))
            {
                string relative = Path.GetRelativePath(root, file);
                LingFengEnvirFileClassification result = LingFengEnvirFileClassifier.Classify(relative);
                Assert.True(result.Owner != LingFengEnvirFileOwner.Unassigned,
                    $"{row["根ID"]}:{relative}");
                Assert.Equal(result.Owner == LingFengEnvirFileOwner.Script, result.MayPublishAsScript);
                Assert.Equal(result.MayPublishAsScript, result.LogicKey is not null);
                string normalized = relative.Replace('\\', '/');
                if (normalized.Equals("QFunction-0.txt", StringComparison.OrdinalIgnoreCase) ||
                    normalized.Equals("Market_Def/QFunction-0.txt", StringComparison.OrdinalIgnoreCase))
                {
                    Assert.Equal(LingFengEnvirFileOwner.Script, result.Owner);
                    Assert.Equal("systemscripts/qfunction-0", result.LogicKey);
                }
            }
        }
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, string>> ReadRepresentativeRoots()
        => ReadCsv("lingfeng-envir-roots.csv")
            .Where(row => row["代表样本"] == "是")
            .ToArray();

    private static IReadOnlyList<IReadOnlyDictionary<string, string>> ReadCsv(string fileName)
    {
        string path = Path.Combine(RepositoryRoot(), "Docs", "generated", "scripting", fileName);
        using var parser = new TextFieldParser(path) { TextFieldType = FieldType.Delimited, HasFieldsEnclosedInQuotes = true };
        parser.SetDelimiters(",");
        string[] headers = parser.ReadFields()!;
        var rows = new List<IReadOnlyDictionary<string, string>>();
        while (!parser.EndOfData)
        {
            string[] fields = parser.ReadFields()!;
            var row = headers.Select((header, index) => (header, fields[index]))
                .ToDictionary(item => item.header, item => item.Item2, StringComparer.Ordinal);
            rows.Add(row);
        }
        return rows;
    }

    private static string TemporaryRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "LyoCrystal-LFENV09-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void Write(string root, string relativePath, string text)
    {
        string file = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, text, new UTF8Encoding(false));
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
}
