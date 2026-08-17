using System.Drawing;
using System.Globalization;
using Server.MirDatabase;
using Server.MirObjects;

namespace Server.Scripting
{
    internal readonly record struct LingFengEctypeDefinition(
        int Capacity,
        string Name,
        int AccessMode,
        int EntryExtensionMinutes,
        int EmptyRecycleSeconds)
    {
        public int EntryWindowMinutes => checked(1 + EntryExtensionMinutes);

        public static bool TryParse(string value, out LingFengEctypeDefinition definition)
        {
            definition = default;
            string[] parts = (value ?? string.Empty).Split(
                ',', StringSplitOptions.TrimEntries);
            if (parts.Length is not (4 or 5) ||
                !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture,
                    out int capacity) || capacity is < 1 or > 10_000 ||
                string.IsNullOrWhiteSpace(parts[1]) || parts[1].Length > 256 ||
                !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture,
                    out int accessMode) || accessMode is < 0 or > 3 ||
                !int.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture,
                    out int entryExtension) || entryExtension is < 0 or > 525_599)
                return false;

            int emptyRecycleSeconds = 10;
            if (parts.Length == 5 &&
                (!int.TryParse(parts[4], NumberStyles.None, CultureInfo.InvariantCulture,
                     out emptyRecycleSeconds) || emptyRecycleSeconds is < 1 or > 86_400))
                return false;

            definition = new LingFengEctypeDefinition(
                capacity, parts[1], accessMode, entryExtension, emptyRecycleSeconds);
            return true;
        }
    }

    internal enum LingFengEctypeCreateResult
    {
        Created,
        DefinitionMissing,
        CapacityFull,
        GroupLeaderRequired,
        GuildLeaderRequired,
        ExistingForOwner,
        ExistingForMember,
        InvalidDuration,
        RuntimeFailure
    }

    internal enum LingFengEctypeMoveResult
    {
        Success,
        Missing,
        EntryWindowExpired,
        InvalidLocation
    }
}

namespace Server.MirEnvir
{
    using Server.Scripting;

    public partial class Envir
    {
        private sealed class LingFengEctypeEntry
        {
            public required LingFengEctypeDefinition Definition { get; init; }
            public required string RuntimeName { get; init; }
            public required Map Map { get; init; }
            public required int CreatorCharacterIndex { get; init; }
            public required string OwnerKey { get; init; }
            public required HashSet<int> MemberCharacterIndices { get; init; }
            public long EntryClosesAt { get; init; }
            public bool HasBeenOccupied { get; set; }
            public long EmptySince { get; set; }
        }

        private readonly Dictionary<string, LingFengEctypeEntry> _lingFengEctypes =
            new(StringComparer.OrdinalIgnoreCase);
        private long _lingFengEctypeSequence;

        internal LingFengEctypeCreateResult TryCreateLingFengEctype(
            PlayerObject player, string ectypeName, int durationMinutes)
        {
            if (!IsMainThread && Volatile.Read(ref _mainThreadId) != 0)
                return InvokeOnMainThread(() => TryCreateLingFengEctype(
                    player, ectypeName, durationMinutes));
            if (player?.Info == null || durationMinutes is < 1 or > 525_600)
                return LingFengEctypeCreateResult.InvalidDuration;
            if (!TryResolveLingFengEctypeDefinition(
                    ectypeName, out Map source, out LingFengEctypeDefinition definition))
                return LingFengEctypeCreateResult.DefinitionMissing;

            LingFengEctypeEntry existing = FindAccessibleLingFengEctype(player, definition.Name);
            if (existing != null)
                return existing.CreatorCharacterIndex == player.Info.Index
                    ? LingFengEctypeCreateResult.ExistingForOwner
                    : LingFengEctypeCreateResult.ExistingForMember;

            if (!TryResolveLingFengEctypeOwner(
                    player, definition, out string ownerKey,
                    out HashSet<int> memberIndices, out LingFengEctypeCreateResult failure))
                return failure;

            if (_lingFengEctypes.Values.Count(entry =>
                    string.Equals(entry.Definition.Name, definition.Name,
                        StringComparison.OrdinalIgnoreCase)) >= definition.Capacity)
                return LingFengEctypeCreateResult.CapacityFull;

            long seconds = (long)durationMinutes * 60;
            if (seconds > int.MaxValue)
                return LingFengEctypeCreateResult.InvalidDuration;
            string runtimeName = $"LFECTYPE-{source.Info.Index}-{++_lingFengEctypeSequence}";
            if (!TryCreateLingFengMirrorMap(
                    source.Info.FileName, runtimeName, source.Info.Title,
                    (int)seconds, player.CurrentMap?.Info?.FileName,
                    source.Info.MiniMap, player.CurrentLocation, out Map map))
                return LingFengEctypeCreateResult.RuntimeFailure;

            InitialiseLingFengEctypeRespawns(source, map);
            long entryWindow = (long)definition.EntryWindowMinutes * 60 * Settings.Second;
            _lingFengEctypes.Add(runtimeName, new LingFengEctypeEntry
            {
                Definition = definition,
                RuntimeName = runtimeName,
                Map = map,
                CreatorCharacterIndex = player.Info.Index,
                OwnerKey = ownerKey,
                MemberCharacterIndices = memberIndices,
                EntryClosesAt = Time + Math.Min(long.MaxValue - Time, entryWindow)
            });
            return LingFengEctypeCreateResult.Created;
        }

