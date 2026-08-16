using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.VisualBasic.FileIO;
using Xunit;

namespace Base05.Tests;

public sealed class LingFengEnvirCorpusCatalogTests
{
    private static readonly HashSet<string> SymbolStatuses =
        new(StringComparer.Ordinal) { "B", "C", "D", "E", "X" };

    [Fact]
    public void Envir画像覆盖全部五十三个根并具备可复核摘要()
    {
        IReadOnlyList<IReadOnlyDictionary<string, string>> rows =
            ReadCsv("lingfeng-envir-roots.csv");

        Assert.Equal(53, rows.Count);
        Assert.Equal(53, rows.Select(row => row["根ID"]).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(53, rows.Select(row => row["相对路径"]).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(68140, rows.Sum(row => int.Parse(row["文件数"], System.Globalization.CultureInfo.InvariantCulture)));

        foreach (IReadOnlyDictionary<string, string> row in rows)
        {
            Assert.Equal("1", row["Schema版本"]);
            Assert.False(string.IsNullOrWhiteSpace(row["版本家族"]));
            Assert.Contains(row["角色"], new[] { "完整版本", "变体", "覆盖包", "提取包" });
            Assert.Matches("^[0-9A-F]{64}$", row["清单SHA256"]);
            Assert.Matches("^[0-9A-F]{64}$", row["内容SHA256"]);
            Assert.True(long.Parse(row["总字节"], System.Globalization.CultureInfo.InvariantCulture) >= 0);
            Assert.True(int.Parse(row["文件数"], System.Globalization.CultureInfo.InvariantCulture) > 0);
            Assert.False(string.IsNullOrWhiteSpace(row["编码摘要"]));
            int textFileCount = int.Parse(row["TXT数"], System.Globalization.CultureInfo.InvariantCulture) +
                                int.Parse(row["INI数"], System.Globalization.CultureInfo.InvariantCulture);
            int classifiedTextCount = int.Parse(row["UTF8文本数"], System.Globalization.CultureInfo.InvariantCulture) +
                                      int.Parse(row["UTF8BOM数"], System.Globalization.CultureInfo.InvariantCulture) +
                                      int.Parse(row["UTF16文本数"], System.Globalization.CultureInfo.InvariantCulture) +
                                      int.Parse(row["CP936候选数"], System.Globalization.CultureInfo.InvariantCulture) +
                                      int.Parse(row["二进制文本数"], System.Globalization.CultureInfo.InvariantCulture);
            Assert.Equal(textFileCount, classifiedTextCount);
        }

        Assert.Contains(rows, row => row["相对路径"] == @"无尽\MirServer\Mir200\Envir" && row["文件数"] == "6699");
        Assert.Contains(rows, row => row["相对路径"] == @"01酷明传奇\MirServer_01\Mir200\Envir" && row["文件数"] == "2370");
        Assert.True(rows.Select(row => row["版本家族"]).Distinct(StringComparer.Ordinal).Count() >= 20);
        Assert.True(rows.Count(row => row["代表样本"] == "是") >= 20);
    }

    [Fact]
    public void Envir画像每个家族恰有一个代表样本且精确重复组可追踪()
    {
        IReadOnlyList<IReadOnlyDictionary<string, string>> rows =
            ReadCsv("lingfeng-envir-roots.csv");

        foreach (IGrouping<string, IReadOnlyDictionary<string, string>> family in
                 rows.GroupBy(row => row["版本家族"], StringComparer.Ordinal))
            Assert.Single(family, row => row["代表样本"] == "是");

        foreach (IGrouping<string, IReadOnlyDictionary<string, string>> duplicate in
                 rows.GroupBy(row => row["内容SHA256"], StringComparer.Ordinal))
        {
            string expected = duplicate.Count() > 1 ? "是" : "否";
            Assert.All(duplicate, row => Assert.Equal(expected, row["精确重复"]));
        }
    }

    [Fact]
    public void 服务器常量目录覆盖附件和真实语料且没有未知状态()
    {
        IReadOnlyList<IReadOnlyDictionary<string, string>> rows =
            ReadCsv("lingfeng-server-symbols.csv");

        Assert.Equal(905, rows.Count);
        Assert.Equal(rows.Count, rows.Select(row => row["ID"]).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(rows.Count, rows
            .Select(row => $"{row["符号种类"]}|{row["规范名称"]}")
            .Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(281, rows.Count(row => row["符号种类"] == "附件原文" && row["附件出现"] == "是"));
        Assert.Equal(513, rows.Count(row => row["真实语料次数"] != "0"));
        Assert.Equal(new[] { "B", "D", "X" }, rows
            .Select(row => row["兼容状态"])
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal));

        foreach (IReadOnlyDictionary<string, string> row in rows)
        {
            Assert.Equal("1", row["Schema版本"]);
            Assert.Contains(row["兼容状态"], SymbolStatuses);
            Assert.DoesNotContain("未知", row["类别"], StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(row["所需上下文"]));
            Assert.False(string.IsNullOrWhiteSpace(row["无上下文结果"]));
            Assert.False(string.IsNullOrWhiteSpace(row["可用脚本入口"]));
            Assert.False(string.IsNullOrWhiteSpace(row["触发时点"]));
            Assert.False(string.IsNullOrWhiteSpace(row["当前实现"]));
            Assert.False(string.IsNullOrWhiteSpace(row["已知差异或实施结论"]));
            Assert.False(string.IsNullOrWhiteSpace(row["来源"]));
            Assert.Equal("LFENV-CATALOG-001", row["测试编号"]);
            Assert.False(string.IsNullOrWhiteSpace(row["最后复核日期"]));
        }

        AssertSymbol(rows, "直接", "USERNAME", "人物", "24581");
        AssertSymbol(rows, "直接", "KILLMONNAME", "战斗事件", "4183");
        AssertSymbol(rows, "函数", "STR()", "变量表达式", "507158");
        AssertSymbol(rows, "索引", "BOXITEM[].NAME", "物品事件", "5360");
    }

    [Fact]
    public void 敏感服务器常量默认拒绝且当前实现映射不会冒充完整兼容()
    {
        IReadOnlyList<IReadOnlyDictionary<string, string>> rows =
            ReadCsv("lingfeng-server-symbols.csv");

        foreach (string symbol in new[] { "PASSWORD", "MACHINEID", "GAMEDIRECTORY", "M2DIRECTORY" })
        {
            IReadOnlyDictionary<string, string> row = Assert.Single(rows,
                item => item["符号种类"] == "直接" && item["规范名称"] == symbol);
            Assert.Equal("X", row["兼容状态"]);
            Assert.Equal("敏感", row["安全等级"]);
        }

        IReadOnlyDictionary<string, string> username = Assert.Single(rows,
            item => item["符号种类"] == "直接" && item["规范名称"] == "USERNAME");
        Assert.Contains("LingFengP0ServerSymbols", username["当前实现"], StringComparison.Ordinal);
        Assert.Equal("B", username["兼容状态"]);
        Assert.Contains("LFENV-05", username["已知差异或实施结论"], StringComparison.Ordinal);

        foreach (string symbol in Enumerable.Range(0, 10).Select(index => $"BANKACCOUNT{index}").Append("QQ"))
        {
            IReadOnlyDictionary<string, string> row = Assert.Single(rows,
                item => item["符号种类"] == "直接" && item["规范名称"] == symbol);
            Assert.Equal("运营配置", row["类别"]);
            Assert.Equal("String", row["值类型"]);
            Assert.Equal("公开运营配置", row["所需上下文"]);
            Assert.Equal("敏感", row["安全等级"]);
            Assert.Equal("空字符串并记录 ConfigMissing", row["无上下文结果"]);
        }

        IReadOnlyDictionary<string, string> job = Assert.Single(rows,
            item => item["符号种类"] == "直接" && item["规范名称"] == "JOB");
        Assert.Equal("CLASS", job["别名"]);
    }

    [Fact]
    public void 本机权威语料存在时重新计算并拒绝生成目录漂移()
    {
        const string corpusRoot = @"D:\ChuanQi\服务端";
        if (!Directory.Exists(corpusRoot))
            throw Xunit.Sdk.SkipException.ForSkip("本机未挂载 D:\\ChuanQi\\服务端 权威语料。");

        IReadOnlyList<IReadOnlyDictionary<string, string>> rootRows = ReadCsv("lingfeng-envir-roots.csv");
        Assert.All(rootRows, row => VerifyRootSnapshot(row, corpusRoot));

        var usage = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "rg",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        process.StartInfo.ArgumentList.Add("--encoding");
        process.StartInfo.ArgumentList.Add("gbk");
        process.StartInfo.ArgumentList.Add("-o");
        process.StartInfo.ArgumentList.Add("--no-filename");
        process.StartInfo.ArgumentList.Add(@"<\$[^>\r\n]{1,80}>");
        foreach (IReadOnlyDictionary<string, string> row in rootRows)
            process.StartInfo.ArgumentList.Add(Path.Combine(corpusRoot, row["相对路径"]));
        Assert.True(process.Start());
        while (process.StandardOutput.ReadLine() is { } token)
        {
            string inner = token[2..^1].Trim().ToUpperInvariant();
            string? identity = NormalizeCorpusIdentity(inner);
            if (identity is not null) usage[identity] = usage.GetValueOrDefault(identity) + 1;
        }
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);

        IReadOnlyList<IReadOnlyDictionary<string, string>> symbols = ReadCsv("lingfeng-server-symbols.csv");
        IReadOnlyDictionary<string, long> catalogUsage = symbols
            .Where(row => row["真实语料次数"] != "0")
            .ToDictionary(row => $"{row["符号种类"]}|{row["规范名称"]}",
                row => long.Parse(row["真实语料次数"], System.Globalization.CultureInfo.InvariantCulture),
                StringComparer.OrdinalIgnoreCase);
        Assert.Equal(catalogUsage.Count, usage.Count);
        Assert.All(usage, item => Assert.Equal(item.Value, catalogUsage[item.Key]));

        string attachment = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".codex", "attachments", "ce95e84e-d950-4361-b3c0-434da8313c6d", "pasted-text.txt");
        if (!File.Exists(attachment))
            throw Xunit.Sdk.SkipException.ForSkip("本机未保留用户提供的翎风服务器常量附件。");
        HashSet<string> attachmentNames = Regex.Matches(File.ReadAllText(attachment), @"<\$(?<name>[^>]+)>")
            .Select(match => match.Groups["name"].Value.Trim().ToUpperInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        HashSet<string> catalogAttachmentNames = symbols
            .Where(row => row["符号种类"] == "附件原文")
            .Select(row => row["规范名称"])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Equal(attachmentNames, catalogAttachmentNames);
    }

    private static void VerifyRootSnapshot(IReadOnlyDictionary<string, string> row, string corpusRoot)
    {
        string root = Path.Combine(corpusRoot, row["相对路径"]);
        Assert.True(Directory.Exists(root), $"语料根不存在：{row["相对路径"]}");
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = false,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint
        };
        FileInfo[] files = new DirectoryInfo(root).EnumerateFiles("*", options)
            .OrderBy(file => file.FullName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        using var manifest = System.Security.Cryptography.IncrementalHash.CreateHash(
            System.Security.Cryptography.HashAlgorithmName.SHA256);
        using var content = System.Security.Cryptography.IncrementalHash.CreateHash(
            System.Security.Cryptography.HashAlgorithmName.SHA256);
        var strictUtf8 = new System.Text.UTF8Encoding(false, true);
        int txt = 0, ini = 0, other = 0, utf8 = 0, utf8Bom = 0, utf16 = 0, cp936 = 0, binary = 0;
        long totalBytes = 0;
        foreach (FileInfo file in files)
        {
            byte[] data = File.ReadAllBytes(file.FullName);
            totalBytes += data.LongLength;
            string extension = file.Extension.ToLowerInvariant();
            if (extension == ".txt") txt++;
            else if (extension == ".ini") ini++;
            else other++;

            string relative = Path.GetRelativePath(root, file.FullName).Replace('\\', '/');
            manifest.AppendData(System.Text.Encoding.UTF8.GetBytes($"{relative}\0{data.LongLength}\n"));
            content.AppendData(System.Text.Encoding.UTF8.GetBytes($"{relative}\0"));
            content.AppendData(System.Security.Cryptography.SHA256.HashData(data));

            if (extension is not (".txt" or ".ini")) continue;
            if (data is [0xEF, 0xBB, 0xBF, ..]) utf8Bom++;
            else if (data is [0xFF, 0xFE, ..] or [0xFE, 0xFF, ..]) utf16++;
            else if (Array.IndexOf(data, (byte)0) >= 0) binary++;
            else
            {
                try
                {
                    strictUtf8.GetString(data);
                    utf8++;
                }
                catch (System.Text.DecoderFallbackException)
                {
                    cp936++;
                }
            }
        }

        Assert.Equal(int.Parse(row["文件数"], System.Globalization.CultureInfo.InvariantCulture), files.Length);
        Assert.Equal(txt.ToString(System.Globalization.CultureInfo.InvariantCulture), row["TXT数"]);
        Assert.Equal(ini.ToString(System.Globalization.CultureInfo.InvariantCulture), row["INI数"]);
        Assert.Equal(other.ToString(System.Globalization.CultureInfo.InvariantCulture), row["其他数"]);
        Assert.Equal(totalBytes.ToString(System.Globalization.CultureInfo.InvariantCulture), row["总字节"]);
        Assert.Equal(Convert.ToHexString(manifest.GetHashAndReset()), row["清单SHA256"]);
        Assert.Equal(Convert.ToHexString(content.GetHashAndReset()), row["内容SHA256"]);
        Assert.Equal(utf8.ToString(System.Globalization.CultureInfo.InvariantCulture), row["UTF8文本数"]);
        Assert.Equal(utf8Bom.ToString(System.Globalization.CultureInfo.InvariantCulture), row["UTF8BOM数"]);
        Assert.Equal(utf16.ToString(System.Globalization.CultureInfo.InvariantCulture), row["UTF16文本数"]);
        Assert.Equal(cp936.ToString(System.Globalization.CultureInfo.InvariantCulture), row["CP936候选数"]);
        Assert.Equal(binary.ToString(System.Globalization.CultureInfo.InvariantCulture), row["二进制文本数"]);
    }

    private static string? NormalizeCorpusIdentity(string inner)
    {
        if (Regex.IsMatch(inner, @"^[A-Z_][A-Z0-9_.]*$")) return $"直接|{inner}";
        Match function = Regex.Match(inner, @"^(?<name>[A-Z_][A-Z0-9_.]*)\(");
        if (function.Success) return $"函数|{function.Groups["name"].Value}()";
        Match indexed = Regex.Match(inner,
            @"^(?<name>[A-Z_][A-Z0-9_.]*)\[[^\[\]]+\](?:\.(?<field>[A-Z_][A-Z0-9_.]*))?$");
        if (!indexed.Success) return null;
        string name = $"{indexed.Groups["name"].Value}[]";
        if (indexed.Groups["field"].Success) name += $".{indexed.Groups["field"].Value}";
        return $"索引|{name}";
    }

    private static void AssertSymbol(
        IReadOnlyList<IReadOnlyDictionary<string, string>> rows,
        string kind,
        string name,
        string category,
        string usage)
    {
        IReadOnlyDictionary<string, string> row = Assert.Single(rows,
            item => item["符号种类"] == kind && item["规范名称"] == name);
        Assert.Equal(category, row["类别"]);
        Assert.Equal(usage, row["真实语料次数"]);
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, string>> ReadCsv(string fileName)
    {
        string path = Path.Combine(
            FindRepositoryRoot(AppContext.BaseDirectory),
            "Docs", "generated", "scripting", fileName);
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
