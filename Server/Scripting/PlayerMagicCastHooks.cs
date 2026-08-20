using System;
using System.Drawing;
using Server.MirDatabase;
using Server.MirEnvir;

namespace Server.Scripting
{
    public enum ScriptHookDecision
    {
        Continue = 0,
        Cancel = 1,
        Handled = 2,
    }

    public sealed class PlayerMagicCastRequest
    {
        public PlayerMagicCastRequest(
            Spell spell,
            MirDirection direction,
            uint targetID,
            Point location,
            bool spellTargetLock,
            UserMagic magic,
            int mpCost,
            long cooldownDelay)
        {
            Spell = spell;
            Direction = direction;
            TargetID = targetID;
            Location = location;
            SpellTargetLock = spellTargetLock;
            Magic = magic ?? throw new ArgumentNullException(nameof(magic));
            MpCost = mpCost;
            CooldownDelay = cooldownDelay;
        }

        public Spell Spell { get; }
        public UserMagic Magic { get; }

        public MirDirection Direction { get; set; }
        public uint TargetID { get; set; }
        public Point Location { get; set; }
        public bool SpellTargetLock { get; }

        public int MpCost { get; set; }
        public long CooldownDelay { get; set; }

        public bool IgnoreCooldownCheck { get; set; }
        public bool IgnoreMpCostCheck { get; set; }

        public bool ConsumeMp { get; set; } = true;
        public bool ApplyCooldown { get; set; } = true;

        public ScriptHookDecision Decision { get; set; } = ScriptHookDecision.Continue;
    }

    public sealed class PlayerMagicCastResult
    {
        public PlayerMagicCastResult(
            Spell spell,
            UserMagic magic,
            MirDirection direction,
            uint targetID,
            Point location,
            bool spellTargetLock,
            int mpCost,
            long cooldownDelay,
            bool mpConsumed,
            bool cooldownApplied,
            bool cast,
            ScriptHookDecision decision)
        {
            Spell = spell;
            Magic = magic ?? throw new ArgumentNullException(nameof(magic));
            Direction = direction;
            TargetID = targetID;
            Location = location;
            SpellTargetLock = spellTargetLock;
            MpCost = mpCost;
            CooldownDelay = cooldownDelay;
            MpConsumed = mpConsumed;
            CooldownApplied = cooldownApplied;
            Cast = cast;
            Decision = decision;
        }

        public Spell Spell { get; }
        public UserMagic Magic { get; }

        public MirDirection Direction { get; }
        public uint TargetID { get; }
        public Point Location { get; }
        public bool SpellTargetLock { get; }

        public int MpCost { get; }
        public long CooldownDelay { get; }

        public bool MpConsumed { get; }
        public bool CooldownApplied { get; }
        public bool Cast { get; }

        public ScriptHookDecision Decision { get; }
    }
}
