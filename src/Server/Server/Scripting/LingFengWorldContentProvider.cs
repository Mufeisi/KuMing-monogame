using System.Collections.ObjectModel;
using System.Drawing;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Server.MirDatabase;
using Server.MirObjects;

namespace Server.Scripting
{
    public sealed record LingFengMapQuestRule(
        string MapAlias,
        int FlagIndex,
        bool FlagValue,
        string MonsterName,
        string ScriptKey);

    public sealed class LingFengWorldContentPlan
    {
        private readonly IReadOnlyList<Action> _commits;
        private int _committed;

        internal LingFengWorldContentPlan(IReadOnlyList<Action> commits) => _commits = commits;

        internal void Commit()
        {
            if (Interlocked.Exchange(ref _committed, 1) != 0)
                throw new InvalidOperationException("LFENV12-WORLD-PLAN：世界内容计划只能提交一次。");
            for (int index = 0; index < _commits.Count; index++)
                _commits[index]();
        }
    }

    public sealed class LingFengWorldContentProvider
    {
        private static readonly HashSet<string> KnownMapOptions = new(StringComparer.OrdinalIgnoreCase)
        {
            "ALLOWUSEMYSHOP", "DARK", "DAY", "EXPRATE", "FB", "FIGHT", "FIGHT2", "KILLFUNC",
            "KILLMON", "MINE", "NEEDSET_OFF", "NEEDSET_ON", "NOALLOWUSEITEMS", "NOAUTOONLINE",
            "NOAUTORANGEPICKITEM", "NODEAL", "NODEARRECALL", "NODRUG", "NOGUILDRECAL",
            "NOGUILDRECALL", "NOMANNOMON", "NOMASTERRECALL", "NOPOSITIONMOVE", "NORANDOMMOVE",
            "NORECALL", "NORECONNECT", "NORUNHUMAN", "NORUNMON", "NOSAFEPOSITIONMOVE", "NOSHOPPING",
            "NOTALLOWUSEITEMS", "NOTALLOWUSEMAGIC", "NOTHROWITEM", "ONKILLMON", "SAFE", "SAYLEVEL",
            "TIMEMAP"
        };

        private sealed record MapDefinition(
            string Alias,
            string FileName,
            string Title,
            IReadOnlyDictionary<string, string> Options);

        private sealed record MovementDefinition(
            string SourceMap,
            Point Source,
            string DestinationMap,
            Point Destination);

        private sealed record RespawnDefinition(
            string MapAlias,
            Point Location,
            string MonsterName,
            ushort Spread,
            ushort Count,
            ushort Delay,
            ushort RandomDelay,
            byte Direction,
            ushort RespawnTicks);

        private readonly IReadOnlyDictionary<string, MapDefinition> _maps;
        private readonly IReadOnlyList<MovementDefinition> _movements;
        private readonly IReadOnlyList<RespawnDefinition> _respawns;

        private LingFengWorldContentProvider(
            IReadOnlyDictionary<string, MapDefinition> maps,
            IReadOnlyList<MovementDefinition> movements,
            IReadOnlyList<RespawnDefinition> respawns,
            IReadOnlyList<LingFengMapQuestRule> mapQuests,
            string fingerprint)
        {
            _maps = maps;
            _movements = movements;
            _respawns = respawns;
            MapQuests = mapQuests;
            Fingerprint = fingerprint;
        }

        public IReadOnlyList<LingFengMapQuestRule> MapQuests { get; }
        public string Fingerprint { get; }

