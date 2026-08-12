namespace Server.Scripting
{
    public sealed class RootConfigRegistry
    {
        public DisabledCharsDefinition DisabledChars { get; private set; }
        public LineMessageDefinition LineMessages { get; private set; }
        public NoticeDefinition Notice { get; private set; }
        public SetBuffsDefinition SetBuffs { get; private set; }

        public int Count
        {
            get
            {
                var count = 0;
                if (DisabledChars != null) count++;
                if (LineMessages != null) count++;
                if (Notice != null) count++;
                if (SetBuffs != null) count++;
                return count;
            }
        }

        public void RegisterDisabledChars(DisabledCharsDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (DisabledChars != null) throw new InvalidOperationException("重复的 RootConfig：DisabledChars");
            DisabledChars = definition;
        }

        public void RegisterLineMessages(LineMessageDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (LineMessages != null) throw new InvalidOperationException("重复的 RootConfig：LineMessage");
            LineMessages = definition;
        }

        public void RegisterNotice(NoticeDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (Notice != null) throw new InvalidOperationException("重复的 RootConfig：Notice");
            Notice = definition;
        }

        public void RegisterSetBuffs(SetBuffsDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (SetBuffs != null) throw new InvalidOperationException("重复的 RootConfig：SetBuffs");
            SetBuffs = definition;
        }
    }
}
