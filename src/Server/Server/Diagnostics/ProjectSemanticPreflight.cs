using System.Drawing;
using System.Security.Cryptography;
using Server.MirDatabase;
using Server.Scripting;

namespace Server.Diagnostics;

public enum ProjectPreflightSeverity
{
    Error,
    Warning,
    Suggestion,
}

public sealed record ProjectPreflightDiagnostic(
    string Code,
    ProjectPreflightSeverity Severity,
    string Source,
    string Message);

public sealed record ProjectResourceReference(
    string Path,
    long Size,
    string Sha256,
    string Source);

public sealed record ProjectMapBounds(int MapIndex, int Width, int Height);

public sealed class ProjectPreflightRequest
{
    public string MapDirectory { get; init; } = string.Empty;
    public string NpcDirectory { get; init; } = string.Empty;
    public string CSharpNpcDirectory { get; init; } = string.Empty;
    public IReadOnlyCollection<MapInfo> Maps { get; init; } = Array.Empty<MapInfo>();
    public IReadOnlyCollection<MonsterInfo> Monsters { get; init; } = Array.Empty<MonsterInfo>();
    public IReadOnlyCollection<ItemInfo> Items { get; init; } = Array.Empty<ItemInfo>();
    public IReadOnlyCollection<NPCInfo> Npcs { get; init; } = Array.Empty<NPCInfo>();
    public IReadOnlyCollection<ProjectMapBounds> MapBounds { get; init; } = Array.Empty<ProjectMapBounds>();
    public ScriptRegistry Scripts { get; init; } = new();
    public IReadOnlyCollection<ProjectResourceReference> StartupResources { get; init; } = Array.Empty<ProjectResourceReference>();
    public Func<string, bool> ItemExists { get; init; }
}

public sealed class ProjectPreflightReport
{
    internal ProjectPreflightReport(IReadOnlyList<ProjectPreflightDiagnostic> diagnostics)
    {
        Diagnostics = diagnostics;
    }

    public IReadOnlyList<ProjectPreflightDiagnostic> Diagnostics { get; }
    public bool HasErrors => Diagnostics.Any(value => value.Severity == ProjectPreflightSeverity.Error);
}

