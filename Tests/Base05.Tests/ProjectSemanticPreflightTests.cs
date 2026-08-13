using System.Drawing;
using System.Reflection;
using System.Security.Cryptography;
using Microsoft.CodeAnalysis;
using Server.Diagnostics;
using Server.MirDatabase;
using Server.Scripting;
using Xunit;

namespace Base05.Tests;

public sealed class ProjectSemanticPreflightTests
{
    [Fact]
    public void Broken_project_reports_every_first_wave_rule_with_stable_locations()
    {
        using var fixture = new Fixture();
        var map = new MapInfo { Index = 1, FileName = "missing-map" };
        map.Movements.Add(new MovementInfo { MapIndex = 999 });
        map.Respawns.Add(new RespawnInfo
        {
            MonsterIndex = 999,
            Location = new Point(-1, -2),
            Count = 0,
            Spread = 1001,
            Delay = 0,
        });
        var npc = new NPCInfo { Index = 3, FileName = "missing/npc", MapIndex = 999, Location = new Point(-1, 2) };
        var scripts = new ScriptRegistry();
        var drop = new DropTableDefinition("Drops/broken");
        drop.Drops.Add(new DropEntryDefinition { Chance = 0, Weight = 0, ItemName = "missing-item", Count = 0, Gold = 1 });
        drop.Drops.Add(DropEntryDefinition.GroupDrop(1, new DropGroupDefinition()));
        scripts.Drops.Register(drop);
        scripts.RegisterNpcShop(new NpcShopDefinition("missing/shop", Array.Empty<ItemType>(), [new NpcShopGoodDefinition("missing-item")], craftRecipeOutputItemNames: ["missing-product"]));
        scripts.RegisterNpcShop(new NpcShopDefinition("reachable-shop", Array.Empty<ItemType>(), [new NpcShopGoodDefinition("missing-item")], craftRecipeOutputItemNames: ["missing-product"]));
        var recipe = new RecipeDefinition("Recipe/missing-product") { Amount = 0 };
        recipe.Ingredients.Add(new RecipeIngredientDefinition("missing-material", 0));
        recipe.Tools.Add("missing-tool");
        scripts.Recipes.Register(recipe);

        ProjectPreflightReport report = ProjectSemanticPreflight.Validate(new ProjectPreflightRequest
        {
            MapDirectory = fixture.Root,
            NpcDirectory = fixture.Root,
            Maps = [map],
            Monsters = Array.Empty<MonsterInfo>(),
            Items = Array.Empty<ItemInfo>(),
            Npcs = [npc, new NPCInfo { Index = 4, FileName = "reachable-shop", MapIndex = 1, Location = new Point(1, 1) }],
            MapBounds = [new ProjectMapBounds(1, 100, 100)],
            Scripts = scripts,
            StartupResources =
            [
                new ProjectResourceReference(Path.Combine(fixture.Root, "missing-core.dll"), 1, new string('0', 64), "launcher.loginCoreResources[0]"),
            ],
        });

        string[] expectedCodes =
        [
            "LEG02-MAP-001", "LEG02-MAP-002",
            "LEG02-SPAWN-001", "LEG02-SPAWN-002", "LEG02-SPAWN-003", "LEG02-SPAWN-004", "LEG02-SPAWN-005",
            "LEG02-NPC-001", "LEG02-NPC-002", "LEG02-NPC-003",
            "LEG02-DROP-002", "LEG02-DROP-003", "LEG02-DROP-004", "LEG02-DROP-005", "LEG02-DROP-006", "LEG02-DROP-007",
            "LEG02-SHOP-001", "LEG02-SHOP-002", "LEG02-SHOP-003",
            "LEG02-RECIPE-001", "LEG02-RECIPE-002", "LEG02-RECIPE-003", "LEG02-RECIPE-004", "LEG02-RECIPE-005",
            "LEG02-RESOURCE-002",
        ];
        Assert.True(report.HasErrors);
        Assert.All(expectedCodes, code => Assert.Contains(report.Diagnostics, diagnostic => diagnostic.Code == code));
        Assert.All(report.Diagnostics, diagnostic => Assert.False(string.IsNullOrWhiteSpace(diagnostic.Source)));
        Assert.Equal(report.Diagnostics.OrderBy(value => value.Code).ThenBy(value => value.Source), report.Diagnostics);
    }

