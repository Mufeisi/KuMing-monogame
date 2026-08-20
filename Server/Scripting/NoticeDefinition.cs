namespace Server.Scripting
{
    public sealed class NoticeDefinition
    {
        private readonly string[] _lines;

        public string Title { get; }

        public IReadOnlyList<string> Lines => _lines;

        public NoticeDefinition(string title, IEnumerable<string> lines)
        {
            Title = title ?? string.Empty;

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
