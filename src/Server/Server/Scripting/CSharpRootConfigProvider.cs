namespace Server.Scripting
{
    public sealed class CSharpRootConfigProvider : IRootConfigProvider
    {
        public int Count { get; }

        public DisabledCharsDefinition DisabledChars { get; }

        public LineMessageDefinition LineMessages { get; }

        public NoticeDefinition Notice { get; }

        public SetBuffsDefinition SetBuffs { get; }

        public CSharpRootConfigProvider(RootConfigRegistry registry)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));

            DisabledChars = registry.DisabledChars;
            LineMessages = registry.LineMessages;
            Notice = registry.Notice;
            SetBuffs = registry.SetBuffs;
            Count = registry.Count;
        }
    }
}
