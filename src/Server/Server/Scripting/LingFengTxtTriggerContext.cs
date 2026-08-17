using System;

namespace Server.Scripting
{
    public enum LingFengCombatActorKind
    {
        Unknown,
        Player,
        Hero,
        Pet
    }

    public readonly record struct LingFengMonsterKillEvent(
        string MonsterName,
        int X,
        int Y,
        uint Experience,
        LingFengCombatActorKind ActorKind = LingFengCombatActorKind.Unknown)
    {
        public LingFengMonsterKillEvent(string monsterName, int x, int y, uint experience)
            : this(monsterName, x, y, experience, LingFengCombatActorKind.Unknown)
        {
        }

        public void Deconstruct(out string monsterName, out int x, out int y, out uint experience)
        {
            monsterName = MonsterName;
            x = X;
            y = Y;
            experience = Experience;
        }
    }

    public enum LingFengItemTriggerKind
    {
        Pickup,
        Use,
        Drop
    }

    public readonly record struct LingFengItemTriggerEvent(
        LingFengItemTriggerKind Kind,
        string ItemName,
        int? Position,
        uint Gold);

    public readonly record struct LingFengDamageEvent(
        PlayerDamagePerspective Perspective,
        string AttackerName,
        string TargetName,
        string CurrentTargetName,
        int DamageValue,
        int AppliedDamage,
        bool IsAfter,
        bool TargetIsMonster = false,
        int TargetX = 0,
        int TargetY = 0,
        int TargetHp = 0,
        int TargetMaxHp = 0,
        string MagicId = "0",
        LingFengCombatActorKind ActorKind = LingFengCombatActorKind.Unknown)
    {
        public uint CurrentTargetObjectId { get; init; }
        public uint ActorObjectId { get; init; }

        public LingFengDamageEvent(
            PlayerDamagePerspective perspective,
            string attackerName,
            string targetName,
            string currentTargetName,
            int damageValue,
            int appliedDamage,
            bool isAfter,
            bool targetIsMonster,
            int targetX,
            int targetY,
            int targetHp,
            int targetMaxHp,
            string magicId)
            : this(perspective, attackerName, targetName, currentTargetName, damageValue, appliedDamage,
                isAfter, targetIsMonster, targetX, targetY, targetHp, targetMaxHp, magicId,
                LingFengCombatActorKind.Unknown)
        {
        }

        public void Deconstruct(
            out PlayerDamagePerspective perspective,
            out string attackerName,
            out string targetName,
            out string currentTargetName,
            out int damageValue,
            out int appliedDamage,
            out bool isAfter,
            out bool targetIsMonster,
            out int targetX,
            out int targetY,
            out int targetHp,
            out int targetMaxHp,
            out string magicId)
        {
            perspective = Perspective;
            attackerName = AttackerName;
            targetName = TargetName;
            currentTargetName = CurrentTargetName;
            damageValue = DamageValue;
            appliedDamage = AppliedDamage;
            isAfter = IsAfter;
            targetIsMonster = TargetIsMonster;
            targetX = TargetX;
            targetY = TargetY;
            targetHp = TargetHp;
            targetMaxHp = TargetMaxHp;
            magicId = MagicId;
        }
    }

    public sealed class LingFengTxtTriggerContext
    {
        [ThreadStatic]
        private static LingFengTxtTriggerContext _current;

        private LingFengTxtTriggerContext(
            object payload,
            PlayerDamageRequest damageRequest,
            IReadOnlyList<string> scriptParameters,
            string magicId,
            LingFengTxtTriggerContext previous)
        {
            Payload = payload;
            DamageRequest = damageRequest;
            ScriptParameters = scriptParameters;
            MagicId = magicId;
            Previous = previous;
        }

        public static LingFengTxtTriggerContext Current => _current;

        public object Payload { get; }
        public IReadOnlyList<string> ScriptParameters { get; }
        public string MagicId { get; }

        private PlayerDamageRequest DamageRequest { get; }
        private LingFengTxtTriggerContext Previous { get; }

        public static IDisposable Push(object payload)
        {
            var context = new LingFengTxtTriggerContext(
                payload, null, _current?.ScriptParameters, _current?.MagicId, _current);
            _current = context;
            return new Scope(context);
        }

        internal static IDisposable PushDamage(LingFengDamageEvent payload, PlayerDamageRequest request)
        {
            var context = new LingFengTxtTriggerContext(
                payload, request, _current?.ScriptParameters, _current?.MagicId, _current);
            _current = context;
            return new Scope(context);
        }

        internal static IDisposable PushScriptParameters(IEnumerable<string> parameters)
        {
            string[] snapshot = parameters?.Select(value => value ?? string.Empty).ToArray()
                                ?? Array.Empty<string>();
            var context = new LingFengTxtTriggerContext(
                _current?.Payload,
                _current?.DamageRequest,
                Array.AsReadOnly(snapshot),
                _current?.MagicId,
                _current);
            _current = context;
            return new Scope(context);
        }

        internal static IDisposable PushMagic(string magicId)
        {
            var context = new LingFengTxtTriggerContext(
                _current?.Payload,
                _current?.DamageRequest,
                _current?.ScriptParameters,
                string.IsNullOrWhiteSpace(magicId) ? "0" : magicId,
                _current);
            _current = context;
            return new Scope(context);
        }

        public bool TryChangeDamageValue(string fieldText, string operation, string valueText)
        {
            if (DamageRequest is not PlayerDamageRequest request ||
                !int.TryParse(fieldText, out int field) ||
                !int.TryParse(valueText, out int operand))
                return false;

            int current = field switch
            {
                0 => request.Damage,
                1 => request.Armour,
                _ => int.MinValue
            };

            if (current == int.MinValue) return false;

            int changed;
            try
            {
                changed = operation switch
                {
                    "=" => operand,
                    "+" => checked(current + operand),
                    "-" => checked(current - operand),
                    "*" => checked(current * operand),
                    "/" when operand != 0 => current / operand,
                    "%" when operand != 0 => current % operand,
                    _ => int.MinValue
                };
            }
            catch (OverflowException)
            {
                return false;
            }

            if (changed == int.MinValue) return false;

            if (field == 0)
                request.Damage = Math.Max(0, changed);
            else
                request.Armour = Math.Max(0, changed);

            if (request.ComputeFinalDamage() <= 0)
                request.Decision = ScriptHookDecision.Cancel;

            return true;
        }

        private sealed class Scope : IDisposable
        {
            private LingFengTxtTriggerContext _context;

            public Scope(LingFengTxtTriggerContext context) => _context = context;

            public void Dispose()
            {
                if (_context == null) return;
                if (ReferenceEquals(_current, _context))
                    _current = _context.Previous;
                _context = null;
            }
        }
    }

    public static class LingFengScriptTriggerSuppression
    {
        [ThreadStatic]
        private static int _depth;

        public static bool IsActive => _depth > 0;

        public static IDisposable Enter()
        {
            _depth++;
            return new SuppressionScope();
        }

        private sealed class SuppressionScope : IDisposable
        {
            private bool _disposed;

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                if (_depth > 0) _depth--;
            }
        }
    }
}
