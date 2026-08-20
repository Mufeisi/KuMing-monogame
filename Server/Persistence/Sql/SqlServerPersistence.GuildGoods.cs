using System.Drawing;
using Server.MirDatabase;
using Server.MirEnvir;
using Server.MirObjects;

namespace Server.Persistence.Sql
{
    public sealed partial class SqlServerPersistence
    {
        private sealed class GuildRow
        {
            public long GuildId { get; set; }
            public string GuildName { get; set; }
            public long LeaderCharacterId { get; set; }
            public long Gold { get; set; }
            public int Level { get; set; }
            public int SparePoints { get; set; }
            public long Experience { get; set; }
            public int Votes { get; set; }
            public long LastVoteAttemptUtcMs { get; set; }
            public int Voting { get; set; }
            public int FlagImage { get; set; }
            public int FlagColourArgb { get; set; }
        }

        private sealed class GuildRankRow
        {
            public long GuildId { get; set; }
            public int RankIndex { get; set; }
            public string RankName { get; set; }
            public long Permissions { get; set; }
        }

        private sealed class GuildMemberRow
        {
            public long GuildId { get; set; }
            public long CharacterId { get; set; }
            public int RankIndex { get; set; }
            public long LastLoginUtcMs { get; set; }
            public int HasVoted { get; set; }
            public int Online { get; set; }
        }

        private sealed class GuildNoticeRow
        {
            public long GuildId { get; set; }
            public int NoticeIndex { get; set; }
            public string NoticeText { get; set; }
        }

        private sealed class GuildBuffRow
        {
            public long GuildId { get; set; }
            public int BuffType { get; set; }
            public int Active { get; set; }
            public int ActiveTimeRemaining { get; set; }
        }

        private sealed class GuildStorageSlotRow
        {
            public long GuildId { get; set; }
            public int SlotIndex { get; set; }
            public long ItemId { get; set; }
            public long UserCharacterId { get; set; }
        }

        private sealed class NpcBuybackRow
        {
            public long BuybackId { get; set; }
            public long NpcId { get; set; }
            public long CharacterId { get; set; }
            public long ItemId { get; set; }
            public long Price { get; set; }
            public long ExpiresUtcMs { get; set; }
        }

        private sealed class NpcUsedGoodRow
        {
            public long UsedGoodId { get; set; }
            public long NpcId { get; set; }
            public long ItemId { get; set; }
            public long Price { get; set; }
            public long AvailableUtcMs { get; set; }
        }

        private sealed class GuildRuntimeSnapshot
        {
            public IReadOnlyList<GuildRow> Guilds { get; init; } = Array.Empty<GuildRow>();
            public IReadOnlyList<GuildRankRow> Ranks { get; init; } = Array.Empty<GuildRankRow>();
            public IReadOnlyList<GuildMemberRow> Members { get; init; } = Array.Empty<GuildMemberRow>();
            public IReadOnlyList<GuildNoticeRow> Notices { get; init; } = Array.Empty<GuildNoticeRow>();
            public IReadOnlyList<GuildBuffRow> Buffs { get; init; } = Array.Empty<GuildBuffRow>();
            public IReadOnlyList<GuildStorageSlotRow> StorageSlots { get; init; } = Array.Empty<GuildStorageSlotRow>();
        }

        private sealed class NpcGoodsSnapshot
        {
            public IReadOnlyList<NpcBuybackRow> Buybacks { get; init; } = Array.Empty<NpcBuybackRow>();
            public IReadOnlyList<NpcUsedGoodRow> UsedGoods { get; init; } = Array.Empty<NpcUsedGoodRow>();
        }

        private Dictionary<long, UserItem> _startupItemsById;

