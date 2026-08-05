using System;
using System.Drawing;
using Server.MirObjects;

namespace Server.Scripting
{
    public sealed class MonsterAiTargetSelectRequest
    {
        public MonsterAiTargetSelectRequest(MonsterObject monster, MapObject currentTarget)
        {
            Monster = monster ?? throw new ArgumentNullException(nameof(monster));
            CurrentTarget = currentTarget;
            Target = currentTarget;
        }

        public MonsterObject Monster { get; }

        public MapObject CurrentTarget { get; }

        /// <summary>
        /// 期望的目标（默认为 <see cref="CurrentTarget"/>；可设为 null 表示强制重新搜寻）。
        /// </summary>
        public MapObject Target { get; set; }

        public ScriptHookDecision Decision { get; set; } = ScriptHookDecision.Continue;
    }

    public sealed class MonsterAiTargetSelectResult
    {
        public MonsterAiTargetSelectResult(
            MonsterObject monster,
            MapObject previousTarget,
            MapObject target,
            bool executedLegacy,
            ScriptHookDecision decision)
        {
            Monster = monster ?? throw new ArgumentNullException(nameof(monster));
            PreviousTarget = previousTarget;
            Target = target;
            ExecutedLegacy = executedLegacy;
            Decision = decision;
        }

        public MonsterObject Monster { get; }

        public MapObject PreviousTarget { get; }

        public MapObject Target { get; }

        public bool ExecutedLegacy { get; }

        public ScriptHookDecision Decision { get; }
    }

    public sealed class MonsterAiSkillSelectRequest
    {
        public MonsterAiSkillSelectRequest(
            MonsterObject monster,
            MapObject target,
            bool legacyCanAttack,
            bool legacyInAttackRange)
        {
            Monster = monster ?? throw new ArgumentNullException(nameof(monster));
            Target = target;
            LegacyCanAttack = legacyCanAttack;
            LegacyInAttackRange = legacyInAttackRange;
        }

        public MonsterObject Monster { get; }

        /// <summary>
        /// 期望攻击的目标（默认使用当前 Target；可调整为其它目标或设为 null 以取消）。
        /// </summary>
        public MapObject Target { get; set; }

        public bool LegacyCanAttack { get; }

        public bool LegacyInAttackRange { get; }

        public ScriptHookDecision Decision { get; set; } = ScriptHookDecision.Continue;
    }

    public sealed class MonsterAiSkillSelectResult
    {
        public MonsterAiSkillSelectResult(
            MonsterObject monster,
            MapObject previousTarget,
            MapObject target,
            bool legacyCanAttack,
            bool legacyInAttackRange,
            long actionTimeBefore,
            long attackTimeBefore,
            long actionTimeAfter,
            long attackTimeAfter,
            bool executedLegacy,
            ScriptHookDecision decision)
        {
            Monster = monster ?? throw new ArgumentNullException(nameof(monster));
            PreviousTarget = previousTarget;
            Target = target;
            LegacyCanAttack = legacyCanAttack;
            LegacyInAttackRange = legacyInAttackRange;
            ActionTimeBefore = actionTimeBefore;
            AttackTimeBefore = attackTimeBefore;
            ActionTimeAfter = actionTimeAfter;
            AttackTimeAfter = attackTimeAfter;
            ExecutedLegacy = executedLegacy;
            Decision = decision;
        }

        public MonsterObject Monster { get; }

        public MapObject PreviousTarget { get; }

        public MapObject Target { get; }

        public bool LegacyCanAttack { get; }

        public bool LegacyInAttackRange { get; }

        public long ActionTimeBefore { get; }

        public long AttackTimeBefore { get; }

        public long ActionTimeAfter { get; }

        public long AttackTimeAfter { get; }

        public bool ExecutedLegacy { get; }

        public ScriptHookDecision Decision { get; }
    }

    public sealed class MonsterAiMoveRequest
    {
        public MonsterAiMoveRequest(
            MonsterObject monster,
            MapObject target,
            Point targetLocation,
            Point requestedLocation)
        {
            Monster = monster ?? throw new ArgumentNullException(nameof(monster));
            Target = target;
            TargetLocation = targetLocation;
            RequestedLocation = requestedLocation;
            Location = requestedLocation;
        }

        public MonsterObject Monster { get; }

        /// <summary>
        /// 调用时刻的目标快照（可能为 null）。
        /// </summary>
        public MapObject Target { get; }

        public Point TargetLocation { get; }

        public Point RequestedLocation { get; }

        /// <summary>
        /// 最终执行 MoveTo 的目标点（可改为“逃跑点/绕路点”等）。
        /// </summary>
        public Point Location { get; set; }

        public ScriptHookDecision Decision { get; set; } = ScriptHookDecision.Continue;
    }

    public sealed class MonsterAiMoveResult
    {
        public MonsterAiMoveResult(
            MonsterObject monster,
            MapObject target,
            Point targetLocation,
            Point requestedLocation,
            Point finalLocation,
            Point previousLocation,
            Point currentLocation,
            bool executedLegacy,
            ScriptHookDecision decision)
        {
            Monster = monster ?? throw new ArgumentNullException(nameof(monster));
            Target = target;
            TargetLocation = targetLocation;
            RequestedLocation = requestedLocation;
            FinalLocation = finalLocation;
            PreviousLocation = previousLocation;
            CurrentLocation = currentLocation;
            ExecutedLegacy = executedLegacy;
            Decision = decision;
        }

        public MonsterObject Monster { get; }

        public MapObject Target { get; }

        public Point TargetLocation { get; }

        public Point RequestedLocation { get; }

        public Point FinalLocation { get; }

        public Point PreviousLocation { get; }

        public Point CurrentLocation { get; }

        public bool ExecutedLegacy { get; }

        public ScriptHookDecision Decision { get; }
    }
}

