using System;
using System.Drawing;
using System.IO;
using MonoShare.MirSounds;

namespace MonoShare
{
    class Settings
    {
        public static bool UseConfig = false;
        public static string IPAddress = "192.168.0.100";
        public static int Port = 7000;
        public const int TimeOut = 5000;

        public static string AsynDownLoadIPAddress = "ftp://192.168.0.100:8888/";
        public static bool P_Patcher = true;
        public static string P_Host = @"http://www.baidu.com/";
        public static string P_PatchFileName = @"PList.gz";
        public static bool P_NeedLogin = false;
        public static string P_Login = string.Empty;
        public static string P_Password = string.Empty;
        public static string P_ServerName = string.Empty;
        public static string P_BrowserAddress = "http://www.baidu.com/";
        public static string P_Client = AppDomain.CurrentDomain.BaseDirectory;
        public static bool P_AutoStart = false;

        // Bootstrap 分包按需安装（资源下载/校验/重试）
        // 说明：PackageRepo 为旧方案（静态仓库根）。若为空，会自动使用 MicroBaseUrl + "file/"（统一走微端）。
        public static string BootstrapPackageRepo = string.Empty;
        public static bool BootstrapAutoDownloadPackages = true;
        public static bool BootstrapVerifyDownloadedPackages = true;
        public static int BootstrapDownloadRetryCount = 3;

        // Micro 微端（按需拉取资源）
        public static string MicroBaseUrl = "http://192.168.0.100:7777/api/";
        public static string MicroUser = "MicroUser";
        public static string MicroCode = string.Empty;

        public const long CleanDelay = 600000;
        public static int ScreenWidth = 1334, ScreenHeight = 750;
        private static InIReader Reader = CreateReader();

        private static bool _useTestConfig;
        public static bool UseTestConfig
        {
            get
            {
                return _useTestConfig;
            }
            set
            {
                Reader = CreateReader();
                _useTestConfig = value;
            }
        }

        public const string DataPath = @".\Data\",
                            MapPath = @".\Map\",
                            SoundPath = @".\Sound\",
                            ExtraDataPath = @".\Data\Extra\",
                            ShadersPath = @".\Data\Shaders\",
                            MonsterPath = @".\Data\Monster\",
                            GatePath = @".\Data\Gate\",
                            FlagPath = @".\Data\Flag\",
                            NPCPath = @".\Data\NPC\",
                            CArmourPath = @".\Data\CArmour\",
                            CWeaponPath = @".\Data\CWeapon\",
                            CWeaponEffectPath = @".\Data\CWeaponEffect\",
                            CHairPath = @".\Data\CHair\",
                            AArmourPath = @".\Data\AArmour\",
                            AWeaponPath = @".\Data\AWeapon\",
                            AHairPath = @".\Data\AHair\",
                            ARArmourPath = @".\Data\ARArmour\",
                            ARWeaponPath = @".\Data\ARWeapon\",
                            ARHairPath = @".\Data\ARHair\",
                            CHumEffectPath = @".\Data\CHumEffect\",
                            AHumEffectPath = @".\Data\AHumEffect\",
                            ARHumEffectPath = @".\Data\ARHumEffect\",
                            MountPath = @".\Data\Mount\",
                            FishingPath = @".\Data\Fishing\",
                            PetsPath = @".\Data\Pet\",
                            TransformPath = @".\Data\Transform\",
                            TransformMountsPath = @".\Data\TransformRide2\",
                            TransformEffectPath = @".\Data\TransformEffect\",
                            TransformWeaponEffectPath = @".\Data\TransformWeaponEffect\",
                            MouseCursorPath = @".\Data\Cursors\";

        public static string ConfigFilePath => ClientResourceLayout.ConfigFilePath;
        public static string LanguageFilePath => ClientResourceLayout.LanguageFilePath;
        public static string DataDirectory => ClientResourceLayout.DataRoot;
        public static string MapDirectory => ClientResourceLayout.MapRoot;
        public static string SoundDirectory => ClientResourceLayout.SoundRoot;
        public static string FontCacheDirectory => ClientResourceLayout.FontCacheRoot;
        public static string LibraryCacheDirectory => ClientResourceLayout.LibraryCacheRoot;
        public static string MapCacheDirectory => ClientResourceLayout.MapCacheRoot;
        public static string SoundCacheDirectory => ClientResourceLayout.SoundCacheRoot;

        public static bool LogErrors = true;
        public static bool LogChat = true;
        public static bool TracePackets = false;
        public static int RemainingErrorLogs = 100;

        // SmokeTest / Automation（默认关闭，仅用于联调与验收）
        public static bool SmokeTestAutoLogin = false;
        public static bool SmokeTestAutoCreateAccount = false;
        public static bool SmokeTestAutoCreateCharacter = false;
        public static bool SmokeTestAutoStartGame = false;
        public static string SmokeTestCharacterName = string.Empty;

        // Interface / UI
        public static string UIProfileId = Environment.OSVersion.Platform == PlatformID.Win32NT ? "Classic" : "Mobile";
        public static bool MobileProfileInitialized = false;

        // Mobile / Touch
        public static bool MobileVirtualJoystickEnabled = Environment.OSVersion.Platform != PlatformID.Win32NT;
        public static bool MobileVirtualJoystickFollowMode = false;
        public static bool MobileActionPanelEnabled = true;
        public static bool MobileQuickBarEnabled = true;
        public static bool MobileBeltBarEnabled = true;

        public static int MobileSoftKeyboardAvoidanceHeight = 0;
        public static int MobileTouchLongPressThresholdMs = 450;
        public static float MobileTouchTapMoveTolerancePixels = 32F;
        public static int MobileTwoFingerTapMaxDurationMs = 250;
        public static float MobileTwoFingerTapMoveTolerancePixels = 28F;
        public static float MobilePinchStartDistanceThresholdPixels = 10F;
        public static float MobileJoystickDeadzone = 0.20F;
        public static float MobileJoystickRunThreshold = 0.75F;
        public static float MobileJoystickActivationWidthRatio = 0.50F;
        public static float MobileJoystickActivationHeightRatio = 0.50F;
        public static float MobileJoystickRadiusRatio = 0.09F;
        public static float MobileJoystickRadiusMin = 60F;
        public static float MobileJoystickFixedMarginRatio = 0.12F;
        public static float MobileJoystickFixedMarginMin = 80F;
        public static bool MobileMiniMapEnabled = true;
        public static float MobileMiniMapZoom = 1F;
        public static float MobileMiniMapZoomMin = 0.75F;
        public static float MobileMiniMapZoomMax = 2.5F;
        public static float MobileMiniMapPinchZoomPerPixel = 0.01F;

