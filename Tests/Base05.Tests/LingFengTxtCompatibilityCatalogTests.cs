using Microsoft.VisualBasic.FileIO;
using Xunit;

namespace Base05.Tests;

public sealed class LingFengTxtCompatibilityCatalogTests
{
    private static readonly HashSet<string> AllowedStatuses =
        new(StringComparer.Ordinal) { "A", "B", "C", "D", "E", "X", "?" };

    [Fact]
    public void 翎风主题清单覆盖CHM目录并保持分类基线()
    {
        IReadOnlyList<IReadOnlyDictionary<string, string>> rows = ReadCsv("lingfeng-txt-topics.csv");

        Assert.Equal(1012, rows.Count);
        Assert.Equal(1012, rows.Select(row => row["ID"]).Distinct(StringComparer.Ordinal).Count());

        Dictionary<string, int> categories = rows
            .GroupBy(row => row["类别"], StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        Assert.Equal(342, categories["动作"]);
        Assert.Equal(136, categories["检测"]);
        Assert.Equal(62, categories["触发"]);
        Assert.Equal(165, categories["NPC代码"]);
        Assert.Equal(126, categories["系统功能"]);
        Assert.Equal(93, categories["示例"]);
        Assert.Equal(49, categories["资料"]);
        Assert.Equal(17, categories["数据"]);
        Assert.Equal(16, categories["问答"]);
        Assert.Equal(6, categories["根主题"]);
    }

    [Fact]
    public void 翎风兼容清单状态唯一且已支持条目具备完整证据链()
    {
        IReadOnlyList<IReadOnlyDictionary<string, string>> rows = ReadCsv("lingfeng-txt-compatibility.csv");

        Assert.True(rows.Count >= 453, $"兼容候选条目异常减少：{rows.Count}");
        Assert.Equal(rows.Count, rows.Select(row => row["ID"]).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(rows.Count, rows
            .Select(row => $"{row["类别"]}|{row["翎风名称"]}")
            .Distinct(StringComparer.OrdinalIgnoreCase).Count());

        foreach (IReadOnlyDictionary<string, string> row in rows)
        {
            Assert.Contains(row["兼容状态"], AllowedStatuses);
            Assert.False(string.IsNullOrWhiteSpace(row["来源相对页"]));
            Assert.False(string.IsNullOrWhiteSpace(row["说明书版本"]));

            if (row["兼容状态"] is "A" or "B" or "C")
            {
                Assert.False(string.IsNullOrWhiteSpace(row["当前实现位置"]), EvidenceMessage(row));
                Assert.False(string.IsNullOrWhiteSpace(row["测试编号"]), EvidenceMessage(row));
                Assert.False(string.IsNullOrWhiteSpace(row["说明书页面"]), EvidenceMessage(row));
                Assert.False(string.IsNullOrWhiteSpace(row["最后复核日期"]), EvidenceMessage(row));
            }
        }
    }

    [Fact]
    public void 生产候选兼容状态分布与版本声明一致()
    {
        IReadOnlyList<IReadOnlyDictionary<string, string>> rows = ReadCsv("lingfeng-txt-compatibility.csv");
        Dictionary<string, int> statuses = rows
            .GroupBy(row => row["兼容状态"], StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        Assert.Equal(496, rows.Count);
        Assert.Equal(51, statuses["B"]);
        Assert.Equal(15, statuses["C"]);
        Assert.Equal(322, statuses["D"]);
        Assert.Equal(83, statuses["E"]);
        Assert.Equal(25, statuses["X"]);
        Assert.False(statuses.ContainsKey("?"));
    }

    [Fact]
    public void 触发类候选已完成正文核对且语义卡完整()
    {
        IReadOnlyList<IReadOnlyDictionary<string, string>> triggers =
            ReadCsv("lingfeng-txt-compatibility.csv")
                .Where(row => row["类别"] == "触发")
                .ToArray();

        Assert.Equal(112, triggers.Count);
        Assert.DoesNotContain(triggers, row => row["兼容状态"] == "?");

        string[] semanticFields =
        [
            "脚本入口", "对象上下文", "当前实现位置", "已知差异",
            "客户端依赖", "数据依赖", "说明书页面", "负责人", "最后复核日期"
        ];
        foreach (IReadOnlyDictionary<string, string> row in triggers)
        {
            foreach (string field in semanticFields)
                Assert.False(string.IsNullOrWhiteSpace(row[field]), $"{row["ID"]} 缺少 {field}。 ");
        }
    }

    [Fact]
    public void 动作类全部二百四十九项已完成正文核对且语义卡完整()
    {
        IReadOnlyList<IReadOnlyDictionary<string, string>> actions =
            ReadCsv("lingfeng-txt-compatibility.csv")
                .Where(row => row["类别"] == "动作" &&
                    string.CompareOrdinal(row["ID"], "LF-CMD-0110") >= 0 &&
                    string.CompareOrdinal(row["ID"], "LF-CMD-0358") <= 0)
                .ToArray();

        Assert.Equal(249, actions.Count);
        Assert.DoesNotContain(actions, row => row["兼容状态"] == "?");
        AssertSemanticCards(actions);
    }

    [Fact]
    public void 检测类全部九十五项已完成正文核对且语义卡完整()
    {
        IReadOnlyList<IReadOnlyDictionary<string, string>> checks =
            ReadCsv("lingfeng-txt-compatibility.csv")
                .Where(row => row["类别"] == "检测" &&
                    string.CompareOrdinal(row["ID"], "LF-CMD-0359") >= 0 &&
                    string.CompareOrdinal(row["ID"], "LF-CMD-0453") <= 0)
                .ToArray();

        Assert.Equal(95, checks.Count);
        Assert.DoesNotContain(checks, row => row["兼容状态"] == "?");
        AssertSemanticCards(checks);
    }

    private static void AssertSemanticCards(
        IEnumerable<IReadOnlyDictionary<string, string>> rows)
    {
        string[] semanticFields =
        [
            "脚本入口", "对象上下文", "当前实现位置", "已知差异",
            "客户端依赖", "数据依赖", "说明书页面", "负责人", "最后复核日期"
        ];
        foreach (IReadOnlyDictionary<string, string> row in rows)
        {
            foreach (string field in semanticFields)
                Assert.False(string.IsNullOrWhiteSpace(row[field]), $"{row["ID"]} 缺少 {field}。 ");
        }
    }

    private static string EvidenceMessage(IReadOnlyDictionary<string, string> row) =>
        $"{row["ID"]} {row["翎风名称"]} 标记为 {row["兼容状态"]}，但证据链不完整。";

    private static IReadOnlyList<IReadOnlyDictionary<string, string>> ReadCsv(string fileName)
    {
        string path = Path.Combine(FindRepositoryRoot(AppContext.BaseDirectory), "Docs", "generated", "scripting", fileName);
        using var parser = new TextFieldParser(path)
        {
            TextFieldType = FieldType.Delimited,
            HasFieldsEnclosedInQuotes = true,
            TrimWhiteSpace = false
        };
        parser.SetDelimiters(",");

        string[] headers = parser.ReadFields() ?? throw new InvalidDataException($"{fileName} 缺少表头。");
        var rows = new List<IReadOnlyDictionary<string, string>>();
        while (!parser.EndOfData)
        {
            string[] fields = parser.ReadFields() ?? Array.Empty<string>();
            Assert.Equal(headers.Length, fields.Length);
            rows.Add(headers.Select((header, index) => (header, value: fields[index]))
                .ToDictionary(item => item.header, item => item.value, StringComparer.Ordinal));
        }

        return rows;
    }

    private static string FindRepositoryRoot(string start)
    {
        DirectoryInfo? current = new(start);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "global.json"))) return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("无法定位仓库根目录。");
    }
}
