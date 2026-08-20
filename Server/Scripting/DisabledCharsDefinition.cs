namespace Server.Scripting
{
    public sealed class DisabledCharsDefinition
    {
        private readonly string[] _values;

        public IReadOnlyList<string> Values => _values;

        public DisabledCharsDefinition(IEnumerable<string> values)
        {
            if (values == null)
            {
                _values = Array.Empty<string>();
                return;
            }

            var list = new List<string>();

            foreach (var value in values)
            {
                if (value == null) continue;
                list.Add(value);
            }

            _values = list.ToArray();
        }
    }
}