        public static float MobileMapScaleMin = 1F;
        public static float MobileMapScaleMax = 2F;
        public static float MobilePinchScalePerPixel = 0.005F;
        public static float MobilePinchDeadzonePixels = 0.01F;
        public static float MobilePinchDeltaSmoothing = 0F;

        // 说明：SafeArea 目前仅提供“配置驱动”的占位口径，用于在 Windows 上模拟刘海/底部手势条遮挡。
        // 真正的安全区读取仍需在 Android/iOS 壳子中接入平台 API 并回填到这些字段或通过运行时 Override 注入。
        public static int MobileSafeAreaLeft = 0;
        public static int MobileSafeAreaTop = 0;
        public static int MobileSafeAreaRight = 0;
        public static int MobileSafeAreaBottom = 0;

        private static bool _runtimeMobileSafeAreaEnabled;
        private static int _runtimeMobileSafeAreaLeft;
        private static int _runtimeMobileSafeAreaTop;
        private static int _runtimeMobileSafeAreaRight;
        private static int _runtimeMobileSafeAreaBottom;
        private static int _runtimeMobileSafeAreaSourceWidth;
        private static int _runtimeMobileSafeAreaSourceHeight;
        private static int _runtimeSoftKeyboardAvoidanceHeight = -1;
        private static int _runtimeSoftKeyboardAvoidanceSourceHeight;

        private static int _runtimeBatteryPercent = -1;
        private static bool _runtimeBatteryCharging;
        private static int _runtimeSignalLevel = -1;

        public static int RuntimeBatteryPercent => _runtimeBatteryPercent;
        public static bool RuntimeBatteryCharging => _runtimeBatteryCharging;
        public static int RuntimeSignalLevel => _runtimeSignalLevel;

        public static void SetRuntimeMobileBatteryStatus(int percent, bool charging)
        {
            _runtimeBatteryPercent = percent < 0 ? -1 : Math.Clamp(percent, 0, 100);
            _runtimeBatteryCharging = charging;
        }

        public static void SetRuntimeMobileSignalLevel(int level)
        {
            _runtimeSignalLevel = level < 0 ? -1 : Math.Clamp(level, 0, 4);
        }

        public static void SetRuntimeMobileSafeAreaInsets(int left, int top, int right, int bottom)
        {
            SetRuntimeMobileSafeAreaInsets(left, top, right, bottom, sourceWidth: 0, sourceHeight: 0);
        }

        public static void SetRuntimeMobileSafeAreaInsets(int left, int top, int right, int bottom, int sourceWidth, int sourceHeight)
        {
            _runtimeMobileSafeAreaLeft = Math.Max(0, left);
            _runtimeMobileSafeAreaTop = Math.Max(0, top);
            _runtimeMobileSafeAreaRight = Math.Max(0, right);
            _runtimeMobileSafeAreaBottom = Math.Max(0, bottom);

            _runtimeMobileSafeAreaSourceWidth = Math.Max(0, sourceWidth);
            _runtimeMobileSafeAreaSourceHeight = Math.Max(0, sourceHeight);

            _runtimeMobileSafeAreaEnabled = true;
        }

        public static void SetRuntimeSoftKeyboardAvoidanceHeight(int height)
        {
            SetRuntimeSoftKeyboardAvoidanceHeight(height, sourceHeight: 0);
        }

        public static void SetRuntimeSoftKeyboardAvoidanceHeight(int height, int sourceHeight)
        {
            _runtimeSoftKeyboardAvoidanceHeight = Math.Max(0, height);
            _runtimeSoftKeyboardAvoidanceSourceHeight = Math.Max(0, sourceHeight);
        }

        public static Rectangle GetMobileSafeAreaBounds()
        {
            int width = Math.Max(1, ScreenWidth);
            int height = Math.Max(1, ScreenHeight);

            int left = Math.Max(0, _runtimeMobileSafeAreaEnabled ? _runtimeMobileSafeAreaLeft : MobileSafeAreaLeft);
            int top = Math.Max(0, _runtimeMobileSafeAreaEnabled ? _runtimeMobileSafeAreaTop : MobileSafeAreaTop);
            int right = Math.Max(0, _runtimeMobileSafeAreaEnabled ? _runtimeMobileSafeAreaRight : MobileSafeAreaRight);
            int bottom = Math.Max(0, _runtimeMobileSafeAreaEnabled ? _runtimeMobileSafeAreaBottom : MobileSafeAreaBottom);

            if (_runtimeMobileSafeAreaEnabled &&
                _runtimeMobileSafeAreaSourceWidth > 0 &&
                _runtimeMobileSafeAreaSourceHeight > 0 &&
                (_runtimeMobileSafeAreaSourceWidth != width || _runtimeMobileSafeAreaSourceHeight != height))
            {
                float scaleX = width / (float)_runtimeMobileSafeAreaSourceWidth;
                float scaleY = height / (float)_runtimeMobileSafeAreaSourceHeight;

                left = (int)Math.Round(left * scaleX);
                right = (int)Math.Round(right * scaleX);
                top = (int)Math.Round(top * scaleY);
                bottom = (int)Math.Round(bottom * scaleY);

                left = Math.Max(0, left);
                right = Math.Max(0, right);
                top = Math.Max(0, top);
                bottom = Math.Max(0, bottom);
            }

            // 说明：软键盘弹出时通常会遮挡底部 UI。共享层无法直接获取键盘高度，
            // 这里先提供“配置驱动”的避让高度，并由 `CMain.SoftKeyboardVisible` 指示是否需要避让。
            int avoidance = _runtimeSoftKeyboardAvoidanceHeight >= 0 ? _runtimeSoftKeyboardAvoidanceHeight : MobileSoftKeyboardAvoidanceHeight;
            if (avoidance > 0 &&
                _runtimeSoftKeyboardAvoidanceSourceHeight > 0 &&
                _runtimeSoftKeyboardAvoidanceSourceHeight != height)
            {
                float scaleY = height / (float)_runtimeSoftKeyboardAvoidanceSourceHeight;
                avoidance = (int)Math.Round(avoidance * scaleY);
                avoidance = Math.Max(0, avoidance);
            }
            if (avoidance > 0 && CMain.SoftKeyboardVisible)
                bottom += avoidance;

            left = Math.Clamp(left, 0, Math.Max(0, width - 1));
            right = Math.Clamp(right, 0, Math.Max(0, width - 1 - left));
            top = Math.Clamp(top, 0, Math.Max(0, height - 1));
            bottom = Math.Clamp(bottom, 0, Math.Max(0, height - 1 - top));

            int safeWidth = Math.Max(1, width - left - right);
            int safeHeight = Math.Max(1, height - top - bottom);

            if (left + safeWidth > width)
                safeWidth = Math.Max(1, width - left);
            if (top + safeHeight > height)
                safeHeight = Math.Max(1, height - top);

            return new Rectangle(left, top, safeWidth, safeHeight);
        }