        internal bool CanMoveLingFengEctype(PlayerObject player, string ectypeName) =>
            InvokeLingFengEctypeMainThread(() =>
                FindAccessibleLingFengEctype(player, ectypeName) != null);

        internal LingFengEctypeMoveResult TryMoveLingFengEctype(
            PlayerObject player, string ectypeName, Point location)
        {
            if (!IsMainThread && Volatile.Read(ref _mainThreadId) != 0)
                return InvokeOnMainThread(() => TryMoveLingFengEctype(
                    player, ectypeName, location));
            LingFengEctypeEntry entry = FindAccessibleLingFengEctype(player, ectypeName);
            if (entry == null) return LingFengEctypeMoveResult.Missing;
            if (player.CurrentMap != entry.Map && Time > entry.EntryClosesAt)
                return LingFengEctypeMoveResult.EntryWindowExpired;
            if (!entry.Map.ValidPoint(location) || !player.Teleport(entry.Map, location))
                return LingFengEctypeMoveResult.InvalidLocation;
            entry.HasBeenOccupied = true;
            entry.EmptySince = 0;
            return LingFengEctypeMoveResult.Success;
        }

        internal bool TrySpawnLingFengEctypeMonsters(
            PlayerObject player, string selector, Point center, string monsterName,
            int count, int range, Color? nameColour)
        {
            if (!IsMainThread && Volatile.Read(ref _mainThreadId) != 0)
                return InvokeOnMainThread(() => TrySpawnLingFengEctypeMonsters(
                    player, selector, center, monsterName, count, range, nameColour));
            if (player == null || center.X < 0 || center.Y < 0 ||
                count is < 1 or > byte.MaxValue || range < 0)
                return false;

            LingFengEctypeEntry entry = ResolveLingFengEctypeSelector(player, selector);
            MonsterInfo monsterInfo = GetMonsterInfo(monsterName);
            if (entry == null || monsterInfo == null) return false;

            var locations = new List<Point>(count);
            var reserved = new HashSet<Point>();
            for (int index = 0; index < count; index++)
            {
                Point location = Point.Empty;
                bool found = false;
                for (int attempt = 0; attempt < 128; attempt++)
                {
                    var candidate = range == 0
                        ? center
                        : new Point(
                            Random.Next(center.X - range, center.X + range + 1),
                            Random.Next(center.Y - range, center.Y + range + 1));
                    if (!reserved.Add(candidate) || !entry.Map.ValidPoint(candidate)) continue;
                    location = candidate;
                    found = true;
                    break;
                }
                if (!found) return false;
                locations.Add(location);
            }

            var monsters = new List<MonsterObject>(count);
            for (int index = 0; index < count; index++)
            {
                MonsterObject monster = MonsterObject.GetMonster(monsterInfo);
                if (monster == null) return false;
                if (nameColour.HasValue) monster.NameColour = nameColour.Value;
                monster.Direction = 0;
                monster.ActionTime = Time + 1000;
                monsters.Add(monster);
            }
            for (int index = 0; index < monsters.Count; index++)
                if (!monsters[index].Spawn(entry.Map, locations[index]))
                    return false;
            return true;
        }

        private bool InvokeLingFengEctypeMainThread(Func<bool> action)
        {
            if (!IsMainThread && Volatile.Read(ref _mainThreadId) != 0)
                return InvokeOnMainThread(action);
            return action();
        }

