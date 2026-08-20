namespace Client.UI
{
    public enum UIProfileChatPlacement
    {
        RelativeToMainDialog,
        BottomLeft,
        BottomRight,
    }

    public enum UIProfileMainPlacement
    {
        Default,
        CenterBottom,
        BottomLeft,
        BottomRight,
    }

    public sealed class UIProfile
    {
        public UIProfileId Id { get; init; } = UIProfileId.Classic;

        public UIProfileMainPlacement MainPlacement { get; init; } = UIProfileMainPlacement.Default;
        public UIProfileChatPlacement ChatPlacement { get; init; } = UIProfileChatPlacement.RelativeToMainDialog;

        public int? ChatWindowSize { get; init; }
        public bool? TransparentChat { get; init; }
    }
}

