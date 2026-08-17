using System.Text;
using System.Security.Cryptography;
using System.Globalization;
using Server.Scripting.Variables;

namespace Server.MirDatabase
{
    public sealed class LingFengCharacterProgress
    {
        private const string RenewLevelKey = "LFENV.RENEWLEVEL";
        private const string RenewPointsKey = "LFENV.RENEWPOINTS";
        private const string TitlesKey = "LFENV.FENGHAO";
        private const string ActiveTitleKey = "LFENV.ACTIVEFENGHAO";
        private const string NameColourKey = "LFENV.NAMECOLOUR";
        private const string GameGirdKey = "LFENV.GAMEGIRD";
        private const string GamePointKey = "LFENV.GAMEPOINT";
        private const string GameDiamondKey = "LFENV.GAMEDIAMOND";
        private const string EnhancedSkillPrefix = "LFENV.SKILLENHANCE.";
        private const string TimedMembershipPrefix = "LFENV.NAMEDATETIME.";
        private const string ExperienceRatePrefix = "LFENV.EXPRATE.";
        private const string PowerRatePrefix = "LFENV.POWERRATE.";
        private const string DropRatePrefix = "LFENV.DROPRATE.";
        private const string GlobalMessageFilterKey = "LFENV.GLOBALMSGFILTER";
        private const int MaximumTitles = 100;
        private const int MaximumTitleLength = 128;

        private readonly CharacterScriptVariableStore _store;

        public LingFengCharacterProgress(CharacterScriptVariableStore store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
        }

        public int RenewLevel
        {
            get
            {
                if (!_store.TryGet(ScriptVariableScope.U, RenewLevelKey, out ScriptVariableValue value) ||
                    value.Kind != ScriptVariableKind.Integer)
                    return 0;
                return (int)Math.Clamp(value.Integer, 0L, byte.MaxValue);
            }
        }

        public int RenewPoints
        {
            get
            {
                if (!_store.TryGet(ScriptVariableScope.U, RenewPointsKey,
                        out ScriptVariableValue value) ||
                    value.Kind != ScriptVariableKind.Integer)
                    return 0;
                return (int)Math.Clamp(value.Integer, 0L, int.MaxValue);
            }
        }

        public string ActiveTitle
        {
            get
            {
                if (!_store.TryGet(ScriptVariableScope.Z, ActiveTitleKey, out ScriptVariableValue value) ||
                    value.Kind != ScriptVariableKind.String || !HasTitle(value.Text))
                    return string.Empty;
                return value.Text;
            }
        }

        public byte NameColour
        {
            get
            {
                if (!_store.TryGet(ScriptVariableScope.U, NameColourKey, out ScriptVariableValue value) ||
                    value.Kind != ScriptVariableKind.Integer ||
                    value.Integer is < byte.MinValue or > byte.MaxValue)
                    return byte.MaxValue;
                return (byte)value.Integer;
            }
        }

        public int GameGird
        {
            get
            {
                if (!_store.TryGet(ScriptVariableScope.U, GameGirdKey, out ScriptVariableValue value) ||
                    value.Kind != ScriptVariableKind.Integer)
                    return 0;
                return (int)Math.Clamp(value.Integer, 0L, int.MaxValue);
            }
        }

        public int GamePoint
        {
            get
            {
                if (!_store.TryGet(ScriptVariableScope.U, GamePointKey,
                        out ScriptVariableValue value) ||
                    value.Kind != ScriptVariableKind.Integer)
                    return 0;
                return (int)Math.Clamp(value.Integer, 0L, int.MaxValue);
            }
        }

        public int GameDiamond
        {
            get
            {
                if (!_store.TryGet(ScriptVariableScope.U, GameDiamondKey,
                        out ScriptVariableValue value) ||
                    value.Kind != ScriptVariableKind.Integer)
                    return 0;
                return (int)Math.Clamp(value.Integer, 0L, int.MaxValue);
            }
        }

