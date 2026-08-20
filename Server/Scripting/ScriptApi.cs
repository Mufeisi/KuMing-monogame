using System.Collections.Concurrent;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Server.MirDatabase;
using Server.MirEnvir;
using Server.MirObjects;
using S = ServerPackets;

namespace Server.Scripting
{
    public sealed class ScriptApi
    {
        private static readonly Regex VarKeyRegex = new Regex(@"^[A-Za-z][0-9]$", RegexOptions.Compiled);
        private static readonly ConcurrentDictionary<string, object> ValueFileLocks = new ConcurrentDictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        private Envir Envir => Envir.Main;

        private static object GetValueFileLock(string fullFilePath)
        {
            return ValueFileLocks.GetOrAdd(fullFilePath, _ => new object());
        }

        public bool LocalMessage(PlayerObject player, string message, ChatType type = ChatType.System)
        {
            try
            {
                if (player == null) return false;
                if (string.IsNullOrWhiteSpace(message)) return true;

                player.ReceiveChat(message, type);

                if (ScriptTrace.IsEnabled(player))
                {
                    ScriptTrace.Record(player, $"[C#] MESSAGE({type}) {TruncForTrace(message)}");
                }

                return true;
            }
            catch (Exception ex)
            {
                LogException(nameof(LocalMessage), ex);
                return false;
            }
        }

        public bool Hint(PlayerObject player, string message) => LocalMessage(player, message, ChatType.Hint);

        public bool GlobalMessage(string message, ChatType type = ChatType.System)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(message)) return true;

