using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Xunit;

namespace Base05.Tests;

public sealed class ProtocolGeneratedManifestTests
{
    [Fact]
    public void GeneratedManifestHasCompleteDeterministicCoverage()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "protocol-wire-manifest.generated.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = document.RootElement;

        Assert.Equal("PROTO-02.generated-wire-manifest.v2", root.GetProperty("schemaVersion").GetString());
        JsonElement coverage = root.GetProperty("coverage");
        Assert.Equal(145, coverage.GetProperty("clientPacketCount").GetInt32());
        Assert.Equal(275, coverage.GetProperty("serverPacketCount").GetInt32());
        Assert.Equal(64, coverage.GetProperty("enumCount").GetInt32());

        JsonElement.ArrayEnumerator packetEnumerator = root.GetProperty("packets").EnumerateArray();
        JsonElement[] packets = packetEnumerator.ToArray();
        Assert.Equal(420, packets.Length);
        Assert.Equal(420, packets.Select(packet =>
            $"{packet.GetProperty("direction").GetString()}:{packet.GetProperty("id").GetInt32()}").Distinct().Count());
        Assert.All(packets, packet =>
        {
            AssertHash(packet.GetProperty("readIlSha256").GetString());
            AssertHash(packet.GetProperty("writeIlSha256").GetString());
        });

        JsonElement[] sources = root.GetProperty("sources").EnumerateArray().ToArray();
        Assert.Equal(17, sources.Length);
        Assert.Contains(sources, source => source.GetProperty("path").GetString() == "src/Shared/Shared/Packet.cs");
        Assert.Contains(sources, source => source.GetProperty("path").GetString() == "src/Shared/Shared/ClientPackets.cs");
        Assert.Contains(sources, source => source.GetProperty("path").GetString() == "src/Shared/Shared/ServerPackets.cs");
        Assert.Contains(sources, source => source.GetProperty("path").GetString() == "src/Shared/Shared/Enums.cs");
        Assert.All(sources, source => AssertHash(source.GetProperty("sha256").GetString()));
        Assert.All(sources, source =>
        {
            string relativePath = source.GetProperty("path").GetString()!;
            string content = File.ReadAllText(Path.Combine(FindRepositoryRoot(AppContext.BaseDirectory), relativePath.Replace('/', Path.DirectorySeparatorChar)));
            string normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal);
            string expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
            Assert.Equal(expected, source.GetProperty("sha256").GetString());
        });
        Assert.Equal(64, root.GetProperty("enums").GetArrayLength());
    }

    [Fact]
    public void CompatibilityMatrixMatchesTrackedRuntimeVersions()
    {
        string repositoryRoot = FindRepositoryRoot(AppContext.BaseDirectory);
        string pcLayout = File.ReadAllText(Path.Combine(repositoryRoot, "src", "Clients", "Client_VorticeDX11", "Bootstrap", "PcBootstrapLayout.cs"));
        string mobileLayout = File.ReadAllText(Path.Combine(repositoryRoot, "src", "Clients", "Client_MonoGame.Shared", "ClientResourceLayout.cs"));
        string serverSettings = File.ReadAllText(Path.Combine(repositoryRoot, "src", "Server", "Server", "Settings.cs"));
        using JsonDocument index = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            repositoryRoot, "src", "Clients", "Client_MonoGame.Shared", "BootstrapAssets", "bootstrap-package-index.json")));
        XDocument androidProject = XDocument.Load(Path.Combine(repositoryRoot, "src", "Clients", "Client_MonoGame.Android", "Client_MonoGame.Android.csproj"));

        Assert.Contains("ClientCompatibilityVersion { get; } = new Version(1, 0, 0)", pcLayout, StringComparison.Ordinal);
        Assert.Contains("BootstrapClientCompatibilityVersion { get; } = new Version(2, 0, 0)", mobileLayout, StringComparison.Ordinal);
        Assert.Contains("public static bool CheckVersion = true", serverSettings, StringComparison.Ordinal);
        Assert.Equal("2.0.0", androidProject.Descendants("ApplicationDisplayVersion").Single().Value);
        Assert.Equal(
            "content-988b1bb85432df58363d3b307b7971157680b207fcd3213f12eb520c032176c9",
            index.RootElement.GetProperty("ResourceVersion").GetString());
    }

    [Fact]
    public void AndroidFormalBuildLinksCanonicalSharedProtocolSources()
    {
        string repositoryRoot = FindRepositoryRoot(AppContext.BaseDirectory);
        string projectPath = Path.Combine(repositoryRoot, "src", "Clients", "Client_MonoGame.Shared", "Client_MonoGame.Shared.csproj");
        XDocument project = XDocument.Load(projectPath);
        string[] androidIncludes = project.Descendants("ItemGroup")
            .Where(group => ((string?)group.Attribute("Condition"))?.Contains("'$(TargetFramework)' == 'net10.0-android'", StringComparison.Ordinal) == true)
            .Descendants("Compile")
            .Select(item => ((string?)item.Attribute("Include") ?? string.Empty).Replace('/', '\\'))
            .ToArray();

        Assert.DoesNotContain(androidIncludes, include => include.Contains("Share\\**\\*.cs", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("..\\..\\..\\src\\Shared\\Shared\\Packet.cs", androidIncludes);
        Assert.Contains("..\\..\\..\\src\\Shared\\Shared\\ClientPackets.cs", androidIncludes);
        Assert.Contains("..\\..\\..\\src\\Shared\\Shared\\ServerPackets.cs", androidIncludes);
        Assert.Contains("..\\..\\..\\src\\Shared\\Shared\\Enums.cs", androidIncludes);
        Assert.Contains("Share\\Language.cs", androidIncludes);
        Assert.Contains("Share\\Functions\\IniReader.cs", androidIncludes);

        string[] retainedForkFiles = androidIncludes
            .Where(include => include.StartsWith("Share\\", StringComparison.OrdinalIgnoreCase))
            .OrderBy(include => include, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(["Share\\Functions\\IniReader.cs", "Share\\Language.cs"], retainedForkFiles);

        AssertReferencesSharedProject(Path.Combine(repositoryRoot, "src", "Clients", "Client_VorticeDX11", "Client_VorticeDX11.csproj"));
        AssertReferencesSharedProject(Path.Combine(repositoryRoot, "src", "Server", "Server", "Server.Library.csproj"));
    }

    private static void AssertReferencesSharedProject(string projectPath)
    {
        XDocument project = XDocument.Load(projectPath);
        string expectedPath = Path.GetFullPath(Path.Combine(
            FindRepositoryRoot(Path.GetDirectoryName(projectPath)!),
            "src", "Shared", "Shared", "Shared.csproj"));
        Assert.Contains(project.Descendants("ProjectReference"), reference =>
        {
            string include = ((string?)reference.Attribute("Include")) ?? string.Empty;
            string actualPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(projectPath)!, include));
            return string.Equals(actualPath, expectedPath, StringComparison.OrdinalIgnoreCase);
        });
    }

    private static void AssertHash(string? value)
    {
        Assert.NotNull(value);
        Assert.Matches("^[0-9a-f]{64}$", value);
    }

    private static string FindRepositoryRoot(string start)
    {
        DirectoryInfo? current = new(start);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "global.json")) &&
                Directory.Exists(Path.Combine(current.FullName, "src", "Shared")))
                return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("无法定位仓库根目录。");
    }
}