        public static bool FullScreen = true, Borderless = true, TopMost = true;
        public static string FontName = "Tahoma";
        public static float FontSize = 8F;
        public static bool UseMouseCursors = true;

        public static bool FPSCap = true;
        public static int MaxFPS = 60;
        public static int BackgroundMaxFPS = 15;
        public static int BackgroundNetworkTickMs = 1000;
        public static float MobileBackBufferScale = 1F;
        public static int MobileMaxTextureCreatesPerFrame = 12;
        public static int Resolution = 800;
        public static bool DebugMode = false;

        public static int SoundOverLap = 3;
        private static byte _volume = 100;
        public static byte Volume
        {
            get { return _volume; }
            set
            {
                if (_volume == value) return;

                _volume = (byte)(value > 100 ? 100 : value);

                if (_volume == 0)
                    SoundManager.Vol = -10000;
                else
                    SoundManager.Vol = (int)(-3000 + (3000 * (_volume / 100M)));
            }
        }

        private static byte _musicVolume = 100;
        public static byte MusicVolume
        {
            get { return _musicVolume; }
            set
            {
                if (_musicVolume == value) return;

                _musicVolume = (byte)(value > 100 ? 100 : value);

                if (_musicVolume == 0)
                    SoundManager.MusicVol = -10000;
                else
                    SoundManager.MusicVol = (int)(-3000 + (3000 * (_musicVolume / 100M)));
            }
        }

        public static string AccountID = "",
                             Password = "";
        public static bool RememberPassword = Environment.OSVersion.Platform == PlatformID.Win32NT;

        public static bool
            SkillMode = false,
            SkillBar = true,
            Effect = true,
            LevelEffect = true,
            DropView = true,
            NameView = true,
            HPView = true,
            TransparentChat = false,
            ModeView = false,
            DuraView = false,
            DisplayDamage = true,
            TargetDead = false,
            ExpandedBuffWindow = true;

        public static int[,] SkillbarLocation = new int[2, 2] { { 0, 0 }, { 216, 0 } };

        public static int[] TrackedQuests = new int[5];

        public static bool
            ShowNormalChat = true,
            ShowYellChat = true,
            ShowWhisperChat = true,
            ShowLoverChat = true,
            ShowMentorChat = true,
            ShowGroupChat = true,
            ShowGuildChat = true;

        public static bool
            FilterNormalChat = false,
            FilterWhisperChat = false,
            FilterShoutChat = false,
            FilterSystemChat = false,
            FilterLoverChat = false,
            FilterMentorChat = false,
            FilterGroupChat = false,
            FilterGuildChat = false;

        private static InIReader CreateReader()
        {
            return new InIReader(ConfigFilePath);
        }

        public static void ConfigureClientRoot(string absolutePath)
        {
            if (!string.IsNullOrWhiteSpace(absolutePath))
                P_Client = absolutePath;

            ClientResourceLayout.Configure(P_Client);
            Reader = CreateReader();
        }

        public static string ResolvePath(string relativeOrAbsolutePath)
        {
            return ClientResourceLayout.ResolvePath(relativeOrAbsolutePath);
        }

        public static string ResolveLibraryDirectory(string relativeOrAbsoluteDirectory)
        {
            return ClientResourceLayout.ResolveLibraryDirectory(relativeOrAbsoluteDirectory);
        }

        public static string ResolveLibraryFile(string relativeOrAbsoluteFilePath)
        {
            return ClientResourceLayout.ResolveLibraryFilePath(relativeOrAbsoluteFilePath);
        }

        public static string ResolveMapFile(string relativeOrAbsoluteFilePath)
        {
            return ClientResourceLayout.ResolveMapFilePath(relativeOrAbsoluteFilePath);
        }

        public static string ResolveSoundFile(string relativeOrAbsoluteFilePath)
        {
            return ClientResourceLayout.ResolveSoundFilePath(relativeOrAbsoluteFilePath);
        }

        public static string ResolveFontFile(string fileName = "hm.ttf")
        {
            return ClientResourceLayout.ResolveFontFilePath(fileName);
        }

        public static Stream OpenFontStream(string fileName = "hm.ttf")
        {
            string resolvedFontPath = ResolveFontFile(fileName);
            return ClientResourceLayout.OpenReadStream(
                resolvedFontPath,
                fileName,
                Path.Combine("Content", fileName),
                Path.Combine("BootstrapAssets", "Content", fileName),
                Path.Combine("Data", "Fonts", fileName),
                Path.Combine("BootstrapAssets", "Data", "Fonts", fileName));
        }

