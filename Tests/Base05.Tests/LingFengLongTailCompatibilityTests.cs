using Server;
using Server.Scripting;
using System.Text.RegularExpressions;
using Xunit;

namespace Base05.Tests;

[Collection(nameof(PhysicalTextFileProviderCollection))]
public sealed class LingFengLongTailCompatibilityTests
{
    private static readonly HashSet<string> SelectedCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "EQUAL", "LARGE", "SMALL", "NOT"
    };

    private static readonly HashSet<string> SelectedTriggers = new(StringComparer.OrdinalIgnoreCase)
    {
        "[@MAGICATTACK]", "[@MAGICSTRUCK]", "[@KILLPLAY]", "[@PLAYDIE]"
    };

    [Fact]
    public void 代表Envir所选高频长尾命令与触发严格预检为零()
    {
        const string root = @"D:\ChuanQi\服务端\01酷明传奇\MirServer_01\Mir200\Envir";
        if (!Directory.Exists(root))
            throw Xunit.Sdk.SkipException.ForSkip("本机未挂载 LFENV-ROOT-0002 代表语料。");

        bool oldStrict = Settings.TxtScriptsStrictCompatibility;
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        try
        {
            Settings.TxtScriptsStrictCompatibility = true;
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            var provider = new PhysicalTextFileProvider(
                new PhysicalTextFileProviderOptions(root, TxtScriptLayout.LingFeng)
                {
                    MaxFileBytes = 2 * 1024 * 1024
                });

            var observed = provider.GetAll()
                .SelectMany(definition => definition.Lines)
                .Select(line => TxtScriptTokenizer.TryTokenize(line.Trim(), out string[] tokens, out _) && tokens.Length > 0
                    ? tokens[0].ToUpperInvariant()
                    : string.Empty)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            Assert.All(SelectedCommands, command => Assert.Contains(command, observed));
            Assert.All(SelectedTriggers, trigger => Assert.Contains(trigger, observed));

            string[] longTailErrors = TxtScriptSnapshotValidator.Validate(provider)
                .Where(value => value.StartsWith("TXT-SNAPSHOT-014", StringComparison.Ordinal) ||
                                value.StartsWith("TXT-SNAPSHOT-016", StringComparison.Ordinal))
                .Where(IsSelectedLongTailError)
                .ToArray();

            Assert.True(longTailErrors.Length == 0,
                string.Join(Environment.NewLine, longTailErrors.Take(20)) + Environment.NewLine + Summarize(longTailErrors));
        }
        finally
        {
            Settings.TxtScriptsStrictCompatibility = oldStrict;
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    private static bool IsSelectedLongTailError(string error)
    {
        if (error.StartsWith("TXT-SNAPSHOT-014", StringComparison.Ordinal) &&
            !error.Contains("未知 IF 命令", StringComparison.Ordinal))
            return false;
        Match match = Regex.Match(error,
            error.StartsWith("TXT-SNAPSHOT-016", StringComparison.Ordinal)
                ? @"触发 (?<name>\[@[^\]]+\])"
                : @"命令 (?<name>[^（]+)（");
        if (!match.Success) return false;
        string name = match.Groups["name"].Value.Trim();
        return SelectedCommands.Contains(name) || SelectedTriggers.Contains(name);
    }

    private static string Summarize(IEnumerable<string> errors)
    {
        return string.Join(Environment.NewLine, errors
            .Select(value => Regex.Match(value,
                value.StartsWith("TXT-SNAPSHOT-016", StringComparison.Ordinal)
                    ? @"触发 (?<name>\[@[^\]]+\])"
                    : @"命令 (?<name>[^（]+)（"))
            .Where(value => value.Success)
            .GroupBy(value => value.Groups["name"].Value, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(value => value.Count())
            .ThenBy(value => value.Key, StringComparer.OrdinalIgnoreCase)
            .Select(value => $"{value.Key}|{value.Count()}"));
    }
}