        public void SetRenewLevel(int level)
        {
            if (level is < 0 or > byte.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(level));
            _store.Set(ScriptVariableScope.U, RenewLevelKey, ScriptVariableValue.FromInteger(level));
        }

        public bool TryAddRenewPoints(int points)
        {
            if (points < 0 || RenewPoints > int.MaxValue - points) return false;
            _store.Set(ScriptVariableScope.U, RenewPointsKey,
                ScriptVariableValue.FromInteger(RenewPoints + points));
            return true;
        }

        public void SetNameColour(int colour)
        {
            if (colour is < byte.MinValue or > byte.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(colour));
            _store.Set(ScriptVariableScope.U, NameColourKey,
                ScriptVariableValue.FromInteger(colour));
        }

        public void SetGameGird(int amount)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
            _store.Set(ScriptVariableScope.U, GameGirdKey,
                ScriptVariableValue.FromInteger(amount));
        }

        public void SetGamePoint(int amount)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
            _store.Set(ScriptVariableScope.U, GamePointKey,
                ScriptVariableValue.FromInteger(amount));
        }

        public void SetGameDiamond(int amount)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
            _store.Set(ScriptVariableScope.U, GameDiamondKey,
                ScriptVariableValue.FromInteger(amount));
        }

        public bool IsGlobalMessageFiltered(int category)
        {
            if (category is < 1 or > 4) return false;
            if (!_store.TryGet(ScriptVariableScope.U, GlobalMessageFilterKey,
                    out ScriptVariableValue value) || value.Kind != ScriptVariableKind.Integer)
                return false;
            return (value.Integer & (1L << (category - 1))) != 0;
        }

        public void SetGlobalMessageFilter(int category, bool enabled)
        {
            if (category is < 1 or > 4)
                throw new ArgumentOutOfRangeException(nameof(category));
            long mask = 0;
            if (_store.TryGet(ScriptVariableScope.U, GlobalMessageFilterKey,
                    out ScriptVariableValue value) && value.Kind == ScriptVariableKind.Integer)
                mask = value.Integer;
            long bit = 1L << (category - 1);
            mask = enabled ? mask | bit : mask & ~bit;
            _store.Set(ScriptVariableScope.U, GlobalMessageFilterKey,
                ScriptVariableValue.FromInteger(mask));
        }

        public int GetEnhancedSkillLevel(Spell spell)
        {
            if (!_store.TryGet(ScriptVariableScope.U,
                    EnhancedSkillPrefix + (ushort)spell, out ScriptVariableValue value) ||
                value.Kind != ScriptVariableKind.Integer)
                return 0;
            return (int)Math.Clamp(value.Integer, 0L, byte.MaxValue);
        }

        public void SetEnhancedSkillLevel(Spell spell, int level)
        {
            if (level is < 0 or > byte.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(level));
            _store.Set(ScriptVariableScope.U, EnhancedSkillPrefix + (ushort)spell,
                ScriptVariableValue.FromInteger(level));
        }

        public bool TryGetTimedMembership(
            string sourceKey, DateTime now, bool clearExpired,
            out DateTime expiry, out TimeSpan remaining)
        {
            expiry = default;
            remaining = TimeSpan.Zero;
            string key = TimedMembershipKey(sourceKey);
            if (!_store.TryGet(ScriptVariableScope.U, key, out ScriptVariableValue value) ||
                value.Kind != ScriptVariableKind.Integer || value.Integer <= 0)
                return false;
            try
            {
                expiry = new DateTime(value.Integer, DateTimeKind.Unspecified);
            }
            catch (ArgumentOutOfRangeException)
            {
                if (clearExpired)
                    _store.Set(ScriptVariableScope.U, key, ScriptVariableValue.FromInteger(0));
                expiry = default;
                return false;
            }
            if (expiry <= now)
            {
                if (clearExpired)
                    _store.Set(ScriptVariableScope.U, key, ScriptVariableValue.FromInteger(0));
                expiry = default;
                return false;
            }
            remaining = expiry - now;
            return true;
        }

        public bool AddTimedMembership(
            string sourceKey, DateTime now, int days, int hours, int minutes)
        {
            if (days < 0 || hours < 0 || minutes < 0) return false;
            TimeSpan addition;
            try
            {
                addition = TimeSpan.FromDays(days) + TimeSpan.FromHours(hours) +
                           TimeSpan.FromMinutes(minutes);
            }
            catch (OverflowException)
            {
                return false;
            }
            if (addition <= TimeSpan.Zero) return false;
            DateTime baseline = now;
            if (TryGetTimedMembership(sourceKey, now, false, out DateTime current, out _))
                baseline = current;
            DateTime expiry;
            try { expiry = baseline.Add(addition); }
            catch (ArgumentOutOfRangeException) { return false; }
            _store.Set(ScriptVariableScope.U, TimedMembershipKey(sourceKey),
                ScriptVariableValue.FromInteger(expiry.Ticks));
            return true;
        }

        private static string TimedMembershipKey(string sourceKey)
        {
            string normalized = (sourceKey ?? string.Empty).Trim().ToLowerInvariant();
            if (normalized.Length == 0) throw new ArgumentException("会员名单键不能为空。", nameof(sourceKey));
            byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
            string hash = Convert.ToHexString(digest.AsSpan(0, 16));
            return TimedMembershipPrefix + hash;
        }

        internal string ExperienceRateSourceKey(string sourceKey) =>
            HashedSourceKey(ExperienceRatePrefix, sourceKey, "经验倍率来源不能为空。");

        internal bool TryStoreExperienceRate(
            string sourceKey, int rate, DateTime now, int durationSeconds, bool save)
        {
            if (rate is < 100 or > 1_000_000 || durationSeconds < 0) return false;
            string key = ExperienceRateSourceKey(sourceKey);
            if (!save)
            {
                if (_store.TryGet(ScriptVariableScope.T, key, out _))
                    _store.Set(ScriptVariableScope.T, key,
                        ScriptVariableValue.FromString(string.Empty));
                return true;
            }

            long expiryTicks = 0;
            if (durationSeconds > 0)
            {
                try { expiryTicks = now.AddSeconds(durationSeconds).Ticks; }
                catch (ArgumentOutOfRangeException) { return false; }
            }
            _store.Set(ScriptVariableScope.T, key, ScriptVariableValue.FromString(
                rate.ToString(CultureInfo.InvariantCulture) + "|" +
                expiryTicks.ToString(CultureInfo.InvariantCulture)));
            return true;
        }

        internal IReadOnlyList<LingFengSavedExperienceRate> ReadExperienceRates(
            DateTime now, bool clearExpired)
        {
            var result = new List<LingFengSavedExperienceRate>();
            foreach (CharacterScriptVariableEntry entry in _store.Snapshot())
            {
                if (entry.Scope != ScriptVariableScope.T ||
                    !entry.Key.StartsWith(ExperienceRatePrefix, StringComparison.Ordinal) ||
                    entry.Value.Kind != ScriptVariableKind.String ||
                    string.IsNullOrEmpty(entry.Value.Text))
                    continue;
                string[] parts = entry.Value.Text.Split('|');
                if (parts.Length != 2 ||
                    !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture,
                        out int rate) || rate is < 100 or > 1_000_000 ||
                    !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture,
                        out long expiryTicks) || expiryTicks < 0)
                {
                    if (clearExpired)
                        _store.Set(ScriptVariableScope.T, entry.Key,
                            ScriptVariableValue.FromString(string.Empty));
                    continue;
                }

                DateTime? expiry = null;
                if (expiryTicks > 0)
                {
                    try { expiry = new DateTime(expiryTicks, DateTimeKind.Unspecified); }
                    catch (ArgumentOutOfRangeException)
                    {
                        if (clearExpired)
                            _store.Set(ScriptVariableScope.T, entry.Key,
                                ScriptVariableValue.FromString(string.Empty));
                        continue;
                    }
                    if (expiry <= now)
                    {
                        if (clearExpired)
                            _store.Set(ScriptVariableScope.T, entry.Key,
                                ScriptVariableValue.FromString(string.Empty));
                        continue;
                    }
                }
                result.Add(new LingFengSavedExperienceRate(entry.Key, rate, expiry));
            }
            return result;
        }

        internal string PowerRateSourceKey(string sourceKey) =>
            HashedSourceKey(PowerRatePrefix, sourceKey, "攻击倍率来源不能为空。");

        internal string DropRateSourceKey(string sourceKey) =>
            HashedSourceKey(DropRatePrefix, sourceKey, "爆率倍率来源不能为空。");

        internal bool TryStorePowerRate(
            string sourceKey, int rate, DateTime now, int durationSeconds, bool save,
            int targetType) =>
            TryStoreCombatRate(PowerRatePrefix, sourceKey, rate, now,
                durationSeconds, save, targetType);

        internal bool TryStoreDropRate(
            string sourceKey, int rate, DateTime now, int durationSeconds, bool save) =>
            TryStoreCombatRate(DropRatePrefix, sourceKey, rate, now,
                durationSeconds, save, 0);

        internal IReadOnlyList<LingFengSavedCombatRate> ReadPowerRates(
            DateTime now, bool clearExpired) =>
            ReadCombatRates(PowerRatePrefix, now, clearExpired, allowTargetType: true);

        internal IReadOnlyList<LingFengSavedCombatRate> ReadDropRates(
            DateTime now, bool clearExpired) =>
            ReadCombatRates(DropRatePrefix, now, clearExpired, allowTargetType: false);

        private bool TryStoreCombatRate(
            string prefix, string sourceKey, int rate, DateTime now,
            int durationSeconds, bool save, int targetType)
        {
            if (rate is < 0 or > 1_000_000 || durationSeconds < 0 ||
                targetType is < 0 or > 2)
                return false;
            string key = HashedSourceKey(prefix, sourceKey, "倍率来源不能为空。");
            if (!save)
            {
                if (_store.TryGet(ScriptVariableScope.T, key, out _))
                    _store.Set(ScriptVariableScope.T, key,
                        ScriptVariableValue.FromString(string.Empty));
                return true;
            }

            long expiryTicks = 0;
            if (durationSeconds > 0)
            {
                try { expiryTicks = now.AddSeconds(durationSeconds).Ticks; }
                catch (ArgumentOutOfRangeException) { return false; }
            }
            _store.Set(ScriptVariableScope.T, key, ScriptVariableValue.FromString(
                rate.ToString(CultureInfo.InvariantCulture) + "|" +
                expiryTicks.ToString(CultureInfo.InvariantCulture) + "|" +
                targetType.ToString(CultureInfo.InvariantCulture)));
            return true;
        }

        private IReadOnlyList<LingFengSavedCombatRate> ReadCombatRates(
            string prefix, DateTime now, bool clearExpired, bool allowTargetType)
        {
            var result = new List<LingFengSavedCombatRate>();
            foreach (CharacterScriptVariableEntry entry in _store.Snapshot())
            {
                if (entry.Scope != ScriptVariableScope.T ||
                    !entry.Key.StartsWith(prefix, StringComparison.Ordinal) ||
                    entry.Value.Kind != ScriptVariableKind.String ||
                    string.IsNullOrEmpty(entry.Value.Text))
                    continue;
                string[] parts = entry.Value.Text.Split('|');
                int rate = 0;
                long expiryTicks = 0;
                int targetType = 0;
                bool valid = parts.Length == 3 &&
                    int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture,
                        out rate) && rate is >= 0 and <= 1_000_000 &&
                    long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture,
                        out expiryTicks) && expiryTicks >= 0 &&
                    int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture,
                        out targetType) && targetType is >= 0 and <= 2 &&
                    (allowTargetType || targetType == 0);
                if (!valid)
                {
                    if (clearExpired)
                        _store.Set(ScriptVariableScope.T, entry.Key,
                            ScriptVariableValue.FromString(string.Empty));
                    continue;
                }

                DateTime? expiry = null;
                if (expiryTicks > 0)
                {
                    try { expiry = new DateTime(expiryTicks, DateTimeKind.Unspecified); }
                    catch (ArgumentOutOfRangeException)
                    {
                        if (clearExpired)
                            _store.Set(ScriptVariableScope.T, entry.Key,
                                ScriptVariableValue.FromString(string.Empty));
                        continue;
                    }
                    if (expiry <= now)
                    {
                        if (clearExpired)
                            _store.Set(ScriptVariableScope.T, entry.Key,
                                ScriptVariableValue.FromString(string.Empty));
                        continue;
                    }
                }
                result.Add(new LingFengSavedCombatRate(
                    entry.Key, rate, targetType, expiry));
            }
            return result;
        }

        private static string HashedSourceKey(
            string prefix, string sourceKey, string emptyMessage)
        {
            string normalized = (sourceKey ?? string.Empty).Trim().ToLowerInvariant();
            if (normalized.Length == 0) throw new ArgumentException(emptyMessage, nameof(sourceKey));
            byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
            return prefix + Convert.ToHexString(digest.AsSpan(0, 16));
        }

        public bool HasTitle(string title)
        {
            if (!TryNormalizeTitle(title, out string normalized)) return false;
            return ReadTitles().Contains(normalized);
        }

        public bool GrantTitle(string title, bool activate)
        {
            if (!TryNormalizeTitle(title, out string normalized)) return false;

            SortedSet<string> titles = ReadTitles();
            if (!titles.Contains(normalized))
            {
                if (titles.Count >= MaximumTitles) return false;
                titles.Add(normalized);
                WriteTitles(titles);
            }

            if (activate)
                _store.Set(ScriptVariableScope.Z, ActiveTitleKey,
                    ScriptVariableValue.FromString(normalized));
            return true;
        }

        public bool RevokeTitle(string title)
        {
            if (!TryNormalizeTitle(title, out string normalized)) return false;

            SortedSet<string> titles = ReadTitles();
            bool wasActive = string.Equals(
                ActiveTitle, normalized, StringComparison.Ordinal);
            if (!titles.Remove(normalized)) return true;

            WriteTitles(titles);
            if (wasActive)
                _store.Set(ScriptVariableScope.Z, ActiveTitleKey,
                    ScriptVariableValue.FromString(string.Empty));
            return true;
        }

        private SortedSet<string> ReadTitles()
        {
            var result = new SortedSet<string>(StringComparer.Ordinal);
            if (!_store.TryGet(ScriptVariableScope.Z, TitlesKey, out ScriptVariableValue value) ||
                value.Kind != ScriptVariableKind.String || string.IsNullOrEmpty(value.Text))
                return result;

            foreach (string encoded in value.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    string title = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
                    if (TryNormalizeTitle(title, out string normalized)) result.Add(normalized);
                }
                catch (FormatException)
                {
                    return new SortedSet<string>(StringComparer.Ordinal);
                }
            }
            return result;
        }

        private void WriteTitles(IEnumerable<string> titles)
        {
            string serialized = string.Join("\n", titles.Select(title =>
                Convert.ToBase64String(Encoding.UTF8.GetBytes(title))));
            _store.Set(ScriptVariableScope.Z, TitlesKey,
                ScriptVariableValue.FromString(serialized));
        }

        private static bool TryNormalizeTitle(string title, out string normalized)
        {
            normalized = title?.Trim() ?? string.Empty;
            return normalized.Length is > 0 and <= MaximumTitleLength &&
                   !normalized.Any(char.IsControl);
        }
    }

    internal readonly record struct LingFengSavedExperienceRate(
        string SourceKey, int Rate, DateTime? Expiry);

    internal readonly record struct LingFengSavedCombatRate(
        string SourceKey, int Rate, int TargetType, DateTime? Expiry);
}
