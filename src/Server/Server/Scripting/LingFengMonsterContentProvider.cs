using System.Collections.ObjectModel;
using System.Globalization;
using Server.MirDatabase;

namespace Server.Scripting
{
    public sealed record LingFengMonsterSkill(string Name, int Level, int NewLevel);

    public sealed class LingFengMonsterContentSnapshot
    {
        internal LingFengMonsterContentSnapshot(
            string monsterName,
            IReadOnlyDictionary<string, string> options,
            IReadOnlyList<ItemInfo> equipment,
            IReadOnlyList<LingFengMonsterSkill> skills,
            IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> smartSections)
        {
            MonsterName = monsterName;
            Options = options;
            Equipment = equipment;
            Skills = skills;
            SmartSections = smartSections;
        }

        public string MonsterName { get; }
        public IReadOnlyDictionary<string, string> Options { get; }
        public IReadOnlyList<ItemInfo> Equipment { get; }
        public IReadOnlyList<LingFengMonsterSkill> Skills { get; }
        public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> SmartSections { get; }

        public bool DropUseItems => GetInt("DropUseItem") != 0 || GetInt("DieDropUseItemRate") > 0;
        public int DropUseItemRate => Math.Max(0,
            GetInt("DieDropUseItemRate") > 0 ? GetInt("DieDropUseItemRate") : GetInt("DropUseItemRate"));
        public bool RunWithAttack => GetInt("RunWithAttack") != 0;
        public int RunWithAttackRate => Math.Max(0, GetInt("RunWithAttackRate"));

