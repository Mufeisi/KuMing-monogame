namespace Server.Scripting
{
    public interface IRootConfigProvider
    {
        int Count { get; }

        DisabledCharsDefinition DisabledChars { get; }

        LineMessageDefinition LineMessages { get; }

        NoticeDefinition Notice { get; }

        SetBuffsDefinition SetBuffs { get; }
    }
}