                Envir.Broadcast(new S.Chat { Message = message, Type = type });
                return true;
            }
            catch (Exception ex)
            {
                LogException(nameof(GlobalMessage), ex);
                return false;
            }
        }

        public bool Announcement(string message) => GlobalMessage(message, ChatType.Announcement);

        public bool OpenBrowser(PlayerObject player, string url)
        {
            try
            {
                if (player == null) return false;
                if (string.IsNullOrWhiteSpace(url)) return true;

                player.Enqueue(new S.OpenBrowser { Url = url });

                if (ScriptTrace.IsEnabled(player))
                {
                    ScriptTrace.Record(player, $"[C#] OPENBROWSER {TruncForTrace(url)}");
                }

                return true;
            }
            catch (Exception ex)
            {
                LogException(nameof(OpenBrowser), ex);
                return false;
            }
        }

        public int GetBagFreeSlotCount(PlayerObject player)
        {
            try
            {
                if (player == null) return 0;

                var count = 0;
                for (var i = 0; i < player.Info.Inventory.Length; i++)
                {
                    if (player.Info.Inventory[i] == null) count++;
                }

                return count;
            }
            catch (Exception ex)
            {
                LogException(nameof(GetBagFreeSlotCount), ex);
                return 0;
            }
        }

        public bool CheckBagSpace(PlayerObject player, string itemName, int count = 1)
        {
            try
            {
                if (player == null) return false;
                if (count <= 0) return true;

                var info = Envir.GetItemInfo(itemName);
                if (info == null) return false;

                return CheckBagSpace(player, info, count);
            }
            catch (Exception ex)
            {
                LogException(nameof(CheckBagSpace), ex);
                return false;
            }
        }

        public bool CheckBagSpace(PlayerObject player, int itemIndex, int count = 1)
        {
            try
            {
                if (player == null) return false;
                if (count <= 0) return true;

                var info = Envir.GetItemInfo(itemIndex);
                if (info == null) return false;

                return CheckBagSpace(player, info, count);
            }
            catch (Exception ex)
            {
                LogException(nameof(CheckBagSpace), ex);
                return false;
            }
        }

        public bool GiveItem(PlayerObject player, string itemName, int count = 1)
        {
            try
            {
                if (player == null) return false;
                if (count <= 0) return true;

                var info = Envir.GetItemInfo(itemName);
                if (info == null) return false;

                return GiveItem(player, info, count);
            }
            catch (Exception ex)
            {
                LogException(nameof(GiveItem), ex);
                return false;
            }
        }

        public bool GiveItem(PlayerObject player, int itemIndex, int count = 1)
        {
            try
            {
                if (player == null) return false;
                if (count <= 0) return true;

                var info = Envir.GetItemInfo(itemIndex);
                if (info == null) return false;

                return GiveItem(player, info, count);
            }
            catch (Exception ex)
            {
                LogException(nameof(GiveItem), ex);
                return false;
            }
        }

        public bool TakeItem(PlayerObject player, string itemName, int count = 1, ushort? minDura = null)
        {
            try
            {
                if (player == null) return false;
                if (count <= 0) return true;

                var info = Envir.GetItemInfo(itemName);
                if (info == null) return false;

                return TakeItem(player, info, count, minDura);
            }
            catch (Exception ex)
            {
                LogException(nameof(TakeItem), ex);
                return false;
            }
        }

        public bool TakeItem(PlayerObject player, int itemIndex, int count = 1, ushort? minDura = null)
        {
            try
            {
                if (player == null) return false;
                if (count <= 0) return true;

                var info = Envir.GetItemInfo(itemIndex);
                if (info == null) return false;

                return TakeItem(player, info, count, minDura);
            }
            catch (Exception ex)
            {
                LogException(nameof(TakeItem), ex);
                return false;
            }
        }

        public bool HasItem(PlayerObject player, string itemName, int count = 1, ushort? minDura = null)
        {
            try
            {
                if (player == null) return false;
                if (count <= 0) return true;

                var info = Envir.GetItemInfo(itemName);
                if (info == null) return false;

                return GetItemCount(player, info, minDura) >= count;
            }
            catch (Exception ex)
            {
                LogException(nameof(HasItem), ex);
                return false;
            }
        }

        public bool HasItem(PlayerObject player, int itemIndex, int count = 1, ushort? minDura = null)
        {
            try
            {
                if (player == null) return false;
                if (count <= 0) return true;

                var info = Envir.GetItemInfo(itemIndex);
                if (info == null) return false;

                return GetItemCount(player, info, minDura) >= count;
            }
            catch (Exception ex)
            {
                LogException(nameof(HasItem), ex);
                return false;
            }
        }

        public bool Compare<T>(string op, T left, T right) where T : IComparable<T>
        {
            try
            {
                if (string.IsNullOrWhiteSpace(op)) return false;

                switch (op.Trim())
                {
                    case "<": return left.CompareTo(right) < 0;
                    case ">": return left.CompareTo(right) > 0;
                    case "<=": return left.CompareTo(right) <= 0;
                    case ">=": return left.CompareTo(right) >= 0;
                    case "==": return EqualityComparer<T>.Default.Equals(left, right);
                    case "!=": return !EqualityComparer<T>.Default.Equals(left, right);
                    default: return false;
                }
            }
            catch (Exception ex)
            {
                LogException(nameof(Compare), ex);
                return false;
            }
        }

        public uint GetGold(PlayerObject player)
        {
            try
            {
                if (player == null) return 0;
                return player.Account.Gold;
            }
            catch (Exception ex)
            {
                LogException(nameof(GetGold), ex);
                return 0;
            }
        }

        public bool CheckGold(PlayerObject player, string op, uint amount)
        {
            try
            {
                if (player == null) return false;
                return Compare(op, player.Account.Gold, amount);
            }
            catch (Exception ex)
            {
                LogException(nameof(CheckGold), ex);
                return false;
            }
        }

        public uint GetCredit(PlayerObject player)
        {
            try
            {
                if (player == null) return 0;
                return player.Account.Credit;
            }
            catch (Exception ex)
            {
                LogException(nameof(GetCredit), ex);
                return 0;
            }
        }

        public bool CheckCredit(PlayerObject player, string op, uint amount)
        {
            try
            {
                if (player == null) return false;
                return Compare(op, player.Account.Credit, amount);
            }
            catch (Exception ex)
            {
                LogException(nameof(CheckCredit), ex);
                return false;
            }
        }

        public int GetLevel(PlayerObject player)
        {
            try
            {
                if (player == null) return 0;
                return player.Level;
            }
            catch (Exception ex)
            {
                LogException(nameof(GetLevel), ex);
                return 0;
            }
        }

        public bool CheckLevel(PlayerObject player, string op, int level)
        {
            try
            {
                if (player == null) return false;
                return Compare(op, player.Level, level);
            }
            catch (Exception ex)
            {
                LogException(nameof(CheckLevel), ex);
                return false;
            }
        }

        public bool IsQuestActive(PlayerObject player, int questIndex)
        {
            try
            {
                if (player == null) return false;

                for (int i = 0; i < player.CurrentQuests.Count; i++)
                {
                    var q = player.CurrentQuests[i];
                    if (q == null) continue;
                    if (q.Index == questIndex) return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                LogException(nameof(IsQuestActive), ex);
                return false;
            }
        }

        public bool CheckQuest(PlayerObject player, int questIndex, string state)
        {
            try
            {
                if (player == null) return false;
                if (string.IsNullOrWhiteSpace(state)) return false;

                var s = state.Trim();

                if (s.Equals("ACTIVE", StringComparison.OrdinalIgnoreCase))
                    return IsQuestActive(player, questIndex);

                if (s.Equals("COMPLETE", StringComparison.OrdinalIgnoreCase) ||
                    s.Equals("COMPLETED", StringComparison.OrdinalIgnoreCase))
                    return IsQuestCompleted(player, questIndex);

                return false;
            }
            catch (Exception ex)
            {
                LogException(nameof(CheckQuest), ex);
                return false;
            }
        }

        public bool IsGroupLeader(PlayerObject player)
        {
            try
            {
                if (player == null) return false;
                if (player.GroupMembers == null) return false;
                if (player.GroupMembers.Count <= 0) return false;

                return player.GroupMembers[0] == player;
            }
            catch (Exception ex)
            {
                LogException(nameof(IsGroupLeader), ex);
                return false;
            }
        }

        public int GetGroupCount(PlayerObject player)
        {
            try
            {
                if (player == null) return 0;
                return player.GroupMembers?.Count ?? 0;
            }
            catch (Exception ex)
            {
                LogException(nameof(GetGroupCount), ex);
                return 0;
            }
        }

        public bool CheckGroupCount(PlayerObject player, string op, int count)
        {
            try
            {
                if (player == null) return false;
                if (player.GroupMembers == null) return false;

                return Compare(op, player.GroupMembers.Count, count);
            }
            catch (Exception ex)
            {
                LogException(nameof(CheckGroupCount), ex);
                return false;
            }
        }

        public bool GroupCheckNearby(PlayerObject player, NpcPageCall call, int range = 9)
        {
            try
            {
                if (player == null) return false;
                if (call == null) return false;
                if (player.GroupMembers == null) return false;
                if (range < 0) return false;

                var npc = NPCObject.Get(call.NpcObjectID);
                if (npc == null) return false;

                var target = npc.CurrentLocation;

                for (int i = 0; i < player.GroupMembers.Count; i++)
                {
                    var member = player.GroupMembers[i];
                    if (member == null) continue;

                    if (!Functions.InRange(member.CurrentLocation, target, range))
                    {
                        if (ScriptTrace.IsEnabled(player))
                        {
                            ScriptTrace.Record(player, $"[C#] GROUPCHECKNEARBY range={range} -> false");
                        }

                        return false;
                    }
                }

                if (ScriptTrace.IsEnabled(player))
                {
                    ScriptTrace.Record(player, $"[C#] GROUPCHECKNEARBY range={range} -> true");
                }

                return true;
            }
            catch (Exception ex)
            {
                LogException(nameof(GroupCheckNearby), ex);
                return false;
            }
        }

        public bool Random(PlayerObject player, int oneIn)
        {
            try
            {
                if (player == null) return false;
                if (oneIn <= 0) return false;

                var ok = Envir.Random.Next(0, oneIn) == 0;

                if (ScriptTrace.IsEnabled(player))
                {
                    ScriptTrace.Record(player, $"[C#] RANDOM 1/{oneIn} -> {ok}");
                }

                return ok;
            }
            catch (Exception ex)
            {
                LogException(nameof(Random), ex);
                return false;
            }
        }

        public bool IsInRange(PlayerObject player, int x, int y, int range)
        {
            try
            {
                if (player == null) return false;
                if (range < 0) return false;

                return Functions.InRange(player.CurrentLocation, new Point(x, y), range);
            }
            catch (Exception ex)
            {
                LogException(nameof(IsInRange), ex);
                return false;
            }
        }

        public uint GiveGold(PlayerObject player, uint amount)
        {
            try
            {
                if (player == null) return 0;
                if (amount == 0) return 0;

                var gold = amount;
                if (gold + player.Account.Gold >= uint.MaxValue)
                    gold = uint.MaxValue - player.Account.Gold;

                player.GainGold(gold);
                return gold;
            }
            catch (Exception ex)
            {
                LogException(nameof(GiveGold), ex);
                return 0;
            }
        }

        public uint TakeGold(PlayerObject player, uint amount)
        {
            try
            {
                if (player == null) return 0;
                if (amount == 0) return 0;

                var gold = amount;
                if (gold >= player.Account.Gold) gold = player.Account.Gold;

                player.Account.Gold -= gold;
                player.Enqueue(new S.LoseGold { Gold = gold });
                return gold;
            }
            catch (Exception ex)
            {
                LogException(nameof(TakeGold), ex);
                return 0;
            }
        }

        public uint GiveCredit(PlayerObject player, uint amount)
        {
            try
            {
                if (player == null) return 0;
                if (amount == 0) return 0;

                var credit = amount;
                if (credit + player.Account.Credit >= uint.MaxValue)
                    credit = uint.MaxValue - player.Account.Credit;

                player.GainCredit(credit);
                return credit;
            }
            catch (Exception ex)
            {
                LogException(nameof(GiveCredit), ex);
                return 0;
            }
        }

        public uint TakeCredit(PlayerObject player, uint amount)
        {
            try
            {
                if (player == null) return 0;
                if (amount == 0) return 0;

                var credit = amount;
                if (credit >= player.Account.Credit) credit = player.Account.Credit;

                player.Account.Credit -= credit;
                player.Enqueue(new S.LoseCredit { Credit = credit });
                return credit;
            }
            catch (Exception ex)
            {
                LogException(nameof(TakeCredit), ex);
                return 0;
            }
        }

        public uint GivePearls(PlayerObject player, uint amount)
        {
            try
            {
                if (player == null) return 0;
                if (amount == 0) return 0;

                var current = player.Info.PearlCount;
                if (current < 0) current = 0;

                var maxAdd = int.MaxValue - current;
                if (maxAdd <= 0) return 0;

                var pearls = amount;
                if (pearls > (uint)maxAdd) pearls = (uint)maxAdd;
                if (pearls == 0) return 0;

                player.IntelligentCreatureGainPearls((int)pearls);

                if (ScriptTrace.IsEnabled(player))
                {
                    ScriptTrace.Record(player, $"[C#] GIVEPEARLS x{amount} -> {pearls}");
                }

                return pearls;
            }
            catch (Exception ex)
            {
                LogException(nameof(GivePearls), ex);
                return 0;
            }
        }

        public uint TakePearls(PlayerObject player, uint amount)
        {
            try
            {
                if (player == null) return 0;
                if (amount == 0) return 0;

                var current = player.Info.PearlCount;
                if (current <= 0) return 0;

                var pearls = amount;
                if (pearls > (uint)current) pearls = (uint)current;
                if (pearls == 0) return 0;

                player.IntelligentCreatureLosePearls((int)pearls);

                if (ScriptTrace.IsEnabled(player))
                {
                    ScriptTrace.Record(player, $"[C#] TAKEPEARLS x{amount} -> {pearls}");
                }

                return pearls;
            }
            catch (Exception ex)
            {
                LogException(nameof(TakePearls), ex);
                return 0;
            }
        }

        public uint GiveGuildGold(PlayerObject player, uint amount)
        {
            try
            {
                if (player == null) return 0;
                if (player.MyGuild == null) return 0;
                if (amount == 0) return 0;

                var gold = amount;
                if (gold + player.MyGuild.Gold >= uint.MaxValue)
                    gold = uint.MaxValue - player.MyGuild.Gold;

                player.MyGuild.Gold += gold;
                player.MyGuild.SendServerPacket(new S.GuildStorageGoldChange { Type = 3, Amount = gold });
                return gold;
            }
            catch (Exception ex)
            {
                LogException(nameof(GiveGuildGold), ex);
                return 0;
            }
        }

        public uint TakeGuildGold(PlayerObject player, uint amount)
        {
            try
            {
                if (player == null) return 0;
                if (player.MyGuild == null) return 0;
                if (amount == 0) return 0;

                var gold = amount;
                if (gold >= player.MyGuild.Gold) gold = player.MyGuild.Gold;

                player.MyGuild.Gold -= gold;
                player.MyGuild.SendServerPacket(new S.GuildStorageGoldChange { Type = 2, Amount = gold });
                return gold;
            }
            catch (Exception ex)
            {
                LogException(nameof(TakeGuildGold), ex);
                return 0;
            }
        }

        public bool ConquestGuard(PlayerObject player, NpcPageCall call, string conquestIndexToken, string archerIndexToken)
        {
            try
            {
                if (!TryResolveLegacyInt(player, call, conquestIndexToken, out var conquestIndex)) return false;
                if (!TryResolveLegacyInt(player, call, archerIndexToken, out var archerIndex)) return false;

                return ConquestGuard(player, conquestIndex, archerIndex);
            }
            catch (Exception ex)
            {
                LogException(nameof(ConquestGuard), ex);
                return false;
            }
        }

        public bool ConquestGuard(PlayerObject player, int conquestIndex, int archerIndex)
        {
            try
            {
                if (player == null) return false;

                var conquest = Envir.Conquests.FirstOrDefault(z => z.Info.Index == conquestIndex);
                if (conquest == null) return false;

                var conquestArcher = conquest.ArcherList.FirstOrDefault(z => z.Index == archerIndex);
                if (conquestArcher == null) return false;

                if (conquestArcher.ArcherMonster != null && !conquestArcher.ArcherMonster.Dead) return false;

                if (player.IsGM)
                {
                    conquestArcher.Spawn(true);
                }
                else
                {
                    if (player.MyGuild == null) return false;

                    var cost = conquestArcher.GetRepairCost();
                    if (player.MyGuild.Gold < cost) return false;

                    player.MyGuild.Gold -= cost;
                    player.MyGuild.SendServerPacket(new S.GuildStorageGoldChange { Type = 2, Amount = cost });

                    conquestArcher.Spawn(true);
                }

                if (ScriptTrace.IsEnabled(player))
                {
                    ScriptTrace.Record(player, $"[C#] CONQUESTGUARD {conquestIndex} {archerIndex}");
                }

                return true;
            }
            catch (Exception ex)
            {
                LogException(nameof(ConquestGuard), ex);
                return false;
            }
        }

        public bool ConquestGate(PlayerObject player, NpcPageCall call, string conquestIndexToken, string gateIndexToken)
        {
            try
            {
                if (!TryResolveLegacyInt(player, call, conquestIndexToken, out var conquestIndex)) return false;
                if (!TryResolveLegacyInt(player, call, gateIndexToken, out var gateIndex)) return false;

                return ConquestGate(player, conquestIndex, gateIndex);
            }
            catch (Exception ex)
            {
                LogException(nameof(ConquestGate), ex);
                return false;
            }
        }

        public bool ConquestGate(PlayerObject player, int conquestIndex, int gateIndex)
        {
            try
            {
                if (player == null) return false;

                var conquest = Envir.Conquests.FirstOrDefault(z => z.Info.Index == conquestIndex);
                if (conquest == null) return false;

                var conquestGate = conquest.GateList.FirstOrDefault(z => z.Index == gateIndex);
                if (conquestGate == null) return false;

                if (player.IsGM)
                {
                    conquestGate.Repair();
                }
                else
                {
                    if (player.MyGuild == null) return false;

                    var cost = conquestGate.GetRepairCost();
                    if (player.MyGuild.Gold < cost) return false;

                    player.MyGuild.Gold -= (uint)cost;
                    player.MyGuild.SendServerPacket(new S.GuildStorageGoldChange { Type = 2, Amount = (uint)cost });

                    conquestGate.Repair();
                }

                if (ScriptTrace.IsEnabled(player))
                {
                    ScriptTrace.Record(player, $"[C#] CONQUESTGATE {conquestIndex} {gateIndex}");
                }

                return true;
            }
            catch (Exception ex)
            {
                LogException(nameof(ConquestGate), ex);
                return false;
            }
        }

        public bool ConquestWall(PlayerObject player, NpcPageCall call, string conquestIndexToken, string wallIndexToken)
        {
            try
            {
                if (!TryResolveLegacyInt(player, call, conquestIndexToken, out var conquestIndex)) return false;
                if (!TryResolveLegacyInt(player, call, wallIndexToken, out var wallIndex)) return false;

                return ConquestWall(player, conquestIndex, wallIndex);
            }
            catch (Exception ex)
            {
                LogException(nameof(ConquestWall), ex);
                return false;
            }
        }

        public bool ConquestWall(PlayerObject player, int conquestIndex, int wallIndex)
        {
            try
            {
                if (player == null) return false;

                var conquest = Envir.Conquests.FirstOrDefault(z => z.Info.Index == conquestIndex);
                if (conquest == null) return false;

                var conquestWall = conquest.WallList.FirstOrDefault(z => z.Index == wallIndex);
                if (conquestWall == null) return false;

                if (player.IsGM)
                {
                    conquestWall.Repair();
                }
                else
                {
                    if (player.MyGuild == null) return false;

                    var cost = conquestWall.GetRepairCost();
                    if (player.MyGuild.Gold < cost) return false;

                    player.MyGuild.Gold -= (uint)cost;
                    player.MyGuild.SendServerPacket(new S.GuildStorageGoldChange { Type = 2, Amount = (uint)cost });

                    conquestWall.Repair();
                }

                if (ScriptTrace.IsEnabled(player))
                {
                    ScriptTrace.Record(player, $"[C#] CONQUESTWALL {conquestIndex} {wallIndex}");
                }

                return true;
            }
            catch (Exception ex)
            {
                LogException(nameof(ConquestWall), ex);
                return false;
            }
        }

        public bool ConquestSiege(PlayerObject player, NpcPageCall call, string conquestIndexToken, string siegeIndexToken)
        {
            try
            {
                if (!TryResolveLegacyInt(player, call, conquestIndexToken, out var conquestIndex)) return false;
                if (!TryResolveLegacyInt(player, call, siegeIndexToken, out var siegeIndex)) return false;

                return ConquestSiege(player, conquestIndex, siegeIndex);
            }
            catch (Exception ex)
            {
                LogException(nameof(ConquestSiege), ex);
                return false;
            }
        }

        public bool ConquestSiege(PlayerObject player, int conquestIndex, int siegeIndex)
        {
            try
            {
                if (player == null) return false;

                var conquest = Envir.Conquests.FirstOrDefault(z => z.Info.Index == conquestIndex);
                if (conquest == null) return false;

                var conquestSiege = conquest.SiegeList.FirstOrDefault(z => z.Index == siegeIndex);
                if (conquestSiege == null) return false;

                if (conquestSiege.Gate != null && !conquestSiege.Gate.Dead) return false;

                if (player.IsGM)
                {
                    conquestSiege.Repair();
                }
                else
                {
                    if (player.MyGuild == null) return false;

                    var cost = conquestSiege.GetRepairCost();
                    if (player.MyGuild.Gold < cost) return false;

                    player.MyGuild.Gold -= (uint)cost;
                    player.MyGuild.SendServerPacket(new S.GuildStorageGoldChange { Type = 2, Amount = (uint)cost });

                    conquestSiege.Repair();
                }

                if (ScriptTrace.IsEnabled(player))
                {
                    ScriptTrace.Record(player, $"[C#] CONQUESTSIEGE {conquestIndex} {siegeIndex}");
                }

                return true;
            }
            catch (Exception ex)
            {
                LogException(nameof(ConquestSiege), ex);
                return false;
            }
        }

        public bool TakeConquestGold(PlayerObject player, NpcPageCall call, string conquestIndexToken)
        {
            try
            {
                if (!TryResolveLegacyInt(player, call, conquestIndexToken, out var conquestIndex)) return false;

                return TakeConquestGold(player, conquestIndex);
            }
            catch (Exception ex)
            {
                LogException(nameof(TakeConquestGold), ex);
                return false;
            }
        }

        public bool TakeConquestGold(PlayerObject player, int conquestIndex)
        {
            try
            {
                if (player == null) return false;

                var conquest = Envir.Conquests.FirstOrDefault(z => z.Info.Index == conquestIndex);
                if (conquest == null) return false;

                if (player.MyGuild != null && player.MyGuild.Guildindex == conquest.GuildInfo.Owner)
                {
                    player.MyGuild.Gold += conquest.GuildInfo.GoldStorage;
                    player.MyGuild.SendServerPacket(new S.GuildStorageGoldChange { Type = 3, Amount = conquest.GuildInfo.GoldStorage });
                    conquest.GuildInfo.GoldStorage = 0;
                }

                if (ScriptTrace.IsEnabled(player))
                {
                    ScriptTrace.Record(player, $"[C#] TAKECONQUESTGOLD {conquestIndex}");
                }

                return true;
            }
            catch (Exception ex)
            {
                LogException(nameof(TakeConquestGold), ex);
                return false;
            }
        }

        public bool SetConquestRate(PlayerObject player, NpcPageCall call, string conquestIndexToken, string rateToken)
        {
            try
            {
                if (!TryResolveLegacyInt(player, call, conquestIndexToken, out var conquestIndex)) return false;
                if (!TryResolveLegacyInt(player, call, rateToken, out var rate)) return false;

                return SetConquestRate(player, conquestIndex, rate);
            }
            catch (Exception ex)
            {
                LogException(nameof(SetConquestRate), ex);
                return false;
            }
        }

        public bool SetConquestRate(PlayerObject player, int conquestIndex, int rate)
        {
            try
            {
                if (player == null) return false;
                if (rate < byte.MinValue || rate > byte.MaxValue) return false;

                var conquest = Envir.Conquests.FirstOrDefault(z => z.Info.Index == conquestIndex);
                if (conquest == null) return false;

                if (player.MyGuild != null && player.MyGuild.Guildindex == conquest.GuildInfo.Owner)
                {
                    conquest.GuildInfo.NPCRate = (byte)rate;
                }

                if (ScriptTrace.IsEnabled(player))
                {
                    ScriptTrace.Record(player, $"[C#] SETCONQUESTRATE {conquestIndex} {rate}");
                }

                return true;
            }
            catch (Exception ex)
            {
                LogException(nameof(SetConquestRate), ex);
                return false;
            }
        }

        public bool OpenGate(PlayerObject player, NpcPageCall call, string conquestIndexToken, string gateIndexToken)
        {
            try
            {
                if (!TryResolveLegacyInt(player, call, conquestIndexToken, out var conquestIndex)) return false;
                if (!TryResolveLegacyInt(player, call, gateIndexToken, out var gateIndex)) return false;

                return OpenGate(player, conquestIndex, gateIndex);
            }
            catch (Exception ex)
            {
                LogException(nameof(OpenGate), ex);
                return false;
            }
        }

        public bool OpenGate(PlayerObject player, int conquestIndex, int gateIndex)
        {
            try
            {
                if (player == null) return false;

                var conquest = Envir.Conquests.FirstOrDefault(z => z.Info.Index == conquestIndex);
                if (conquest == null) return false;

                var openGate = conquest.GateList.FirstOrDefault(z => z.Index == gateIndex);
                if (openGate == null) return false;
                if (openGate.Gate == null) return false;

                openGate.Gate.OpenDoor();

                if (ScriptTrace.IsEnabled(player))
                {
                    ScriptTrace.Record(player, $"[C#] OPENGATE {conquestIndex} {gateIndex}");
                }

                return true;
            }
            catch (Exception ex)
            {
                LogException(nameof(OpenGate), ex);
                return false;
            }
        }

        public bool CloseGate(PlayerObject player, NpcPageCall call, string conquestIndexToken, string gateIndexToken)
        {
            try
            {
                if (!TryResolveLegacyInt(player, call, conquestIndexToken, out var conquestIndex)) return false;
                if (!TryResolveLegacyInt(player, call, gateIndexToken, out var gateIndex)) return false;

                return CloseGate(player, conquestIndex, gateIndex);
            }
            catch (Exception ex)
            {
                LogException(nameof(CloseGate), ex);
                return false;
            }
        }

        public bool CloseGate(PlayerObject player, int conquestIndex, int gateIndex)
        {
            try
            {
                if (player == null) return false;

                var conquest = Envir.Conquests.FirstOrDefault(z => z.Info.Index == conquestIndex);
                if (conquest == null) return false;

                var closeGate = conquest.GateList.FirstOrDefault(z => z.Index == gateIndex);
                if (closeGate == null) return false;
                if (closeGate.Gate == null) return false;

                closeGate.Gate.CloseDoor();

                if (ScriptTrace.IsEnabled(player))
                {
                    ScriptTrace.Record(player, $"[C#] CLOSEGATE {conquestIndex} {gateIndex}");
                }

                return true;
            }
            catch (Exception ex)
            {
                LogException(nameof(CloseGate), ex);
                return false;
            }
        }

        public bool ConquestRepairAll(PlayerObject player, NpcPageCall call, string conquestIndexToken)
        {
            try
            {
                if (!TryResolveLegacyInt(player, call, conquestIndexToken, out var conquestIndex)) return false;
                return ConquestRepairAll(player, conquestIndex);
            }
            catch (Exception ex)
            {
                LogException(nameof(ConquestRepairAll), ex);
                return false;
            }
        }

        public bool ConquestRepairAll(PlayerObject player, int conquestIndex)
        {
            try
            {
                if (player == null) return false;

                if (!player.IsGM)
                {
                    player.ReceiveChat("非游戏管理员，该命令无效", ChatType.System);
                    MessageQueue.Instance.Enqueue($"非管理员玩家: {player.Name} 调用了 CONQUESTREPAIRALL 命令");
                    return false;
                }

                var conquest = Envir.Conquests.FirstOrDefault(z => z.Info.Index == conquestIndex);
                if (conquest == null) return false;

                MessageQueue.Instance.Enqueue($"游戏管理员:{player.Name} 在账户目录为:{player.Info.AccountInfo.Index}上调用了 CONQUESTREPAIRALL 命令");
                MessageQueue.Instance.Enqueue($"攻城战: {conquest.Info.Name}");

                if (conquest.Guild != null)
                {
                    MessageQueue.Instance.Enqueue($"城堡拥有者: {conquest.Guild.Name}");
                }
                else
                {
                    MessageQueue.Instance.Enqueue("城堡当前没有拥有者");
                }

                int fixedCount = 0;
                foreach (var archer in conquest.ArcherList)
                {
                    if (archer?.ArcherMonster != null && archer.ArcherMonster.Dead)
                    {
                        archer.Spawn(true);
                        fixedCount++;
                    }
                }

                player.ReceiveChat($"恢复弓箭手: {fixedCount}/{conquest.ArcherList.Count}", ChatType.System);
                MessageQueue.Instance.Enqueue($"恢复弓箭手: {fixedCount}/{conquest.ArcherList.Count}");

                fixedCount = 0;
                foreach (var conquestGate in conquest.GateList)
                {
                    if (conquestGate != null)
                    {
                        conquestGate.Repair();
                        fixedCount++;
                    }
                }

                player.ReceiveChat($"恢复卫士: {fixedCount}/{conquest.GateList.Count}", ChatType.System);
                MessageQueue.Instance.Enqueue($"恢复卫士: {fixedCount}/{conquest.GateList.Count}");

                fixedCount = 0;
                foreach (var conquestWall in conquest.WallList)
                {
                    if (conquestWall != null)
                    {
                        conquestWall.Repair();
                        fixedCount++;
                    }
                }

                player.ReceiveChat($"修复城墙: {fixedCount}/{conquest.WallList.Count}", ChatType.System);
                MessageQueue.Instance.Enqueue($"修复城墙: {fixedCount}/{conquest.WallList.Count}");

                fixedCount = 0;
                foreach (var conquestSiege in conquest.SiegeList)
                {
                    if (conquestSiege != null)
                    {
                        conquestSiege.Repair();
                        fixedCount++;
                    }
                }

                player.ReceiveChat($"Sieges repaired: {fixedCount}/{conquest.SiegeList.Count}", ChatType.System);
                MessageQueue.Instance.Enqueue($"Sieges repaired: {fixedCount}/{conquest.SiegeList.Count}");

                if (ScriptTrace.IsEnabled(player))
                {
                    ScriptTrace.Record(player, $"[C#] CONQUESTREPAIRALL {conquestIndex}");
                }

                return true;
            }
            catch (Exception ex)
            {
                LogException(nameof(ConquestRepairAll), ex);
                return false;
            }
        }

        public bool TryRemoveFromGuild(PlayerObject player)
        {
            try
            {
                if (player == null) return false;
                if (player.MyGuild == null) return false;
                if (player.MyGuildRank == null) return false;

                if (string.Equals(player.MyGuild.Name, Settings.NewbieGuild, StringComparison.Ordinal))
                {
                    RemoveBuff(player, BuffType.新人特效);
                }

                if (player.HasBuff(BuffType.公会特效))
                {
                    RemoveBuff(player, BuffType.公会特效);
                }

                player.MyGuild.DeleteMember(player, player.Name);
                return true;
            }
            catch (Exception ex)
            {
                LogException(nameof(TryRemoveFromGuild), ex);
                return false;
            }
        }

        public bool AddToGuild(PlayerObject player, string guildName)
        {
            try
            {
                if (player == null) return false;

                guildName = (guildName ?? string.Empty).Trim();
                if (guildName.Length == 0) return false;

                if (player.MyGuild != null) return false;

                var guild = Envir.GetGuild(guildName);
                if (guild == null) return false;

                player.PendingGuildInvite = guild;
                player.GuildInvite(true);

                if (ScriptTrace.IsEnabled(player))
                {
                    ScriptTrace.Record(player, $"[C#] ADDTOGUILD \"{guildName}\" (ok=true)");
                }

                return true;
            }
            catch (Exception ex)
            {
                LogException(nameof(AddToGuild), ex);
                return false;
            }
        }

        public bool NameListContains(string listPath, string value)
        {
            try
            {
                return Envir.NameListContains(listPath, value);
            }
            catch (Exception ex)
            {
                LogException(nameof(NameListContains), ex);
                return false;
            }
        }

        public bool AddNameToNameList(string listPath, string value)
        {
            try
            {
                return Envir.AddNameToNameList(listPath, value);
            }
            catch (Exception ex)
            {
                LogException(nameof(AddNameToNameList), ex);
                return false;
            }
        }

        public bool RemoveNameFromNameList(string listPath, string value)
        {
            try
            {
                return Envir.RemoveNameFromNameList(listPath, value);
            }
            catch (Exception ex)
            {
                LogException(nameof(RemoveNameFromNameList), ex);
                return false;
            }
        }

        public string LoadValue(string fileName, string section, string key, string defaultValue = "")
        {
            try
            {
                if (string.IsNullOrWhiteSpace(fileName)) return defaultValue ?? string.Empty;
                if (string.IsNullOrWhiteSpace(key)) return defaultValue ?? string.Empty;
                return Envir.LoadValue(fileName, section, key, defaultValue ?? string.Empty, writeWhenNull: true);
            }
            catch (Exception ex)
            {
                LogException(nameof(LoadValue), ex);
                return defaultValue ?? string.Empty;
            }
        }

        public string LoadValue(PlayerObject player, string fileName, string section, string key, string defaultValue = "")
        {
            var result = LoadValue(fileName, section, key, defaultValue);

            if (ScriptTrace.IsEnabled(player))
            {
                ScriptTrace.Record(player, $"[C#] LOADVALUE \"{fileName}\" [{section}] {key} -> \"{TruncForTrace(result)}\"");
            }

            return result;
        }

        public bool SaveValue(string fileName, string section, string key, string value)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(fileName)) return false;
                if (string.IsNullOrWhiteSpace(key)) return false;
                return Envir.SaveValue(fileName, section, key, value ?? string.Empty);
            }
            catch (Exception ex)
            {
                LogException(nameof(SaveValue), ex);
                return false;
            }
        }

        public bool SaveValue(PlayerObject player, string fileName, string section, string key, string value)
        {
            var ok = SaveValue(fileName, section, key, value);

            if (ScriptTrace.IsEnabled(player))
            {
                ScriptTrace.Record(player, $"[C#] SAVEVALUE \"{fileName}\" [{section}] {key} = \"{TruncForTrace(value)}\" (ok={ok})");
            }

            return ok;
        }

        public bool IsOnMap(PlayerObject player, string mapFileName, int instanceId = 0)
        {
            try
            {
                if (player == null) return false;
                if (string.IsNullOrWhiteSpace(mapFileName)) return false;

                var map = Envir.GetMapByNameAndInstance(mapFileName, instanceId);
                if (map == null) return false;

                return player.CurrentMap == map;
            }
            catch (Exception ex)
            {
                LogException(nameof(IsOnMap), ex);
                return false;
            }
        }

        public bool Teleport(PlayerObject player, string mapFileName, int x, int y, int instanceId = 0)
        {
            try
            {
                if (player == null) return false;

                var map = Envir.GetMapByNameAndInstance(mapFileName, instanceId);
                if (map == null) return false;

                if (x > 0 && y > 0)
                    player.Teleport(map, new Point(x, y));
                else
                    player.TeleportRandom(200, 0, map);

                if (ScriptTrace.IsEnabled(player))
                {
                    var mode = (x > 0 && y > 0) ? $"{mapFileName}({instanceId}) {x},{y}" : $"{mapFileName}({instanceId}) RANDOM";
                    ScriptTrace.Record(player, $"[C#] TELEPORT {mode}");
                }

                return true;
            }
            catch (Exception ex)
            {
                LogException(nameof(Teleport), ex);
                return false;
            }
        }

        public bool RollYut(PlayerObject player, string returnPageLabel, bool autoRoll = false)
        {
            try
            {
                if (player == null) return false;
                if (string.IsNullOrWhiteSpace(returnPageLabel)) return false;

                var page = returnPageLabel.Trim();
                var result = Envir.Random.Next(1, 7);

                player.NPCData["NPCRollResult"] = result;
                player.Enqueue(new S.Roll { Type = 1, Page = page, AutoRoll = autoRoll, Result = result });

                if (ScriptTrace.IsEnabled(player))
                {
                    ScriptTrace.Record(player, $"[C#] ROLLYUT {page} {autoRoll} => {result}");
                }

                return true;
            }
            catch (Exception ex)
            {
                LogException(nameof(RollYut), ex);
                return false;
            }
        }

        public bool OpenDefaultNpcPage(PlayerObject player, string pageKey)
        {
            try
            {
                if (player == null) return false;
                if (string.IsNullOrWhiteSpace(pageKey)) return false;

                if (Envir.DefaultNPC == null) return false;

                var key = NormalizePageKey(pageKey);
                if (string.IsNullOrEmpty(key)) return false;

                var action = new DelayedAction(DelayedType.NPC, Envir.Time, Envir.DefaultNPC.LoadedObjectID, Envir.DefaultNPC.ScriptID, key);
                player.ActionList.Add(action);

                player.Enqueue(new S.NPCUpdate { NPCID = Envir.DefaultNPC.LoadedObjectID });

                if (ScriptTrace.IsEnabled(player))
                {
                    ScriptTrace.Record(player, $"[C#] OPENNPCPAGE {key}");
                }

                return true;
            }
            catch (Exception ex)
            {
                LogException(nameof(OpenDefaultNpcPage), ex);
                return false;
            }
        }

        private static string NormalizePageKey(string pageKey)
        {
            if (string.IsNullOrWhiteSpace(pageKey)) return string.Empty;

            var key = pageKey.Trim();

            if (key.StartsWith("[@", StringComparison.OrdinalIgnoreCase) && key.EndsWith("]", StringComparison.Ordinal))
                return key;

            if (key.StartsWith("@", StringComparison.Ordinal))
                return "[" + key + "]";

            return key;
        }

        public bool DelayGoto(PlayerObject player, NpcPageCall call, string secondsToken, string pageKey)
        {
            try
            {
                if (!TryResolveLegacyLong(player, call, secondsToken, out var seconds)) return false;
                return DelayGoto(player, call, seconds, pageKey);
            }
            catch (Exception ex)
            {
                LogException(nameof(DelayGoto), ex);
                return false;
            }
        }

        public bool DelayGoto(PlayerObject player, NpcPageCall call, long seconds, string pageKey)
        {
            try
            {
                if (player == null) return false;
                if (call == null) return false;
                if (string.IsNullOrWhiteSpace(pageKey)) return false;

                var key = NormalizePageKey(pageKey);
                if (string.IsNullOrWhiteSpace(key)) return false;

                var action = new DelayedAction(DelayedType.NPC, Envir.Time + (seconds * 1000), call.NpcObjectID, call.NpcScriptID, key);
                player.ActionList.Add(action);

                if (ScriptTrace.IsEnabled(player))
                {
                    ScriptTrace.Record(player, $"[C#] DELAYGOTO {seconds}s -> {key}");
                }

                return true;
            }
            catch (Exception ex)
            {
                LogException(nameof(DelayGoto), ex);
                return false;
            }
        }

        public bool TimeRecall(PlayerObject player, NpcPageCall call, string secondsToken, string pageKey = "")
        {
            try
            {
                if (!TryResolveLegacyLong(player, call, secondsToken, out var seconds)) return false;
                return TimeRecall(player, call, seconds, pageKey);
            }
            catch (Exception ex)
            {
                LogException(nameof(TimeRecall), ex);
                return false;
            }
        }

        public bool TimeRecall(PlayerObject player, NpcPageCall call, long seconds, string pageKey = "")
        {
            try
            {
                if (player == null) return false;
                if (call == null) return false;

                var map = player.CurrentMap;
                if (map == null) return false;

                var coords = player.CurrentLocation;

                var key = string.Empty;
                if (!string.IsNullOrWhiteSpace(pageKey))
                {
                    key = NormalizePageKey(pageKey);
                    if (string.IsNullOrWhiteSpace(key)) return false;
                }

                var action = new DelayedAction(DelayedType.NPC, Envir.Time + (seconds * 1000), call.NpcObjectID, call.NpcScriptID, key, map, coords);
                player.ActionList.Add(action);

                if (ScriptTrace.IsEnabled(player))
                {
                    if (string.IsNullOrEmpty(key))
                        ScriptTrace.Record(player, $"[C#] TIMERECALL {seconds}s");
                    else
                        ScriptTrace.Record(player, $"[C#] TIMERECALL {seconds}s -> {key}");
                }

                return true;
            }
            catch (Exception ex)
            {
                LogException(nameof(TimeRecall), ex);
                return false;
            }
        }

        public bool TimeRecallGroup(PlayerObject player, NpcPageCall call, string secondsToken, string pageKey = "")
        {
            try
            {
                if (!TryResolveLegacyLong(player, call, secondsToken, out var seconds)) return false;
                return TimeRecallGroup(player, call, seconds, pageKey);
            }
            catch (Exception ex)
            {
                LogException(nameof(TimeRecallGroup), ex);
                return false;
            }
        }

        public bool TimeRecallGroup(PlayerObject player, NpcPageCall call, long seconds, string pageKey = "")
        {
            try
            {
                if (player == null) return false;
                if (call == null) return false;
                if (player.GroupMembers == null) return false;

                var map = player.CurrentMap;
                if (map == null) return false;

                var coords = player.CurrentLocation;

                var key = string.Empty;
                if (!string.IsNullOrWhiteSpace(pageKey))
                {
                    key = NormalizePageKey(pageKey);
                    if (string.IsNullOrWhiteSpace(key)) return false;
                }

                for (int i = 0; i < player.GroupMembers.Count; i++)
                {
                    var member = player.GroupMembers[i];
                    if (member == null) continue;

                    var action = new DelayedAction(DelayedType.NPC, Envir.Time + (seconds * 1000), call.NpcObjectID, call.NpcScriptID, key, map, coords);
                    member.ActionList.Add(action);
                }

                if (ScriptTrace.IsEnabled(player))
                {
                    var target = string.IsNullOrEmpty(key) ? "" : $" -> {key}";
                    ScriptTrace.Record(player, $"[C#] TIMERECALLGROUP {seconds}s{target} (count={player.GroupMembers.Count})");
                }

                return true;
            }
            catch (Exception ex)
            {
                LogException(nameof(TimeRecallGroup), ex);
                return false;
            }
        }

        public bool BreakTimeRecall(PlayerObject player)
        {
            try
            {
                if (player == null) return false;

                for (int i = 0; i < player.ActionList.Count; i++)
                {
                    var action = player.ActionList[i];
                    if (action == null) continue;
                    if (action.Type != DelayedType.NPC) continue;

                    action.FlaggedToRemove = true;
                }

                if (ScriptTrace.IsEnabled(player))
                {
                    ScriptTrace.Record(player, "[C#] BREAKTIMERECALL");
                }

                return true;
            }
            catch (Exception ex)
            {
                LogException(nameof(BreakTimeRecall), ex);
                return false;
            }
        }

        public bool OpenNpcStoragePanel(PlayerObject player)
        {
            try
            {
                if (player == null) return false;

                player.SendStorage();
                player.Enqueue(new S.NPCStorage());
                return true;
            }
            catch (Exception ex)
            {
                LogException(nameof(OpenNpcStoragePanel), ex);
                return false;
            }
        }

        public bool OpenNpcConsignPanel(PlayerObject player)
        {
            try
            {
                if (player == null) return false;

                player.Enqueue(new S.NPCConsign());

                if (ScriptTrace.IsEnabled(player))
                {
                    ScriptTrace.Record(player, "[C#] NPCCONSIGN");
                }

                return true;
            }
            catch (Exception ex)
            {
                LogException(nameof(OpenNpcConsignPanel), ex);
                return false;
            }
        }

        public bool OpenNpcMarketPanel(
            PlayerObject player,
            bool userMode = false,
            MarketPanelType marketType = MarketPanelType.Market,
            string match = "",
            ItemType type = ItemType.杂物)
        {
            try
            {
                if (player == null) return false;

                player.UserMatch = userMode;
                player.MarketPanelType = marketType;
                player.GetMarket(match ?? string.Empty, type);

                if (ScriptTrace.IsEnabled(player))
                {
                    ScriptTrace.Record(player, $"[C#] NPCMARKET userMode={userMode} marketType={marketType} match={TruncForTrace(match ?? string.Empty)} type={type}");
                }

                return true;
            }
            catch (Exception ex)
            {
                LogException(nameof(OpenNpcMarketPanel), ex);
                return false;
            }
        }

        public bool OpenNpcSendParcelPanel(PlayerObject player)
        {
            try
            {
                if (player == null) return false;

                player.Enqueue(new S.MailSendRequest());

                if (ScriptTrace.IsEnabled(player))
                {
                    ScriptTrace.Record(player, "[C#] MAILSENDREQUEST");
                }

                return true;
            }
            catch (Exception ex)
            {
                LogException(nameof(OpenNpcSendParcelPanel), ex);
                return false;
            }
        }

        public bool CollectNpcParcels(PlayerObject player)
        {
            try
            {
                if (player == null) return false;

                sbyte result = 0;

                if (player.GetMailAwaitingCollectionAmount() < 1)
                {
                    result = -1;
                }
                else
                {
                    foreach (var mail in player.Info.Mail)
                    {
                        if (mail == null) continue;
                        if (mail.Parcel) mail.Collected = true;
                    }
                }

                player.Enqueue(new S.ParcelCollected { Result = result });
                player.GetMail();

                if (ScriptTrace.IsEnabled(player))
                {
                    ScriptTrace.Record(player, $"[C#] PARCELCOLLECT result={result}");
                }

                return true;
            }
            catch (Exception ex)
            {
                LogException(nameof(CollectNpcParcels), ex);
                return false;
            }
        }

        public bool OpenNpcSellPanel(PlayerObject player, NpcPageCall call)
        {
            try
            {
                if (player == null) return false;
                if (call == null) return false;

                var script = NPCScript.Get(call.NpcScriptID);
                player.Enqueue(new S.NPCSell { Rate = script.GetSellRate(player) });
                return true;
            }
            catch (Exception ex)
            {
                LogException(nameof(OpenNpcSellPanel), ex);
                return false;
            }
        }

        public bool OpenNpcRepairPanel(PlayerObject player, NpcPageCall call, bool specialRepair = false)
        {
            try
            {
                if (player == null) return false;
                if (call == null) return false;

                var script = NPCScript.Get(call.NpcScriptID);
                var rate = script.PriceRate(player);

                if (specialRepair)
                    player.Enqueue(new S.NPCSRepair { Rate = rate });
                else
                    player.Enqueue(new S.NPCRepair { Rate = rate });

                return true;
            }
            catch (Exception ex)
            {
                LogException(nameof(OpenNpcRepairPanel), ex);
                return false;
            }
        }

        public bool OpenNpcCraftPanel(PlayerObject player, NpcPageCall call)
        {
            try
            {
                if (player == null) return false;
                if (call == null) return false;

                var script = NPCScript.Get(call.NpcScriptID);

                for (var i = 0; i < script.CraftGoods.Count; i++)
                    player.CheckItemInfo(script.CraftGoods[i].Item.Info);

                var craftable = new List<UserItem>();
                for (var i = 0; i < script.CraftGoods.Count; i++)
                {
                    var recipe = script.CraftGoods[i];
                    if (recipe == null) continue;
                    if (!recipe.CanCraft(player)) continue;
                    craftable.Add(recipe.Item);
                }

                player.SendNPCGoods(craftable, script.PriceRate(player), PanelType.Craft);
                return true;
            }
            catch (Exception ex)
            {
                LogException(nameof(OpenNpcCraftPanel), ex);
                return false;
            }
        }

        public bool OpenNpcRefinePanel(PlayerObject player)
        {
            try
            {
                if (player == null) return false;

                if (player.Info.CurrentRefine != null)
                {
                    player.ReceiveChat("精炼正在进行中...", ChatType.System);
                    player.Enqueue(new S.NPCRefine { Rate = Settings.RefineCost, Refining = true });
                    return true;
                }

                player.Enqueue(new S.NPCRefine { Rate = Settings.RefineCost, Refining = false });
                return true;
            }
            catch (Exception ex)
            {
                LogException(nameof(OpenNpcRefinePanel), ex);
                return false;
            }
        }

        public bool OpenNpcRefineCheckPanel(PlayerObject player)
        {
            try
            {
                if (player == null) return false;
                player.Enqueue(new S.NPCCheckRefine());
                return true;
            }
            catch (Exception ex)
            {
                LogException(nameof(OpenNpcRefineCheckPanel), ex);
                return false;
            }
        }

        public bool OpenNpcRefineCollect(PlayerObject player)
        {
            try
            {
                if (player == null) return false;
                player.CollectRefine();
                return true;
            }
            catch (Exception ex)
            {
                LogException(nameof(OpenNpcRefineCollect), ex);
                return false;
            }
        }

        public bool OpenNpcReplaceWeddingRingPanel(PlayerObject player)
        {
            try
            {
                if (player == null) return false;
                player.Enqueue(new S.NPCReplaceWedRing { Rate = Settings.ReplaceWedRingCost });
                return true;
            }
            catch (Exception ex)
            {
                LogException(nameof(OpenNpcReplaceWeddingRingPanel), ex);
                return false;
            }
        }

        public bool OpenNpcBuyPanel(PlayerObject player, NpcPageCall call) =>
            OpenNpcTradePanel(player, call, includeUsedGoods: true, includeSellPanel: false);

        public bool OpenNpcBuySellPanel(PlayerObject player, NpcPageCall call) =>
            OpenNpcTradePanel(player, call, includeUsedGoods: true, includeSellPanel: true);

        public bool OpenNpcBuyNewPanel(PlayerObject player, NpcPageCall call) =>
            OpenNpcTradePanel(player, call, includeUsedGoods: false, includeSellPanel: false);

        public bool OpenNpcBuySellNewPanel(PlayerObject player, NpcPageCall call) =>
            OpenNpcTradePanel(player, call, includeUsedGoods: false, includeSellPanel: true);

        private bool OpenNpcTradePanel(PlayerObject player, NpcPageCall call, bool includeUsedGoods, bool includeSellPanel)
        {
            try
            {
                if (player == null) return false;
                if (call == null) return false;

                var script = NPCScript.Get(call.NpcScriptID);

                var sentGoods = new List<UserItem>(script.Goods);

                for (int i = 0; i < script.Goods.Count; i++)
                    player.CheckItem(script.Goods[i]);

                if (includeUsedGoods && Settings.GoodsOn)
                {
                    var callingNPC = NPCObject.Get(call.NpcObjectID);

                    if (callingNPC != null)
                    {
                        for (int i = 0; i < callingNPC.UsedGoods.Count; i++)
                            player.CheckItem(callingNPC.UsedGoods[i]);

                        sentGoods.AddRange(callingNPC.UsedGoods);
                    }
                }

                player.SendNPCGoods(sentGoods, script.GetBuyRate(player), PanelType.Buy, Settings.GoodsHideAddedStats);

                if (includeSellPanel)
                    player.Enqueue(new S.NPCSell { Rate = script.GetSellRate(player) });

                return true;
            }
            catch (Exception ex)
            {
                LogException(nameof(OpenNpcBuyPanel), ex);
                return false;
            }
        }

        public bool OpenNpcBuyBackPanel(PlayerObject player, NpcPageCall call)
        {
            try
            {
                if (player == null) return false;
                if (call == null) return false;
                if (!Settings.GoodsOn) return false;

                var callingNPC = NPCObject.Get(call.NpcObjectID);
                if (callingNPC == null) return false;

                if (!callingNPC.BuyBack.ContainsKey(player.Name))
                    callingNPC.BuyBack[player.Name] = new List<UserItem>();

                for (int i = 0; i < callingNPC.BuyBack[player.Name].Count; i++)
                    player.CheckItem(callingNPC.BuyBack[player.Name][i]);

                var script = NPCScript.Get(call.NpcScriptID);
                player.SendNPCGoods(callingNPC.BuyBack[player.Name], script.GetBuyRate(player), PanelType.Buy);
                return true;
            }
            catch (Exception ex)
            {
                LogException(nameof(OpenNpcBuyBackPanel), ex);
                return false;
            }
        }

        public bool OpenNpcBuyUsedPanel(PlayerObject player, NpcPageCall call)
        {
            try
            {
                if (player == null) return false;
                if (call == null) return false;
                if (!Settings.GoodsOn) return false;

                var callingNPC = NPCObject.Get(call.NpcObjectID);
                if (callingNPC == null) return false;

                for (int i = 0; i < callingNPC.UsedGoods.Count; i++)
                    player.CheckItem(callingNPC.UsedGoods[i]);

                var script = NPCScript.Get(call.NpcScriptID);
                player.SendNPCGoods(callingNPC.UsedGoods, script.GetBuyRate(player), PanelType.BuySub, Settings.GoodsHideAddedStats);
                return true;
            }
            catch (Exception ex)
            {
                LogException(nameof(OpenNpcBuyUsedPanel), ex);
                return false;
            }
        }

        public bool TryEnterMap(PlayerObject player)
        {
            try
            {
                if (player == null) return false;

                if (!player.NPCData.TryGetValue("NPCMoveMap", out var npcMoveMap) ||
                    !player.NPCData.TryGetValue("NPCMoveCoord", out var npcMoveCoord))
                    return false;

                if (npcMoveMap is not Map map) return false;
                if (npcMoveCoord is not Point coord) return false;

                player.Teleport(map, coord, false);

                player.NPCData.Remove("NPCMoveMap");
                player.NPCData.Remove("NPCMoveCoord");

                return true;
            }
            catch (Exception ex)
            {
                LogException(nameof(TryEnterMap), ex);
                return false;
            }
        }

        public bool Recall(PlayerObject source, PlayerObject target)
        {
            try
            {
                if (source == null) return false;
                if (target == null) return false;

                target.Teleport(source.CurrentMap, source.CurrentLocation);
                return true;
            }
            catch (Exception ex)
            {
                LogException(nameof(Recall), ex);
                return false;
            }
        }

        public bool GroupRecall(PlayerObject player)
        {
            try
            {
                if (player == null) return false;
                if (player.GroupMembers == null) return false;

                for (int i = 0; i < player.GroupMembers.Count; i++)
                {
                    player.GroupMembers[i]?.Teleport(player.CurrentMap, player.CurrentLocation);
                }

                if (ScriptTrace.IsEnabled(player))
                {
                    ScriptTrace.Record(player, $"[C#] GROUPRECALL (count={player.GroupMembers.Count})");
                }

                return true;
            }
            catch (Exception ex)
            {
                LogException(nameof(GroupRecall), ex);
                return false;
            }
        }

        public bool GroupTeleport(PlayerObject player, string mapFileName, int x, int y, int instanceId = 0)
        {
            try
            {
                if (player == null) return false;
                if (player.GroupMembers == null) return false;
                if (string.IsNullOrWhiteSpace(mapFileName)) return false;

                var map = Envir.GetMapByNameAndInstance(mapFileName, instanceId);
                if (map == null) return false;

                for (int i = 0; i < player.GroupMembers.Count; i++)
                {
                    var member = player.GroupMembers[i];
                    if (member == null) continue;

                    if (x > 0 && y > 0)
                        member.Teleport(map, new Point(x, y));
                    else
                        member.TeleportRandom(200, 0, map);
                }

                if (ScriptTrace.IsEnabled(player))
                {
                    var mode = (x > 0 && y > 0) ? $"{mapFileName}({instanceId}) {x},{y}" : $"{mapFileName}({instanceId}) RANDOM";
                    ScriptTrace.Record(player, $"[C#] GROUPTELEPORT {mode} (count={player.GroupMembers.Count})");
                }

                return true;
            }
            catch (Exception ex)
            {
                LogException(nameof(GroupTeleport), ex);
                return false;
            }
        }

        public bool GroupGoto(PlayerObject player, NpcPageCall call, string pageKey)
        {
            try
            {
                if (player == null) return false;
                if (call == null) return false;
                if (player.GroupMembers == null) return false;

                var key = NormalizePageKey(pageKey);
                if (string.IsNullOrWhiteSpace(key)) return false;

                for (int i = 0; i < player.GroupMembers.Count; i++)
                {
                    var member = player.GroupMembers[i];
                    if (member == null) continue;

                    var action = new DelayedAction(DelayedType.NPC, Envir.Time, call.NpcObjectID, call.NpcScriptID, key);
                    member.ActionList.Add(action);
                }

                if (ScriptTrace.IsEnabled(player))
                {
                    ScriptTrace.Record(player, $"[C#] GROUPGOTO {key} (count={player.GroupMembers.Count})");
                }

                return true;
            }
            catch (Exception ex)
            {
                LogException(nameof(GroupGoto), ex);
                return false;
            }
        }

        public bool MonGen(string mapFileName, int x, int y, string monsterName, int count = 1, int instanceId = 0)
        {
            try
            {
                if (count <= 0) return true;

                var map = Envir.GetMapByNameAndInstance(mapFileName, instanceId);
                if (map == null) return false;

                var monInfo = Envir.GetMonsterInfo(monsterName);
                if (monInfo == null) return false;

                return MonGen(map, new Point(x, y), monInfo, count);
            }
            catch (Exception ex)
            {
                LogException(nameof(MonGen), ex);
                return false;
            }
        }

        public bool MonGen(string mapFileName, int x, int y, int monsterIndex, int count = 1, int instanceId = 0)
        {
            try
            {
                if (count <= 0) return true;

                var map = Envir.GetMapByNameAndInstance(mapFileName, instanceId);
                if (map == null) return false;

                var monInfo = Envir.GetMonsterInfo(monsterIndex);
                if (monInfo == null) return false;

                return MonGen(map, new Point(x, y), monInfo, count);
            }
            catch (Exception ex)
            {
                LogException(nameof(MonGen), ex);
                return false;
            }
        }

        public bool MonGen(Map map, Point location, string monsterName, int count = 1)
        {
            try
            {
                if (count <= 0) return true;
                if (map == null) return false;

                var monInfo = Envir.GetMonsterInfo(monsterName);
                if (monInfo == null) return false;

                return MonGen(map, location, monInfo, count);
            }
            catch (Exception ex)
            {
                LogException(nameof(MonGen), ex);
                return false;
            }
        }

        public int GetPlayerCountOnMap(string mapFileName, int instanceId = 0)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(mapFileName)) return 0;

                var map = Envir.GetMapByNameAndInstance(mapFileName, instanceId);
                if (map == null) return 0;

                return map.Players.Count;
            }
            catch (Exception ex)
            {
                LogException(nameof(GetPlayerCountOnMap), ex);
                return 0;
            }
        }

        public bool ClearMonsters(string mapFileName, int instanceId = 0)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(mapFileName)) return false;

                var map = Envir.GetMapByNameAndInstance(mapFileName, instanceId);
                if (map == null) return false;

                foreach (var cell in map.Cells)
                {
                    if (cell == null || cell.Objects == null) continue;

                    for (var i = 0; i < cell.Objects.Count; i++)
                    {
                        var obj = cell.Objects[i];
                        if (obj == null) continue;
                        if (obj.Race != ObjectType.Monster) continue;
                        if (obj.Dead) continue;

                        obj.Die();
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                LogException(nameof(ClearMonsters), ex);
                return false;
            }
        }

        /// <summary>
        /// legacy `MONCLEAR` 语义：清除指定地图上的怪物（可选过滤怪物名），并跳过玩家宠物/召唤物。
        /// 注意：instanceId 为 1 基（与 legacy 脚本一致；传 1 表示基础实例）。
        /// </summary>
        public bool MonClear(PlayerObject player, string mapFileName, int instanceId = 1, string monsterName = "")
        {
            try
            {
                if (string.IsNullOrWhiteSpace(mapFileName)) return false;

                var map = Envir.GetMapByNameAndInstance(mapFileName, instanceId);
                if (map == null) return false;

                var nameFilter = monsterName ?? string.Empty;

                foreach (var cell in map.Cells)
                {
                    if (cell == null || cell.Objects == null) continue;

                    for (var i = 0; i < cell.Objects.Count; i++)
                    {
                        var obj = cell.Objects[i];
                        if (obj == null) continue;
                        if (obj.Race != ObjectType.Monster) continue;
                        if (obj.Master != null && obj.Master.Race == ObjectType.Player) continue;
                        if (obj.Dead) continue;

                        if (!string.IsNullOrEmpty(nameFilter))
                        {
                            if (obj is not MonsterObject monster) continue;
                            if (string.Compare(nameFilter, monster.Info?.Name, StringComparison.OrdinalIgnoreCase) != 0)
                                continue;
                        }

                        obj.Die();
                    }
                }

                if (player != null && ScriptTrace.IsEnabled(player))
                {
                    var filterText = string.IsNullOrWhiteSpace(nameFilter) ? "*" : nameFilter;
                    ScriptTrace.Record(player, $"[C#] MONCLEAR {mapFileName}({instanceId}) {filterText}");
                }

                return true;
            }
            catch (Exception ex)
            {
                LogException(nameof(MonClear), ex);
                return false;
            }
        }

        public int GetPetCount(PlayerObject player)
        {
            try
            {
                if (player == null) return 0;
                return player.Pets.Count;
            }
            catch (Exception ex)
            {
                LogException(nameof(GetPetCount), ex);
                return 0;
            }
        }

        public bool ClearPets(PlayerObject player)
        {
            try
            {
                if (player == null) return false;

                for (var i = player.Pets.Count - 1; i >= 0; i--)
                {
                    player.Pets[i].DieNextTurn = true;
                }

                return true;
            }
            catch (Exception ex)
            {
                LogException(nameof(ClearPets), ex);
                return false;
            }
        }

        public bool GivePet(PlayerObject player, string monsterName, int petCount = 1, int petLevel = 0)
        {
            try
            {
                if (player == null) return false;
                if (string.IsNullOrWhiteSpace(monsterName)) return false;
                if (petCount <= 0) return true;

                var monInfo = Envir.GetMonsterInfo(monsterName);
                if (monInfo == null) return false;

                var count = Math.Min(5, petCount);

                var level = petLevel;
                if (level < 0) level = 0;
                if (level > 7) level = 7;

                for (var i = 0; i < count; i++)
                {
                    var monster = MonsterObject.GetMonster(monInfo);
                    if (monster == null) return false;

                    monster.PetLevel = (byte)level;
                    monster.Master = player;
                    monster.MaxPetLevel = 7;
                    monster.Direction = player.Direction;
                    monster.ActionTime = Envir.Time + 1000;
                    monster.Spawn(player.CurrentMap, player.CurrentLocation);
                    player.Pets.Add(monster);
                }

                return true;
            }
            catch (Exception ex)
            {
                LogException(nameof(GivePet), ex);
                return false;
            }
        }

        public bool GiveSkill(PlayerObject player, string spellNameOrId, int level = 0)
        {
            try
            {
                if (player == null) return false;
                if (string.IsNullOrWhiteSpace(spellNameOrId)) return false;

                if (!Enum.TryParse(spellNameOrId, true, out Spell skill) || skill == Spell.None)
                    return false;

                for (var i = 0; i < player.Info.Magics.Count; i++)
                {
                    if (player.Info.Magics[i].Spell == skill)
                        return true;
                }

                var spellLevel = level;
                if (spellLevel < 0) spellLevel = 0;
                if (spellLevel > 3) spellLevel = 3;

                var magic = new UserMagic(skill) { Level = (byte)spellLevel };
                if (magic.Info == null) return false;

                player.Info.Magics.Add(magic);
                player.SendMagicInfo(magic);

                if (ScriptTrace.IsEnabled(player))
                {
                    ScriptTrace.Record(player, $"[C#] GIVESKILL {skill} lv={magic.Level}");
                }

                return true;
            }
            catch (Exception ex)
            {
                LogException(nameof(GiveSkill), ex);
                return false;
            }
        }

        public bool RemoveSkill(PlayerObject player, string spellNameOrId)
        {
            try
            {
                if (player == null) return false;
                if (string.IsNullOrWhiteSpace(spellNameOrId)) return false;

                if (!Enum.TryParse(spellNameOrId, true, out Spell skill) || skill == Spell.None)
                    return false;

                var removed = 0;

                for (var i = player.Info.Magics.Count - 1; i >= 0; i--)
                {
                    if (player.Info.Magics[i].Spell != skill) continue;

                    player.Info.Magics.RemoveAt(i);
                    player.Enqueue(new S.RemoveMagic { PlaceId = i });
                    removed++;
                }

                if (ScriptTrace.IsEnabled(player))
                {
                    ScriptTrace.Record(player, $"[C#] REMOVESKILL {skill} (removed={removed})");
                }

                return true;
            }
            catch (Exception ex)
            {
                LogException(nameof(RemoveSkill), ex);
                return false;
            }
        }

        public bool SetPKPoint(PlayerObject player, int value)
        {
            try
            {
                if (player == null) return false;

                player.PKPoints = Math.Max(0, value);
                return true;
            }
            catch (Exception ex)
            {
                LogException(nameof(SetPKPoint), ex);
                return false;
            }
        }

        public bool IncreasePKPoint(PlayerObject player, int amount)
        {
            try
            {
                if (player == null) return false;
                if (amount <= 0) return true;

                player.PKPoints += amount;

                if (ScriptTrace.IsEnabled(player))
                {
                    ScriptTrace.Record(player, $"[C#] PKPOINT +{amount} -> {player.PKPoints}");
                }

                return true;
            }
            catch (Exception ex)
            {
                LogException(nameof(IncreasePKPoint), ex);
                return false;
            }
        }

        public bool ReducePKPoint(PlayerObject player, int amount)
        {
            try
            {
                if (player == null) return false;
                if (amount <= 0) return true;

                player.PKPoints -= amount;
                if (player.PKPoints < 0) player.PKPoints = 0;

                if (ScriptTrace.IsEnabled(player))
                {
                    ScriptTrace.Record(player, $"[C#] PKPOINT -{amount} -> {player.PKPoints}");
                }

                return true;
            }
            catch (Exception ex)
            {
                LogException(nameof(ReducePKPoint), ex);
                return false;
            }
        }

        public bool GiveExp(PlayerObject player, uint amount)
        {
            try
            {
                if (player == null) return false;
                if (amount == 0) return true;

                player.GainExp(amount);
                return true;
            }
            catch (Exception ex)
            {
                LogException(nameof(GiveExp), ex);
                return false;
            }
        }

        public bool GiveGuildExp(PlayerObject player, uint amount)
        {
            try
            {
                if (player == null) return false;
                if (amount == 0) return true;
                if (player.MyGuild == null) return false;

                player.MyGuild.GainExp(amount);
                return true;
            }
            catch (Exception ex)
            {
                LogException(nameof(GiveGuildExp), ex);
                return false;
            }
        }

        public bool ReviveHero(PlayerObject player)
        {
            try
            {
                if (player == null) return false;
                player.ReviveHero();
                return true;
            }
            catch (Exception ex)
            {
                LogException(nameof(ReviveHero), ex);
                return false;
            }
        }

        public bool CreateGuild(PlayerObject player)
        {
            try
            {
                if (player == null) return false;

                if (player.Info.Level < Settings.Guild_RequiredLevel)
                {
                    player.ReceiveChat(string.Format("创建行会需要 {0} 级", Settings.Guild_RequiredLevel), ChatType.System);
                    return true;
                }

                if (player.MyGuild == null)
                {
                    player.CanCreateGuild = true;
                    player.Enqueue(new S.GuildNameRequest());
                    return true;
                }

                player.ReceiveChat("你已经是公会成员", ChatType.System);
                return true;
            }
            catch (Exception ex)
            {
                LogException(nameof(CreateGuild), ex);
                return false;
            }
        }

        public bool RequestGuildWar(PlayerObject player)
        {
            try
            {
                if (player == null) return false;

                if (player.MyGuild != null)
                {
                    if (player.MyGuildRank != player.MyGuild.Ranks[0])
                    {
                        player.ReceiveChat("必须由会长发起行会战", ChatType.System);
                        return true;
                    }

                    player.Enqueue(new S.GuildRequestWar());
                    return true;
                }

                player.ReceiveChat(GameLanguage.NotInGuild, ChatType.System);
                return true;
            }
            catch (Exception ex)
            {
                LogException(nameof(RequestGuildWar), ex);
                return false;
            }
        }

        public bool ScheduleConquest(PlayerObject player, int conquestIndex)
        {
            try
            {
                if (player == null) return false;

                var conquest = Envir.Conquests.FirstOrDefault(z => z.Info.Index == conquestIndex);
                if (conquest == null) return false;

                if (player.MyGuild != null && player.MyGuild.Guildindex != conquest.GuildInfo.Owner && !conquest.WarIsOn)
                {
                    conquest.GuildInfo.AttackerID = player.MyGuild.Guildindex;
                    ScriptTrace.Record(player, $"[C#] SCHEDULECONQUEST {conquestIndex}");
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                LogException(nameof(ScheduleConquest), ex);
                return false;
            }
        }

        public bool CreateHero(PlayerObject player)
        {
            try
            {
                if (player == null) return false;

                if (player.Info.Level < Settings.Hero_RequiredLevel)
                {
                    player.ReceiveChat(string.Format("召唤英雄需要角色达到 {0} 级", Settings.Hero_RequiredLevel), ChatType.System);
                    return true;
                }

                player.CanCreateHero = true;
                player.Enqueue(new S.HeroCreateRequest
                {
                    CanCreateClass = Settings.Hero_CanCreateClass
                });
                return true;
            }
            catch (Exception ex)
            {
                LogException(nameof(CreateHero), ex);
                return false;
            }
        }

        public bool SealHero(PlayerObject player)
        {
            try
            {
                if (player == null) return false;
                player.SealHero();
                return true;
            }
            catch (Exception ex)
            {
                LogException(nameof(SealHero), ex);
                return false;
            }
        }

        public bool DeleteHero(PlayerObject player)
        {
            try
            {
                if (player == null) return false;
                player.DeleteHero();
                return true;
            }
            catch (Exception ex)
            {
                LogException(nameof(DeleteHero), ex);
                return false;
            }
        }

        public bool ManageHeroes(PlayerObject player)
        {
            try
            {
                if (player == null) return false;
                player.ManageHeroes();
                return true;
            }
            catch (Exception ex)
            {
                LogException(nameof(ManageHeroes), ex);
                return false;
            }
        }

        public Buff AddBuff(MapObject target, BuffType type, MapObject owner, int seconds, Stats stats = null, bool refreshStats = true, bool updateOnly = false, params int[] values)
        {
            try
            {
                if (target == null) return null;

                var duration = seconds <= 0 ? 0 : seconds * Settings.Second;

                if (target is PlayerObject tracedPlayer && ScriptTrace.IsEnabled(tracedPlayer))
                {
                    ScriptTrace.Record(tracedPlayer, $"[C#] ADDBUFF {type} {seconds}s");
                }

                return target.AddBuff(type, owner, duration, stats, refreshStats, updateOnly, values);
            }
            catch (Exception ex)
            {
                LogException(nameof(AddBuff), ex);
                return null;
            }
        }

        public Buff AddBuffFromSetBuffs(MapObject target, BuffType type, MapObject owner, int seconds, bool refreshStats = true, bool updateOnly = false, params int[] values)
        {
            try
            {
                if (target == null) return null;

                if (target is PlayerObject tracedPlayer && ScriptTrace.IsEnabled(tracedPlayer))
                {
                    ScriptTrace.Record(tracedPlayer, $"[C#] ADDBUFF(SetBuffs) {type} {seconds}s");
                }

                var stats = LoadBuffStatsFromSetBuffs(type.ToString());
                return AddBuff(target, type, owner, seconds, stats, refreshStats, updateOnly, values);
            }
            catch (Exception ex)
            {
                LogException(nameof(AddBuffFromSetBuffs), ex);
                return null;
            }
        }

        /// <summary>
        /// legacy `GIVEBUFF` 语义（简化版）：从 SetBuffs 基础配置构建 Stats，并支持额外的 Stat=Value 形式覆盖。
        /// </summary>
        public bool GiveBuff(PlayerObject player, string buffTypeName, int seconds, bool refreshStats = true, params string[] extraStatPairs)
        {
            try
            {
                if (player == null) return false;
                if (string.IsNullOrWhiteSpace(buffTypeName)) return false;

                if (!Enum.TryParse(buffTypeName, true, out BuffType type) || !Enum.IsDefined(typeof(BuffType), type))
                    return false;

                var extras = extraStatPairs ?? Array.Empty<string>();

                if (!Envir.TryBuildBuffStatsFromSetBuffs(type.ToString(), extras, out var buffStats))
                    return false;

                AddBuff(player, type, player, seconds, buffStats, refreshStats);
                return true;
            }
            catch (Exception ex)
            {
                LogException(nameof(GiveBuff), ex);
                return false;
            }
        }

        public bool RemoveBuff(PlayerObject player, string buffTypeName)
        {
            try
            {
                if (player == null) return false;
                if (string.IsNullOrWhiteSpace(buffTypeName)) return false;

                if (!Enum.TryParse(buffTypeName, true, out BuffType type) || !Enum.IsDefined(typeof(BuffType), type))
                    return false;

                return RemoveBuff(player, type);
            }
            catch (Exception ex)
            {
                LogException(nameof(RemoveBuff), ex);
                return false;
            }
        }

        public Stats LoadBuffStatsFromSetBuffs(string buffKey)
        {
            return Envir.LoadBuffStatsFromSetBuffs(buffKey);
        }

        public bool HasBuff(MapObject target, BuffType type)
        {
            try
            {
                if (target == null) return false;
                return target.HasBuff(type);
            }
            catch (Exception ex)
            {
                LogException(nameof(HasBuff), ex);
                return false;
            }
        }

        public bool CheckBuff(PlayerObject player, string buffTypeName)
        {
            try
            {
                if (player == null) return false;
                if (string.IsNullOrWhiteSpace(buffTypeName)) return false;

                if (!Enum.TryParse(buffTypeName, true, out BuffType buffType))
                    return false;

                return HasBuff(player, buffType);
            }
            catch (Exception ex)
            {
                LogException(nameof(CheckBuff), ex);
                return false;
            }
        }

        public bool ToggleGender(PlayerObject player)
        {
            try
            {
                if (player == null) return false;

                player.Info.Gender = player.Info.Gender == MirGender.男性 ? MirGender.女性 : MirGender.男性;

                if (ScriptTrace.IsEnabled(player))
                {
                    ScriptTrace.Record(player, $"[C#] GENDER -> {player.Info.Gender}");
                }

                return true;
            }
            catch (Exception ex)
            {
                LogException(nameof(ToggleGender), ex);
                return false;
            }
        }

        public bool ChangeLevel(PlayerObject player, int level)
        {
            try
            {
                if (player == null) return false;
                if (level < 0 || level > ushort.MaxValue) return false;

                player.Level = (ushort)level;
                player.Experience = 0;
                player.LevelUp();

                if (ScriptTrace.IsEnabled(player))
                {
                    ScriptTrace.Record(player, $"[C#] CHANGELEVEL -> {player.Level}");
                }

                return true;
            }
            catch (Exception ex)
            {
                LogException(nameof(ChangeLevel), ex);
                return false;
            }
        }

        public bool SetHair(PlayerObject player, byte hair)
        {
            try
            {
                if (player == null) return false;
                if (hair > 9) return false;

                player.Info.Hair = hair;

                if (ScriptTrace.IsEnabled(player))
                {
                    ScriptTrace.Record(player, $"[C#] HAIR -> {hair}");
                }

                return true;
            }
            catch (Exception ex)
            {
                LogException(nameof(SetHair), ex);
                return false;
            }
        }

        public bool RemoveBuff(MapObject target, BuffType type)
        {
            try
            {
                if (target == null) return false;

                if (target is PlayerObject tracedPlayer && ScriptTrace.IsEnabled(tracedPlayer))
                {
                    ScriptTrace.Record(tracedPlayer, $"[C#] REMOVEBUFF {type}");
                }

                target.RemoveBuff(type);
                return true;
            }
            catch (Exception ex)
            {
                LogException(nameof(RemoveBuff), ex);
                return false;
            }
        }

        public bool IsQuestCompleted(PlayerObject player, int questIndex)
        {
            try
            {
                if (player == null) return false;
                if (questIndex <= 0) return false;

                return player.CompletedQuests.Contains(questIndex);
            }
            catch (Exception ex)
            {
                LogException(nameof(IsQuestCompleted), ex);
                return false;
            }
        }

        public bool AcceptQuest(PlayerObject player, int questIndex)
        {
            try
            {
                if (player == null) return false;
                player.AcceptQuest(questIndex);
                return true;
            }
            catch (Exception ex)
            {
                LogException(nameof(AcceptQuest), ex);
                return false;
            }
        }

        public bool FinishQuest(PlayerObject player, int questIndex, int selectedItemIndex = -1)
        {
            try
            {
                if (player == null) return false;
                player.FinishQuest(questIndex, selectedItemIndex);
                return true;
            }
            catch (Exception ex)
            {
                LogException(nameof(FinishQuest), ex);
                return false;
            }
        }

        public bool AbandonQuest(PlayerObject player, int questIndex)
        {
            try
            {
                if (player == null) return false;
                player.AbandonQuest(questIndex);
                return true;
            }
            catch (Exception ex)
            {
                LogException(nameof(AbandonQuest), ex);
                return false;
            }
        }

        public bool GetFlag(PlayerObject player, int flagIndex, out bool value)
        {
            try
            {
                value = false;

                if (player == null) return false;
                if (flagIndex < 0 || flagIndex >= Globals.FlagIndexCount) return false;

                value = player.Info.Flags[flagIndex];
                return true;
            }
            catch (Exception ex)
            {
                value = false;
                LogException(nameof(GetFlag), ex);
                return false;
            }
        }

        public bool SetFlag(PlayerObject player, int flagIndex, bool value)
        {
            try
            {
                if (player == null) return false;
                if (flagIndex < 0 || flagIndex >= Globals.FlagIndexCount) return false;

                player.Info.Flags[flagIndex] = value;

                for (int f = player.CurrentMap.NPCs.Count - 1; f >= 0; f--)
                {
                    if (Functions.InRange(player.CurrentMap.NPCs[f].CurrentLocation, player.CurrentLocation, Globals.DataRange))
                        player.CurrentMap.NPCs[f].CheckVisible(player);
                }

                if (value) player.CheckNeedQuestFlag(flagIndex);

                return true;
            }
            catch (Exception ex)
            {
                LogException(nameof(SetFlag), ex);
                return false;
            }
        }

        public bool SetVar(MapObject owner, string key, string value)
        {
            try
            {
                if (owner == null) return false;
                if (string.IsNullOrWhiteSpace(key)) return false;

                var normalizedKey = NormalizeVarKey(key);
                if (normalizedKey == null) return false;

                for (var i = 0; i < owner.NPCVar.Count; i++)
                {
                    if (!string.Equals(owner.NPCVar[i].Key, normalizedKey, StringComparison.CurrentCultureIgnoreCase)) continue;
                    owner.NPCVar[i] = new KeyValuePair<string, string>(owner.NPCVar[i].Key, value ?? string.Empty);
                    return true;
                }

                owner.NPCVar.Add(new KeyValuePair<string, string>(normalizedKey, value ?? string.Empty));
                return true;
            }
            catch (Exception ex)
            {
                LogException(nameof(SetVar), ex);
                return false;
            }
        }

        public string GetVar(MapObject owner, string key, string defaultValue = "")
        {
            try
            {
                if (owner == null) return defaultValue ?? string.Empty;
                if (string.IsNullOrWhiteSpace(key)) return defaultValue ?? string.Empty;

                var normalizedKey = NormalizeVarKey(key);
                if (normalizedKey == null) return defaultValue ?? string.Empty;

                for (var i = 0; i < owner.NPCVar.Count; i++)
                {
                    if (string.Equals(owner.NPCVar[i].Key, normalizedKey, StringComparison.CurrentCultureIgnoreCase))
                        return owner.NPCVar[i].Value;
                }

                return defaultValue ?? string.Empty;
            }
            catch (Exception ex)
            {
                LogException(nameof(GetVar), ex);
                return defaultValue ?? string.Empty;
            }
        }

        public string ResolveLegacyToken(PlayerObject player, string token)
        {
            return ResolveLegacyToken(player, call: null, token);
        }

        public string ResolveLegacyToken(PlayerObject player, NpcPageCall call, string token)
        {
            try
            {
                if (player == null) return token ?? string.Empty;
                if (string.IsNullOrEmpty(token)) return token ?? string.Empty;

                var value = token;

                // 1) %A1 变量（仅支持“完整 token”形式；保持与 legacy FindVariable 的使用场景一致）
                if (value.StartsWith("%", StringComparison.Ordinal))
                {
                    value = GetVar(player, value, value);
                }

                // 2) %INPUTSTR（legacy 在 Act 阶段对 param[j] 做 Replace；此处对齐）
                var input = call?.Input;

                if (string.IsNullOrEmpty(input) &&
                    player.NPCData != null &&
                    player.NPCData.TryGetValue("NPCInputStr", out object npcInputStrObj) &&
                    npcInputStrObj != null)
                {
                    input = npcInputStrObj.ToString();
                }

                if (!string.IsNullOrEmpty(input))
                {
                    value = value.Replace("%INPUTSTR", input);
                }

                // 3) <$...>（沿用 legacy ReplaceValue 机制，避免重复实现）
                if (value.Contains("<$", StringComparison.Ordinal))
                {
                    var parserPage = new NPCPage("[@_CSharpResolve]");
                    var parserSegment = new NPCSegment(parserPage, new List<string>(), new List<string>(), new List<string>(), new List<string>(), new List<string>());
                    value = parserSegment.ReplaceValue(player, value);
                }

                return value;
            }
            catch (Exception ex)
            {
                LogException(nameof(ResolveLegacyToken), ex);
                return token ?? string.Empty;
            }
        }

        public bool TryResolveLegacyInt(PlayerObject player, string token, out int value)
        {
            return TryResolveLegacyInt(player, call: null, token, out value);
        }

        public bool TryResolveLegacyInt(PlayerObject player, NpcPageCall call, string token, out int value)
        {
            value = 0;

            if (player == null) return false;
            if (string.IsNullOrWhiteSpace(token)) return false;

            var resolved = ResolveLegacyToken(player, call, token);
            if (string.IsNullOrWhiteSpace(resolved)) return false;

            return int.TryParse(resolved.Trim(), out value);
        }

        public bool TryResolveLegacyLong(PlayerObject player, string token, out long value)
        {
            return TryResolveLegacyLong(player, call: null, token, out value);
        }

        public bool TryResolveLegacyLong(PlayerObject player, NpcPageCall call, string token, out long value)
        {
            value = 0;

            if (player == null) return false;
            if (string.IsNullOrWhiteSpace(token)) return false;

            var resolved = ResolveLegacyToken(player, call, token);
            if (string.IsNullOrWhiteSpace(resolved)) return false;

            return long.TryParse(resolved.Trim(), out value);
        }

        public bool CalcVar(PlayerObject player, NpcPageCall call, string key, string op, string rightToken)
        {
            try
            {
                if (player == null) return false;
                if (string.IsNullOrWhiteSpace(key)) return false;
                if (string.IsNullOrWhiteSpace(op)) return false;

                var normalizedKey = NormalizeVarKey(key);
                if (normalizedKey == null) return false;

                var leftRaw = GetVar(player, normalizedKey, "%" + normalizedKey);
                var rightRaw = ResolveLegacyToken(player, call, rightToken ?? string.Empty);

                var leftIsInt = int.TryParse(leftRaw, out var leftInt);
                var rightIsInt = int.TryParse(rightRaw, out var rightInt);

                if (leftIsInt && rightIsInt)
                {
                    var opTrim = op.Trim();
                    var ok = true;
                    var result = 0;

                    switch (opTrim)
                    {
                        case "+":
                            result = leftInt + rightInt;
                            break;
                        case "-":
                            result = leftInt - rightInt;
                            break;
                        case "*":
                            result = leftInt * rightInt;
                            break;
                        case "/":
                            if (rightInt == 0) ok = false;
                            else result = leftInt / rightInt;
                            break;
                        default:
                            ok = false;
                            break;
                    }

                    if (!ok) return false;

                    if (!SetVar(player, normalizedKey, result.ToString()))
                        return false;

                    if (ScriptTrace.IsEnabled(player))
                    {
                        ScriptTrace.Record(player, $"[C#] CALC {normalizedKey} {opTrim} {rightRaw} (left={leftRaw}, result={result})");
                    }

                    return true;
                }

                var concatenated = (leftRaw ?? string.Empty) + (rightRaw ?? string.Empty);

                if (!SetVar(player, normalizedKey, concatenated))
                    return false;

                if (ScriptTrace.IsEnabled(player))
                {
                    ScriptTrace.Record(player, $"[C#] CALC {normalizedKey} {op.Trim()} {rightRaw} (left={leftRaw}, concat=true)");
                }

                return true;
            }
            catch (Exception ex)
            {
                LogException(nameof(CalcVar), ex);
                return false;
            }
        }

        public bool CheckCalc(PlayerObject player, NpcPageCall call, string leftToken, string op, string rightToken)
        {
            try
            {
                if (player == null) return false;
                if (string.IsNullOrWhiteSpace(op)) return false;

                var leftRaw = ResolveLegacyToken(player, call, leftToken ?? string.Empty);
                var rightRaw = ResolveLegacyToken(player, call, rightToken ?? string.Empty);

                bool ok;

                if (int.TryParse(leftRaw, out var leftInt) && int.TryParse(rightRaw, out var rightInt))
                {
                    ok = Compare(op, leftInt, rightInt);
                }
                else
                {
                    ok = Compare(op, leftRaw ?? string.Empty, rightRaw ?? string.Empty);
                }

                if (ScriptTrace.IsEnabled(player))
                {
                    ScriptTrace.Record(player, $"[C#] CHECKCALC {leftRaw} {op.Trim()} {rightRaw} -> {ok}");
                }

                return ok;
            }
            catch (Exception ex)
            {
                LogException(nameof(CheckCalc), ex);
                return false;
            }
        }

        public Server.MirEnvir.Timer GetTimer(PlayerObject player, string key)
        {
            try
            {
                if (player == null) return null;
                if (string.IsNullOrWhiteSpace(key)) return null;
                return player.GetTimer(key);
            }
            catch (Exception ex)
            {
                LogException(nameof(GetTimer), ex);
                return null;
            }
        }

        public Server.MirEnvir.Timer GetTimer(PlayerObject player, string key, bool includeGlobal)
        {
            try
            {
                if (player == null) return null;
                if (string.IsNullOrWhiteSpace(key)) return null;

                if (includeGlobal)
                {
                    var globalTimerKey = "_-" + key;
                    if (Envir.Timers.TryGetValue(globalTimerKey, out var globalTimer))
                        return globalTimer;
                }

                return player.GetTimer(key);
            }
            catch (Exception ex)
            {
                LogException(nameof(GetTimer), ex);
                return null;
            }
        }

        public int GetTimerRemainingSeconds(PlayerObject player, string key)
        {
            try
            {
                var t = GetTimer(player, key);
                if (t == null) return 0;

                var remainingMs = t.RelativeTime - Envir.Time;
                if (remainingMs <= 0) return 0;

                return (int)Math.Ceiling(remainingMs / (double)Settings.Second);
            }
            catch (Exception ex)
            {
                LogException(nameof(GetTimerRemainingSeconds), ex);
                return 0;
            }
        }

        public int GetTimerRemainingSeconds(PlayerObject player, string key, bool includeGlobal)
        {
            try
            {
                var t = GetTimer(player, key, includeGlobal);
                if (t == null) return 0;

                var remainingMs = t.RelativeTime - Envir.Time;
                if (remainingMs <= 0) return 0;

                return (int)Math.Ceiling(remainingMs / (double)Settings.Second);
            }
            catch (Exception ex)
            {
                LogException(nameof(GetTimerRemainingSeconds), ex);
                return 0;
            }
        }

        public bool CheckTimer(PlayerObject player, string key, string op, long seconds, bool includeGlobal = true)
        {
            try
            {
                if (player == null) return false;
                if (string.IsNullOrWhiteSpace(key)) return false;

                var remainingSeconds = (long)GetTimerRemainingSeconds(player, key, includeGlobal);
                var ok = Compare(op, remainingSeconds, seconds);

                if (ScriptTrace.IsEnabled(player))
                {
                    ScriptTrace.Record(player,
                        $"[C#] CHECKTIMER {key} {op} {seconds} (remaining={remainingSeconds}, global={includeGlobal}) -> {ok}");
                }

                return ok;
            }
            catch (Exception ex)
            {
                LogException(nameof(CheckTimer), ex);
                return false;
            }
        }

        public bool SetTimer(PlayerObject player, string key, string secondsToken, byte type = 0)
        {
            return SetTimer(player, key, secondsToken, type, global: false);
        }

        public bool SetTimer(PlayerObject player, string key, string secondsToken, byte type, bool global)
        {
            try
            {
                if (!TryResolveLegacyInt(player, secondsToken, out var seconds))
                    return false;

                return SetTimer(player, key, seconds, type, global);
            }
            catch (Exception ex)
            {
                LogException(nameof(SetTimer), ex);
                return false;
            }
        }

        public bool SetTimer(PlayerObject player, string key, int seconds, byte type = 0)
        {
            return SetTimer(player, key, seconds, type, global: false);
        }

        public bool SetTimer(PlayerObject player, string key, int seconds, byte type, bool global)
        {
            try
            {
                if (player == null) return false;
                if (string.IsNullOrWhiteSpace(key)) return false;

                if (seconds < 0) seconds = 0;

                if (global)
                {
                    var globalTimerKey = "_-" + key;
                    Envir.Timers[globalTimerKey] = new Server.MirEnvir.Timer(globalTimerKey, seconds, type);
                }
                else
                {
                    player.SetTimer(key, seconds, type);
                }

                if (ScriptTrace.IsEnabled(player))
                {
                    ScriptTrace.Record(player, $"[C#] SETTIMER {key} {seconds}s type={type} global={global}");
                }

                return true;
            }
            catch (Exception ex)
            {
                LogException(nameof(SetTimer), ex);
                return false;
            }
        }

        public bool ExpireTimer(PlayerObject player, string key)
        {
            return ExpireTimer(player, key, expireGlobal: true);
        }

        public bool ExpireTimer(PlayerObject player, string key, bool expireGlobal)
        {
            try
            {
                if (player == null) return false;
                if (string.IsNullOrWhiteSpace(key)) return false;

                if (expireGlobal)
                {
                    var globalTimerKey = "_-" + key;
                    if (Envir.Timers.ContainsKey(globalTimerKey))
                        Envir.Timers.Remove(globalTimerKey);
                }

                player.ExpireTimer(key);

                if (ScriptTrace.IsEnabled(player))
                {
                    ScriptTrace.Record(player, $"[C#] EXPIRETIMER {key} expireGlobal={expireGlobal}");
                }

                return true;
            }
            catch (Exception ex)
            {
                LogException(nameof(ExpireTimer), ex);
                return false;
            }
        }

        private bool CheckBagSpace(PlayerObject player, ItemInfo info, int count)
        {
            var items = BuildItemStacks(info, count);
            return player.CanGainItems(items);
        }

        private bool GiveItem(PlayerObject player, ItemInfo info, int count)
        {
            var requested = count;
            var items = BuildItemStacks(info, count);

            var any = false;
            var given = 0;
            for (var i = 0; i < items.Length; i++)
            {
                var item = items[i];
                if (item == null) continue;

                if (!player.CanGainItem(item)) continue;

                player.GainItem(item);
                any = true;
                given += item.Count;
            }

            if (ScriptTrace.IsEnabled(player))
            {
                ScriptTrace.Record(player, $"[C#] GIVEITEM {info?.Name} x{requested} -> {given}");
            }

            return any;
        }

        private bool TakeItem(PlayerObject player, ItemInfo info, int count, ushort? minDura)
        {
            if (count <= 0) return true;

            var requested = count;
            var available = GetItemCount(player, info, minDura);
            if (available < count) return false;

            var remaining = count;

            for (int j = 0; j < player.Info.Inventory.Length; j++)
            {
                UserItem item = player.Info.Inventory[j];
                if (item == null) continue;
                if (item.Info != info) continue;

                if (minDura.HasValue)
                {
                    if (item.CurrentDura < (minDura.Value * 1000)) continue;
                }

                if (remaining > item.Count)
                {
                    player.Enqueue(new S.DeleteItem { UniqueID = item.UniqueID, Count = item.Count });
                    player.Info.Inventory[j] = null;

                    remaining -= item.Count;
                    continue;
                }

                player.Enqueue(new S.DeleteItem { UniqueID = item.UniqueID, Count = (ushort)remaining });
                if (remaining == item.Count)
                    player.Info.Inventory[j] = null;
                else
                    item.Count -= (ushort)remaining;

                remaining = 0;
                break;
            }

            player.RefreshStats();

            if (ScriptTrace.IsEnabled(player))
            {
                var taken = requested - remaining;
                ScriptTrace.Record(player, $"[C#] TAKEITEM {info?.Name} x{requested} -> {taken} (ok={remaining <= 0})");
            }

            return remaining <= 0;
        }

        private static int GetItemCount(PlayerObject player, ItemInfo info, ushort? minDura)
        {
            if (player == null) return 0;
            if (info == null) return 0;

            var total = 0;
            for (int j = 0; j < player.Info.Inventory.Length; j++)
            {
                var item = player.Info.Inventory[j];
                if (item == null) continue;
                if (item.Info != info) continue;

                if (minDura.HasValue)
                {
                    if (item.CurrentDura < (minDura.Value * 1000)) continue;
                }

                total += item.Count;
            }

            return total;
        }

        private UserItem[] BuildItemStacks(ItemInfo info, int count)
        {
            if (count <= 0) return Array.Empty<UserItem>();

            var list = new List<UserItem>();
            var remaining = count;

            while (remaining > 0)
            {
                var item = Envir.CreateFreshItem(info);
                if (item == null) break;

                var stack = Math.Min(remaining, item.Info.StackSize);
                item.Count = (ushort)stack;
                remaining -= stack;

                list.Add(item);
            }

            return list.ToArray();
        }

        private bool MonGen(Map map, Point location, MonsterInfo monInfo, int count)
        {
            if (map == null) return false;
            if (monInfo == null) return false;
            if (count <= 0) return true;

            for (int i = 0; i < count; i++)
            {
                MonsterObject monster = MonsterObject.GetMonster(monInfo);
                if (monster == null) return false;

                monster.Direction = 0;
                monster.ActionTime = Envir.Time + 1000;
                monster.Spawn(map, location);
            }

            return true;
        }

        private static string TruncForTrace(string value, int maxLength = 80)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;

            var s = value.Replace("\r", " ").Replace("\n", " ");
            if (s.Length <= maxLength) return s;
            return s.Substring(0, maxLength) + "...";
        }

        private static string NormalizeVarKey(string key)
        {
            var trimmed = key.Trim();
            if (trimmed.StartsWith("%", StringComparison.Ordinal))
                trimmed = trimmed.Substring(1);

            if (!VarKeyRegex.IsMatch(trimmed)) return null;
            return trimmed;
        }

        private static void LogException(string apiName, Exception exception, [CallerMemberName] string caller = null)
        {
            try
            {
                var name = string.IsNullOrWhiteSpace(apiName) ? (caller ?? "Unknown") : apiName;
                MessageQueue.Instance.Enqueue($"[Scripts][Api] {name} 异常：{exception}");
            }
            catch
            {
                // ignored
            }
        }
    }
}