    [Fact]
    public void Valid_project_has_no_blocking_diagnostics_and_is_repeatable()
    {
        using var fixture = new Fixture();
        string mapPath = fixture.Write("map-1.map", [1, 2, 3, 4]);
        string npcPath = fixture.Write(Path.Combine("npcs", "merchant.txt"), [1]);
        string resourcePath = fixture.Write("Client.exe", [10, 20, 30]);
        _ = mapPath;
        _ = npcPath;
        string sha256;
        using (FileStream stream = File.OpenRead(resourcePath))
            sha256 = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();

        var map = new MapInfo { Index = 1, FileName = "map-1" };
        map.Movements.Add(new MovementInfo { MapIndex = 1 });
        map.Respawns.Add(new RespawnInfo { MonsterIndex = 5, Location = new Point(10, 10), Count = 1, Spread = 10, Delay = 1 });
        var scripts = new ScriptRegistry();
        var drop = new DropTableDefinition("Drops/monster");
        drop.Drops.Add(DropEntryDefinition.Item(10, "material"));
        scripts.Drops.Register(drop);
        scripts.RegisterNpcShop(new NpcShopDefinition("merchant", Array.Empty<ItemType>(), [new NpcShopGoodDefinition("material")], craftRecipeOutputItemNames: ["product"]));
        var recipe = new RecipeDefinition("Recipe/product");
        recipe.Ingredients.Add(new RecipeIngredientDefinition("material", 1));
        recipe.Tools.Add("tool");
        scripts.Recipes.Register(recipe);

        var request = new ProjectPreflightRequest
        {
            MapDirectory = fixture.Root,
            NpcDirectory = Path.Combine(fixture.Root, "npcs"),
            Maps = [map],
            Monsters = [new MonsterInfo { Index = 5, Name = "monster" }],
            Items =
            [
                new ItemInfo { Index = 1, Name = "material" },
                new ItemInfo { Index = 2, Name = "product" },
                new ItemInfo { Index = 3, Name = "tool" },
            ],
            Npcs = [new NPCInfo { Index = 7, FileName = "merchant", MapIndex = 1, Location = new Point(5, 5) }],
            MapBounds = [new ProjectMapBounds(1, 100, 100)],
            Scripts = scripts,
            StartupResources = [new ProjectResourceReference(resourcePath, 3, sha256, "launcher.loginCoreResources[0]")],
        };

        ProjectPreflightReport first = ProjectSemanticPreflight.Validate(request);
        ProjectPreflightReport second = ProjectSemanticPreflight.Validate(request);
        Assert.False(first.HasErrors);
        Assert.Empty(first.Diagnostics);
        Assert.Equal(first.Diagnostics, second.Diagnostics);
    }

    [Fact]
    public void Coordinates_outside_known_map_bounds_are_rejected()
    {
        using var fixture = new Fixture();
        fixture.Write("map-1.map", [1, 2, 3, 4]);
        fixture.Write("npc.txt", [1]);
        var map = new MapInfo { Index = 1, FileName = "map-1" };
        map.Respawns.Add(new RespawnInfo { MonsterIndex = 5, Count = 1, Delay = 1, Location = new Point(50, 2) });

        ProjectPreflightReport report = ProjectSemanticPreflight.Validate(new ProjectPreflightRequest
        {
            MapDirectory = fixture.Root,
            NpcDirectory = fixture.Root,
            Maps = [map],
            Monsters = [new MonsterInfo { Index = 5 }],
            Npcs = [new NPCInfo { Index = 9, FileName = "npc", MapIndex = 1, Location = new Point(2, 50) }],
            MapBounds = [new ProjectMapBounds(1, 20, 20)],
            StartupResources = Array.Empty<ProjectResourceReference>(),
        });

        Assert.Contains(report.Diagnostics, value => value.Code == "LEG02-SPAWN-005" && value.Message.Contains("20x20", StringComparison.Ordinal));
        Assert.Contains(report.Diagnostics, value => value.Code == "LEG02-NPC-002" && value.Message.Contains("20x20", StringComparison.Ordinal));
        Assert.Contains(report.Diagnostics, value => value.Code == "LEG02-RESOURCE-000" && value.Severity == ProjectPreflightSeverity.Suggestion);
    }

    [Fact]
    public void Resource_size_and_digest_drift_are_located_separately()
    {
        using var fixture = new Fixture();
        string path = fixture.Write("Client.exe", [1, 2, 3]);

        ProjectPreflightReport report = ProjectSemanticPreflight.Validate(new ProjectPreflightRequest
        {
            StartupResources = [new ProjectResourceReference(path, 9, new string('0', 64), "launcher.loginCoreResources[2]")],
        });

        Assert.Collection(
            report.Diagnostics,
            diagnostic => { Assert.Equal("LEG02-RESOURCE-003", diagnostic.Code); Assert.Equal("launcher.loginCoreResources[2]", diagnostic.Source); },
            diagnostic => { Assert.Equal("LEG02-RESOURCE-004", diagnostic.Code); Assert.Equal("launcher.loginCoreResources[2]", diagnostic.Source); });
    }

    [Fact]
    public void Script_compiler_does_not_reference_extern_alias_protocol_fixture()
    {
        MethodInfo collect = typeof(ScriptCompiler).GetMethod("CollectMetadataReferencesCore", BindingFlags.Static | BindingFlags.NonPublic)!;
        var references = Assert.IsAssignableFrom<IReadOnlyList<MetadataReference>>(collect.Invoke(null, null));

        Assert.DoesNotContain(references.OfType<PortableExecutableReference>(), reference =>
            string.Equals(Path.GetFileNameWithoutExtension(reference.FilePath), "ShareProtocolCompat", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class Fixture : IDisposable
    {
        public Fixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "lyocrystal-leg02-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public string Write(string relativePath, byte[] content)
        {
            string path = Path.Combine(Root, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, content);
            return path;
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
