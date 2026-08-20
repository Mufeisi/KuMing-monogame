namespace Server.Persistence.Sql
{
    public sealed partial class SqlServerPersistence
    {
        private void ValidateWorldReferences(SqlWorldRelationsSnapshot world)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            var itemIds = world.ItemInfos.Select(row => row.ItemId).ToHashSet();
            var mapIds = world.MapInfos.Select(row => row.MapId).ToHashSet();
            var respawnIds = world.MapRespawns.Select(row => (long)row.RespawnIndex).ToHashSet();
            var npcIds = world.NpcInfos.Select(row => row.NpcId).ToHashSet();
            var questIds = world.QuestInfos.Select(row => row.QuestId).ToHashSet();
            var spellIds = world.MagicInfos.Select(row => (long)row.Spell).ToHashSet();
            var conquestIds = world.Conquests.Select(row => row.ConquestId).ToHashSet();

            using var session = SqlSession.Open(_provider, _databaseOptions, maxRetries: 3, baseRetryDelayMs: 200);
            session.RunInTransaction(s =>
            {
                EnsureAllReferenced("item definition", s.Query<long>("SELECT DISTINCT item_index FROM item_instances"), itemIds);
                EnsureAllReferenced("current map", s.Query<long>("SELECT DISTINCT current_map_id FROM characters WHERE current_map_id<>0"), mapIds);
                EnsureAllReferenced("bind map", s.Query<long>("SELECT DISTINCT bind_map_id FROM characters WHERE bind_map_id<>0"), mapIds);
                EnsureAllReferenced("respawn definition", s.Query<long>("SELECT DISTINCT respawn_index FROM respawn_saves"), respawnIds);
                EnsureAllReferenced("NPC buyback definition", s.Query<long>("SELECT DISTINCT npc_id FROM npc_buybacks"), npcIds);
                EnsureAllReferenced("NPC used-goods definition", s.Query<long>("SELECT DISTINCT npc_id FROM npc_used_goods"), npcIds);
                EnsureAllReferenced("completed quest definition", s.Query<long>("SELECT DISTINCT quest_id FROM character_completed_quests"), questIds);
                EnsureAllReferenced("current quest definition", s.Query<long>("SELECT DISTINCT quest_id FROM current_quests"), questIds);
                EnsureAllReferenced("magic definition", s.Query<long>("SELECT DISTINCT spell FROM character_magics"), spellIds);
                EnsureAllReferenced("conquest definition", s.Query<long>("SELECT DISTINCT conquest_id FROM conquest_runtime"), conquestIds);
            });
        }

        private static void EnsureAllReferenced(string domain, IReadOnlyList<long> referenced, IReadOnlySet<long> available)
        {
            var missing = (referenced ?? Array.Empty<long>()).Where(id => !available.Contains(id)).Distinct().OrderBy(id => id).Take(20).ToArray();
            if (missing.Length > 0)
                throw new InvalidOperationException($"World 发布拒绝：{domain} 仍被 Character Runtime 引用但定义不存在：{string.Join(",", missing)}");
        }
    }
}