        private static GuildRuntimeSnapshot CaptureGuildRuntime(Envir envir)
        {
            var guilds = new List<GuildRow>();
            var ranks = new List<GuildRankRow>();
            var members = new List<GuildMemberRow>();
            var notices = new List<GuildNoticeRow>();
            var buffs = new List<GuildBuffRow>();
            var storage = new List<GuildStorageSlotRow>();

            foreach (var guild in envir.GuildList)
            {
                if (guild == null || guild.GuildIndex <= 0) continue;
                var leaderId = guild.Ranks.OrderBy(rank => rank.Index).SelectMany(rank => rank.Members).Select(member => (long)member.Id).FirstOrDefault();
                guilds.Add(new GuildRow
                {
                    GuildId = guild.GuildIndex,
                    GuildName = guild.Name ?? string.Empty,
                    LeaderCharacterId = leaderId,
                    Gold = guild.Gold,
                    Level = guild.Level,
                    SparePoints = guild.SparePoints,
                    Experience = guild.Experience,
                    Votes = guild.Votes,
                    LastVoteAttemptUtcMs = ToUtcMs(guild.LastVoteAttempt),
                    Voting = guild.Voting ? 1 : 0,
                    FlagImage = guild.FlagImage,
                    FlagColourArgb = guild.FlagColour.ToArgb(),
                });

                foreach (var rank in guild.Ranks)
                {
                    ranks.Add(new GuildRankRow { GuildId = guild.GuildIndex, RankIndex = rank.Index, RankName = rank.Name ?? string.Empty, Permissions = (long)rank.Options });
                    foreach (var member in rank.Members)
                        members.Add(new GuildMemberRow { GuildId = guild.GuildIndex, CharacterId = member.Id, RankIndex = rank.Index, LastLoginUtcMs = ToUtcMs(member.LastLogin), HasVoted = member.hasvoted ? 1 : 0, Online = member.Online ? 1 : 0 });
                }

                for (var index = 0; index < guild.Notice.Count; index++)
                    notices.Add(new GuildNoticeRow { GuildId = guild.GuildIndex, NoticeIndex = index, NoticeText = guild.Notice[index] ?? string.Empty });
                foreach (var buff in guild.BuffList)
                    if (buff != null) buffs.Add(new GuildBuffRow { GuildId = guild.GuildIndex, BuffType = buff.Id, Active = buff.Active ? 1 : 0, ActiveTimeRemaining = buff.ActiveTimeRemaining });
                for (var index = 0; index < guild.StoredItems.Length; index++)
                {
                    var entry = guild.StoredItems[index];
                    if (entry?.Item == null || entry.Item.UniqueID == 0) continue;
                    storage.Add(new GuildStorageSlotRow { GuildId = guild.GuildIndex, SlotIndex = index, ItemId = ToDbInt64(entry.Item.UniqueID, "guild_item_id"), UserCharacterId = entry.UserId });
                }
            }

            return new GuildRuntimeSnapshot { Guilds = guilds, Ranks = ranks, Members = members, Notices = notices, Buffs = buffs, StorageSlots = storage };
        }

