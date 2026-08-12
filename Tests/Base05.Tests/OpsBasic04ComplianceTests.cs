using System.Text.Json;
using Xunit;

namespace Base05.Tests;

public sealed class OpsBasic04ComplianceTests
{
    private static readonly HashSet<string> ReviewedNoAssertionPackages = new(StringComparer.Ordinal)
    {
        "LyoCrystal@2026.08.10",
        "StbTrueTypeSharp@1.26.11",
        "StbImageSharp@2.27.13",
        "System.Configuration.ConfigurationManager@4.5.0",
        "Microsoft.Windows.SDK.Win32Metadata@61.0.15-preview",
        "Microsoft.Web.WebView2@1.0.2903.40",
        "Microsoft.Windows.SDK.Win32Docs@0.1.42-alpha",
        "Microsoft.AspNet.WebApi.Client@6.0.0",
        "System.Security.Permissions@4.5.0",
        "Microsoft.Windows.WDK.Win32Metadata@0.12.8-experimental",
        "NAudio@2.2.1",
        "System.Data.DataSetExtensions@4.5.0"
    };

    [Fact]
    public void Sbom覆盖真实发布工件且没有未复核许可证项()
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "manifest.spdx.json")));
        JsonElement root = document.RootElement;
        Assert.Equal("SPDX-2.2", root.GetProperty("spdxVersion").GetString());
        Assert.Equal(6, root.GetProperty("files").GetArrayLength());
        Assert.True(root.GetProperty("packages").GetArrayLength() >= 218);

        HashSet<string> noAssertion = root.GetProperty("packages").EnumerateArray()
            .Where(package => package.GetProperty("licenseDeclared").GetString() == "NOASSERTION")
            .Select(package => $"{package.GetProperty("name").GetString()}@{package.GetProperty("versionInfo").GetString()}")
            .ToHashSet(StringComparer.Ordinal);

        Assert.True(ReviewedNoAssertionPackages.SetEquals(noAssertion),
            "SBOM 出现未复核或已过期的 NOASSERTION 许可证项：" + string.Join(", ", noAssertion.OrderBy(name => name)));
    }

    [Fact]
    public void 发布合规包包含依赖清单与许可证正文()
    {
        string baseDirectory = AppContext.BaseDirectory;
        using JsonDocument dependencySbom = JsonDocument.Parse(File.ReadAllText(Path.Combine(baseDirectory, "dependencies.spdx.json")));
        JsonElement dependencyRoot = dependencySbom.RootElement;
        Assert.Empty(dependencyRoot.GetProperty("files").EnumerateArray());
        Assert.True(dependencyRoot.GetProperty("packages").GetArrayLength() >= 218);

        JsonElement rootPackage = dependencyRoot.GetProperty("packages").EnumerateArray()
            .Single(package => package.GetProperty("SPDXID").GetString() == "SPDXRef-RootPackage");
        Assert.False(rootPackage.GetProperty("filesAnalyzed").GetBoolean());
        Assert.False(rootPackage.TryGetProperty("packageVerificationCode", out _));
        Assert.False(rootPackage.TryGetProperty("hasFiles", out _));

        HashSet<string> declaredIds = dependencyRoot.GetProperty("packages").EnumerateArray()
            .Select(package => package.GetProperty("SPDXID").GetString()!)
            .Append("SPDXRef-DOCUMENT")
            .ToHashSet(StringComparer.Ordinal);
        foreach (JsonElement relationship in dependencyRoot.GetProperty("relationships").EnumerateArray())
        {
            Assert.Contains(relationship.GetProperty("spdxElementId").GetString()!, declaredIds);
            Assert.Contains(relationship.GetProperty("relatedSpdxElement").GetString()!, declaredIds);
        }

        string licenseDirectory = Path.Combine(baseDirectory, "Compliance", "Licenses");
        string[] requiredFiles =
        {
            "MIT.txt", "Apache-2.0.txt", "BSD-2-Clause.txt", "Zlib.txt", "MS-PL.txt", "EPL-2.0.txt", "MPL-2.0.txt",
            "Microsoft-Windows-SDK-License.txt", "Microsoft-WebView2-LICENSE.txt", "Microsoft-WebView2-NOTICE.txt",
            "Microsoft-NET-Library-EULA.txt", "NAudio-MIT.txt", "RoslynPad-MIT.txt", "PACKAGE-ATTRIBUTIONS.md"
        };

        Assert.All(requiredFiles, file => Assert.True(File.Exists(Path.Combine(licenseDirectory, file)), $"缺少许可证文件：{file}"));

        string attributions = File.ReadAllText(Path.Combine(licenseDirectory, "PACKAGE-ATTRIBUTIONS.md"));
        using JsonDocument releaseSbom = JsonDocument.Parse(File.ReadAllText(Path.Combine(baseDirectory, "manifest.spdx.json")));
        foreach (JsonElement package in releaseSbom.RootElement.GetProperty("packages").EnumerateArray()
                     .Where(package => package.GetProperty("name").GetString() != "LyoCrystal"))
        {
            string expectedRow = $"| {package.GetProperty("name").GetString()} | {package.GetProperty("versionInfo").GetString()} |";
            Assert.Contains(expectedRow, attributions, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void 外部资源清单包含授权依据与发布必需类别()
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "external-assets.manifest.json")));
        JsonElement root = document.RootElement;
        Assert.Equal(1, root.GetProperty("formatVersion").GetInt32());
        Assert.Equal("AuthorizedByProjectOwner", root.GetProperty("authorization").GetProperty("status").GetString());

        HashSet<string> names = root.GetProperty("categories").EnumerateArray()
            .Select(category => category.GetProperty("name").GetString()!)
            .ToHashSet(StringComparer.Ordinal);
        Assert.Contains("地图资源", names);
        Assert.Contains("音频资源", names);
        Assert.Contains("FairyGUI MonoGame 运行时", names);
        Assert.Contains("HarmonyOS Sans SC Medium 字体", names);
        Assert.Contains("微端资源", names);
    }
}