        public static void Load()
        {
            ClientResourceLayout.EnsureCoreBootstrapAssetsAvailable();
            Reader = CreateReader();
            GameLanguage.LoadClientLanguage(LanguageFilePath);

            FullScreen = Reader.ReadBoolean("Graphics", "FullScreen", FullScreen);
            Borderless = Reader.ReadBoolean("Graphics", "Borderless", Borderless);
            TopMost = Reader.ReadBoolean("Graphics", "AlwaysOnTop", TopMost);
            FPSCap = Reader.ReadBoolean("Graphics", "FPSCap", FPSCap);
            MaxFPS = Reader.ReadInt32("Graphics", "MaxFPS", MaxFPS);
            BackgroundMaxFPS = Reader.ReadInt32("Graphics", "BackgroundMaxFPS", BackgroundMaxFPS);
            MobileBackBufferScale = Reader.ReadSingle("Graphics", "MobileBackBufferScale", MobileBackBufferScale);
            MobileMaxTextureCreatesPerFrame = Reader.ReadInt32("Graphics", "MobileMaxTextureCreatesPerFrame", MobileMaxTextureCreatesPerFrame);
            BackgroundNetworkTickMs = Reader.ReadInt32("Network", "BackgroundNetworkTickMs", BackgroundNetworkTickMs);

            MaxFPS = Math.Clamp(MaxFPS, 15, 240);
            BackgroundMaxFPS = Math.Clamp(BackgroundMaxFPS, 5, 240);
            MobileBackBufferScale = Math.Clamp(MobileBackBufferScale, 0.25F, 1F);
            MobileMaxTextureCreatesPerFrame = Math.Clamp(MobileMaxTextureCreatesPerFrame, 1, 120);
            BackgroundNetworkTickMs = Math.Clamp(BackgroundNetworkTickMs, 50, 60000);
            Resolution = Reader.ReadInt32("Graphics", "Resolution", Resolution);
            DebugMode = Reader.ReadBoolean("Graphics", "DebugMode", DebugMode);
            UseMouseCursors = Reader.ReadBoolean("Graphics", "UseMouseCursors", UseMouseCursors);

            UIProfileId = Reader.ReadString("Interface", "UIProfileId", UIProfileId);
            MobileProfileInitialized = Reader.ReadBoolean("Interface", "MobileProfileInitialized", MobileProfileInitialized);

            MobileVirtualJoystickEnabled = Reader.ReadBoolean("Mobile", "VirtualJoystickEnabled", MobileVirtualJoystickEnabled);
            MobileVirtualJoystickFollowMode = Reader.ReadBoolean("Mobile", "VirtualJoystickFollowMode", MobileVirtualJoystickFollowMode);
            MobileActionPanelEnabled = Reader.ReadBoolean("Mobile", "ActionPanelEnabled", MobileActionPanelEnabled);
            MobileQuickBarEnabled = Reader.ReadBoolean("Mobile", "QuickBarEnabled", MobileQuickBarEnabled);
            MobileBeltBarEnabled = Reader.ReadBoolean("Mobile", "BeltBarEnabled", MobileBeltBarEnabled);
            MobileSoftKeyboardAvoidanceHeight = Reader.ReadInt32("Mobile", "SoftKeyboardAvoidanceHeight", MobileSoftKeyboardAvoidanceHeight);
            MobileTouchLongPressThresholdMs = Reader.ReadInt32("Mobile", "TouchLongPressThresholdMs", MobileTouchLongPressThresholdMs);
            MobileTouchTapMoveTolerancePixels = Reader.ReadSingle("Mobile", "TouchTapMoveTolerancePixels", MobileTouchTapMoveTolerancePixels);
            MobileTwoFingerTapMaxDurationMs = Reader.ReadInt32("Mobile", "TwoFingerTapMaxDurationMs", MobileTwoFingerTapMaxDurationMs);
            MobileTwoFingerTapMoveTolerancePixels = Reader.ReadSingle("Mobile", "TwoFingerTapMoveTolerancePixels", MobileTwoFingerTapMoveTolerancePixels);
            MobilePinchStartDistanceThresholdPixels = Reader.ReadSingle("Mobile", "PinchStartDistanceThresholdPixels", MobilePinchStartDistanceThresholdPixels);
            MobileJoystickDeadzone = Reader.ReadSingle("Mobile", "JoystickDeadzone", MobileJoystickDeadzone);
            MobileJoystickRunThreshold = Reader.ReadSingle("Mobile", "JoystickRunThreshold", MobileJoystickRunThreshold);
            MobileJoystickActivationWidthRatio = Reader.ReadSingle("Mobile", "JoystickActivationWidthRatio", MobileJoystickActivationWidthRatio);
            MobileJoystickActivationHeightRatio = Reader.ReadSingle("Mobile", "JoystickActivationHeightRatio", MobileJoystickActivationHeightRatio);
            MobileJoystickRadiusRatio = Reader.ReadSingle("Mobile", "JoystickRadiusRatio", MobileJoystickRadiusRatio);
            MobileJoystickRadiusMin = Reader.ReadSingle("Mobile", "JoystickRadiusMin", MobileJoystickRadiusMin);
            MobileJoystickFixedMarginRatio = Reader.ReadSingle("Mobile", "JoystickFixedMarginRatio", MobileJoystickFixedMarginRatio);
            MobileJoystickFixedMarginMin = Reader.ReadSingle("Mobile", "JoystickFixedMarginMin", MobileJoystickFixedMarginMin);
            MobileMiniMapEnabled = Reader.ReadBoolean("Mobile", "MiniMapEnabled", MobileMiniMapEnabled);
            MobileMiniMapZoom = Reader.ReadSingle("Mobile", "MiniMapZoom", MobileMiniMapZoom);
            MobileMiniMapZoomMin = Reader.ReadSingle("Mobile", "MiniMapZoomMin", MobileMiniMapZoomMin);
            MobileMiniMapZoomMax = Reader.ReadSingle("Mobile", "MiniMapZoomMax", MobileMiniMapZoomMax);
            MobileMiniMapPinchZoomPerPixel = Reader.ReadSingle("Mobile", "MiniMapPinchZoomPerPixel", MobileMiniMapPinchZoomPerPixel);

            MobileMapScaleMin = Reader.ReadSingle("Mobile", "MapScaleMin", MobileMapScaleMin);
            MobileMapScaleMax = Reader.ReadSingle("Mobile", "MapScaleMax", MobileMapScaleMax);
            MobilePinchScalePerPixel = Reader.ReadSingle("Mobile", "PinchScalePerPixel", MobilePinchScalePerPixel);
            MobilePinchDeadzonePixels = Reader.ReadSingle("Mobile", "PinchDeadzonePixels", MobilePinchDeadzonePixels);
            MobilePinchDeltaSmoothing = Reader.ReadSingle("Mobile", "PinchDeltaSmoothing", MobilePinchDeltaSmoothing);

            MobileSafeAreaLeft = Reader.ReadInt32("Mobile", "SafeAreaLeft", MobileSafeAreaLeft);
            MobileSafeAreaTop = Reader.ReadInt32("Mobile", "SafeAreaTop", MobileSafeAreaTop);
            MobileSafeAreaRight = Reader.ReadInt32("Mobile", "SafeAreaRight", MobileSafeAreaRight);
            MobileSafeAreaBottom = Reader.ReadInt32("Mobile", "SafeAreaBottom", MobileSafeAreaBottom);

            MobileTouchLongPressThresholdMs = Math.Clamp(MobileTouchLongPressThresholdMs, 100, 2000);
            MobileTouchTapMoveTolerancePixels = Math.Clamp(MobileTouchTapMoveTolerancePixels, 0F, 500F);
            MobileTwoFingerTapMaxDurationMs = Math.Clamp(MobileTwoFingerTapMaxDurationMs, 50, 1500);
            MobileTwoFingerTapMoveTolerancePixels = Math.Clamp(MobileTwoFingerTapMoveTolerancePixels, 0F, 500F);
            MobilePinchStartDistanceThresholdPixels = Math.Clamp(MobilePinchStartDistanceThresholdPixels, 0F, 500F);

            MobileJoystickDeadzone = Math.Clamp(MobileJoystickDeadzone, 0F, 0.95F);
            MobileJoystickRunThreshold = Math.Clamp(MobileJoystickRunThreshold, 0F, 1F);
            if (MobileJoystickRunThreshold < MobileJoystickDeadzone)
                MobileJoystickRunThreshold = MobileJoystickDeadzone;

            MobileJoystickActivationWidthRatio = Math.Clamp(MobileJoystickActivationWidthRatio, 0.1F, 1F);
            MobileJoystickActivationHeightRatio = Math.Clamp(MobileJoystickActivationHeightRatio, 0.1F, 1F);

            MobileJoystickRadiusRatio = Math.Clamp(MobileJoystickRadiusRatio, 0.02F, 0.5F);
            MobileJoystickRadiusMin = Math.Clamp(MobileJoystickRadiusMin, 16F, 1000F);

            MobileJoystickFixedMarginRatio = Math.Clamp(MobileJoystickFixedMarginRatio, 0.02F, 0.5F);
            MobileJoystickFixedMarginMin = Math.Clamp(MobileJoystickFixedMarginMin, 0F, 1000F);

            MobileMiniMapZoomMin = Math.Clamp(MobileMiniMapZoomMin, 0.25F, 8F);
            MobileMiniMapZoomMax = Math.Clamp(MobileMiniMapZoomMax, 0.25F, 8F);
            if (MobileMiniMapZoomMax < MobileMiniMapZoomMin)
                MobileMiniMapZoomMax = MobileMiniMapZoomMin;

            MobileMiniMapPinchZoomPerPixel = Math.Clamp(MobileMiniMapPinchZoomPerPixel, 0.0001F, 0.05F);
            MobileMiniMapZoom = Math.Clamp(MobileMiniMapZoom, MobileMiniMapZoomMin, MobileMiniMapZoomMax);

            MobileMapScaleMin = Math.Clamp(MobileMapScaleMin, 0.5F, 8F);
            MobileMapScaleMax = Math.Clamp(MobileMapScaleMax, 0.5F, 8F);
            if (MobileMapScaleMax < MobileMapScaleMin)
                MobileMapScaleMax = MobileMapScaleMin;

            MobilePinchScalePerPixel = Math.Clamp(MobilePinchScalePerPixel, 0.0001F, 0.05F);
            MobilePinchDeadzonePixels = Math.Clamp(MobilePinchDeadzonePixels, 0F, 100F);
            MobilePinchDeltaSmoothing = Math.Clamp(MobilePinchDeltaSmoothing, 0F, 0.95F);

            MobileSafeAreaLeft = Math.Max(0, MobileSafeAreaLeft);
            MobileSafeAreaTop = Math.Max(0, MobileSafeAreaTop);
            MobileSafeAreaRight = Math.Max(0, MobileSafeAreaRight);
            MobileSafeAreaBottom = Math.Max(0, MobileSafeAreaBottom);
            MobileSoftKeyboardAvoidanceHeight = Math.Max(0, MobileSoftKeyboardAvoidanceHeight);

            UseConfig = Reader.ReadBoolean("Network", "UseConfig", UseConfig);
            if (UseConfig)
            {
                IPAddress = Reader.ReadString("Network", "IPAddress", IPAddress);
                Port = Reader.ReadInt32("Network", "Port", Port);
            }

            BootstrapPackageRepo = Reader.ReadString("Bootstrap", "PackageRepo", BootstrapPackageRepo)?.Trim() ?? string.Empty;
            BootstrapAutoDownloadPackages = Reader.ReadBoolean("Bootstrap", "AutoDownload", BootstrapAutoDownloadPackages);
            BootstrapVerifyDownloadedPackages = Reader.ReadBoolean("Bootstrap", "VerifySha256", BootstrapVerifyDownloadedPackages);
            BootstrapDownloadRetryCount = Reader.ReadInt32("Bootstrap", "RetryCount", BootstrapDownloadRetryCount);
            BootstrapDownloadRetryCount = Math.Clamp(BootstrapDownloadRetryCount, 0, 20);

            MicroBaseUrl = Reader.ReadString("Micro", "BaseUrl", MicroBaseUrl)?.Trim() ?? string.Empty;
            MicroUser = Reader.ReadString("Micro", "User", MicroUser)?.Trim() ?? string.Empty;
            MicroCode = Reader.ReadString("Micro", "Code", MicroCode)?.Trim() ?? string.Empty;
#if REAL_ANDROID
            TryNormalizeAndroidEmulatorNetworking();
#endif

            LogErrors = Reader.ReadBoolean("Logs", "LogErrors", LogErrors);
            LogChat = Reader.ReadBoolean("Logs", "LogChat", LogChat);
            TracePackets = Reader.ReadBoolean("Logs", "TracePackets", TracePackets);

            SmokeTestAutoLogin = Reader.ReadBoolean("SmokeTest", "AutoLogin", SmokeTestAutoLogin);
            SmokeTestAutoCreateAccount = Reader.ReadBoolean("SmokeTest", "AutoCreateAccount", SmokeTestAutoCreateAccount);
            SmokeTestAutoCreateCharacter = Reader.ReadBoolean("SmokeTest", "AutoCreateCharacter", SmokeTestAutoCreateCharacter);
            SmokeTestAutoStartGame = Reader.ReadBoolean("SmokeTest", "AutoStartGame", SmokeTestAutoStartGame);
            SmokeTestCharacterName = Reader.ReadString("SmokeTest", "CharacterName", SmokeTestCharacterName)?.Trim() ?? string.Empty;

            Volume = Reader.ReadByte("Sound", "Volume", Volume);
            SoundOverLap = Reader.ReadInt32("Sound", "SoundOverLap", SoundOverLap);
            MusicVolume = Reader.ReadByte("Sound", "Music", MusicVolume);

            AccountID = Reader.ReadString("Game", "AccountID", AccountID);
            RememberPassword = Reader.ReadBoolean("Game", "RememberPassword", RememberPassword);
            Password = Reader.ReadString("Game", "Password", Password);
            if (!RememberPassword)
                Password = string.Empty;

            SkillMode = Reader.ReadBoolean("Game", "SkillMode", SkillMode);
            SkillBar = Reader.ReadBoolean("Game", "SkillBar", SkillBar);
            Effect = Reader.ReadBoolean("Game", "Effect", Effect);
            LevelEffect = Reader.ReadBoolean("Game", "LevelEffect", Effect);
            DropView = Reader.ReadBoolean("Game", "DropView", DropView);
            NameView = Reader.ReadBoolean("Game", "NameView", NameView);
            HPView = Reader.ReadBoolean("Game", "HPMPView", HPView);
            ModeView = Reader.ReadBoolean("Game", "ModeView", ModeView);
            FontName = Reader.ReadString("Game", "FontName", FontName);
            TransparentChat = Reader.ReadBoolean("Game", "TransparentChat", TransparentChat);
            DisplayDamage = Reader.ReadBoolean("Game", "DisplayDamage", DisplayDamage);
            TargetDead = Reader.ReadBoolean("Game", "TargetDead", TargetDead);
            ExpandedBuffWindow = Reader.ReadBoolean("Game", "ExpandedBuffWindow", ExpandedBuffWindow);
            DuraView = Reader.ReadBoolean("Game", "DuraWindow", DuraView);

            for (int i = 0; i < SkillbarLocation.Length / 2; i++)
            {
                SkillbarLocation[i, 0] = Reader.ReadInt32("Game", "Skillbar" + i.ToString() + "X", SkillbarLocation[i, 0]);
                SkillbarLocation[i, 1] = Reader.ReadInt32("Game", "Skillbar" + i.ToString() + "Y", SkillbarLocation[i, 1]);
            }

            ShowNormalChat = Reader.ReadBoolean("Chat", "ShowNormalChat", ShowNormalChat);
            ShowYellChat = Reader.ReadBoolean("Chat", "ShowYellChat", ShowYellChat);
            ShowWhisperChat = Reader.ReadBoolean("Chat", "ShowWhisperChat", ShowWhisperChat);
            ShowLoverChat = Reader.ReadBoolean("Chat", "ShowLoverChat", ShowLoverChat);
            ShowMentorChat = Reader.ReadBoolean("Chat", "ShowMentorChat", ShowMentorChat);
            ShowGroupChat = Reader.ReadBoolean("Chat", "ShowGroupChat", ShowGroupChat);
            ShowGuildChat = Reader.ReadBoolean("Chat", "ShowGuildChat", ShowGuildChat);

            FilterNormalChat = Reader.ReadBoolean("Filter", "FilterNormalChat", FilterNormalChat);
            FilterWhisperChat = Reader.ReadBoolean("Filter", "FilterWhisperChat", FilterWhisperChat);
            FilterShoutChat = Reader.ReadBoolean("Filter", "FilterShoutChat", FilterShoutChat);
            FilterSystemChat = Reader.ReadBoolean("Filter", "FilterSystemChat", FilterSystemChat);
            FilterLoverChat = Reader.ReadBoolean("Filter", "FilterLoverChat", FilterLoverChat);
            FilterMentorChat = Reader.ReadBoolean("Filter", "FilterMentorChat", FilterMentorChat);
            FilterGroupChat = Reader.ReadBoolean("Filter", "FilterGroupChat", FilterGroupChat);
            FilterGuildChat = Reader.ReadBoolean("Filter", "FilterGuildChat", FilterGuildChat);

            P_Patcher = Reader.ReadBoolean("Launcher", "Enabled", P_Patcher);
            P_Host = Reader.ReadString("Launcher", "Host", P_Host);
            P_PatchFileName = Reader.ReadString("Launcher", "PatchFile", P_PatchFileName);
            P_NeedLogin = Reader.ReadBoolean("Launcher", "NeedLogin", P_NeedLogin);
            P_Login = Reader.ReadString("Launcher", "Login", P_Login);
            P_Password = Reader.ReadString("Launcher", "Password", P_Password);
            P_AutoStart = Reader.ReadBoolean("Launcher", "AutoStart", P_AutoStart);
            P_ServerName = Reader.ReadString("Launcher", "ServerName", P_ServerName);
            P_BrowserAddress = Reader.ReadString("Launcher", "Browser", P_BrowserAddress);

            if (!P_Host.EndsWith("/")) P_Host += "/";
            if (P_Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) P_Host = P_Host.Insert(0, "http://");
            if (P_BrowserAddress.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) P_BrowserAddress = P_BrowserAddress.Insert(0, "http://");

            if (P_Host.ToLower() == "http://mirfiles.co.uk/mir2/cmir/patch/")
            {
                P_Host = "http://mirfiles.com/mir2/cmir/patch/";
            }

            if (ApplyMobileDefaultProfileIfNeeded())
                Save();
        }