        public static bool TryCreate(
            TextFileDefinition mapInfo,
            TextFileDefinition mongen,
            TextFileDefinition mapQuest,
            IReadOnlyDictionary<string, TextFileDefinition> mapQuestScripts,
            out LingFengWorldContentProvider provider,
            out IReadOnlyList<string> errors)
        {
            var failures = new List<string>();
            var maps = new Dictionary<string, MapDefinition>(StringComparer.OrdinalIgnoreCase);
            var movements = new List<MovementDefinition>();
            var respawns = new List<RespawnDefinition>();
            var quests = new List<LingFengMapQuestRule>();
            var fingerprint = new StringBuilder();

            ParseMapInfo(mapInfo, maps, movements, failures, fingerprint);
            ParseMongen(mongen, respawns, failures, fingerprint);
            ParseMapQuest(mapQuest, mapQuestScripts ??
                new Dictionary<string, TextFileDefinition>(StringComparer.Ordinal), quests, failures, fingerprint);

            errors = failures.AsReadOnly();
            provider = failures.Count == 0
                ? new LingFengWorldContentProvider(
                    new ReadOnlyDictionary<string, MapDefinition>(maps),
                    Array.AsReadOnly(movements.ToArray()),
                    Array.AsReadOnly(respawns.ToArray()),
                    Array.AsReadOnly(quests.ToArray()),
                    Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint.ToString()))))
                : null;
            return failures.Count == 0;
        }

        public bool TryBuildPlan(
            IEnumerable<MapInfo> maps,
            IEnumerable<MonsterInfo> monsters,
            out LingFengWorldContentPlan plan,
            out IReadOnlyList<string> errors)
        {
            var failures = new List<string>();
            MapInfo[] mapArray = (maps ?? Array.Empty<MapInfo>()).Where(map => map != null).ToArray();
            MonsterInfo[] monsterArray = (monsters ?? Array.Empty<MonsterInfo>()).Where(monster => monster != null).ToArray();
            var byFile = mapArray.GroupBy(map => map.FileName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
            var byMonster = monsterArray.GroupBy(monster => monster.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            var resolvedMaps = new Dictionary<string, MapInfo>(StringComparer.OrdinalIgnoreCase);
            var commits = new List<Action>();

            foreach (IGrouping<string, MapDefinition> definitionGroup in
                     _maps.Values.GroupBy(definition => definition.FileName, StringComparer.OrdinalIgnoreCase))
            {
                MapDefinition[] definitions = definitionGroup.ToArray();
                if (!byFile.TryGetValue(definitionGroup.Key, out MapInfo[] candidates) || candidates.Length == 0)
                {
                    failures.Add($"LFENV12-MAP-DEPENDENCY：逻辑地图 {string.Join(',', definitions.Select(item => item.Alias))} 缺少物理地图记录 {definitionGroup.Key}。");
                    continue;
                }
                if (candidates.Length != definitions.Length)
                {
                    failures.Add($"LFENV12-MAP-DEPENDENCY：物理地图 {definitionGroup.Key} 的逻辑别名数 {definitions.Length} 与数据库实例数 {candidates.Length} 不一致。");
                    continue;
                }
                MapInfo[] orderedCandidates = candidates.OrderBy(item => item.Index).ToArray();
                for (int index = 0; index < definitions.Length; index++)
                {
                    MapDefinition definition = definitions[index];
                    MapInfo target = orderedCandidates[index];
                    resolvedMaps.Add(definition.Alias, target);
                    commits.Add(() => ApplyMapDefinition(target, definition));
                }
            }

            foreach (MapDefinition definition in _maps.Values)
            {
                if (!definition.Options.TryGetValue("NORECONNECT", out string reconnectTarget)) continue;
                if (!string.IsNullOrWhiteSpace(reconnectTarget) &&
                    !TryResolveMap(reconnectTarget, resolvedMaps, byFile, out _))
                    failures.Add($"LFENV12-MAP-DEPENDENCY：地图 {definition.Alias} 的 NORECONNECT 目标不存在 {reconnectTarget}。");
            }

            foreach (MovementDefinition movement in _movements)
            {
                if (!TryResolveMap(movement.SourceMap, resolvedMaps, byFile, out MapInfo source) ||
                    !TryResolveMap(movement.DestinationMap, resolvedMaps, byFile, out MapInfo destination))
                {
                    failures.Add($"LFENV12-MAP-DEPENDENCY：传送引用地图不存在 {movement.SourceMap}->{movement.DestinationMap}。");
                    continue;
                }
                commits.Add(() =>
                {
                    source.CaptureLingFengPersistenceBaseline();
                    source.Movements.Add(new MovementInfo
                    {
                        Source = movement.Source,
                        MapIndex = destination.Index,
                        Destination = movement.Destination
                    });
                });
            }

            int respawnIndex = mapArray.SelectMany(map => map.Respawns ?? new List<RespawnInfo>())
                .Select(respawn => respawn.RespawnIndex).DefaultIfEmpty().Max();
            foreach (RespawnDefinition respawn in _respawns)
            {
                if (!TryResolveMap(respawn.MapAlias, resolvedMaps, byFile, out MapInfo map))
                {
                    failures.Add($"LFENV12-MONGEN-DEPENDENCY：刷怪地图不存在 {respawn.MapAlias}。");
                    continue;
                }
                if (!byMonster.TryGetValue(respawn.MonsterName, out MonsterInfo monster))
                {
                    failures.Add($"LFENV12-MONGEN-DEPENDENCY：刷怪怪物不存在 {respawn.MonsterName}。");
                    continue;
                }
                int assignedIndex = ++respawnIndex;
                commits.Add(() =>
                {
                    map.CaptureLingFengPersistenceBaseline();
                    map.Respawns.Add(new RespawnInfo
                    {
                        MonsterIndex = monster.Index,
                        Location = respawn.Location,
                        Spread = respawn.Spread,
                        Count = respawn.Count,
                        Delay = respawn.Delay,
                        RandomDelay = respawn.RandomDelay,
                        Direction = respawn.Direction,
                        RespawnTicks = respawn.RespawnTicks,
                        SaveRespawnTime = respawn.RespawnTicks > 0,
                        RespawnIndex = assignedIndex
                    });
                });
            }

            foreach (LingFengMapQuestRule quest in MapQuests)
            {
                if (!quest.MapAlias.Equals("*", StringComparison.Ordinal) &&
                    !TryResolveMap(quest.MapAlias, resolvedMaps, byFile, out _))
                    failures.Add($"LFENV12-MAPQUEST-DEPENDENCY：地图任务引用地图不存在 {quest.MapAlias}。");
                if (!quest.MonsterName.Equals("*", StringComparison.Ordinal) &&
                    !byMonster.ContainsKey(quest.MonsterName))
                    failures.Add($"LFENV12-MAPQUEST-DEPENDENCY：地图任务引用怪物不存在 {quest.MonsterName}。");
            }

            errors = failures.AsReadOnly();
            plan = failures.Count == 0 ? new LingFengWorldContentPlan(commits.AsReadOnly()) : null;
            return failures.Count == 0;
        }

        public IReadOnlyList<LingFengMapQuestRule> MatchMapQuests(MonsterObject monster, PlayerObject player)
        {
            if (monster?.CurrentMap?.Info == null || player?.Info?.Flags == null)
                return Array.Empty<LingFengMapQuestRule>();
            string mapAlias = monster.CurrentMap.Info.LingFengAlias;
            string mapFileName = monster.CurrentMap.Info.FileName;
            return MapQuests.Where(rule =>
                    (rule.MapAlias == "*" ||
                     rule.MapAlias.Equals(mapAlias, StringComparison.OrdinalIgnoreCase) ||
                     rule.MapAlias.Equals(mapFileName, StringComparison.OrdinalIgnoreCase)) &&
                    (rule.MonsterName == "*" || rule.MonsterName.Equals(monster.Info.Name, StringComparison.OrdinalIgnoreCase)) &&
                    rule.FlagIndex >= 0 && rule.FlagIndex < player.Info.Flags.Length &&
                    player.Info.Flags[rule.FlagIndex] == rule.FlagValue)
                .ToArray();
        }

        private static void ApplyMapDefinition(MapInfo target, MapDefinition definition)
        {
            target.CaptureLingFengPersistenceBaseline();
            target.LingFengAlias = definition.Alias;
            target.Title = definition.Title;
            target.LingFengOptions = definition.Options;
            foreach ((string name, string argument) in definition.Options)
            {
                switch (name.ToUpperInvariant())
                {
                    case "NORECONNECT":
                        target.NoReconnect = true;
                        target.NoReconnectMap = argument;
                        break;
                    case "NORECALL":
                    case "NODEARRECALL":
                    case "NOGUILDRECALL":
                    case "NOGUILDRECAL":
                    case "NOMASTERRECALL": target.NoRecall = true; break;
                    case "NORANDOMMOVE": target.NoRandom = true; break;
                    case "NODRUG": target.NoDrug = true; break;
                    case "NOPOSITIONMOVE":
                    case "NOSAFEPOSITIONMOVE": target.NoPosition = true; break;
                    case "NOTHROWITEM": target.NoThrowItem = true; break;
                    case "FIGHT":
                    case "FIGHT2": target.Fight = true; break;
                    case "SAFE": target.Fight = false; break;
                    case "DARK": target.Light = LightSetting.Night; break;
                    case "DAY": target.Light = LightSetting.Day; break;
                }
            }
        }

        private static bool TryResolveMap(
            string name,
            IReadOnlyDictionary<string, MapInfo> resolvedMaps,
            IReadOnlyDictionary<string, MapInfo[]> byFile,
            out MapInfo map)
        {
            if (resolvedMaps.TryGetValue(name, out map)) return true;
            if (byFile.TryGetValue(name, out MapInfo[] candidates) && candidates.Length == 1)
            {
                map = candidates[0];
                return true;
            }
            map = null;
            return false;
        }

        private static void ParseMapInfo(
            TextFileDefinition source,
            IDictionary<string, MapDefinition> maps,
            ICollection<MovementDefinition> movements,
            ICollection<string> errors,
            StringBuilder fingerprint)
        {
            if (source == null) return;
            for (int index = 0; index < source.Lines.Count; index++)
            {
                string line = Clean(source.Lines[index]);
                if (line.Length == 0) continue;
                fingerprint.Append("M|").Append(line).Append('\n');
                if (line.StartsWith('['))
                {
                    Match header = Regex.Match(line, @"^\[(?<body>[^\]]+)\](?<options>.*)$",
                        RegexOptions.CultureInvariant);
                    if (!header.Success)
                    {
                        errors.Add($"LFENV12-MAP-SYNTAX：地图头格式无效（{source.GetSourceLocation(index)}）。");
                        continue;
                    }
                    string body = header.Groups["body"].Value.Trim();
                    string[] bodyParts = body.Split((char[])null, 2, StringSplitOptions.RemoveEmptyEntries);
                    if (bodyParts.Length == 0)
                    {
                        errors.Add($"LFENV12-MAP-SYNTAX：地图头不能为空（{source.GetSourceLocation(index)}）。");
                        continue;
                    }
                    string[] names = bodyParts[0].Split('|', 2, StringSplitOptions.TrimEntries);
                    string alias = names[0];
                    string fileName = names.Length == 2 ? names[1] : names[0];
                    string title = bodyParts.Length == 2 ? bodyParts[1].Trim() : alias;
                    if (alias.Length == 0 || fileName.Length == 0 || title.Length == 0 || maps.ContainsKey(alias))
                    {
                        errors.Add($"LFENV12-MAP-DUPLICATE：地图别名无效或重复 {alias}（{source.GetSourceLocation(index)}）。");
                        continue;
                    }
                    if (!TryParseOptions(header.Groups["options"].Value, source, index,
                            out IReadOnlyDictionary<string, string> options, errors)) continue;
                    maps.Add(alias, new MapDefinition(alias, fileName, title, options));
                    continue;
                }

                Match movement = Regex.Match(line,
                    @"^(?<source>\S+)\s+(?<sx>\d+)(?:\s*[,，:]\s*|\s+)(?<sy>\d+)\s*->\s*(?<destination>\S+)\s+(?<dx>\d+)(?:\s*[,，:]\s*|\s+)(?<dy>\d+)$",
                    RegexOptions.CultureInvariant);
                if (!movement.Success || !TryPoint(movement, "sx", "sy", out Point sourcePoint) ||
                    !TryPoint(movement, "dx", "dy", out Point destinationPoint))
                {
                    errors.Add($"LFENV12-MAP-SYNTAX：无法解析地图行（{source.GetSourceLocation(index)}）。");
                    continue;
                }
                movements.Add(new MovementDefinition(
                    movement.Groups["source"].Value, sourcePoint,
                    movement.Groups["destination"].Value, destinationPoint));
            }
        }

        private static bool TryParseOptions(
            string raw,
            TextFileDefinition source,
            int lineIndex,
            out IReadOnlyDictionary<string, string> options,
            ICollection<string> errors)
        {
            var parsed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            int offset = 0;
            while (offset < raw.Length)
            {
                while (offset < raw.Length && char.IsWhiteSpace(raw[offset])) offset++;
                if (offset >= raw.Length) break;
                int nameStart = offset;
                while (offset < raw.Length && (char.IsLetterOrDigit(raw[offset]) || raw[offset] == '_')) offset++;
                if (offset == nameStart)
                {
                    errors.Add($"LFENV12-MAP-OPTION：地图选项包含非法字符（{source.GetSourceLocation(lineIndex)}）。");
                    options = null;
                    return false;
                }
                string name = raw[nameStart..offset].ToUpperInvariant();
                if (!KnownMapOptions.Contains(name))
                {
                    errors.Add($"LFENV12-MAP-OPTION：未知地图选项 {name}（{source.GetSourceLocation(lineIndex)}）。");
                    options = null;
                    return false;
                }
                string argument = string.Empty;
                if (offset < raw.Length && raw[offset] == '(')
                {
                    int start = ++offset;
                    int depth = 1;
                    while (offset < raw.Length && depth > 0)
                    {
                        if (raw[offset] == '(') depth++;
                        else if (raw[offset] == ')') depth--;
                        offset++;
                    }
                    if (depth != 0)
                    {
                        errors.Add($"LFENV12-MAP-OPTION：地图选项括号未闭合（{source.GetSourceLocation(lineIndex)}）。");
                        options = null;
                        return false;
                    }
                    argument = raw[start..(offset - 1)].Trim();
                }
                // 翎风 MapInfo 允许重复属性；标志位重复是幂等的，有参属性按原文件最后一项生效。
                parsed[name] = argument;
            }
            options = new ReadOnlyDictionary<string, string>(parsed);
            return true;
        }

        private static void ParseMongen(
            TextFileDefinition source,
            ICollection<RespawnDefinition> respawns,
            ICollection<string> errors,
            StringBuilder fingerprint)
        {
            if (source == null) return;
            for (int index = 0; index < source.Lines.Count; index++)
            {
                string line = Clean(source.Lines[index]);
                if (line.Length == 0) continue;
                fingerprint.Append("G|").Append(line).Append('\n');
                string[] parts = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 8 || parts.Length > 10 ||
                    !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int x) ||
                    !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out int y) || x < 0 || y < 0 ||
                    !ushort.TryParse(parts[4], NumberStyles.None, CultureInfo.InvariantCulture, out ushort spread) ||
                    !ushort.TryParse(parts[5], NumberStyles.None, CultureInfo.InvariantCulture, out ushort count) || count == 0 ||
                    !ushort.TryParse(parts[6], NumberStyles.None, CultureInfo.InvariantCulture, out ushort delay))
                {
                    errors.Add($"LFENV12-MONGEN-SYNTAX：刷怪行必须为 8-10 列有效数值（{source.GetSourceLocation(index)}）。");
                    continue;
                }
                ushort randomDelay = 0;
                byte direction;
                ushort respawnTicks = 0;
                int directionIndex = parts.Length == 8 ? 7 : 8;
                if ((parts.Length >= 9 && !ushort.TryParse(parts[7], NumberStyles.None, CultureInfo.InvariantCulture, out randomDelay)) ||
                    !byte.TryParse(parts[directionIndex], NumberStyles.None, CultureInfo.InvariantCulture, out direction) ||
                    (parts.Length == 10 && !ushort.TryParse(parts[9], NumberStyles.None, CultureInfo.InvariantCulture, out respawnTicks)))
                {
                    errors.Add($"LFENV12-MONGEN-SYNTAX：刷怪扩展列无效（{source.GetSourceLocation(index)}）。");
                    continue;
                }
                respawns.Add(new RespawnDefinition(parts[0], new Point(x, y), parts[3],
                    spread, count, delay, randomDelay, direction, respawnTicks));
            }
        }

        private static void ParseMapQuest(
            TextFileDefinition source,
            IReadOnlyDictionary<string, TextFileDefinition> scripts,
            ICollection<LingFengMapQuestRule> quests,
            ICollection<string> errors,
            StringBuilder fingerprint)
        {
            if (source == null) return;
            for (int index = 0; index < source.Lines.Count; index++)
            {
                string line = Clean(source.Lines[index]);
                if (line.Length == 0) continue;
                string[] parts = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 6 && parts[0].Equals("地图号", StringComparison.OrdinalIgnoreCase)) continue;
                fingerprint.Append("Q|").Append(line).Append('\n');
                Match flag = parts.Length >= 2 ? Regex.Match(parts[1], @"^\[(\d+)\]$") : Match.Empty;
                if (parts.Length != 6 || !flag.Success ||
                    !int.TryParse(flag.Groups[1].Value, out int flagIndex) || flagIndex < 0 || flagIndex >= Globals.FlagIndexCount ||
                    !int.TryParse(parts[2], out int flagValue) || flagValue is < 0 or > 1 || parts[4] != "*")
                {
                    errors.Add($"LFENV12-MAPQUEST-SYNTAX：地图任务行格式无效（{source.GetSourceLocation(index)}）。");
                    continue;
                }
                string nested = parts[5].Replace('\\', '/').Trim('/');
                if (!LogicKey.TryNormalize("MapQuests/" + nested, out string key) ||
                    !scripts.TryGetValue(key, out TextFileDefinition page) || !ContainsMainPage(page))
                {
                    errors.Add($"LFENV12-MAPQUEST-PAGE：地图任务页面不存在或缺少 [@MAIN] {parts[5]}（{source.GetSourceLocation(index)}）。");
                    continue;
                }
                var rule = new LingFengMapQuestRule(parts[0], flagIndex, flagValue != 0, parts[3], key);
                if (quests.Any(existing =>
                        existing.FlagIndex == rule.FlagIndex &&
                        existing.FlagValue == rule.FlagValue &&
                        existing.MapAlias.Equals(rule.MapAlias, StringComparison.OrdinalIgnoreCase) &&
                        existing.MonsterName.Equals(rule.MonsterName, StringComparison.OrdinalIgnoreCase) &&
                        existing.ScriptKey.Equals(rule.ScriptKey, StringComparison.OrdinalIgnoreCase)))
                {
                    errors.Add($"LFENV12-MAPQUEST-DUPLICATE：地图任务规则重复（{source.GetSourceLocation(index)}）。");
                    continue;
                }
                quests.Add(rule);
            }
        }

        private static bool ContainsMainPage(TextFileDefinition definition) =>
            definition?.Lines.Any(line => line.Trim().Equals("[@MAIN]", StringComparison.OrdinalIgnoreCase)) == true;

        private static bool TryPoint(Match match, string xGroup, string yGroup, out Point point)
        {
            point = default;
            if (!int.TryParse(match.Groups[xGroup].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int x) ||
                !int.TryParse(match.Groups[yGroup].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int y)) return false;
            point = new Point(x, y);
            return true;
        }

        private static string Clean(string line)
        {
            string value = (line ?? string.Empty).Trim();
            return value.StartsWith(';') || value.StartsWith("//", StringComparison.Ordinal)
                ? string.Empty
                : value;
        }
    }
}
