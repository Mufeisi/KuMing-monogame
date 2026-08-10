using System.Text.Json;
using Xunit;

namespace Base05.Tests;

public sealed class OpsBasic04ComplianceTests
{
    private static readonly HashSet<string> ReviewedNoAssertionPackages = new(StringComparer.Ordinal)
    {
        "LyoCrystal",
        "StbTrueTypeSharp",
        "StbImageSharp",
        "System.Configuration.ConfigurationManager",
        "Microsoft.Windows.SDK.Win32Metadata",
        "Microsoft.Web.WebView2",
        "Microsoft.Windows.SDK.Win32Docs",
        "Microsoft.AspNet.WebApi.Client",
        "System.Security.Permissions",
        "Microsoft.Windows.WDK.Win32Metadata",
        "NAudio",
        "System.Data.DataSetExtensions"
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
            .Select(package => package.GetProperty("name").GetString()!)
            .ToHashSet(StringComparer.Ordinal);

        Assert.True(ReviewedNoAssertionPackages.SetEquals(noAssertion),
            "SBOM 出现未复核或已过期的 NOASSERTION 许可证项：" + string.Join(", ", noAssertion.OrderBy(name => name)));
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