        public static void StartBootstrapWarmup()
        {
            ClientResourceLayout.StartBootstrapWarmup();
        }

        public static void Save()
        {
            Reader.Write("Interface", "UIProfileId", UIProfileId);
            Reader.Write("Interface", "MobileProfileInitialized", MobileProfileInitialized);

            Reader.Write("Logs", "LogErrors", LogErrors);
            Reader.Write("Logs", "LogChat", LogChat);
            Reader.Write("Logs", "TracePackets", TracePackets);

            Reader.Write("SmokeTest", "AutoLogin", SmokeTestAutoLogin);
            Reader.Write("SmokeTest", "AutoCreateAccount", SmokeTestAutoCreateAccount);
            Reader.Write("SmokeTest", "AutoCreateCharacter", SmokeTestAutoCreateCharacter);
            Reader.Write("SmokeTest", "AutoStartGame", SmokeTestAutoStartGame);
            Reader.Write("SmokeTest", "CharacterName", SmokeTestCharacterName ?? string.Empty);

            Reader.Write("Mobile", "VirtualJoystickEnabled", MobileVirtualJoystickEnabled);
            Reader.Write("Mobile", "VirtualJoystickFollowMode", MobileVirtualJoystickFollowMode);
            Reader.Write("Mobile", "ActionPanelEnabled", MobileActionPanelEnabled);
            Reader.Write("Mobile", "QuickBarEnabled", MobileQuickBarEnabled);
            Reader.Write("Mobile", "BeltBarEnabled", MobileBeltBarEnabled);
            Reader.Write("Mobile", "SoftKeyboardAvoidanceHeight", MobileSoftKeyboardAvoidanceHeight);
            Reader.Write("Mobile", "TouchLongPressThresholdMs", MobileTouchLongPressThresholdMs);
            Reader.Write("Mobile", "TouchTapMoveTolerancePixels", MobileTouchTapMoveTolerancePixels);
            Reader.Write("Mobile", "TwoFingerTapMaxDurationMs", MobileTwoFingerTapMaxDurationMs);
            Reader.Write("Mobile", "TwoFingerTapMoveTolerancePixels", MobileTwoFingerTapMoveTolerancePixels);
            Reader.Write("Mobile", "PinchStartDistanceThresholdPixels", MobilePinchStartDistanceThresholdPixels);
            Reader.Write("Mobile", "JoystickDeadzone", MobileJoystickDeadzone);
            Reader.Write("Mobile", "JoystickRunThreshold", MobileJoystickRunThreshold);
            Reader.Write("Mobile", "JoystickActivationWidthRatio", MobileJoystickActivationWidthRatio);
            Reader.Write("Mobile", "JoystickActivationHeightRatio", MobileJoystickActivationHeightRatio);
            Reader.Write("Mobile", "JoystickRadiusRatio", MobileJoystickRadiusRatio);
            Reader.Write("Mobile", "JoystickRadiusMin", MobileJoystickRadiusMin);
            Reader.Write("Mobile", "JoystickFixedMarginRatio", MobileJoystickFixedMarginRatio);
            Reader.Write("Mobile", "JoystickFixedMarginMin", MobileJoystickFixedMarginMin);
            Reader.Write("Mobile", "MiniMapEnabled", MobileMiniMapEnabled);
            Reader.Write("Mobile", "MiniMapZoom", MobileMiniMapZoom);
            Reader.Write("Mobile", "MiniMapZoomMin", MobileMiniMapZoomMin);
            Reader.Write("Mobile", "MiniMapZoomMax", MobileMiniMapZoomMax);
            Reader.Write("Mobile", "MiniMapPinchZoomPerPixel", MobileMiniMapPinchZoomPerPixel);

            Reader.Write("Mobile", "MapScaleMin", MobileMapScaleMin);
            Reader.Write("Mobile", "MapScaleMax", MobileMapScaleMax);
            Reader.Write("Mobile", "PinchScalePerPixel", MobilePinchScalePerPixel);
            Reader.Write("Mobile", "PinchDeadzonePixels", MobilePinchDeadzonePixels);
            Reader.Write("Mobile", "PinchDeltaSmoothing", MobilePinchDeltaSmoothing);

            Reader.Write("Mobile", "SafeAreaLeft", MobileSafeAreaLeft);
            Reader.Write("Mobile", "SafeAreaTop", MobileSafeAreaTop);
            Reader.Write("Mobile", "SafeAreaRight", MobileSafeAreaRight);
            Reader.Write("Mobile", "SafeAreaBottom", MobileSafeAreaBottom);

            Reader.Write("Graphics", "FullScreen", FullScreen);
            Reader.Write("Graphics", "Borderless", Borderless);
            Reader.Write("Graphics", "AlwaysOnTop", TopMost);
            Reader.Write("Graphics", "FPSCap", FPSCap);
            Reader.Write("Graphics", "MaxFPS", MaxFPS);
            Reader.Write("Graphics", "BackgroundMaxFPS", BackgroundMaxFPS);
            Reader.Write("Graphics", "MobileBackBufferScale", MobileBackBufferScale);
            Reader.Write("Graphics", "MobileMaxTextureCreatesPerFrame", MobileMaxTextureCreatesPerFrame);
            Reader.Write("Graphics", "Resolution", Resolution);
            Reader.Write("Graphics", "DebugMode", DebugMode);
            Reader.Write("Graphics", "UseMouseCursors", UseMouseCursors);

            Reader.Write("Network", "UseConfig", UseConfig);
            Reader.Write("Network", "IPAddress", IPAddress ?? string.Empty);
            Reader.Write("Network", "Port", Port);
            Reader.Write("Network", "BackgroundNetworkTickMs", BackgroundNetworkTickMs);

            Reader.Write("Bootstrap", "PackageRepo", BootstrapPackageRepo ?? string.Empty);
            Reader.Write("Bootstrap", "AutoDownload", BootstrapAutoDownloadPackages);
            Reader.Write("Bootstrap", "VerifySha256", BootstrapVerifyDownloadedPackages);
            Reader.Write("Bootstrap", "RetryCount", BootstrapDownloadRetryCount);

            Reader.Write("Micro", "BaseUrl", MicroBaseUrl ?? string.Empty);
            Reader.Write("Micro", "User", MicroUser ?? string.Empty);
            Reader.Write("Micro", "Code", MicroCode ?? string.Empty);

            Reader.Write("Sound", "Volume", Volume);
            Reader.Write("Sound", "Music", MusicVolume);
            Reader.Write("Sound", "SoundOverLap", SoundOverLap);

            Reader.Write("Game", "AccountID", AccountID);
            Reader.Write("Game", "RememberPassword", RememberPassword);
            Reader.Write("Game", "Password", RememberPassword ? Password : string.Empty);
            Reader.Write("Game", "SkillMode", SkillMode);
            Reader.Write("Game", "SkillBar", SkillBar);
            Reader.Write("Game", "Effect", Effect);
            Reader.Write("Game", "LevelEffect", LevelEffect);
            Reader.Write("Game", "DropView", DropView);
            Reader.Write("Game", "NameView", NameView);
            Reader.Write("Game", "HPMPView", HPView);
            Reader.Write("Game", "ModeView", ModeView);
            Reader.Write("Game", "FontName", FontName);
            Reader.Write("Game", "TransparentChat", TransparentChat);
            Reader.Write("Game", "DisplayDamage", DisplayDamage);
            Reader.Write("Game", "TargetDead", TargetDead);
            Reader.Write("Game", "ExpandedBuffWindow", ExpandedBuffWindow);
            Reader.Write("Game", "DuraWindow", DuraView);

            for (int i = 0; i < SkillbarLocation.Length / 2; i++)
            {
                Reader.Write("Game", "Skillbar" + i.ToString() + "X", SkillbarLocation[i, 0]);
                Reader.Write("Game", "Skillbar" + i.ToString() + "Y", SkillbarLocation[i, 1]);
            }

            Reader.Write("Chat", "ShowNormalChat", ShowNormalChat);
            Reader.Write("Chat", "ShowYellChat", ShowYellChat);
            Reader.Write("Chat", "ShowWhisperChat", ShowWhisperChat);
            Reader.Write("Chat", "ShowLoverChat", ShowLoverChat);
            Reader.Write("Chat", "ShowMentorChat", ShowMentorChat);
            Reader.Write("Chat", "ShowGroupChat", ShowGroupChat);
            Reader.Write("Chat", "ShowGuildChat", ShowGuildChat);

            Reader.Write("Filter", "FilterNormalChat", FilterNormalChat);
            Reader.Write("Filter", "FilterWhisperChat", FilterWhisperChat);
            Reader.Write("Filter", "FilterShoutChat", FilterShoutChat);
            Reader.Write("Filter", "FilterSystemChat", FilterSystemChat);
            Reader.Write("Filter", "FilterLoverChat", FilterLoverChat);
            Reader.Write("Filter", "FilterMentorChat", FilterMentorChat);
            Reader.Write("Filter", "FilterGroupChat", FilterGroupChat);
            Reader.Write("Filter", "FilterGuildChat", FilterGuildChat);

            Reader.Write("Launcher", "Enabled", P_Patcher);
            Reader.Write("Launcher", "Host", P_Host);
            Reader.Write("Launcher", "PatchFile", P_PatchFileName);
            Reader.Write("Launcher", "NeedLogin", P_NeedLogin);
            Reader.Write("Launcher", "Login", P_Login);
            Reader.Write("Launcher", "Password", P_Password);
            Reader.Write("Launcher", "ServerName", P_ServerName);
            Reader.Write("Launcher", "Browser", P_BrowserAddress);
            Reader.Write("Launcher", "AutoStart", P_AutoStart);
        }

#if REAL_ANDROID
        private static void TryNormalizeAndroidEmulatorNetworking()
        {
            try
            {
                if (!IsProbablyAndroidEmulator())
                    return;

                string oldMicroBaseUrl = MicroBaseUrl ?? string.Empty;
                string newMicroBaseUrl = RewriteMicroBaseUrlForAndroidEmulator(oldMicroBaseUrl);
                if (!string.Equals(oldMicroBaseUrl, newMicroBaseUrl, StringComparison.Ordinal))
                {
                    MicroBaseUrl = newMicroBaseUrl;
                    try { CMain.SaveLog($"AndroidEmulator: Micro.BaseUrl 自动重写：{oldMicroBaseUrl} -> {newMicroBaseUrl}"); } catch { }
                }

                string oldIp = IPAddress ?? string.Empty;
                string newIp = RewriteLocalhostIpForAndroidEmulator(oldIp);
                if (!string.Equals(oldIp, newIp, StringComparison.Ordinal))
                {
                    IPAddress = newIp;
                    try { CMain.SaveLog($"AndroidEmulator: Network.IPAddress 自动重写：{oldIp} -> {newIp}"); } catch { }
                }
            }
            catch
            {
            }
        }