        private bool TryResolveLingFengEctypeDefinition(
            string ectypeName, out Map source, out LingFengEctypeDefinition definition)
        {
            source = null;
            definition = default;
            if (string.IsNullOrWhiteSpace(ectypeName)) return false;
            foreach (Map candidate in MapList)
            {
                if (candidate?.Info == null || candidate.Info.LingFengIsMirror ||
                    !candidate.Info.LingFengOptions.TryGetValue("FB", out string raw) ||
                    !LingFengEctypeDefinition.TryParse(raw, out LingFengEctypeDefinition parsed) ||
                    !string.Equals(parsed.Name, ectypeName, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (source != null) return false;
                source = candidate;
                definition = parsed;
            }
            return source != null;
        }

        private bool TryResolveLingFengEctypeOwner(
            PlayerObject player, LingFengEctypeDefinition definition,
            out string ownerKey, out HashSet<int> members,
            out LingFengEctypeCreateResult failure)
        {
            ownerKey = string.Empty;
            members = new HashSet<int>();
            failure = LingFengEctypeCreateResult.RuntimeFailure;
            switch (definition.AccessMode)
            {
                case 2:
                    ownerKey = $"P:{player.Info.Index}";
                    members.Add(player.Info.Index);
                    return true;
                case 0:
                case 1:
                    if (player.GroupMembers == null || player.GroupMembers.Count == 0 ||
                        !ReferenceEquals(player.GroupMembers[0], player))
                    {
                        failure = LingFengEctypeCreateResult.GroupLeaderRequired;
                        return false;
                    }
                    if (definition.AccessMode == 0)
                    {
                        var required = new HashSet<MirClass>
                            { MirClass.Warrior, MirClass.Wizard, MirClass.Taoist };
                        required.ExceptWith(player.GroupMembers.Select(member => member.Class));
                        if (required.Count != 0)
                        {
                            failure = LingFengEctypeCreateResult.GroupLeaderRequired;
                            return false;
                        }
                    }
                    ownerKey = $"G:{player.Info.Index}";
                    members.UnionWith(player.GroupMembers
                        .Where(member => member?.Info != null)
                        .Select(member => member.Info.Index));
                    return true;
                case 3:
                    if (player.MyGuild == null || player.MyGuildRank == null ||
                        player.MyGuild.Ranks.Count == 0 ||
                        !ReferenceEquals(player.MyGuild.Ranks[0], player.MyGuildRank))
                    {
                        failure = LingFengEctypeCreateResult.GuildLeaderRequired;
                        return false;
                    }
                    ownerKey = $"C:{player.MyGuild.Guildindex}";
                    members.Add(player.Info.Index);
                    return true;
                default:
                    return false;
            }
        }

        private LingFengEctypeEntry FindAccessibleLingFengEctype(
            PlayerObject player, string ectypeName)
        {
            if (player?.Info == null || string.IsNullOrWhiteSpace(ectypeName)) return null;
            return _lingFengEctypes.Values
                .Where(entry => string.Equals(entry.Definition.Name, ectypeName,
                                    StringComparison.OrdinalIgnoreCase) &&
                                CanAccessLingFengEctype(player, entry))
                .OrderByDescending(entry => entry.EntryClosesAt)
                .FirstOrDefault();
        }

        private static bool CanAccessLingFengEctype(
            PlayerObject player, LingFengEctypeEntry entry) => entry.Definition.AccessMode switch
        {
            3 => player.MyGuild != null &&
                 string.Equals(entry.OwnerKey, $"C:{player.MyGuild.Guildindex}",
                     StringComparison.Ordinal),
            _ => entry.MemberCharacterIndices.Contains(player.Info.Index)
        };

        private LingFengEctypeEntry ResolveLingFengEctypeSelector(
            PlayerObject player, string selector)
        {
            if (selector.Equals("SELF", StringComparison.OrdinalIgnoreCase))
                return _lingFengEctypes.Values.FirstOrDefault(entry =>
                    entry.Map == player.CurrentMap && CanAccessLingFengEctype(player, entry));
            if (selector.Equals("FBMAP", StringComparison.OrdinalIgnoreCase))
                return _lingFengEctypes.Values
                    .Where(entry => CanAccessLingFengEctype(player, entry))
                    .OrderByDescending(entry => entry.EntryClosesAt)
                    .FirstOrDefault();
            if (selector.Equals("NPCMAP", StringComparison.OrdinalIgnoreCase))
            {
                Map npcMap = NPCObject.Get(player.NPCObjectID)?.CurrentMap;
                return _lingFengEctypes.Values.FirstOrDefault(entry =>
                    entry.Map == npcMap && CanAccessLingFengEctype(player, entry));
            }
            return FindAccessibleLingFengEctype(player, selector);
        }

        private void InitialiseLingFengEctypeRespawns(Map source, Map target)
        {
            foreach (RespawnInfo info in source.Info.Respawns)
            {
                var respawn = new MapRespawn(info);
                if (respawn.Monster == null) continue;
                respawn.Map = target;
                respawn.WalkableCells = target.WalkableCells.Where(point =>
                    point.X <= info.Location.X + info.Spread &&
                    point.X >= info.Location.X - info.Spread &&
                    point.Y <= info.Location.Y + info.Spread &&
                    point.Y >= info.Location.Y - info.Spread).ToList();
                respawn.RespawnTime = Time;
                target.Respawns.Add(respawn);
            }
        }

        private void ProcessLingFengEctypes()
        {
            foreach (LingFengEctypeEntry entry in _lingFengEctypes.Values.ToArray())
            {
                if (!MapList.Contains(entry.Map))
                {
                    _lingFengEctypes.Remove(entry.RuntimeName);
                    continue;
                }
                if (entry.Map.Players.Count > 0)
                {
                    entry.HasBeenOccupied = true;
                    entry.EmptySince = 0;
                    continue;
                }
                if (!entry.HasBeenOccupied) continue;
                if (entry.EmptySince == 0)
                {
                    entry.EmptySince = Time;
                    continue;
                }
                long recycleDelay =
                    (long)entry.Definition.EmptyRecycleSeconds * Settings.Second;
                if (Time - entry.EmptySince < recycleDelay) continue;
                TryDeleteLingFengMirrorMap(entry.RuntimeName);
            }
        }

        private void RemoveLingFengEctypeRuntime(string runtimeName) =>
            _lingFengEctypes.Remove(runtimeName);

        private void ClearLingFengEctypeRuntime()
        {
            _lingFengEctypes.Clear();
            _lingFengEctypeSequence = 0;
        }
    }
}