        private static NpcGoodsSnapshot CaptureNpcGoods(Envir envir)
        {
            var buybacks = new List<NpcBuybackRow>();
            var usedGoods = new List<NpcUsedGoodRow>();
            var charactersByName = envir.CharacterList.Where(character => character != null)
                .GroupBy(character => character.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            foreach (var npc in envir.NPCs)
            {
                if (npc?.Info == null) continue;
                foreach (var pair in npc.BuyBack)
                {
                    if (!charactersByName.TryGetValue(pair.Key ?? string.Empty, out var character))
                        throw new InvalidOperationException($"NPC {npc.Info.Index} 的私人回购引用未知角色：{pair.Key}");
                    foreach (var item in pair.Value)
                    {
                        if (item == null || item.UniqueID == 0) continue;
                        var itemId = ToDbInt64(item.UniqueID, "npc_buyback_item_id");
                        buybacks.Add(new NpcBuybackRow { BuybackId = itemId, NpcId = npc.Info.Index, CharacterId = character.Index, ItemId = itemId, Price = item.Info?.Price ?? 0, ExpiresUtcMs = ToUtcMs(item.BuybackExpiryDate.AddMinutes(Settings.GoodsBuyBackTime)) });
                    }
                }

                foreach (var item in npc.UsedGoods)
                {
                    if (item == null || item.UniqueID == 0) continue;
                    var itemId = ToDbInt64(item.UniqueID, "npc_used_good_item_id");
                    usedGoods.Add(new NpcUsedGoodRow { UsedGoodId = itemId, NpcId = npc.Info.Index, ItemId = itemId, Price = item.Info?.Price ?? 0, AvailableUtcMs = nowMs });
                }
            }

            return new NpcGoodsSnapshot { Buybacks = buybacks, UsedGoods = usedGoods };
        }

        private static void UpsertGuildRuntime(SqlSession session, GuildRuntimeSnapshot snapshot, long saveEpochUtcMs)
        {
            snapshot ??= new GuildRuntimeSnapshot();
            var nowMs = saveEpochUtcMs > 0 ? saveEpochUtcMs : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            ExecuteUpsert(session, "guilds", ["guild_id", "guild_name", "leader_character_id", "gold", "level", "experience", "updated_utc_ms", "spare_points", "votes", "last_vote_attempt_utc_ms", "voting", "flag_image", "flag_colour_argb", "snapshot_generation", "snapshot_active"], ["guild_id"], snapshot.Guilds.Select(row => new { guild_id = row.GuildId, guild_name = row.GuildName, leader_character_id = row.LeaderCharacterId, gold = row.Gold, level = row.Level, experience = row.Experience, updated_utc_ms = nowMs, spare_points = row.SparePoints, votes = row.Votes, last_vote_attempt_utc_ms = row.LastVoteAttemptUtcMs, voting = row.Voting, flag_image = row.FlagImage, flag_colour_argb = row.FlagColourArgb, snapshot_generation = nowMs, snapshot_active = 1 }));
            ExecuteUpsert(session, "guild_ranks", ["guild_id", "rank_index", "rank_name", "permissions", "updated_utc_ms", "snapshot_generation", "snapshot_active"], ["guild_id", "rank_index"], snapshot.Ranks.Select(row => new { guild_id = row.GuildId, rank_index = row.RankIndex, rank_name = row.RankName, permissions = row.Permissions, updated_utc_ms = nowMs, snapshot_generation = nowMs, snapshot_active = 1 }));
            ExecuteUpsert(session, "guild_members", ["guild_id", "character_id", "rank_index", "joined_utc_ms", "updated_utc_ms", "last_login_utc_ms", "has_voted", "online", "snapshot_generation", "snapshot_active"], ["guild_id", "character_id"], snapshot.Members.Select(row => new { guild_id = row.GuildId, character_id = row.CharacterId, rank_index = row.RankIndex, joined_utc_ms = row.LastLoginUtcMs, updated_utc_ms = nowMs, last_login_utc_ms = row.LastLoginUtcMs, has_voted = row.HasVoted, online = row.Online, snapshot_generation = nowMs, snapshot_active = 1 }));
            ExecuteUpsert(session, "guild_notices", ["guild_id", "notice_index", "notice_text", "updated_utc_ms", "snapshot_generation", "snapshot_active"], ["guild_id", "notice_index"], snapshot.Notices.Select(row => new { guild_id = row.GuildId, notice_index = row.NoticeIndex, notice_text = row.NoticeText, updated_utc_ms = nowMs, snapshot_generation = nowMs, snapshot_active = 1 }));
            ExecuteUpsert(session, "guild_buffs", ["guild_id", "buff_type", "buff_level", "expiry_utc_ms", "updated_utc_ms", "active", "active_time_remaining", "snapshot_generation", "snapshot_active"], ["guild_id", "buff_type"], snapshot.Buffs.Select(row => new { guild_id = row.GuildId, buff_type = row.BuffType, buff_level = 0, expiry_utc_ms = row.ActiveTimeRemaining, updated_utc_ms = nowMs, active = row.Active, active_time_remaining = row.ActiveTimeRemaining, snapshot_generation = nowMs, snapshot_active = 1 }));
            ExecuteUpsert(session, "guild_storage_slots", ["guild_id", "slot_index", "item_id", "updated_utc_ms", "user_character_id", "snapshot_generation", "snapshot_active"], ["guild_id", "slot_index"], snapshot.StorageSlots.Select(row => new { guild_id = row.GuildId, slot_index = row.SlotIndex, item_id = row.ItemId, updated_utc_ms = nowMs, user_character_id = row.UserCharacterId, snapshot_generation = nowMs, snapshot_active = 1 }));
        }

        private static void UpsertNpcGoods(SqlSession session, NpcGoodsSnapshot snapshot, long saveEpochUtcMs)
        {
            snapshot ??= new NpcGoodsSnapshot();
            var nowMs = saveEpochUtcMs > 0 ? saveEpochUtcMs : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            ExecuteUpsert(session, "npc_buybacks", ["buyback_id", "npc_id", "character_id", "item_id", "price", "expires_utc_ms", "updated_utc_ms", "snapshot_generation", "snapshot_active"], ["buyback_id"], snapshot.Buybacks.Select(row => new { buyback_id = row.BuybackId, npc_id = row.NpcId, character_id = row.CharacterId, item_id = row.ItemId, price = row.Price, expires_utc_ms = row.ExpiresUtcMs, updated_utc_ms = nowMs, snapshot_generation = nowMs, snapshot_active = 1 }));
            ExecuteUpsert(session, "npc_used_goods", ["used_good_id", "npc_id", "item_id", "price", "available_utc_ms", "updated_utc_ms", "snapshot_generation", "snapshot_active"], ["used_good_id"], snapshot.UsedGoods.Select(row => new { used_good_id = row.UsedGoodId, npc_id = row.NpcId, item_id = row.ItemId, price = row.Price, available_utc_ms = row.AvailableUtcMs, updated_utc_ms = nowMs, snapshot_generation = nowMs, snapshot_active = 1 }));
        }

        private static void ExecuteUpsert<T>(SqlSession session, string table, IReadOnlyList<string> columns, IReadOnlyList<string> keys, IEnumerable<T> rows)
        {
            var values = rows?.Cast<object>().ToArray() ?? Array.Empty<object>();
            if (values.Length == 0) return;
            var updates = columns.Where(column => !keys.Contains(column, StringComparer.OrdinalIgnoreCase)).ToArray();
            session.Execute(session.Dialect.BuildUpsert(table, columns, keys, updates), values);
        }

        private static GuildRuntimeSnapshot LoadGuildRuntime(SqlSession session)
        {
            return new GuildRuntimeSnapshot
            {
                Guilds = session.Query<GuildRow>("SELECT guild_id AS GuildId,guild_name AS GuildName,leader_character_id AS LeaderCharacterId,gold AS Gold,level AS Level,spare_points AS SparePoints,experience AS Experience,votes AS Votes,last_vote_attempt_utc_ms AS LastVoteAttemptUtcMs,voting AS Voting,flag_image AS FlagImage,flag_colour_argb AS FlagColourArgb FROM guilds ORDER BY guild_id"),
                Ranks = session.Query<GuildRankRow>("SELECT guild_id AS GuildId,rank_index AS RankIndex,rank_name AS RankName,permissions AS Permissions FROM guild_ranks ORDER BY guild_id,rank_index"),
                Members = session.Query<GuildMemberRow>("SELECT guild_id AS GuildId,character_id AS CharacterId,rank_index AS RankIndex,last_login_utc_ms AS LastLoginUtcMs,has_voted AS HasVoted,online AS Online FROM guild_members ORDER BY guild_id,rank_index,character_id"),
                Notices = session.Query<GuildNoticeRow>("SELECT guild_id AS GuildId,notice_index AS NoticeIndex,notice_text AS NoticeText FROM guild_notices ORDER BY guild_id,notice_index"),
                Buffs = session.Query<GuildBuffRow>("SELECT guild_id AS GuildId,buff_type AS BuffType,active AS Active,active_time_remaining AS ActiveTimeRemaining FROM guild_buffs ORDER BY guild_id,buff_type"),
                StorageSlots = session.Query<GuildStorageSlotRow>("SELECT guild_id AS GuildId,slot_index AS SlotIndex,item_id AS ItemId,user_character_id AS UserCharacterId FROM guild_storage_slots ORDER BY guild_id,slot_index"),
            };
        }

        private static NpcGoodsSnapshot LoadAndNormalizeNpcGoods(SqlSession session)
        {
            var buybacks = session.Query<NpcBuybackRow>("SELECT buyback_id AS BuybackId,npc_id AS NpcId,character_id AS CharacterId,item_id AS ItemId,price AS Price,expires_utc_ms AS ExpiresUtcMs FROM npc_buybacks ORDER BY npc_id,character_id,buyback_id").ToList();
            var usedGoods = session.Query<NpcUsedGoodRow>("SELECT used_good_id AS UsedGoodId,npc_id AS NpcId,item_id AS ItemId,price AS Price,available_utc_ms AS AvailableUtcMs FROM npc_used_goods ORDER BY npc_id,used_good_id").ToList();
            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var expired = buybacks.Where(row => row.ExpiresUtcMs <= nowMs).ToArray();
            if (expired.Length == 0)
                return new NpcGoodsSnapshot { Buybacks = buybacks, UsedGoods = usedGoods };

            var usedItemIds = usedGoods.Select(row => row.ItemId).ToHashSet();
            var converted = new List<NpcUsedGoodRow>();
            foreach (var row in expired)
            {
                if (!usedItemIds.Add(row.ItemId)) continue;
                var used = new NpcUsedGoodRow { UsedGoodId = row.ItemId, NpcId = row.NpcId, ItemId = row.ItemId, Price = row.Price, AvailableUtcMs = nowMs };
                usedGoods.Add(used);
                converted.Add(used);
            }

            ExecuteUpsert(session, "npc_used_goods", ["used_good_id", "npc_id", "item_id", "price", "available_utc_ms", "updated_utc_ms", "snapshot_generation", "snapshot_active"], ["used_good_id"], converted.Select(row => new { used_good_id = row.UsedGoodId, npc_id = row.NpcId, item_id = row.ItemId, price = row.Price, available_utc_ms = row.AvailableUtcMs, updated_utc_ms = nowMs, snapshot_generation = nowMs, snapshot_active = 1 }));
            session.Execute("DELETE FROM npc_buybacks WHERE buyback_id=@id", expired.Select(row => new { id = row.BuybackId }).ToArray());
            session.Execute("UPDATE item_locations SET location_kind='npc_used_goods',owner_id=@npcId,container_kind=0,slot_index=0,parent_item_id=NULL,updated_utc_ms=@nowMs WHERE item_id=@itemId", expired.Select(row => new { npcId = row.NpcId, itemId = row.ItemId, nowMs }).ToArray());
            buybacks.RemoveAll(row => row.ExpiresUtcMs <= nowMs);
            return new NpcGoodsSnapshot { Buybacks = buybacks, UsedGoods = usedGoods };
        }

        private static void ApplyGuildRuntime(Envir envir, IReadOnlyDictionary<long, UserItem> itemsById, GuildRuntimeSnapshot snapshot)
        {
            var characters = BuildCharacterIndex(envir);
            var ranksByGuild = snapshot.Ranks.GroupBy(row => row.GuildId).ToDictionary(group => group.Key, group => group.OrderBy(row => row.RankIndex).ToArray());
            var membersByGuild = snapshot.Members.GroupBy(row => row.GuildId).ToDictionary(group => group.Key, group => group.ToArray());
            var noticesByGuild = snapshot.Notices.GroupBy(row => row.GuildId).ToDictionary(group => group.Key, group => group.OrderBy(row => row.NoticeIndex).ToArray());
            var buffsByGuild = snapshot.Buffs.GroupBy(row => row.GuildId).ToDictionary(group => group.Key, group => group.ToArray());
            var storageByGuild = snapshot.StorageSlots.GroupBy(row => row.GuildId).ToDictionary(group => group.Key, group => group.ToArray());

            lock (Envir.LoadLock)
            {
                envir.GuildList.Clear();
                envir.Guilds.Clear();
                foreach (var row in snapshot.Guilds)
                {
                    var info = new GuildInfo
                    {
                        GuildIndex = (int)row.GuildId,
                        Name = row.GuildName ?? string.Empty,
                        Gold = (uint)Math.Clamp(row.Gold, 0, uint.MaxValue),
                        Level = (byte)Math.Clamp(row.Level, 0, byte.MaxValue),
                        SparePoints = (byte)Math.Clamp(row.SparePoints, 0, byte.MaxValue),
                        Experience = row.Experience,
                        Votes = row.Votes,
                        LastVoteAttempt = FromUtcMsToLocal(row.LastVoteAttemptUtcMs),
                        Voting = row.Voting != 0,
                        FlagImage = (ushort)Math.Clamp(row.FlagImage, 0, ushort.MaxValue),
                        FlagColour = Color.FromArgb(row.FlagColourArgb),
                        NeedSave = false,
                    };
                    if (info.Level < Settings.Guild_ExperienceList.Count) info.MaxExperience = Settings.Guild_ExperienceList[info.Level];
                    info.MemberCap = info.Name == Settings.NewbieGuild ? Settings.NewbieGuildMaxSize : info.Level < Settings.Guild_MembercapList.Count ? Settings.Guild_MembercapList[info.Level] : 0;

                    if (ranksByGuild.TryGetValue(row.GuildId, out var guildRanks))
                        foreach (var rankRow in guildRanks)
                            info.Ranks.Add(new GuildRank { Index = rankRow.RankIndex, Name = rankRow.RankName ?? string.Empty, Options = (GuildRankOptions)rankRow.Permissions });

                    var rankByIndex = info.Ranks.ToDictionary(rank => rank.Index);
                    if (membersByGuild.TryGetValue(row.GuildId, out var guildMembers))
                    {
                        foreach (var memberRow in guildMembers)
                        {
                            if (!rankByIndex.TryGetValue(memberRow.RankIndex, out var rank)) throw new InvalidDataException($"Guild {row.GuildId} member references missing rank {memberRow.RankIndex}.");
                            if (!characters.TryGetValue((int)memberRow.CharacterId, out var character)) throw new InvalidDataException($"Guild {row.GuildId} references missing character {memberRow.CharacterId}.");
                            rank.Members.Add(new GuildMember { Id = character.Index, Name = character.Name ?? string.Empty, LastLogin = FromUtcMsToLocal(memberRow.LastLoginUtcMs), hasvoted = memberRow.HasVoted != 0, Online = false });
                        }
                    }

                    info.Membercount = info.Ranks.Sum(rank => rank.Members.Count);
                    if (noticesByGuild.TryGetValue(row.GuildId, out var guildNotices)) info.Notice.AddRange(guildNotices.Select(notice => notice.NoticeText ?? string.Empty));
                    if (buffsByGuild.TryGetValue(row.GuildId, out var guildBuffs))
                        foreach (var buffRow in guildBuffs)
                            info.BuffList.Add(new GuildBuff { Id = buffRow.BuffType, Info = envir.FindGuildBuffInfo(buffRow.BuffType), Active = buffRow.Active != 0, ActiveTimeRemaining = buffRow.ActiveTimeRemaining });

                    if (storageByGuild.TryGetValue(row.GuildId, out var guildStorage))
                    {
                        foreach (var slot in guildStorage)
                        {
                            if (slot.SlotIndex < 0 || slot.SlotIndex >= info.StoredItems.Length) throw new InvalidDataException($"Guild {row.GuildId} storage slot out of range: {slot.SlotIndex}.");
                            if (!itemsById.TryGetValue(slot.ItemId, out var item)) throw new InvalidDataException($"Guild {row.GuildId} storage references missing item {slot.ItemId}.");
                            info.StoredItems[slot.SlotIndex] = new GuildStorageItem { Item = item, UserId = slot.UserCharacterId };
                        }
                    }

                    envir.GuildList.Add(info);
                    _ = new GuildObject(info);
                }
            }
        }

        private static void ApplyNpcGoods(Envir envir, IReadOnlyDictionary<long, UserItem> itemsById, NpcGoodsSnapshot snapshot)
        {
            var npcs = envir.NPCs.Where(npc => npc?.Info != null).ToDictionary(npc => (long)npc.Info.Index);
            var characters = BuildCharacterIndex(envir);
            foreach (var npc in npcs.Values)
            {
                npc.BuyBack.Clear();
                npc.UsedGoods.Clear();
                npc.NeedSave = false;
            }

            foreach (var row in snapshot.Buybacks)
            {
                if (!npcs.TryGetValue(row.NpcId, out var npc)) throw new InvalidDataException($"Buyback references missing NPC {row.NpcId}.");
                if (!characters.TryGetValue((int)row.CharacterId, out var character)) throw new InvalidDataException($"Buyback references missing character {row.CharacterId}.");
                if (!itemsById.TryGetValue(row.ItemId, out var item)) throw new InvalidDataException($"Buyback references missing item {row.ItemId}.");
                item.BuybackExpiryDate = FromUtcMsToLocal(row.ExpiresUtcMs).AddMinutes(-Settings.GoodsBuyBackTime);
                var name = character.Name ?? string.Empty;
                if (!npc.BuyBack.TryGetValue(name, out var list)) npc.BuyBack[name] = list = new List<UserItem>();
                list.Add(item);
            }

            foreach (var row in snapshot.UsedGoods)
            {
                if (!npcs.TryGetValue(row.NpcId, out var npc)) throw new InvalidDataException($"Used goods references missing NPC {row.NpcId}.");
                if (!itemsById.TryGetValue(row.ItemId, out var item)) throw new InvalidDataException($"Used goods references missing item {row.ItemId}.");
                npc.UsedGoods.Add(item);
            }
        }

        private CharacterResult LoadNpcGoodsRuntime()
        {
            NpcGoodsSnapshot snapshot = null;
            using (var session = SqlSession.Open(_provider, _databaseOptions, maxRetries: 3, baseRetryDelayMs: 200))
                session.RunInTransaction(s => snapshot = LoadAndNormalizeNpcGoods(s));

            var itemsById = _startupItemsById ?? CollectInMemoryItems(_statePort.Envir);
            ApplyNpcGoods(_statePort.Envir, itemsById, snapshot);
            _startupItemsById = null;
            return new CharacterResult { Committed = true, Generation = Volatile.Read(ref _generation) };
        }

        private CharacterResult LoadGuildRuntime()
        {
            GuildRuntimeSnapshot snapshot = null;
            using (var session = SqlSession.Open(_provider, _databaseOptions, maxRetries: 3, baseRetryDelayMs: 200))
                session.RunInTransaction(s => snapshot = LoadGuildRuntime(s));

            var itemsById = _startupItemsById ?? CollectInMemoryItems(_statePort.Envir);
            ApplyGuildRuntime(_statePort.Envir, itemsById, snapshot);
            return new CharacterResult { Committed = true, Generation = Volatile.Read(ref _generation) };
        }
    }
}
