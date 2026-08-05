using Server.MirDatabase;
using Server.MirEnvir;

namespace Server.Scripting
{
    public sealed class CSharpDropTableProvider : IDropTableProvider
    {
        private readonly IReadOnlyDictionary<string, DropTableDefinition> _definitions;

        private readonly object _gate = new object();
        private readonly Dictionary<string, IReadOnlyList<DropInfo>> _cache = new Dictionary<string, IReadOnlyList<DropInfo>>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _errors = new Dictionary<string, string>(StringComparer.Ordinal);

        public CSharpDropTableProvider(IReadOnlyDictionary<string, DropTableDefinition> definitions)
        {
            _definitions = definitions ?? new Dictionary<string, DropTableDefinition>(StringComparer.Ordinal);
        }

        public IReadOnlyList<DropInfo> Get(string key)
        {
            if (!LogicKey.TryNormalize(key, out var normalizedKey))
                return null;

            if (!_definitions.TryGetValue(normalizedKey, out var definition) || definition == null)
                return null;

            lock (_gate)
            {
                if (_cache.TryGetValue(normalizedKey, out var cached))
                    return cached;

                if (_errors.ContainsKey(normalizedKey))
                    return null;

                if (!TryBuildDropList(definition, out var drops, out var error))
                {
                    _errors[normalizedKey] = error;
                    MessageQueue.Instance.Enqueue($"[Scripts] Drops 构建失败：{normalizedKey} {error}");
                    return null;
                }

                var snapshot = drops.ToArray();
                _cache[normalizedKey] = snapshot;
                return snapshot;
            }
        }

        private static bool IsMissingItemError(string error)
        {
            if (string.IsNullOrWhiteSpace(error))
                return false;

            return error.Contains("找不到物品", StringComparison.Ordinal);
        }

        private static bool TryBuildDropList(DropTableDefinition table, out List<DropInfo> drops, out string error)
        {
            drops = null;
            error = string.Empty;

            if (table == null)
            {
                error = "DropTableDefinition 不能为空。";
                return false;
            }

            if (string.IsNullOrWhiteSpace(table.Key))
            {
                error = "DropTableDefinition.Key 不能为空。";
                return false;
            }

            var list = new List<DropInfo>(table.Drops?.Count ?? 0);
            var skippedMissingItemCount = 0;

            if (table.Drops != null)
            {
                for (var i = 0; i < table.Drops.Count; i++)
                {
                    if (!TryBuildDropEntry(table.Drops[i], out var entry, out var entryError))
                    {
                        if (IsMissingItemError(entryError))
                        {
                            skippedMissingItemCount++;
                            continue;
                        }

                        error = $"index={i}: {entryError}";
                        return false;
                    }

                    if (entry == null) continue;
                    list.Add(entry);
                }
            }

            if (skippedMissingItemCount > 0 && Settings.TxtScriptsLogDispatch)
            {
                MessageQueue.Instance.Enqueue($"[Scripts] Drops 构建跳过 {skippedMissingItemCount} 条缺失物品项：{table.Key}");
            }

            list.Sort((drop1, drop2) =>
            {
                if (drop1.Gold > 0 && drop2.Gold == 0)
                    return 1;
                if (drop1.Gold == 0 && drop2.Gold > 0)
                    return -1;

                if (drop1.Item == null || drop2.Item == null) return 0;

                return drop1.Item.Type.CompareTo(drop2.Item.Type);
            });

            drops = list;
            return true;
        }

        private static bool TryBuildDropEntry(DropEntryDefinition definition, out DropInfo drop, out string error)
        {
            drop = null;
            error = string.Empty;

            if (definition == null)
            {
                error = "DropEntryDefinition 不能为空。";
                return false;
            }

            if (definition.Chance <= 0)
            {
                error = $"Chance 必须 > 0：{definition.Chance}";
                return false;
            }

            if (definition.Weight <= 0)
            {
                error = $"Weight 必须 > 0：{definition.Weight}";
                return false;
            }

            var hasItem = !string.IsNullOrWhiteSpace(definition.ItemName);
            var hasGold = definition.Gold > 0;
            var hasGroup = definition.Group != null;

            var kindCount = (hasItem ? 1 : 0) + (hasGold ? 1 : 0) + (hasGroup ? 1 : 0);
            if (kindCount != 1)
            {
                error = "掉落项必须且只能指定 ItemName/Gold/Group 三者之一。";
                return false;
            }

            var info = new DropInfo
            {
                Chance = definition.Chance,
                Weight = definition.Weight,
                QuestRequired = definition.QuestRequired,
                Condition = definition.Condition,
            };

            if (hasGold)
            {
                info.Gold = definition.Gold;
                drop = info;
                return true;
            }

            if (hasItem)
            {
                if (definition.Count == 0)
                {
                    error = "Count 必须 > 0。";
                    return false;
                }

                var itemName = definition.ItemName.Trim();
                var item = Envir.Main.GetItemInfo(itemName);
                if (item == null)
                {
                    error = $"找不到物品：{itemName}";
                    return false;
                }

                info.Item = item;
                info.Count = definition.Count;
                drop = info;
                return true;
            }

            var group = definition.Group;

            if (group.Drops == null)
            {
                error = "Group.Drops 不能为空。";
                return false;
            }

            info.GroupedDrop = new GroupDropInfo
            {
                Random = group.Random,
                First = group.First
            };

            for (var i = 0; i < group.Drops.Count; i++)
            {
                if (!TryBuildDropEntry(group.Drops[i], out var childDrop, out var childError))
                {
                    if (IsMissingItemError(childError))
                        continue;

                    error = $"Group.Drops index={i}: {childError}";
                    return false;
                }

                if (childDrop == null) continue;
                info.GroupedDrop.Add(childDrop);
            }

            if (info.GroupedDrop.Count == 0)
            {
                error = "Group.Drops 为空：找不到物品";
                return false;
            }

            drop = info;
            return true;
        }
    }
}
