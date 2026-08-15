using Server.Scripting.Variables;
using Xunit;
using Xunit.Abstractions;

namespace Base05.Tests;

public sealed class RealVariableProjectPreflightTests
{
    private static readonly IReadOnlyDictionary<string, int> ExpectedSymbols =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["AMULET"] = 1, ["ARMOUR"] = 2, ["BELT"] = 2, ["BOOTS"] = 2,
            ["BRACELET_L"] = 2, ["BRACELET_R"] = 2, ["CLASS"] = 1,
            ["CONQUESTGATE"] = 3, ["CONQUESTGOLD"] = 2, ["CONQUESTGUARD"] = 13,
            ["CONQUESTOWNER"] = 5, ["CONQUESTRATE"] = 3, ["CONQUESTSCHEDULE"] = 2,
            ["CONQUESTSIEGE"] = 2, ["CONQUESTWALL"] = 4, ["CREDIT"] = 1,
            ["DATE"] = 3, ["GAMEGOLD"] = 2, ["GUILDNAME"] = 4, ["GUILDWARFEE"] = 3,
            ["GUILDWARTIME"] = 2, ["HELMET"] = 2, ["HP"] = 3, ["LEVEL"] = 3,
            ["MAP"] = 15, ["MAPNAME"] = 16, ["MAXHP"] = 3, ["MAXMP"] = 2,
            ["MONSTERCOUNT"] = 1, ["MOUNT"] = 2, ["MOUNTLOYALTY"] = 1, ["MP"] = 2,
            ["NECKLACE"] = 2, ["NPCNAME"] = 35, ["OUTPUT"] = 76,
            ["PARCELAMOUNT"] = 14, ["PKPOINT"] = 4, ["RING_L"] = 3, ["RING_R"] = 2,
            ["ROLLRESULT"] = 4, ["STONE"] = 2, ["TORCH"] = 1, ["USERCOUNT"] = 2,
            ["USERNAME"] = 195, ["WEAPON"] = 2, ["X_COORD"] = 14, ["Y_COORD"] = 14,
        };

    private readonly ITestOutputHelper _output;

    public RealVariableProjectPreflightTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void SelectedRealProjectHasReviewedVariableAndSymbolSnapshot()
    {
        string? projectRoot = Environment.GetEnvironmentVariable("LYOCRYSTAL_VARIABLE_PROJECT_ROOT");
        if (string.IsNullOrWhiteSpace(projectRoot))
        {
            _output.WriteLine("VARIABLE_REAL_PROJECT status=not-requested");
            return;
        }

        string envirRoot = Path.Combine(Path.GetFullPath(projectRoot), "Envir");
        ScriptVariablePreflightReport report = ScriptVariableCompatibilityPreflight.Scan(envirRoot);

        Assert.False(report.HasErrors, string.Join(Environment.NewLine,
            report.Diagnostics.Where(item => item.Severity == ScriptVariablePreflightSeverity.Error)));
        Assert.Equal(2348, report.FileCount);
        Assert.Equal("7E1E13532F37151BBC15E4B7B43383A8C1D95E3B0DC25944A2F19864D74ABE3D", report.ContentDigest);
        Assert.Equal(
            new Dictionary<string, int>(StringComparer.Ordinal) { ["A"] = 36, ["D"] = 36 },
            report.PrefixUsages.ToDictionary(item => item.Prefix, item => item.Count, StringComparer.Ordinal));
        Assert.Equal(
            ExpectedSymbols,
            report.SymbolUsages.ToDictionary(item => item.Symbol, item => item.Count, StringComparer.Ordinal));

        ScriptVariablePreflightDiagnostic[] aUsages = report.Diagnostics
            .Where(item => item.Code is "VAR07-A-READ" or "VAR07-A-WRITE").ToArray();
        Assert.Equal(36, aUsages.Length);
        Assert.Equal(28, aUsages.Count(item => item.Code == "VAR07-A-READ"));
        Assert.Equal(8, aUsages.Count(item => item.Code == "VAR07-A-WRITE"));
        Assert.All(aUsages, item =>
        {
            string normalizedFile = item.File.Replace('\\', '/');
            Assert.True(
                normalizedFile.EndsWith("00Default/UseItems.cs", StringComparison.OrdinalIgnoreCase) ||
                normalizedFile.EndsWith("00Default/特殊消耗品/定位的符咒.cs", StringComparison.OrdinalIgnoreCase),
                $"发现未审核的 A 变量位置：{item.File}:{item.Line}");
        });

        Assert.All(report.Diagnostics, item => Assert.Contains(
            item.Code,
            new[] { "VAR07-A-READ", "VAR07-A-WRITE", "VAR07-NEWLINE-001" }));

        _output.WriteLine(
            $"VARIABLE_REAL_PROJECT status=reviewed files={report.FileCount} digest={report.ContentDigest} " +
            $"symbols={report.SymbolUsages.Count} a_reads=28 a_writes=8 errors=0");
    }
}
