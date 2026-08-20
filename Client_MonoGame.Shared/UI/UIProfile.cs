namespace MonoShare.UI
{
    public sealed class UIProfile
    {
        public UIProfileId Id { get; init; } = UIProfileId.Classic;

        public bool? VirtualJoystickEnabled { get; init; }
        public bool? VirtualJoystickFollowMode { get; init; }
        public bool? MobileActionPanelEnabled { get; init; }
        public bool? MobileMiniMapEnabled { get; init; }
        public bool? MobileQuickBarEnabled { get; init; }
        public bool? MobileBeltBarEnabled { get; init; }
    }
}