        private static string RewriteLocalhostIpForAndroidEmulator(string hostOrIp)
        {
            string value = (hostOrIp ?? string.Empty).Trim();
            if (string.Equals(value, "localhost", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "127.0.0.1", StringComparison.OrdinalIgnoreCase))
                return "10.0.2.2";

            return value;
        }

        private static string RewriteMicroBaseUrlForAndroidEmulator(string microBaseUrl)
        {
            string value = (microBaseUrl ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(value))
                return value;

            // 兼容旧占位配置（历史默认值/模板值）：在 Emulator 下自动切换到宿主机别名 10.0.2.2
            if (string.Equals(value, "http://192.168.0.100:7777/api/", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "http://192.168.0.100:7777/api", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "https://192.168.0.100:7777/api/", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "https://192.168.0.100:7777/api", StringComparison.OrdinalIgnoreCase))
            {
                return "http://10.0.2.2:7777/api/";
            }

            if (!Uri.TryCreate(value, UriKind.Absolute, out Uri uri))
                return value;

            string host = uri.Host ?? string.Empty;
            if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(host, "127.0.0.1", StringComparison.OrdinalIgnoreCase))
            {
                var builder = new UriBuilder(uri) { Host = "10.0.2.2" };
                string rewritten = builder.Uri.ToString();
                if (value.EndsWith("/", StringComparison.Ordinal) && !rewritten.EndsWith("/", StringComparison.Ordinal))
                    rewritten += "/";
                return rewritten;
            }

