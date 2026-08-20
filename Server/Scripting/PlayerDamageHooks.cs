using System;
using Server.MirObjects;

namespace Server.Scripting
{
    public enum PlayerDamagePerspective
    {
        Outgoing = 0,
        Incoming = 1,
    }

    public sealed class PlayerDamageRequest
    {
        public PlayerDamageRequest(
            PlayerDamagePerspective perspective,
            MapObject attacker,
            MapObject target,
            int damage,
            int armour,
            DefenceType defenceType,
            bool damageWeapon)
        {
            Perspective = perspective;
            Attacker = attacker ?? throw new ArgumentNullException(nameof(attacker));
            Target = target ?? throw new ArgumentNullException(nameof(target));
            DefenceType = defenceType;

            OriginalDamage = damage;
            OriginalArmour = armour;
            OriginalDamageWeapon = damageWeapon;

            Damage = damage;
            Armour = armour;
            DamageWeapon = damageWeapon;
        }

        public PlayerDamagePerspective Perspective { get; }

        public MapObject Attacker { get; }
        public MapObject Target { get; }
        public DefenceType DefenceType { get; }

        public int OriginalDamage { get; }
        public int OriginalArmour { get; }
        public bool OriginalDamageWeapon { get; }

        public int Damage { get; set; }
        public int Armour { get; set; }
        public bool DamageWeapon { get; set; }

        public bool AllowReflect { get; set; } = true;
        public bool AllowCritical { get; set; } = true;
        public bool ForceCritical { get; set; }
        public bool AllowNegativeEffects { get; set; } = true;
        public bool AllowGatherElement { get; set; } = true;

        public ScriptHookDecision Decision { get; set; } = ScriptHookDecision.Continue;

        /// <summary>
        /// 当 <see cref="Decision"/> = Handled 时，返回值将使用该字段（默认 -1 表示“按当前 Damage/Armour 计算”）。
        /// </summary>
        public int OverrideReturnDamage { get; set; } = -1;

        public int ComputeFinalDamage()
        {
            try
            {
                return Math.Max(0, Damage - Armour);
            }
            catch
            {
                return 0;
            }
        }
    }

    public sealed class PlayerDamageResult
    {
        public PlayerDamageResult(
            PlayerDamagePerspective perspective,
            MapObject attacker,
            MapObject target,
            int damage,
            int armour,
            DefenceType defenceType,
            bool damageWeapon,
            bool critical,
            int appliedDamage,
            ScriptHookDecision decision)
        {
            Perspective = perspective;
            Attacker = attacker ?? throw new ArgumentNullException(nameof(attacker));
            Target = target ?? throw new ArgumentNullException(nameof(target));
            Damage = damage;
            Armour = armour;
            DefenceType = defenceType;
            DamageWeapon = damageWeapon;
            Critical = critical;
            AppliedDamage = appliedDamage;
            Decision = decision;
        }

        public PlayerDamagePerspective Perspective { get; }

        public MapObject Attacker { get; }
        public MapObject Target { get; }
        public DefenceType DefenceType { get; }

        public int Damage { get; }
        public int Armour { get; }

        public bool DamageWeapon { get; }
        public bool Critical { get; }

        public int AppliedDamage { get; }

        public ScriptHookDecision Decision { get; }
    }
}