        private int GetInt(string key) => Options.TryGetValue(key, out string value) &&
                                           int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result)
            ? result
            : 0;
    }

    internal sealed class LingFengMonsterContentProvider
    {
        private static readonly HashSet<string> NumericInfoKeys = new(StringComparer.OrdinalIgnoreCase)
        {
            "Job", "Gender", "Hair", "DropUseItem", "DropUseItemRate", "DieDropUseItemRate",
            "DieDropBagItem", "DieDropItemRate", "DropItem", "ButchUseItem", "ButchUseItemRate",
            "ButchUserItemRate", "ButchListItem", "ButchItemTrigger", "ButchChargeMode",
            "ButchChargeClass", "ButchChargeCount", "ButchRate", "ButchCloneItem",
            "OnlyButchItemDelGold", "GetRestrictRange", "RestrictRange", "NonUseSpellPoint",
            "RunWithAttack", "RunWithAttackRate", "NoAttackMode", "ProtectStatus"
        };

        private sealed record Parsed(
            string Name,
            IReadOnlyDictionary<string, string> Options,
            IReadOnlyList<string> Equipment,
            IReadOnlyList<LingFengMonsterSkill> Skills,
            IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> SmartSections);

        private readonly IReadOnlyDictionary<string, Parsed> _definitions;

        private LingFengMonsterContentProvider(IReadOnlyDictionary<string, Parsed> definitions) =>
            _definitions = definitions;

        internal IEnumerable<LingFengDependencyRequirement> GetDependencyRequirements()
        {
            foreach ((string name, Parsed definition) in _definitions)
            {
                yield return new LingFengDependencyRequirement(
                    LingFengDependencyKind.Monster, name, LingFengDependencyLevel.E1,
                    $"MonsterContent/{name}");
                foreach (string itemName in definition.Equipment)
                    yield return new LingFengDependencyRequirement(
                        LingFengDependencyKind.ItemName, itemName, LingFengDependencyLevel.E1,
                        $"MonsterContent/{name}");
            }
        }

        public static bool TryCreate(
            IEnumerable<TextFileDefinition> useItemFiles,
            IEnumerable<TextFileDefinition> smartFiles,
            out LingFengMonsterContentProvider provider,
            out IReadOnlyList<string> errors)
        {
            var failures = new List<string>();
            var parsed = new Dictionary<string, Parsed>(StringComparer.OrdinalIgnoreCase);
            var useItemNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var smartDefinitions = new Dictionary<string,
                IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>>(StringComparer.OrdinalIgnoreCase);
            foreach (TextFileDefinition file in useItemFiles ?? Array.Empty<TextFileDefinition>())
            {
                string name = Path.GetFileName(file.SourcePath);
                name = Path.GetFileNameWithoutExtension(name);
                if (!useItemNames.Add(name))
                {
                    failures.Add($"LFENV11-CONTENT-007：怪物 {name} 存在重复的 MonUseItems 定义。");
                    continue;
                }
                ParseUseItems(file, name, out IReadOnlyDictionary<string, string> options,
                    out IReadOnlyList<string> equipment, out IReadOnlyList<LingFengMonsterSkill> skills, failures);
                parsed[name] = new Parsed(name, options, equipment, skills,
                    new ReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>(
                        new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)));
            }
            foreach (TextFileDefinition file in smartFiles ?? Array.Empty<TextFileDefinition>())
            {
                string name = Path.GetFileNameWithoutExtension(file.SourcePath);
                IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> smart = ParseIni(file, failures);
                if (smartDefinitions.TryGetValue(name, out var previousSmart))
                {
                    if (!SmartDefinitionsEqual(previousSmart, smart))
                        failures.Add($"LFENV11-CONTENT-008：怪物 {name} 存在冲突的 SmartMonster 定义。");
                    continue;
                }
                smartDefinitions.Add(name, smart);
                if (parsed.TryGetValue(name, out Parsed existing))
                    parsed[name] = existing with { SmartSections = smart };
                else
                    parsed[name] = new Parsed(name,
                        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>()),
                        Array.Empty<string>(), Array.Empty<LingFengMonsterSkill>(), smart);
            }
            errors = failures.AsReadOnly();
            provider = failures.Count == 0 ? new LingFengMonsterContentProvider(parsed) : null;
            return failures.Count == 0;
        }

        private static bool SmartDefinitionsEqual(
            IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> left,
            IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> right) =>
            left.Count == right.Count && left.All(section =>
                right.TryGetValue(section.Key, out IReadOnlyDictionary<string, string> other) &&
                section.Value.Count == other.Count && section.Value.All(pair =>
                    other.TryGetValue(pair.Key, out string value) &&
                    string.Equals(pair.Value, value, StringComparison.Ordinal)));

        public IReadOnlyList<string> Apply(IEnumerable<MonsterInfo> monsters, Func<string, ItemInfo> itemResolver)
        {
            var failures = new List<string>();
            MonsterInfo[] monsterArray = (monsters ?? Array.Empty<MonsterInfo>())
                .Where(info => info != null)
                .ToArray();
            var byName = monsterArray
                .Where(info => info != null)
                .ToDictionary(info => info.Name, StringComparer.OrdinalIgnoreCase);
            var snapshots = new Dictionary<MonsterInfo, LingFengMonsterContentSnapshot>();
            foreach ((string name, Parsed definition) in _definitions)
            {
                if (!byName.TryGetValue(name, out MonsterInfo monster)) continue;
                var equipment = new List<ItemInfo>();
                foreach (string itemName in definition.Equipment)
                {
                    ItemInfo item = itemResolver?.Invoke(itemName);
                    if (item == null)
                    {
                        failures.Add($"LFENV11-CONTENT-006：怪物 {name} 的装备不存在：{itemName}。");
                        continue;
                    }
                    equipment.Add(item);
                }
                snapshots[monster] = new LingFengMonsterContentSnapshot(
                    name, definition.Options, equipment.AsReadOnly(), definition.Skills, definition.SmartSections);
            }
            if (failures.Count > 0) return failures.AsReadOnly();
            foreach (MonsterInfo monster in monsterArray)
                monster.LingFengContent = snapshots.TryGetValue(monster, out LingFengMonsterContentSnapshot snapshot)
                    ? snapshot
                    : null;
            return failures.AsReadOnly();
        }

        private static void ParseUseItems(
            TextFileDefinition file,
            string name,
            out IReadOnlyDictionary<string, string> options,
            out IReadOnlyList<string> equipment,
            out IReadOnlyList<LingFengMonsterSkill> skills,
            ICollection<string> errors)
        {
            var info = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var items = new SortedDictionary<int, string>();
            var skillValues = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);
            string section = string.Empty;
            for (int index = 0; index < file.Lines.Count; index++)
            {
                string line = Strip(file.Lines[index]);
                if (line.Length == 0) continue;
                if (line.StartsWith('[') && line.EndsWith(']'))
                {
                    section = line[1..^1].Trim();
                    if (section.Length == 0)
                        errors.Add($"LFENV11-CONTENT-009：节名不能为空（{file.GetSourceLocation(index)}）。");
                    continue;
                }
                int equals = line.IndexOf('=');
                if (equals <= 0)
                {
                    errors.Add($"LFENV11-CONTENT-001：无效键值行（{file.GetSourceLocation(index)}）。");
                    continue;
                }
                string key = line[..equals].Trim();
                string value = line[(equals + 1)..].Trim();
                if (section.Equals("Info", StringComparison.OrdinalIgnoreCase))
                {
                    if (key.Equals("UseSkill", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!info.TryAdd(key, value))
                            errors.Add($"LFENV11-CONTENT-010：Info 字段重复 {key}（{file.GetSourceLocation(index)}）。");
                        continue;
                    }
                    if (!NumericInfoKeys.Contains(key) ||
                        !int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                        errors.Add($"LFENV11-CONTENT-002：未知或非整数 Info 字段 {key}（{file.GetSourceLocation(index)}）。");
                    else if (!info.TryAdd(key, value))
                        errors.Add($"LFENV11-CONTENT-010：Info 字段重复 {key}（{file.GetSourceLocation(index)}）。");
                }
                else if (section.Equals("UseItems", StringComparison.OrdinalIgnoreCase))
                {
                    value = value.TrimEnd('\u007F').TrimEnd();
                    if (!key.StartsWith("UseItems", StringComparison.OrdinalIgnoreCase) ||
                        !int.TryParse(key.AsSpan(8), out int slot) || slot < 0 || slot > 99)
                        errors.Add($"LFENV11-CONTENT-003：无效装备槽 {key}（{file.GetSourceLocation(index)}）。");
                    else if (value.Length > 0 && !items.TryAdd(slot, value))
                        errors.Add($"LFENV11-CONTENT-011：装备槽重复 {key}（{file.GetSourceLocation(index)}）。");
                }
                else
                {
                    if (section.Length == 0 || !(key.Equals("Level", StringComparison.OrdinalIgnoreCase) ||
                                                key.Equals("NewLevel", StringComparison.OrdinalIgnoreCase)) ||
                        !int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int level))
                        errors.Add($"LFENV11-CONTENT-004：无效技能字段 {section}.{key}（{file.GetSourceLocation(index)}）。");
                    else
                    {
                        if (!skillValues.TryGetValue(section, out Dictionary<string, int> values))
                            skillValues.Add(section, values = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase));
                        if (!values.TryAdd(key, level))
                            errors.Add($"LFENV11-CONTENT-012：技能字段重复 {section}.{key}（{file.GetSourceLocation(index)}）。");
                    }
                }
            }
            options = new ReadOnlyDictionary<string, string>(info);
            equipment = Array.AsReadOnly(items.Values.ToArray());
            skills = Array.AsReadOnly(skillValues.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new LingFengMonsterSkill(pair.Key,
                    pair.Value.GetValueOrDefault("Level"), pair.Value.GetValueOrDefault("NewLevel"))).ToArray());
        }

        private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> ParseIni(
            TextFileDefinition file,
            ICollection<string> errors)
        {
            var sections = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            string section = string.Empty;
            for (int index = 0; index < file.Lines.Count; index++)
            {
                string line = Strip(file.Lines[index]);
                if (line.Length == 0) continue;
                if (line.StartsWith('[') && line.EndsWith(']'))
                {
                    section = line[1..^1].Trim();
                    if (section.Length == 0)
                    {
                        errors.Add($"LFENV11-CONTENT-009：节名不能为空（{file.GetSourceLocation(index)}）。");
                        continue;
                    }
                    if (!sections.TryAdd(section, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)))
                        errors.Add($"LFENV11-CONTENT-005：SmartMonster 重复节 {section}（{file.GetSourceLocation(index)}）。");
                    continue;
                }
                int equals = line.IndexOf('=');
                if (section.Length == 0 || equals <= 0 ||
                    !sections[section].TryAdd(line[..equals].Trim(), line[(equals + 1)..].Trim()))
                    errors.Add($"LFENV11-CONTENT-005：SmartMonster 无效或重复键（{file.GetSourceLocation(index)}）。");
            }
            return new ReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>(
                sections.ToDictionary(pair => pair.Key,
                    pair => (IReadOnlyDictionary<string, string>)new ReadOnlyDictionary<string, string>(pair.Value),
                    StringComparer.OrdinalIgnoreCase));
        }

        private static string Strip(string line)
        {
            string value = (line ?? string.Empty).Trim();
            int comment = value.IndexOf(';');
            return (comment < 0 ? value : value[..comment]).Trim();
        }
    }
}
