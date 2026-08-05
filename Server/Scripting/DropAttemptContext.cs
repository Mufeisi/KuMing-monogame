using Server.MirObjects;

namespace Server.Scripting
{
    public sealed class DropAttemptContext
    {
        public string Source { get; }

        /// <summary>
        /// 可选：掉落表逻辑 Key（例如 Drops/00Awakening）；若未知则为空字符串。
        /// </summary>
        public string DropTableKey { get; }

        /// <summary>
        /// 可选：触发掉落的玩家（例如击杀者/钓鱼者/采集者/NPC 掉落接收者）。
        /// </summary>
        public PlayerObject Player { get; }

        /// <summary>
        /// 可选：触发掉落的怪物（例如怪物死亡掉落/采集怪物）。
        /// </summary>
        public MonsterObject Monster { get; }

        public DropAttemptContext(string source, PlayerObject player, MonsterObject monster, string dropTableKey = "")
        {
            Source = source ?? string.Empty;
            Player = player;
            Monster = monster;
            DropTableKey = dropTableKey ?? string.Empty;
        }
    }
}

