using System;

namespace Server.Scripting
{
    public sealed class LingFengTxtTriggerContext
    {
        [ThreadStatic]
        private static LingFengTxtTriggerContext _current;

        private LingFengTxtTriggerContext(object payload, LingFengTxtTriggerContext previous)
        {
            Payload = payload;
            Previous = previous;
        }

        public static LingFengTxtTriggerContext Current => _current;

        public object Payload { get; }

        private LingFengTxtTriggerContext Previous { get; }

        public static IDisposable Push(object payload)
        {
            var context = new LingFengTxtTriggerContext(payload, _current);
            _current = context;
            return new Scope(context);
        }

        public bool TryChangeDamageValue(string fieldText, string operation, string valueText)
        {
            if (Payload is not PlayerDamageRequest request ||
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
}