            return value;
        }

        private static bool IsProbablyAndroidEmulator()
        {
            try
            {
                string fingerprint = global::Android.OS.Build.Fingerprint ?? string.Empty;
                string model = global::Android.OS.Build.Model ?? string.Empty;
                string manufacturer = global::Android.OS.Build.Manufacturer ?? string.Empty;
                string brand = global::Android.OS.Build.Brand ?? string.Empty;
                string device = global::Android.OS.Build.Device ?? string.Empty;
                string product = global::Android.OS.Build.Product ?? string.Empty;
                string hardware = global::Android.OS.Build.Hardware ?? string.Empty;

                if (fingerprint.StartsWith("generic", StringComparison.OrdinalIgnoreCase) ||
                    fingerprint.StartsWith("unknown", StringComparison.OrdinalIgnoreCase))
                    return true;

                if (model.Contains("google_sdk", StringComparison.OrdinalIgnoreCase) ||
                    model.Contains("Emulator", StringComparison.OrdinalIgnoreCase) ||
                    model.Contains("Android SDK built for", StringComparison.OrdinalIgnoreCase))
                    return true;

                if (manufacturer.Contains("Genymotion", StringComparison.OrdinalIgnoreCase))
                    return true;

                if (brand.StartsWith("generic", StringComparison.OrdinalIgnoreCase) &&
                    device.StartsWith("generic", StringComparison.OrdinalIgnoreCase))
                    return true;

                if (product.Contains("sdk", StringComparison.OrdinalIgnoreCase) ||
                    product.Contains("emulator", StringComparison.OrdinalIgnoreCase) ||
                    product.Contains("simulator", StringComparison.OrdinalIgnoreCase))
                    return true;

                if (hardware.Contains("goldfish", StringComparison.OrdinalIgnoreCase) ||
                    hardware.Contains("ranchu", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            catch
            {
            }

            return false;
        }
#endif

        private static bool ApplyMobileDefaultProfileIfNeeded()
        {
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                return false;

            if (MobileProfileInitialized)
                return false;

            UIProfileId = "Mobile";
            MobileVirtualJoystickEnabled = true;
            MobileActionPanelEnabled = true;
            MobileMiniMapEnabled = true;
            MobileQuickBarEnabled = true;
            MobileBeltBarEnabled = true;
            MobileProfileInitialized = true;
            return true;
        }

        public static void LoadTrackedQuests(string Charname)
        {
            for (int i = 0; i < TrackedQuests.Length; i++)
            {
                TrackedQuests[i] = Reader.ReadInt32("Q-" + Charname, "Quest-" + i.ToString(), -1);
            }
        }

        public static void SaveTrackedQuests(string Charname)
        {
            for (int i = 0; i < TrackedQuests.Length; i++)
            {
                Reader.Write("Q-" + Charname, "Quest-" + i.ToString(), TrackedQuests[i]);
            }
        }
    }
}
