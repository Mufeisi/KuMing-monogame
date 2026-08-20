namespace Server.Scripting
{
    public sealed class SetBuffsDefinition
    {
        private readonly string[] _lines;

        public IReadOnlyList<string> Lines => _lines;

        public SetBuffsDefinition(IEnumerable<string> lines)
        {
            if (lines == null)
            {
                _lines = Array.Empty<string>();
                return;
            }

            var list = new List<string>();

            foreach (var line in lines)
            {
                if (line == null) continue;
                list.Add(line);
            }

            _lines = list.ToArray();
        }
    }
}
