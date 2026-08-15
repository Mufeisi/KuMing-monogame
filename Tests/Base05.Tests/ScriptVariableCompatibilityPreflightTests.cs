using System.Text;
using Server.Scripting.Variables;
using Xunit;

namespace Base05.Tests;

public sealed class ScriptVariableCompatibilityPreflightTests
{
    [Fact]
    public void PreflightIsReadOnlyAndReportsPrefixesRangesReservedSlotsPathsAndDynamicNames()
    {
        string root = CreateRoot();
        try
        {
            string file = Path.Combine(root, "NPC", "测试.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            string content = "MOV A0 公告\r\nCHECK N999 > 0\r\nMOV P1000 1\r\nSAVEVAR GLOBAL X C:\\data\\x.ini\r\nMOV N$动态<$STR(N0)> 1\n";
            File.WriteAllText(file, content, new UTF8Encoding(false));
            byte[] before = File.ReadAllBytes(file);

            ScriptVariablePreflightReport report = ScriptVariableCompatibilityPreflight.Scan(root);

            Assert.Equal(1, report.FileCount);
            Assert.Equal(before, File.ReadAllBytes(file));
            Assert.Contains(report.PrefixUsages, item => item.Prefix == "A" && item.Count == 1);
            Assert.Contains(report.Diagnostics, item => item.Code == "VAR07-A-WRITE");
            Assert.Contains(report.Diagnostics, item => item.Code == "VAR07-RESERVED-001");
            Assert.Contains(report.Diagnostics, item => item.Code == "VAR07-RANGE-001" && item.Severity == ScriptVariablePreflightSeverity.Error);
            Assert.Contains(report.Diagnostics, item => item.Code == "VAR07-PATH-001");
            Assert.Contains(report.Diagnostics, item => item.Code == "VAR07-DYNAMIC-001");
            Assert.Contains(report.Diagnostics, item => item.Code == "VAR07-NEWLINE-001");
            Assert.Equal(report.ContentDigest, ScriptVariableCompatibilityPreflight.Scan(root).ContentDigest);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void PreflightReadsCp936AndCompatibleModeRequiresExactReviewedDigest()
    {
        string root = CreateRoot();
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            string file = Path.Combine(root, "QuestDiary", "变量.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, "MOV U0 1\r\nCHECK U0 > 0\r\n", Encoding.GetEncoding(936));

            ScriptVariablePreflightReport report = ScriptVariableCompatibilityPreflight.Scan(root);
            Assert.False(report.HasErrors);
            Assert.Equal(2, Assert.Single(report.PrefixUsages).Count);
            Assert.True(ScriptVariableCompatibilityPreflight.ValidateActivation(
                ScriptVariableCompatibilityMode.Audit, report, string.Empty).Success);
            Assert.False(ScriptVariableCompatibilityPreflight.ValidateActivation(
                ScriptVariableCompatibilityMode.LingFengCompatible, report, "错误摘要").Success);
            Assert.True(ScriptVariableCompatibilityPreflight.ValidateActivation(
                ScriptVariableCompatibilityMode.LingFengCompatible, report, report.ContentDigest).Success);

            File.AppendAllText(file, ";已审核后发生变化\r\n", Encoding.GetEncoding(936));
            ScriptVariablePreflightReport changed = ScriptVariableCompatibilityPreflight.Scan(root);
            Assert.NotEqual(report.ContentDigest, changed.ContentDigest);
            Assert.False(ScriptVariableCompatibilityPreflight.ValidateActivation(
                ScriptVariableCompatibilityMode.LingFengCompatible, changed, report.ContentDigest).Success);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void PreflightIncludesConvertedCSharpContentAndFailsClosedForSensitiveSymbols()
    {
        string root = CreateRoot();
        try
        {
            string file = Path.Combine(root, "CSharpScripts", "Npc.cs");
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file,
                "public sealed class ShopD4221 { }\r\ndialog.Say(\"你好 <$USERNAME>，密码 <$PASSWORD>，未知 <$NOT_REVIEWED>\");\r\nMOV A1 公告\r\n",
                new UTF8Encoding(false));

            ScriptVariablePreflightReport report = ScriptVariableCompatibilityPreflight.Scan(root);

            Assert.Equal(1, report.FileCount);
            Assert.Contains(report.SymbolUsages, item => item.Symbol == "USERNAME" && item.Count == 1);
            Assert.Contains(report.SymbolUsages, item => item.Symbol == "PASSWORD" && item.Count == 1);
            Assert.Contains(report.Diagnostics, item =>
                item.Code == "VAR08-SENSITIVE-001" &&
                item.Severity == ScriptVariablePreflightSeverity.Error);
            Assert.Contains(report.Diagnostics, item =>
                item.Code == "VAR08-UNKNOWN-001" &&
                item.Severity == ScriptVariablePreflightSeverity.Error);
            Assert.Contains(report.Diagnostics, item => item.Code == "VAR07-A-WRITE");
            Assert.DoesNotContain(report.Diagnostics, item => item.Code == "VAR07-RANGE-001");
            Assert.DoesNotContain(report.Diagnostics, item => item.Code == "VAR07-DYNAMIC-001");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void MissingOrEmptyRootsFailClosedOnlyForCompatibleActivation()
    {
        string root = CreateRoot();
        try
        {
            ScriptVariablePreflightReport empty = ScriptVariableCompatibilityPreflight.Scan(root);
            Assert.True(empty.HasErrors);
            Assert.True(ScriptVariableCompatibilityPreflight.ValidateActivation(
                ScriptVariableCompatibilityMode.LegacyCurrent, empty, string.Empty).Success);
            Assert.True(ScriptVariableCompatibilityPreflight.ValidateActivation(
                ScriptVariableCompatibilityMode.Audit, empty, string.Empty).Success);
            Assert.False(ScriptVariableCompatibilityPreflight.ValidateActivation(
                ScriptVariableCompatibilityMode.LingFengCompatible, empty, empty.ContentDigest).Success);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void InvalidRootIsReportedInsteadOfCrashingThePreflightProcess()
    {
        ScriptVariablePreflightReport report = ScriptVariableCompatibilityPreflight.Scan("invalid\0root");

        Assert.True(report.HasErrors);
        Assert.Contains(report.Diagnostics, item => item.Code == "VAR07-ROOT-001");
    }

    private static string CreateRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "LyoVariablePreflight-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