/// <summary>
/// 对现有项目事实对象执行只读的发布前跨域检查。
/// </summary>
public static class ProjectSemanticPreflight
{
    public static ProjectPreflightReport Validate(ProjectPreflightRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var diagnostics = new List<ProjectPreflightDiagnostic>();
        var maps = request.Maps.Where(value => value is not null).ToArray();
        var monsters = request.Monsters.Where(value => value is not null).ToArray();
        var items = request.Items.Where(value => value is not null).ToArray();
        var npcs = request.Npcs.Where(value => value is not null).ToArray();
        var mapIndexes = maps.Select(value => value.Index).ToHashSet();
        var monsterIndexes = monsters.Select(value => value.Index).ToHashSet();
        var itemNames = items.Select(value => value.Name?.Trim()).Where(value => !string.IsNullOrWhiteSpace(value)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool ItemExists(string value) => request.ItemExists?.Invoke(value) ?? itemNames.Contains(value?.Trim() ?? string.Empty);
        var npcFileNames = npcs.Select(value => NormalizePath(value.FileName)).Where(value => value.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var mapBounds = request.MapBounds.Where(value => value is not null).ToDictionary(value => value.MapIndex);

        ValidateMaps(request.MapDirectory, maps, mapIndexes, monsterIndexes, mapBounds, diagnostics);
        ValidateNpcs(request.NpcDirectory, request.CSharpNpcDirectory, npcs, mapIndexes, mapBounds, request.Scripts, diagnostics);
        ValidateDrops(request.Scripts.Drops.Definitions.Values, ItemExists, diagnostics);
        ValidateShops(request.Scripts.NpcShops.Definitions.Values, npcFileNames, ItemExists, diagnostics);
        ValidateRecipes(request.Scripts.Recipes.Definitions.Values, ItemExists, diagnostics);
        ValidateResources(request.StartupResources, diagnostics);

        return new ProjectPreflightReport(diagnostics
            .OrderBy(value => value.Code, StringComparer.Ordinal)
            .ThenBy(value => value.Source, StringComparer.Ordinal)
            .ThenBy(value => value.Message, StringComparer.Ordinal)
            .ToArray());
    }

    /// <summary>
    /// 使用与完整预检相同的地图/NPC规则，只检查指定地图拥有的内容。
    /// 其他地图仍参与目标引用判定，但不会生成其自身诊断。
    /// </summary>
    public static ProjectPreflightReport ValidateMapContent(ProjectPreflightRequest request, int mapIndex)
    {
        ArgumentNullException.ThrowIfNull(request);

        MapInfo[] maps = request.Maps.Where(value => value is not null).ToArray();
        NPCInfo[] npcs = request.Npcs.Where(value => value is not null).ToArray();
        MapInfo[] selectedMaps = maps.Where(value => value.Index == mapIndex).ToArray();
        NPCInfo[] selectedNpcs = npcs.Where(value => value.MapIndex == mapIndex).ToArray();
        var diagnostics = new List<ProjectPreflightDiagnostic>();
        var mapIndexes = maps.Select(value => value.Index).ToHashSet();
        var monsterIndexes = request.Monsters.Where(value => value is not null).Select(value => value.Index).ToHashSet();
        var mapBounds = request.MapBounds.Where(value => value is not null).ToDictionary(value => value.MapIndex);

        ValidateMaps(request.MapDirectory, selectedMaps, mapIndexes, monsterIndexes, mapBounds, diagnostics);
        ValidateNpcs(
            request.NpcDirectory,
            request.CSharpNpcDirectory,
            selectedNpcs,
            mapIndexes,
            mapBounds,
            request.Scripts,
            diagnostics);

        return new ProjectPreflightReport(diagnostics
            .OrderBy(value => value.Code, StringComparer.Ordinal)
            .ThenBy(value => value.Source, StringComparer.Ordinal)
            .ThenBy(value => value.Message, StringComparer.Ordinal)
            .ToArray());
    }

    private static void ValidateMaps(
        string mapDirectory,
        IReadOnlyCollection<MapInfo> maps,
        IReadOnlySet<int> mapIndexes,
        IReadOnlySet<int> monsterIndexes,
        IReadOnlyDictionary<int, ProjectMapBounds> mapBounds,
        List<ProjectPreflightDiagnostic> diagnostics)
    {
        foreach (MapInfo map in maps)
        {
            string source = $"MapInfo[{map.Index}]";
            if (!TryResolveWithinRoot(mapDirectory, (map.FileName ?? string.Empty) + ".map", out string mapPath))
            {
                Add(diagnostics, "LEG02-MAP-001", source, $"地图文件名越出地图目录：{map.FileName}");
            }
            else if (!CanReadFile(mapPath, out string reason))
                Add(diagnostics, "LEG02-MAP-001", source, $"地图文件不可读取：{mapPath}（{reason}）");

            for (int index = 0; index < map.Movements.Count; index++)
            {
                MovementInfo movement = map.Movements[index];
                if (!mapIndexes.Contains(movement.MapIndex))
                    Add(diagnostics, "LEG02-MAP-002", $"{source}.Movements[{index}]", $"目标地图 {movement.MapIndex} 不存在");
            }

            for (int index = 0; index < map.Respawns.Count; index++)
            {
                RespawnInfo respawn = map.Respawns[index];
                string respawnSource = $"{source}.Respawns[{index}]";
                if (!monsterIndexes.Contains(respawn.MonsterIndex))
                    Add(diagnostics, "LEG02-SPAWN-001", respawnSource, $"怪物 {respawn.MonsterIndex} 不存在");
                if (respawn.Count == 0)
                    Add(diagnostics, "LEG02-SPAWN-002", respawnSource, "刷新数量必须大于 0");
                if (respawn.Spread > 1000)
                    Add(diagnostics, "LEG02-SPAWN-003", respawnSource, "刷新范围异常大于 1000，请确认不是录入错误", ProjectPreflightSeverity.Warning);
                if (respawn.Delay == 0)
                    Add(diagnostics, "LEG02-SPAWN-004", respawnSource, "刷新时间为 0，请确认是否需要立即刷新", ProjectPreflightSeverity.Warning);
                if (respawn.Location.X < 0 || respawn.Location.Y < 0)
                    Add(diagnostics, "LEG02-SPAWN-005", respawnSource, "刷新坐标不能为负数");
                else if (mapBounds.TryGetValue(map.Index, out ProjectMapBounds bounds) && !IsWithin(respawn.Location, bounds))
                    Add(diagnostics, "LEG02-SPAWN-005", respawnSource, $"刷新坐标超出地图边界 {bounds.Width}x{bounds.Height}");
            }
        }
    }

    private static void ValidateNpcs(
        string npcDirectory,
        string csharpNpcDirectory,
        IReadOnlyCollection<NPCInfo> npcs,
        IReadOnlySet<int> mapIndexes,
        IReadOnlyDictionary<int, ProjectMapBounds> mapBounds,
        ScriptRegistry scripts,
        List<ProjectPreflightDiagnostic> diagnostics)
    {
        foreach (NPCInfo npc in npcs)
        {
            string source = $"NPCInfo[{npc.Index}:{npc.FileName}]";
            if (!mapIndexes.Contains(npc.MapIndex))
                Add(diagnostics, "LEG02-NPC-001", source, $"所属地图 {npc.MapIndex} 不存在");
            if (npc.Location.X < 0 || npc.Location.Y < 0)
                Add(diagnostics, "LEG02-NPC-002", source, "NPC 坐标不能为负数");
            else if (mapBounds.TryGetValue(npc.MapIndex, out ProjectMapBounds bounds) && !IsWithin(npc.Location, bounds))
                Add(diagnostics, "LEG02-NPC-002", source, $"NPC 坐标超出地图边界 {bounds.Width}x{bounds.Height}");
            if (!HasNpcScript(npcDirectory, csharpNpcDirectory, npc.FileName, scripts))
                Add(diagnostics, "LEG02-NPC-003", source, "找不到 TXT 脚本或 C# 脚本；若该 NPC 需要交互则发布前必须补齐", ProjectPreflightSeverity.Warning);
        }
    }

    private static void ValidateDrops(
        IEnumerable<DropTableDefinition> tables,
        Func<string, bool> itemExists,
        List<ProjectPreflightDiagnostic> diagnostics)
    {
        foreach (DropTableDefinition table in tables.OrderBy(value => value.Key, StringComparer.Ordinal))
        {
            for (int index = 0; index < table.Drops.Count; index++)
                ValidateDropEntry(table.Drops[index], $"{table.Key}.Drops[{index}]", itemExists, diagnostics);
        }
    }

    private static void ValidateDropEntry(
        DropEntryDefinition entry,
        string source,
        Func<string, bool> itemExists,
        List<ProjectPreflightDiagnostic> diagnostics)
    {
        if (entry is null)
        {
            Add(diagnostics, "LEG02-DROP-001", source, "掉落项不能为空");
            return;
        }

        if (entry.Chance <= 0)
            Add(diagnostics, "LEG02-DROP-002", source, "概率分母必须大于 0");
        if (entry.Weight <= 0)
            Add(diagnostics, "LEG02-DROP-003", source, "权重必须大于 0");

        bool hasItem = !string.IsNullOrWhiteSpace(entry.ItemName);
        bool hasGold = entry.Gold > 0;
        bool hasGroup = entry.Group is not null;
        if ((hasItem ? 1 : 0) + (hasGold ? 1 : 0) + (hasGroup ? 1 : 0) != 1)
            Add(diagnostics, "LEG02-DROP-004", source, "物品、金币和分组必须且只能设置一种");
        if (hasItem && !itemExists(entry.ItemName))
            Add(diagnostics, "LEG02-DROP-005", source, $"掉落物品不存在：{entry.ItemName}");
        if (hasItem && entry.Count == 0)
            Add(diagnostics, "LEG02-DROP-006", source, "掉落数量必须大于 0");
        if (hasGroup)
        {
            if (entry.Group.Drops.Count == 0)
                Add(diagnostics, "LEG02-DROP-007", source, "掉落分组不能为空");
            for (int index = 0; index < entry.Group.Drops.Count; index++)
                ValidateDropEntry(entry.Group.Drops[index], $"{source}.Group[{index}]", itemExists, diagnostics);
        }
    }

    private static void ValidateShops(
        IEnumerable<NpcShopDefinition> shops,
        IReadOnlySet<string> npcFileNames,
        Func<string, bool> itemExists,
        List<ProjectPreflightDiagnostic> diagnostics)
    {
        foreach (NpcShopDefinition shop in shops.OrderBy(value => value.Key, StringComparer.Ordinal))
        {
            if (!HasNpcReference(npcFileNames, shop.NpcFileName))
            {
                Add(diagnostics, "LEG02-SHOP-001", shop.Key, $"商店未匹配当前 NPC 记录：{shop.NpcFileName}", ProjectPreflightSeverity.Warning);
                continue;
            }
            for (int index = 0; index < shop.Goods.Count; index++)
            {
                string itemName = shop.Goods[index].ItemName ?? string.Empty;
                if (!itemExists(itemName))
                    Add(diagnostics, "LEG02-SHOP-002", $"{shop.Key}.Goods[{index}]", $"商品不存在：{shop.Goods[index].ItemName}");
            }
            for (int index = 0; index < shop.CraftRecipeOutputItemNames.Count; index++)
            {
                string itemName = shop.CraftRecipeOutputItemNames[index] ?? string.Empty;
                if (!itemExists(itemName))
                    Add(diagnostics, "LEG02-SHOP-003", $"{shop.Key}.Recipes[{index}]", $"合成产出不存在：{itemName}");
            }
        }
    }

    private static void ValidateRecipes(
        IEnumerable<RecipeDefinition> recipes,
        Func<string, bool> itemExists,
        List<ProjectPreflightDiagnostic> diagnostics)
    {
        foreach (RecipeDefinition recipe in recipes.OrderBy(value => value.Key, StringComparer.Ordinal))
        {
            string product = recipe.Key.StartsWith("recipe/", StringComparison.Ordinal)
                ? recipe.Key["recipe/".Length..]
                : string.Empty;
            if (string.IsNullOrWhiteSpace(product) || product.Contains('/') || !itemExists(product))
                Add(diagnostics, "LEG02-RECIPE-001", recipe.Key, $"配方成品不存在或 Key 无效：{product}");
            if (recipe.Amount == 0)
                Add(diagnostics, "LEG02-RECIPE-002", recipe.Key, "成品数量必须大于 0");
            for (int index = 0; index < recipe.Ingredients.Count; index++)
            {
                RecipeIngredientDefinition ingredient = recipe.Ingredients[index];
                string source = $"{recipe.Key}.Ingredients[{index}]";
                if (!itemExists(ingredient.ItemName))
                    Add(diagnostics, "LEG02-RECIPE-003", source, $"材料不存在：{ingredient.ItemName}");
                if (ingredient.Count == 0)
                    Add(diagnostics, "LEG02-RECIPE-004", source, "材料数量必须大于 0");
            }
            for (int index = 0; index < recipe.Tools.Count; index++)
            {
                string tool = recipe.Tools[index] ?? string.Empty;
                if (!itemExists(tool))
                    Add(diagnostics, "LEG02-RECIPE-005", $"{recipe.Key}.Tools[{index}]", $"工具不存在：{tool}");
            }
        }
    }

    private static void ValidateResources(
        IEnumerable<ProjectResourceReference> resources,
        List<ProjectPreflightDiagnostic> diagnostics)
    {
        foreach (ProjectResourceReference resource in resources.OrderBy(value => value.Source, StringComparer.Ordinal))
        {
            string source = string.IsNullOrWhiteSpace(resource.Source) ? resource.Path : resource.Source;
            if (string.IsNullOrWhiteSpace(resource.Path) || !Path.IsPathFullyQualified(resource.Path))
            {
                Add(diagnostics, "LEG02-RESOURCE-001", source, "启动资源必须使用调用方解析后的绝对路径");
                continue;
            }
            if (!File.Exists(resource.Path))
            {
                Add(diagnostics, "LEG02-RESOURCE-002", source, $"启动资源不存在：{resource.Path}");
                continue;
            }
            var info = new FileInfo(resource.Path);
            if (info.Length != resource.Size)
                Add(diagnostics, "LEG02-RESOURCE-003", source, $"资源大小不一致：expected={resource.Size} actual={info.Length}");
            try
            {
                using FileStream stream = File.OpenRead(resource.Path);
                string actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
                if (!string.Equals(actual, resource.Sha256, StringComparison.OrdinalIgnoreCase))
                    Add(diagnostics, "LEG02-RESOURCE-004", source, $"资源 SHA-256 不一致：expected={resource.Sha256} actual={actual}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Add(diagnostics, "LEG02-RESOURCE-002", source, $"启动资源不可读取：{ex.Message}");
            }
        }

        if (!resources.Any())
            Add(diagnostics, "LEG02-RESOURCE-000", "startupResources", "未提供启动资源清单，未执行文件与摘要检查", ProjectPreflightSeverity.Suggestion);
    }

    private static bool HasNpcScript(string npcDirectory, string csharpNpcDirectory, string npcFileName, ScriptRegistry scripts)
    {
        foreach (string alias in NpcFileNameAliases.Enumerate(npcFileName))
        {
            if (TryResolveWithinRoot(npcDirectory, alias + ".txt", out string path) && File.Exists(path))
                return true;
            if (TryResolveWithinRoot(csharpNpcDirectory, alias + ".cs", out string csharpPath) && File.Exists(csharpPath))
                return true;
            string hookKey = LogicKey.NormalizeOrThrow(ScriptHookKeys.OnNpcPage(alias, "[@MAIN]"));
            if (scripts.Handlers.ContainsKey(hookKey))
                return true;
            string prefix = LogicKey.NormalizeOrThrow($"NPCs/{alias}") + "/";
            if (scripts.Handlers.Keys.Any(key => key.StartsWith(prefix, StringComparison.Ordinal)))
                return true;
        }
        return false;
    }

    private static bool CanReadFile(string path, out string reason)
    {
        try
        {
            using FileStream stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (stream.Length < 4)
            {
                reason = "文件长度小于地图头";
                return false;
            }
            reason = string.Empty;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            reason = ex.Message;
            return false;
        }
    }

    private static string NormalizePath(string value) => (value ?? string.Empty).Trim().Replace('\\', '/');

    private static bool IsWithin(Point point, ProjectMapBounds bounds) =>
        bounds.Width > 0 && bounds.Height > 0 && point.X < bounds.Width && point.Y < bounds.Height;

    private static bool HasNpcReference(IReadOnlySet<string> npcFileNames, string value)
    {
        foreach (string alias in NpcFileNameAliases.Enumerate(value))
            if (npcFileNames.Contains(NormalizePath(alias)))
                return true;
        return false;
    }

    private static bool TryResolveWithinRoot(string root, string relativePath, out string resolved)
    {
        resolved = string.Empty;
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(relativePath) || Path.IsPathFullyQualified(relativePath))
            return false;
        try
        {
            string fullRoot = Path.GetFullPath(root);
            string candidate = Path.GetFullPath(Path.Combine(fullRoot, relativePath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar)));
            string relative = Path.GetRelativePath(fullRoot, candidate);
            if (relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                return false;
            resolved = candidate;
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static void Add(
        List<ProjectPreflightDiagnostic> diagnostics,
        string code,
        string source,
        string message,
        ProjectPreflightSeverity severity = ProjectPreflightSeverity.Error) =>
        diagnostics.Add(new ProjectPreflightDiagnostic(code, severity, source, message));
}
