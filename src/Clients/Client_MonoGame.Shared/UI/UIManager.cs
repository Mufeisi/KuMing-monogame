using System;

namespace MonoShare.UI
{
    public static class UIManager
    {
        public static UIProfileId GetDefaultProfileId()
        {
            return Environment.OSVersion.Platform == PlatformID.Win32NT ? UIProfileId.Classic : UIProfileId.Mobile;
        }

        public static bool TryParseProfileId(string value, out UIProfileId id)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                id = GetDefaultProfileId();
                return false;
            }

            return Enum.TryParse(value.Trim(), ignoreCase: true, out id);
        }

        public static UIProfileId GetCurrentProfileId()
        {
            return TryParseProfileId(Settings.UIProfileId, out var id) ? id : GetDefaultProfileId();
        }

        public static UIProfile GetProfile(UIProfileId id)
        {
            switch (id)
            {
                case UIProfileId.Mobile:
                    return new UIProfile
                    {
                        Id = UIProfileId.Mobile,
                        VirtualJoystickEnabled = Settings.MobileVirtualJoystickEnabled,
                        VirtualJoystickFollowMode = Settings.MobileVirtualJoystickFollowMode,
                        MobileActionPanelEnabled = Settings.MobileActionPanelEnabled,
                        MobileMiniMapEnabled = Settings.MobileMiniMapEnabled,
                        MobileQuickBarEnabled = Settings.MobileQuickBarEnabled,
                        MobileBeltBarEnabled = Settings.MobileBeltBarEnabled,
                    };
                case UIProfileId.Custom:
                    return new UIProfile { Id = UIProfileId.Custom };
                default:
                    return new UIProfile
                    {
                        Id = UIProfileId.Classic,
                        VirtualJoystickEnabled = false,
                        VirtualJoystickFollowMode = false,
                        MobileActionPanelEnabled = false,
                        MobileMiniMapEnabled = false,
                        MobileQuickBarEnabled = false,
                        MobileBeltBarEnabled = false,
                    };
            }
        }
    }
}
