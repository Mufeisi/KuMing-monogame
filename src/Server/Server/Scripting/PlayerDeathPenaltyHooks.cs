using System;
using Server.MirObjects;

namespace Server.Scripting
{
    public enum DeathPenaltyDropMode
    {
        None = 0,
        Normal = 1,
        Red = 2,
    }

    public sealed class PlayerDeathPenaltyRequest
    {
        public PlayerDeathPenaltyRequest(MapObject killer, DeathPenaltyDropMode dropMode, int pkPoints, bool inSafeZone)
        {
            Killer = killer;
            DropMode = dropMode;
            PKPoints = pkPoints;
            InSafeZone = inSafeZone;
        }

        public MapObject Killer { get; set; }

        public DeathPenaltyDropMode DropMode { get; set; }

        public int PKPoints { get; }

        public bool InSafeZone { get; }

        public ScriptHookDecision Decision { get; set; } = ScriptHookDecision.Continue;
    }

    public sealed class PlayerDeathPenaltyResult
    {
        public PlayerDeathPenaltyResult(MapObject killer, DeathPenaltyDropMode requestedDropMode, DeathPenaltyDropMode executedDropMode, bool executedLegacy, ScriptHookDecision decision)
        {
            Killer = killer;
            RequestedDropMode = requestedDropMode;
            ExecutedDropMode = executedDropMode;
            ExecutedLegacy = executedLegacy;
            Decision = decision;
        }

        public MapObject Killer { get; }

        public DeathPenaltyDropMode RequestedDropMode { get; }

        public DeathPenaltyDropMode ExecutedDropMode { get; }

        public bool ExecutedLegacy { get; }

        public ScriptHookDecision Decision { get; }
    }
}

