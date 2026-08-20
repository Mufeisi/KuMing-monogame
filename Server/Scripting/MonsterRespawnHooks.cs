using System;
using Server.MirDatabase;
using Server.MirEnvir;

namespace Server.Scripting
{
    public sealed class MonsterRespawnRequest
    {
        public MonsterRespawnRequest(
            Map map,
            MapRespawn respawn,
            MonsterInfo monster,
            int spawnMultiplier,
            long currentTime,
            ulong currentRespawnTick,
            int desiredCount,
            int spawnCount,
            int delayMinutes,
            int randomDelayMinutes,
            bool useRespawnTicks,
            int respawnTicks)
        {
            Map = map ?? throw new ArgumentNullException(nameof(map));
            Respawn = respawn ?? throw new ArgumentNullException(nameof(respawn));
            Monster = monster;
            SpawnMultiplier = spawnMultiplier;
            CurrentTime = currentTime;
            CurrentRespawnTick = currentRespawnTick;

            DesiredCount = desiredCount;
            SpawnCount = spawnCount;
            DelayMinutes = delayMinutes;
            RandomDelayMinutes = randomDelayMinutes;
            UseRespawnTicks = useRespawnTicks;
            RespawnTicks = respawnTicks;
        }

        public Map Map { get; }

        public MapRespawn Respawn { get; }

        public RespawnInfo Info => Respawn.Info;

        public MonsterInfo Monster { get; }

        public int SpawnMultiplier { get; }

        public long CurrentTime { get; }

        public ulong CurrentRespawnTick { get; }

        /// <summary>
        /// 目标存量（默认 = RespawnInfo.Count * Envir.SpawnMultiplier）。可设为 0 用于禁用某个刷新点（活动未开启等）。
        /// </summary>
        public int DesiredCount { get; set; }

        /// <summary>
        /// 本次实际要尝试生成的数量（默认 = max(0, DesiredCount - CurrentCount)）。可设为更大以实现活动“临时加刷”。
        /// </summary>
        public int SpawnCount { get; set; }

        /// <summary>
        /// 成功/跳过（Cancel/Handled）后，下一次刷新间隔（分钟）。默认来自 RespawnInfo.Delay。
        /// </summary>
        public int DelayMinutes { get; set; }

        /// <summary>
        /// 成功/跳过（Cancel/Handled）后，下一次刷新随机浮动（分钟）。默认来自 RespawnInfo.RandomDelay。
        /// </summary>
        public int RandomDelayMinutes { get; set; }

        /// <summary>
        /// 是否使用 RespawnTick 系统（默认 = RespawnInfo.RespawnTicks != 0）。
        /// </summary>
        public bool UseRespawnTicks { get; set; }

        /// <summary>
        /// 使用 RespawnTick 系统时的间隔（tick 数）。默认来自 RespawnInfo.RespawnTicks。
        /// </summary>
        public int RespawnTicks { get; set; }

        public ScriptHookDecision Decision { get; set; } = ScriptHookDecision.Continue;
    }

    public sealed class MonsterRespawnResult
    {
        public MonsterRespawnResult(
            Map map,
            MapRespawn respawn,
            MonsterInfo monster,
            int spawnMultiplier,
            long currentTime,
            ulong currentRespawnTick,
            int desiredCount,
            int spawnRequestedCount,
            int spawnedCount,
            bool useRespawnTicks,
            int respawnTicks,
            int delayMinutes,
            int randomDelayMinutes,
            long nextRespawnTime,
            ulong nextSpawnTick,
            int countBefore,
            int countAfter,
            byte errorCountBefore,
            byte errorCountAfter,
            bool success,
            bool executedLegacy,
            ScriptHookDecision decision)
        {
            Map = map ?? throw new ArgumentNullException(nameof(map));
            Respawn = respawn ?? throw new ArgumentNullException(nameof(respawn));
            Monster = monster;
            SpawnMultiplier = spawnMultiplier;
            CurrentTime = currentTime;
            CurrentRespawnTick = currentRespawnTick;
            DesiredCount = desiredCount;
            SpawnRequestedCount = spawnRequestedCount;
            SpawnedCount = spawnedCount;
            UseRespawnTicks = useRespawnTicks;
            RespawnTicks = respawnTicks;
            DelayMinutes = delayMinutes;
            RandomDelayMinutes = randomDelayMinutes;
            NextRespawnTime = nextRespawnTime;
            NextSpawnTick = nextSpawnTick;
            CountBefore = countBefore;
            CountAfter = countAfter;
            ErrorCountBefore = errorCountBefore;
            ErrorCountAfter = errorCountAfter;
            Success = success;
            ExecutedLegacy = executedLegacy;
            Decision = decision;
        }

        public Map Map { get; }

        public MapRespawn Respawn { get; }

        public RespawnInfo Info => Respawn.Info;

        public MonsterInfo Monster { get; }

        public int SpawnMultiplier { get; }

        public long CurrentTime { get; }

        public ulong CurrentRespawnTick { get; }

        public int DesiredCount { get; }

        public int SpawnRequestedCount { get; }

        public int SpawnedCount { get; }

        public bool UseRespawnTicks { get; }

        public int RespawnTicks { get; }

        public int DelayMinutes { get; }

        public int RandomDelayMinutes { get; }

        public long NextRespawnTime { get; }

        public ulong NextSpawnTick { get; }

        public int CountBefore { get; }

        public int CountAfter { get; }

        public byte ErrorCountBefore { get; }

        public byte ErrorCountAfter { get; }

        public bool Success { get; }

        public bool ExecutedLegacy { get; }

        public ScriptHookDecision Decision { get; }
    }
}
