using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Input.Touch;
using MonoShare.MirControls;
using MonoShare.MirGraphics;
using MonoShare.MirNetwork;
using MonoShare.MirScenes;
using MonoShare.MirSounds;
using MonoShare.Share.Extensions;
using MonoShare.UI;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Threading;

namespace MonoShare
{
 	    public class CMain : Game
 	    {
        public static CMain Main;
        public static GraphicsDeviceManager graphics;
        public static System.Drawing.Point MPoint;
        public readonly static Stopwatch Timer = Stopwatch.StartNew();
        public readonly static DateTime StartTime = DateTime.Now;
        public static long Time, OldTime;
        public static DateTime Now { get { return StartTime.AddMilliseconds(Time); } }
        public static readonly Random Random = new Random();

        public static string DebugText = "";

        private static long _fpsTime;
        private static int _fps;
        public static int FPS;
        private static long _nextSuspendedNetworkProcessTime;
        private static long _nextMobileAsyncResourcePollTime;

        public static long PingTime;
        public static long NextPing = 10000;

        public static bool Shift, Alt, Ctrl, Tilde;
        public static double BytesSent, BytesReceived;
        public static SpriteBatch spriteBatch;
        public static SpriteBatchStack SpriteBatchScope;
        public static FontSystem fontSystem;
        public static bool RuntimeSuspended;

        private static DynamicSpriteFont _debugFont;
        private static int _debugLastSpriteBatchBeginCalls;
        private static int _debugLastSpriteBatchEndCalls;
        private static int _debugLastSpriteBatchStateChanges;

        private bool _mobileBackBufferScaleApplied;
        private bool _mobileBackPressedLastFrame;

        public static MouseState currentMouseState;
        public static MouseState previousMouseState;
        private static Point _lastPointerPosition;
        private static int? _joystickTouchId;
        private static int? _pointerTouchId;
        private static Vector2 _joystickOrigin;
        private static Vector2 _joystickVector;
        private static float _joystickMagnitude;
        private static bool _joystickForceWalk;
        private static bool _joystickForceRun;
        private static float _joystickRadiusOverride;
        private static bool _isPinching;
        private static float _previousPinchDistance;
        private static long _pointerTouchDownAtMs;
        private static Vector2 _pointerTouchDownPosition;
        private static bool _pointerTapCandidate;
        private static bool _pointerLongPressTriggered;
        private static bool _pointerTouchStartedOverHud;
        private static bool _twoFingerTapCandidateActive;
        private static int _twoFingerTapId1;
        private static int _twoFingerTapId2;
        private static long _twoFingerTapStartAtMs;
        private static Vector2 _twoFingerTapInitialCenter;
        private static Vector2 _twoFingerTapLastCenter;
        private static float _twoFingerTapInitialDistance;
        private static float _twoFingerTapMaxCenterDrift;
        private static bool _twoFingerTapCanceled;
        private static MirScene _lastSceneForTouchReset;

        public static bool PointerTouchActive { get; private set; }
        public static bool PointerTouchStarted { get; private set; }
        public static bool PointerTouchEnded { get; private set; }
        public static Vector2 PointerTouchPosition { get; private set; }
        public static Vector2 PointerTouchDelta { get; private set; }

        public static void CancelPointerTapCandidate()
        {
            _pointerTapCandidate = false;
        }

        private static void ResetTouchInputState()
        {
            _joystickTouchId = null;
            _pointerTouchId = null;
            _joystickOrigin = Vector2.Zero;
            _joystickVector = Vector2.Zero;
            _joystickMagnitude = 0F;
            _joystickForceWalk = false;
            _joystickForceRun = false;
            _joystickRadiusOverride = 0F;

            _isPinching = false;
            _previousPinchDistance = 0F;
            PinchDistanceDelta = 0F;
            PinchCenter = Vector2.Zero;

            _pointerTouchDownAtMs = 0;
            _pointerTouchDownPosition = Vector2.Zero;
            _pointerTapCandidate = false;
            _pointerLongPressTriggered = false;
            _pointerTouchStartedOverHud = false;

            ResetTwoFingerTapCandidate();

            PointerTouchActive = false;
            PointerTouchStarted = false;
            PointerTouchEnded = false;
            PointerTouchDelta = Vector2.Zero;

            MirControl.MouseControl = null;
            MirControl.ActiveControl = null;

            RequestSoftKeyboard(false);

            Point position = _lastPointerPosition;
            var clearedMouseState = new MouseState(
                position.X,
                position.Y,
                0,
                ButtonState.Released,
                ButtonState.Released,
                ButtonState.Released,
                ButtonState.Released,
                ButtonState.Released);

            previousMouseState = clearedMouseState;
            currentMouseState = clearedMouseState;
        }

	        private static readonly object _textInputLock = new object();
	        private static readonly Queue<char> _textInputQueue = new Queue<char>();
	        private static readonly object _errorLogLock = new object();
	        private static readonly object _exceptionLogGate = new object();
	        private static bool _exceptionLogInstalled;
	        private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            private const int MobileRuntimeLogMaxQueuedLines = 2500;
            private const int MobileErrorLogMaxQueuedLines = 1000;
            private const int MobileLogMaxLinesPerFlush = 200;

            private static readonly object _mobileLogWriterGate = new object();
            private static bool _mobileLogWriterStarted;
            private static readonly AutoResetEvent _mobileLogWakeup = new AutoResetEvent(false);
            private static readonly ConcurrentQueue<string> _mobileRuntimeLogQueue = new ConcurrentQueue<string>();
            private static readonly ConcurrentQueue<string> _mobileErrorLogQueue = new ConcurrentQueue<string>();
            private static int _mobileRuntimeLogQueueCount;
            private static int _mobileErrorLogQueueCount;

        public static event Action<bool> SoftKeyboardVisibilityRequested;
        public static bool SoftKeyboardVisible { get; private set; }

#if ANDROID || IOS
        private static Task<string> _softKeyboardTask;
        private static MirTextBox _softKeyboardTarget;
        private static FairyGUI.InputTextField _softKeyboardFairyTarget;

        internal static bool SoftKeyboardOwnedByFairyGui => _softKeyboardFairyTarget != null;
#endif
 
        public static void RequestSoftKeyboard(bool visible)
        {
            SoftKeyboardVisible = visible;
#if ANDROID || IOS
            if (visible)
                TryBeginSoftKeyboardInputForActiveTextBox();
            else
                TryCancelSoftKeyboardInput();
#endif
            SoftKeyboardVisibilityRequested?.Invoke(SoftKeyboardVisible);
        }

#if ANDROID || IOS
        private static void TryBeginSoftKeyboardInputForActiveTextBox()
        {
            if (_softKeyboardTask != null)
                return;

            MirTextBox target = MirTextBox.ActiveTextBox;
            FairyGUI.InputTextField fairyTarget = null;

            if (target == null)
            {
                fairyTarget = FairyGUI.Stage.inst?.focus as FairyGUI.InputTextField;
                if (fairyTarget == null)
                {
                    SoftKeyboardVisible = false;
                    return;
                }
            }

            _softKeyboardTarget = target;
            _softKeyboardFairyTarget = fairyTarget;

            string title = target != null ? target.SoftKeyboardTitle : "输入";
            if (string.IsNullOrWhiteSpace(title))
                title = "输入";

            string description = target != null ? (target.SoftKeyboardDescription ?? string.Empty) : string.Empty;

            bool usePasswordMode = target != null
                ? target.Password
                : fairyTarget.displayAsPassword || fairyTarget.hideInput;

            string defaultText = target != null
                ? (target.Password ? string.Empty : (target.Text ?? string.Empty))
                : (usePasswordMode ? string.Empty : (fairyTarget.text ?? string.Empty));

            try
            {
                SoftKeyboardVisible = true;
                _softKeyboardTask = Microsoft.Xna.Framework.Input.KeyboardInput.Show(
                    title: title,
                    description: description,
                    defaultText: defaultText,
                    usePasswordMode: usePasswordMode);
            }
            catch
            {
                _softKeyboardTask = null;
                _softKeyboardTarget = null;
                _softKeyboardFairyTarget = null;
                SoftKeyboardVisible = false;
            }
        }

        private static void TryCancelSoftKeyboardInput()
        {
            if (_softKeyboardTask == null)
                return;

            try
            {
                string fallback = _softKeyboardTarget?.Text ?? _softKeyboardFairyTarget?.text ?? string.Empty;
                Microsoft.Xna.Framework.Input.KeyboardInput.Cancel(fallback);
            }
            catch
            {
            }
            finally
            {
                _softKeyboardTask = null;
                _softKeyboardTarget = null;
                _softKeyboardFairyTarget = null;
                SoftKeyboardVisible = false;
            }
        }

        private static void PumpSoftKeyboardInput()
        {
            if (_softKeyboardTask == null)
                return;

            if (!_softKeyboardTask.IsCompleted)
                return;

            string result = null;
            try
            {
                result = _softKeyboardTask.Result;
            }
            catch
            {
            }

            MirTextBox target = _softKeyboardTarget;
            FairyGUI.InputTextField fairyTarget = _softKeyboardFairyTarget;
            _softKeyboardTask = null;
            _softKeyboardTarget = null;
            _softKeyboardFairyTarget = null;
            SoftKeyboardVisible = false;

            if (result == null)
                return;

            if (target != null)
            {
                if (target.IsDisposed)
                    return;

                target.Text = result;

                try
                {
                    target.MoveCaretToEnd();
                    if (ReferenceEquals(MirTextBox.ActiveTextBox, target))
                        target.NotifySoftKeyboardSubmitted();
                }
                catch
                {
                }

                return;
            }

            if (fairyTarget == null)
                return;

            if (fairyTarget.isDisposed)
                return;

            try
            {
                fairyTarget.text = result;
            }
            catch
            {
                return;
            }

            try
            {
                fairyTarget.caretPosition = fairyTarget.text?.Length ?? 0;
            }
            catch
            {
            }

            try
            {
                if (fairyTarget.textField != null && fairyTarget.textField.singleLine)
                    fairyTarget.DispatchEvent("onSubmit", null);
            }
            catch
            {
            }

            try
            {
                if (FairyGUI.Stage.inst != null && ReferenceEquals(FairyGUI.Stage.inst.focus, fairyTarget))
                    FairyGUI.Stage.inst.focus = null;
            }
            catch
            {
            }
        }
#endif

        public static bool VirtualJoystickEnabled { get; private set; } = Environment.OSVersion.Platform != PlatformID.Win32NT;
        public static bool JoystickFollowMode { get; set; }
        public static bool JoystickTouchActive => _joystickTouchId.HasValue;
        public static bool JoystickForceRunActive => _joystickForceRun;
        public static bool JoystickForceWalkActive => _joystickForceWalk;
        public static Vector2 JoystickOrigin => _joystickOrigin;
        public static Vector2 JoystickVector => _joystickVector;
        public static float JoystickMagnitude => _joystickMagnitude;
        public static bool IsPinching => _isPinching;
        public static float PinchDistanceDelta { get; private set; }
        public static Vector2 PinchCenter { get; private set; }

        public static bool TryConsumePinch(System.Drawing.Rectangle bounds, out float delta)
        {
            delta = 0F;

            if (!_isPinching)
                return false;

            var center = new System.Drawing.Point((int)Math.Round(PinchCenter.X), (int)Math.Round(PinchCenter.Y));
            if (!bounds.Contains(center))
                return false;

            delta = PinchDistanceDelta;
            PinchDistanceDelta = 0F;
            return true;
        }



        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int count);


 	        public CMain(string absolutePath)
 	        {
 	            Settings.ConfigureClientRoot(absolutePath);

 	            try
 	            {
 	                ClientResourceLayout.EnsureWritableResourceDirectories();
 	            }
 	            catch
 	            {
 	            }

 	            EnsureExceptionLoggingInstalled();

                try
                {
                    if (Environment.OSVersion.Platform != PlatformID.Win32NT)
                        Directory.SetCurrentDirectory(ClientResourceLayout.ClientRoot);
                }
                catch (Exception ex)
                {
                    SaveError($"切换工作目录失败：{ex.Message}");
                }
	            SaveLog($"启动：Platform={Environment.OSVersion.Platform} ClientRoot={ClientResourceLayout.ClientRoot} RuntimeRoot={ClientResourceLayout.RuntimeRoot}");

 	            try
 	            {
 	                Settings.Load();
	            }
	            catch (Exception ex)
	            {
	                SaveError($"Settings.Load 失败：{ex}");
	                throw;
	            }
	            ApplyUIProfileConfiguration();
	            Main = this;
	            graphics = new GraphicsDeviceManager(this);
	            Content.RootDirectory = "Content";
	            IsMouseVisible = true;

            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                graphics.PreferredBackBufferHeight = Settings.ScreenHeight;
                graphics.PreferredBackBufferWidth = Settings.ScreenWidth;
            }

            Activated += HandleActivated;
            Deactivated += HandleDeactivated;
            ApplyTimingConfiguration();
            //else
            //{
            //    graphics.PreferredBackBufferHeight = 1080;
            //    graphics.PreferredBackBufferWidth = 2340;
            //}

        }

        protected override void Initialize()
        {
            // TODO: Add your initialization logic here
#if WINDOWS
            Window.TextInput += HandleTextInput;
#endif
#if !WINDOWS
            TryApplyMobileBackBufferScale();
#endif
            currentMouseState = GetPointerMouseState();
            previousMouseState = currentMouseState;
            _lastPointerPosition = currentMouseState.Position;
            ApplyTimingConfiguration();


            base.Initialize();
        }

        private void HandleTextInput(object sender, TextInputEventArgs e)
        {
            if (RuntimeSuspended)
                return;

            lock (_textInputLock)
            {
                _textInputQueue.Enqueue(e.Character);
            }
        }

        public static char[] ConsumeTextInput()
        {
            lock (_textInputLock)
            {
                if (_textInputQueue.Count == 0)
                    return Array.Empty<char>();

                char[] buffer = _textInputQueue.ToArray();
                _textInputQueue.Clear();
                return buffer;
            }
        }

        protected override void LoadContent()
        {
            spriteBatch = new SpriteBatch(GraphicsDevice);
            SpriteBatchScope = new SpriteBatchStack(spriteBatch);

            // TODO: use this.Content to load your game content here
            // 创建 FontSystem 实例
#if ANDROID || IOS
            // FairyGUI TextField 当前仅支持单一字体图集（单 Texture2D）。
            // 这里尽量扩大 FontStashSharp 的默认图集尺寸，减少运行时扩展为多图集的概率。
            fontSystem = new FontSystem(new FontSystemSettings
            {
                TextureWidth = 2048,
                TextureHeight = 2048,
            });
#else
            fontSystem = new FontSystem();
#endif

            // 加载 TrueType 字体文件
            using (var stream = Settings.OpenFontStream())
            {
                // 加载 TrueType 字体
                fontSystem.AddFont(stream);
            }
            SoundManager.Create();

            try
            {
                ClientResourceLayout.TryStagePackage("core-startup");
            }
            catch (Exception)
            {
            }

            if (MirScene.ActiveScene == null)
            {
#if ANDROID || IOS
                MirScene.ActiveScene = new PreLoginUpdateScene();
#else
                MirScene.ActiveScene = new LoginScene();
#endif
            }

            Settings.StartBootstrapWarmup();

#if ANDROID || IOS
            FairyGuiHost.TryInitialize(this);
#endif
        }

        protected override void Update(GameTime gameTime)
        {
            bool backOrEscapeDown = false;
            try
            {
                backOrEscapeDown = GamePad.GetState(PlayerIndex.One).Buttons.Back == ButtonState.Pressed ||
                                   Keyboard.GetState().IsKeyDown(Keys.Escape);
            }
            catch
            {
            }

#if ANDROID || IOS
            bool backPressedThisFrame = backOrEscapeDown && !_mobileBackPressedLastFrame;
            _mobileBackPressedLastFrame = backOrEscapeDown;

            if (backPressedThisFrame)
            {
                if (SoftKeyboardVisible)
                {
                    // Android：软键盘弹出时按返回键通常是“先收起键盘”，不应关闭窗口/退出游戏。
                    // 这里不主动 Cancel，避免把输入当作提交；让系统自行结束 KeyboardInput 任务。
                }
                else if (FairyGuiHost.TryHandleMobileBackRequested())
                {
                    // handled by FairyGUI (close popup/top window)
                }
                else if (MirScene.ActiveScene is GameScene)
                {
                    // 无窗口可关时，在游戏内优先打开“系统设置”窗口；避免直接退出。
                    if (FairyGuiHost.TryShowMobileWindowByKeywords("System", new[] { "设置", "系统", "System", "Set" }))
                        FairyGuiHost.HideAllMobileWindowsExcept("System");
                    else
                        Exit();
                }
                else
                {
                    // 非游戏场景（登录/预登录更新）：直接退出。
                    Exit();
                }
            }
#else
            if (backOrEscapeDown)
                Exit();
#endif

#if ANDROID || IOS
            if (!ReferenceEquals(_lastSceneForTouchReset, MirScene.ActiveScene))
            {
                ResetTouchInputState();
                _lastSceneForTouchReset = MirScene.ActiveScene;
            }
#endif

#if !WINDOWS
            TrySyncScreenSizeFromViewport();
#endif

#if ANDROID || IOS
            PumpSoftKeyboardInput();
#endif

#if ANDROID || IOS
            // Bootstrap 分包：下载与 BundleInbox 应用（预登录更新 / 按需资源）。
            // 说明：Try*IfDue 内部自带节流，可在每帧调用。
            try
            {
                BootstrapPackageDownloader.TryDownloadPendingPackagesIfDue();
                ClientResourceLayout.TryApplyBundleInboxIfDue();
            }
            catch (Exception)
            {
            }
#endif

            // 统一输入：Windows 使用鼠标；移动端用触摸模拟鼠标状态（用于复用现有 UI/场景点击逻辑）
            previousMouseState = currentMouseState;
            currentMouseState = GetPointerMouseState();

            bool pointerActive = Environment.OSVersion.Platform != PlatformID.Win32NT || IsMouseInsideGameWindow();
            if (pointerActive)
                MPoint = currentMouseState.Position.ToDrawPoint();

#if ANDROID || IOS
            FairyGuiHost.Update(gameTime);
#endif

            if (MirScene.ActiveScene != null)
            {
                foreach (var item in MirScene.ActiveScene.Controls.ToList())
                {
                    item.Event();
                    Event(item);
                }

                if (MirScene.ActiveScene is GameScene gameScene && gameScene.MapControl != null)
                    gameScene.MapControl.Event();
            }
            // TODO: Add your update logic here

            base.Update(gameTime);
        }

        private void TrySyncScreenSizeFromViewport()
        {
            try
            {
                int width = GraphicsDevice?.Viewport.Width ?? 0;
                int height = GraphicsDevice?.Viewport.Height ?? 0;

                if (width <= 0 || height <= 0)
                    return;

                if (Settings.ScreenWidth == width && Settings.ScreenHeight == height)
                    return;

                Settings.ScreenWidth = width;
                Settings.ScreenHeight = height;
            }
            catch (Exception)
            {
            }
        }

        private void TryApplyMobileBackBufferScale()
        {
            if (_mobileBackBufferScaleApplied)
                return;

            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                _mobileBackBufferScaleApplied = true;
                return;
            }

            float scale = Settings.MobileBackBufferScale;
            if (scale >= 0.99F)
            {
                _mobileBackBufferScaleApplied = true;
                return;
            }

            try
            {
                int currentWidth = GraphicsDevice?.Viewport.Width ?? 0;
                int currentHeight = GraphicsDevice?.Viewport.Height ?? 0;

                if (currentWidth <= 0 || currentHeight <= 0)
                    return;

                int targetWidth = Math.Max(1, (int)Math.Round(currentWidth * scale));
                int targetHeight = Math.Max(1, (int)Math.Round(currentHeight * scale));

                if (targetWidth == currentWidth && targetHeight == currentHeight)
                {
                    _mobileBackBufferScaleApplied = true;
                    return;
                }

                graphics.PreferredBackBufferWidth = targetWidth;
                graphics.PreferredBackBufferHeight = targetHeight;
                graphics.ApplyChanges();

                TrySyncScreenSizeFromViewport();
                SaveLog($"Graphics: 已应用 MobileBackBufferScale={scale:0.###} -> {Settings.ScreenWidth}x{Settings.ScreenHeight}");
            }
            catch (Exception ex)
            {
                SaveError($"Graphics: 应用 MobileBackBufferScale 失败：{ex.Message}");
            }
            finally
            {
                _mobileBackBufferScaleApplied = true;
            }
        }

        private static void ApplyUIProfileConfiguration()
        {
            UIProfileId profileId = UIManager.GetCurrentProfileId();
            UIProfile profile = UIManager.GetProfile(profileId);

            VirtualJoystickEnabled = profile.VirtualJoystickEnabled ?? Environment.OSVersion.Platform != PlatformID.Win32NT;
            JoystickFollowMode = profile.VirtualJoystickFollowMode ?? false;
        }

        public static bool TryGetJoystickDirection(out MirDirection direction, out bool preferRun)
        {
            direction = default;
            preferRun = false;

            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                return false;

            if (!VirtualJoystickEnabled)
                return false;

            if (_joystickMagnitude <= 0)
                return false;

            float deadzone = Settings.MobileJoystickDeadzone;
            if (_joystickMagnitude < deadzone)
                return false;

            if (_joystickForceRun)
                preferRun = true;
            else if (_joystickForceWalk)
                preferRun = false;
            else
                preferRun = _joystickMagnitude >= Settings.MobileJoystickRunThreshold;

            Vector2 vector = _joystickVector;
            if (vector.LengthSquared() <= 0)
                return false;

            // 角度 0°=Up，90°=Right，顺时针
            double angle = Math.Atan2(vector.X, -vector.Y) * 180D / Math.PI;
            if (angle < 0)
                angle += 360D;

            int index = (int)Math.Floor((angle + 22.5D) / 45D) % 8;
            direction = (MirDirection)index;
            return true;
        }

        private static MouseState GetPointerMouseState()
        {
            Vector2 previousPointerPosition = PointerTouchPosition;
            bool previousPointerTouchActive = PointerTouchActive;

            PointerTouchStarted = false;
            PointerTouchEnded = false;
            PointerTouchDelta = Vector2.Zero;

            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                MouseState state = Mouse.GetState();
                _lastPointerPosition = state.Position;
                _joystickTouchId = null;
                _pointerTouchId = null;
                _joystickVector = Vector2.Zero;
                _joystickMagnitude = 0F;
                _joystickForceWalk = false;
                _joystickForceRun = false;
                _joystickRadiusOverride = 0F;
                _isPinching = false;
                _previousPinchDistance = 0F;
                PinchDistanceDelta = 0F;
                PinchCenter = Vector2.Zero;
                _pointerTouchDownAtMs = 0;
                _pointerTouchDownPosition = Vector2.Zero;
                _pointerTapCandidate = false;
                _pointerLongPressTriggered = false;
                _pointerTouchStartedOverHud = false;
                _twoFingerTapCandidateActive = false;
                _twoFingerTapId1 = 0;
                _twoFingerTapId2 = 0;
                _twoFingerTapStartAtMs = 0;
                _twoFingerTapInitialCenter = Vector2.Zero;
                _twoFingerTapLastCenter = Vector2.Zero;
                _twoFingerTapInitialDistance = 0F;
                _twoFingerTapMaxCenterDrift = 0F;
                _twoFingerTapCanceled = false;

                PointerTouchActive = false;
                PointerTouchStarted = false;
                PointerTouchEnded = previousPointerTouchActive;
                PointerTouchPosition = new Vector2(state.X, state.Y);
                PointerTouchDelta = Vector2.Zero;
                return state;
            }

            long nowMs = Timer.ElapsedMilliseconds;
            TouchCollection touches = TouchPanel.GetState();

            TouchLocation joystickTouch = default;
            bool hasJoystickTouch = false;

            bool joystickEnabled = VirtualJoystickEnabled && IsJoystickAllowedForCurrentScene();
            if (!joystickEnabled)
            {
                _joystickTouchId = null;
                _joystickForceWalk = false;
                _joystickForceRun = false;
                _joystickRadiusOverride = 0F;
            }
            else if (_joystickTouchId.HasValue)
            {
                int existingId = _joystickTouchId.Value;
                for (int i = 0; i < touches.Count; i++)
                {
                    TouchLocation touch = touches[i];
                    if (touch.Id != existingId)
                        continue;

                    if (IsTouchActive(touch))
                    {
                        joystickTouch = touch;
                        hasJoystickTouch = true;
                    }
                    break;
                }

                if (!hasJoystickTouch)
                {
                    _joystickTouchId = null;
                    _joystickForceWalk = false;
                    _joystickForceRun = false;
                    _joystickRadiusOverride = 0F;
                }
            }

            if (joystickEnabled && !_joystickTouchId.HasValue)
            {
                for (int i = 0; i < touches.Count; i++)
                {
                    TouchLocation touch = touches[i];
                    if (!IsTouchActive(touch))
                        continue;

                    if (FairyGuiHost.TryResolveMobileDoubleJoystickActivation(touch.Position, out bool forceRun, out Vector2 origin, out float radius))
                    {
                        joystickTouch = touch;
                        hasJoystickTouch = true;
                        _joystickTouchId = touch.Id;
                        _joystickOrigin = origin;
                        _joystickForceRun = forceRun;
                        _joystickForceWalk = !forceRun;
                        _joystickRadiusOverride = radius;
                        break;
                    }

                    if (!IsWithinJoystickActivationArea(touch.Position))
                        continue;

                    joystickTouch = touch;
                    hasJoystickTouch = true;
                    _joystickTouchId = touch.Id;
                    _joystickOrigin = JoystickFollowMode ? touch.Position : GetFixedJoystickOrigin();
                    _joystickForceWalk = false;
                    _joystickForceRun = false;
                    _joystickRadiusOverride = 0F;
                    break;
                }
            }

            UpdateJoystickState(hasJoystickTouch ? joystickTouch : (TouchLocation?)null);

            var nonJoystickTouches = new List<TouchLocation>(2);
            for (int i = 0; i < touches.Count; i++)
            {
                TouchLocation touch = touches[i];
                if (!IsTouchActive(touch))
                    continue;

                if (_joystickTouchId.HasValue && touch.Id == _joystickTouchId.Value)
                    continue;

                nonJoystickTouches.Add(touch);
            }

            bool emitSyntheticRightClick = false;
            Point syntheticRightClickPosition = _lastPointerPosition;

            UpdatePinchAndTwoFingerTap(nowMs, touches, nonJoystickTouches, ref emitSyntheticRightClick, ref syntheticRightClickPosition);

            if (_isPinching)
            {
                _pointerTouchId = null;
                _pointerTapCandidate = false;
                _pointerLongPressTriggered = false;
                _pointerTouchStartedOverHud = false;

                PointerTouchActive = false;
                PointerTouchStarted = false;
                PointerTouchEnded = previousPointerTouchActive;
                PointerTouchPosition = new Vector2(_lastPointerPosition.X, _lastPointerPosition.Y);
                PointerTouchDelta = Vector2.Zero;
                return new MouseState(
                    _lastPointerPosition.X,
                    _lastPointerPosition.Y,
                    0,
                    ButtonState.Released,
                    ButtonState.Released,
                    ButtonState.Released,
                    ButtonState.Released,
                    ButtonState.Released);
            }

            if (_twoFingerTapCandidateActive)
            {
                _pointerTouchId = null;
                _pointerTapCandidate = false;
                _pointerLongPressTriggered = false;
                _pointerTouchStartedOverHud = false;

                if (emitSyntheticRightClick)
                    _lastPointerPosition = syntheticRightClickPosition;

                PointerTouchActive = false;
                PointerTouchStarted = false;
                PointerTouchEnded = previousPointerTouchActive;
                PointerTouchPosition = new Vector2(_lastPointerPosition.X, _lastPointerPosition.Y);
                PointerTouchDelta = Vector2.Zero;
                return new MouseState(
                    _lastPointerPosition.X,
                    _lastPointerPosition.Y,
                    0,
                    ButtonState.Released,
                    emitSyntheticRightClick ? ButtonState.Pressed : ButtonState.Released,
                    ButtonState.Released,
                    ButtonState.Released,
                    ButtonState.Released);
            }

            TouchLocation pointerTouch = default;
            bool hasPointerTouch = false;

            if (_pointerTouchId.HasValue)
            {
                int existingId = _pointerTouchId.Value;
                for (int i = 0; i < touches.Count; i++)
                {
                    TouchLocation touch = touches[i];
                    if (touch.Id != existingId)
                        continue;

                    if (IsTouchActive(touch) && (!_joystickTouchId.HasValue || touch.Id != _joystickTouchId.Value))
                    {
                        pointerTouch = touch;
                        hasPointerTouch = true;
                    }
                    break;
                }

                if (!hasPointerTouch)
                    _pointerTouchId = null;
            }

            if (!_pointerTouchId.HasValue)
            {
                for (int i = 0; i < touches.Count; i++)
                {
                    TouchLocation touch = touches[i];
                    if (!IsTouchActive(touch))
                        continue;

                    if (_joystickTouchId.HasValue && touch.Id == _joystickTouchId.Value)
                        continue;

                    pointerTouch = touch;
                    hasPointerTouch = true;
                    _pointerTouchId = touch.Id;
                    _pointerTouchDownAtMs = nowMs;
                    _pointerTouchDownPosition = touch.Position;
                    _pointerTapCandidate = true;
                    _pointerLongPressTriggered = false;
                    _pointerTouchStartedOverHud = IsPointOverMobileHud(touch.Position);
                    break;
                }
            }

            bool emitSyntheticLeftClick = false;
            Point syntheticLeftClickPosition = _lastPointerPosition;

            if (hasPointerTouch)
            {
                _lastPointerPosition = pointerTouch.Position.ToPoint();

                float moveDistance = Vector2.Distance(_pointerTouchDownPosition, pointerTouch.Position);
                if (moveDistance > Settings.MobileTouchTapMoveTolerancePixels)
                    _pointerTapCandidate = false;

                if (!_pointerLongPressTriggered &&
                    _pointerTapCandidate &&
                    nowMs - _pointerTouchDownAtMs >= Settings.MobileTouchLongPressThresholdMs)
                {
                    _pointerLongPressTriggered = true;
                }
            }
            else if (_pointerTouchId.HasValue == false && touches.Count > 0)
            {
                // 指针触点刚结束：可能仍存在 Released 触点，因此不能只依赖 touches.Count==0。
                if (_pointerTapCandidate && !_pointerLongPressTriggered)
                {
                    if (!_pointerTouchStartedOverHud ||
                        IsPointOverMobileHud(new Vector2(_lastPointerPosition.X, _lastPointerPosition.Y)))
                    {
                        emitSyntheticLeftClick = true;
                        syntheticLeftClickPosition = _lastPointerPosition;
                    }
                }

                _pointerTapCandidate = false;
                _pointerLongPressTriggered = false;
                _pointerTouchStartedOverHud = false;
            }
            else if (touches.Count == 0)
            {
                if (_pointerTapCandidate && !_pointerLongPressTriggered)
                {
                    if (!_pointerTouchStartedOverHud ||
                        IsPointOverMobileHud(new Vector2(_lastPointerPosition.X, _lastPointerPosition.Y)))
                    {
                        emitSyntheticLeftClick = true;
                        syntheticLeftClickPosition = _lastPointerPosition;
                    }
                }

                _pointerTouchId = null;
                _pointerTapCandidate = false;
                _pointerLongPressTriggered = false;
                _pointerTouchStartedOverHud = false;
            }

            if (emitSyntheticRightClick)
                _lastPointerPosition = syntheticRightClickPosition;
            if (emitSyntheticLeftClick)
                _lastPointerPosition = syntheticLeftClickPosition;

            ButtonState leftButton;
            ButtonState rightButton;

            if (emitSyntheticRightClick)
            {
                leftButton = ButtonState.Released;
                rightButton = ButtonState.Pressed;
            }
            else if (emitSyntheticLeftClick)
            {
                leftButton = ButtonState.Pressed;
                rightButton = ButtonState.Released;
            }
            else if (hasPointerTouch && _pointerLongPressTriggered)
            {
                leftButton = ButtonState.Released;
                rightButton = ButtonState.Pressed;
            }
            else
            {
                leftButton = ButtonState.Released;
                rightButton = ButtonState.Released;
            }

            PointerTouchActive = hasPointerTouch;
            PointerTouchStarted = !previousPointerTouchActive && hasPointerTouch;
            PointerTouchEnded = previousPointerTouchActive && !hasPointerTouch;
            PointerTouchPosition = new Vector2(_lastPointerPosition.X, _lastPointerPosition.Y);
            PointerTouchDelta = previousPointerTouchActive && hasPointerTouch ? pointerTouch.Position - previousPointerPosition : Vector2.Zero;

            return new MouseState(
                _lastPointerPosition.X,
                _lastPointerPosition.Y,
                0,
                leftButton,
                rightButton,
                ButtonState.Released,
                ButtonState.Released,
                ButtonState.Released);
        }

        private static void UpdatePinchAndTwoFingerTap(
            long nowMs,
            TouchCollection touches,
            List<TouchLocation> nonJoystickTouches,
            ref bool emitSyntheticRightClick,
            ref Point syntheticRightClickPosition)
        {
            emitSyntheticRightClick = false;
            syntheticRightClickPosition = _lastPointerPosition;

            if (_isPinching)
            {
                if (nonJoystickTouches.Count < 2)
                {
                    _isPinching = false;
                    _previousPinchDistance = 0F;
                    PinchDistanceDelta = 0F;
                    return;
                }

                TouchLocation first = nonJoystickTouches[0];
                TouchLocation second = nonJoystickTouches[1];
                PinchCenter = (first.Position + second.Position) * 0.5F;
                float distance = Vector2.Distance(first.Position, second.Position);
                PinchDistanceDelta = distance - _previousPinchDistance;
                _previousPinchDistance = distance;
                return;
            }

            PinchDistanceDelta = 0F;
            PinchCenter = Vector2.Zero;

            if (_twoFingerTapCandidateActive)
            {
                bool hasFirst = false;
                bool hasSecond = false;
                TouchLocation first = default;
                TouchLocation second = default;

                for (int i = 0; i < touches.Count; i++)
                {
                    TouchLocation touch = touches[i];
                    if (!IsTouchActive(touch))
                        continue;

                    if (!hasFirst && touch.Id == _twoFingerTapId1)
                    {
                        first = touch;
                        hasFirst = true;
                        continue;
                    }

                    if (!hasSecond && touch.Id == _twoFingerTapId2)
                    {
                        second = touch;
                        hasSecond = true;
                    }
                }

                if (hasFirst && hasSecond)
                {
                    Vector2 center = (first.Position + second.Position) * 0.5F;
                    float distance = Vector2.Distance(first.Position, second.Position);

                    if (!IsTwoFingerGestureAllowedAt(center))
                    {
                        ResetTwoFingerTapCandidate();
                        return;
                    }

                    _twoFingerTapLastCenter = center;

                    float drift = Vector2.Distance(_twoFingerTapInitialCenter, center);
                    if (drift > _twoFingerTapMaxCenterDrift)
                        _twoFingerTapMaxCenterDrift = drift;

                    if (Math.Abs(distance - _twoFingerTapInitialDistance) >= Settings.MobilePinchStartDistanceThresholdPixels)
                    {
                        _isPinching = true;
                        _previousPinchDistance = distance;
                        PinchDistanceDelta = 0F;
                        PinchCenter = center;
                        _twoFingerTapCandidateActive = false;
                        _twoFingerTapCanceled = true;
                        return;
                    }

                    return;
                }

                long elapsed = nowMs - _twoFingerTapStartAtMs;
                bool gestureEnded = !hasFirst && !hasSecond;

                if (gestureEnded)
                {
                    if (!_twoFingerTapCanceled &&
                        elapsed <= Settings.MobileTwoFingerTapMaxDurationMs &&
                        _twoFingerTapMaxCenterDrift <= Settings.MobileTwoFingerTapMoveTolerancePixels &&
                        IsTwoFingerGestureAllowedAt(_twoFingerTapLastCenter))
                    {
                        emitSyntheticRightClick = true;
                        syntheticRightClickPosition = _twoFingerTapLastCenter.ToPoint();
                    }

                    ResetTwoFingerTapCandidate();
                    return;
                }

                if (elapsed > Settings.MobileTwoFingerTapMaxDurationMs)
                {
                    ResetTwoFingerTapCandidate();
                }

                return;
            }

            if (nonJoystickTouches.Count >= 2)
            {
                TouchLocation first = nonJoystickTouches[0];
                TouchLocation second = nonJoystickTouches[1];

                Vector2 center = (first.Position + second.Position) * 0.5F;
                if (!IsTwoFingerGestureAllowedAt(center))
                    return;

                _twoFingerTapCandidateActive = true;
                _twoFingerTapId1 = first.Id;
                _twoFingerTapId2 = second.Id;
                _twoFingerTapStartAtMs = nowMs;
                _twoFingerTapInitialCenter = center;
                _twoFingerTapLastCenter = center;
                _twoFingerTapInitialDistance = Vector2.Distance(first.Position, second.Position);
                _twoFingerTapMaxCenterDrift = 0F;
                _twoFingerTapCanceled = false;
                return;
            }
        }

        private static void ResetTwoFingerTapCandidate()
        {
            _twoFingerTapCandidateActive = false;
            _twoFingerTapCanceled = false;
            _twoFingerTapId1 = 0;
            _twoFingerTapId2 = 0;
            _twoFingerTapStartAtMs = 0;
            _twoFingerTapInitialCenter = Vector2.Zero;
            _twoFingerTapLastCenter = Vector2.Zero;
            _twoFingerTapInitialDistance = 0F;
            _twoFingerTapMaxCenterDrift = 0F;
        }

        private static bool IsPointOverMobileHud(Vector2 position)
        {
            if (MirScene.ActiveScene is GameScene scene)
                return scene.IsPointOverMobileHud(position);

            return false;
        }

        private static bool IsTwoFingerGestureAllowedAt(Vector2 center)
        {
            if (MirScene.ActiveScene is not GameScene scene)
                return false;

            bool centerOnMiniMap = scene.IsPointOverMobileMiniMap(center);
            if (!centerOnMiniMap && scene.IsMobileOverlayBlockingJoystick())
                return false;

            if (!centerOnMiniMap && scene.IsPointOverMobileHud(center))
                return false;

            return true;
        }

        private static bool IsTouchActive(TouchLocation touch)
        {
            return touch.State == TouchLocationState.Pressed ||
                   touch.State == TouchLocationState.Moved;
        }

        private static bool IsJoystickAllowedForCurrentScene()
        {
            if (MirScene.ActiveScene is GameScene scene)
                return !scene.IsMobileOverlayBlockingJoystick();

            return false;
        }

        private static bool IsWithinJoystickActivationArea(Vector2 position)
        {
            System.Drawing.Rectangle safeArea = Settings.GetMobileSafeAreaBounds();

            if (position.X < safeArea.Left || position.X >= safeArea.Right ||
                position.Y < safeArea.Top || position.Y >= safeArea.Bottom)
            {
                return false;
            }

            if (JoystickFollowMode &&
                MirScene.ActiveScene is GameScene scene &&
                scene.IsPointOverMobileHud(position))
            {
                return false;
            }

            if (!JoystickFollowMode)
            {
                Vector2 origin = GetFixedJoystickOrigin();
                float radius = GetJoystickRadius();
                float activationRadius = radius + 6F;

                Vector2 delta = position - origin;
                return delta.LengthSquared() <= activationRadius * activationRadius;
            }

            float width = Math.Max(1F, safeArea.Width);
            float height = Math.Max(1F, safeArea.Height);

            float widthRatio = Settings.MobileJoystickActivationWidthRatio;
            float heightRatio = Settings.MobileJoystickActivationHeightRatio;

            float xLimit = safeArea.Left + width * widthRatio;
            float yLimit = safeArea.Top + height * (1F - heightRatio);
            return position.X <= xLimit && position.Y >= yLimit;
        }

        private static Vector2 GetFixedJoystickOrigin()
        {
            System.Drawing.Rectangle safeArea = Settings.GetMobileSafeAreaBounds();
            float width = Math.Max(1, safeArea.Width);
            float height = Math.Max(1, safeArea.Height);
            float minDimension = Math.Min(width, height);
            float margin = Math.Max(Settings.MobileJoystickFixedMarginMin, minDimension * Settings.MobileJoystickFixedMarginRatio);

            return new Vector2(safeArea.Left + margin, safeArea.Bottom - margin);
        }

        private static float GetJoystickRadius()
        {
            System.Drawing.Rectangle safeArea = Settings.GetMobileSafeAreaBounds();
            float width = Math.Max(1, safeArea.Width);
            float height = Math.Max(1, safeArea.Height);
            float minDimension = Math.Min(width, height);
            return Math.Max(Settings.MobileJoystickRadiusMin, minDimension * Settings.MobileJoystickRadiusRatio);
        }

        private static void UpdateJoystickState(TouchLocation? joystickTouch)
        {
            if (!joystickTouch.HasValue)
            {
                _joystickVector = Vector2.Zero;
                _joystickMagnitude = 0F;
                try
                {
                    FairyGuiHost.TryUpdateMobileDoubleJoystickVisual(active: false, forceRun: false, joystickOrigin: Vector2.Zero, joystickDelta: Vector2.Zero, joystickRadius: 0F);
                }
                catch
                {
                }
                return;
            }

            float radius = _joystickRadiusOverride > 0F ? _joystickRadiusOverride : GetJoystickRadius();
            if (radius <= 0)
            {
                _joystickVector = Vector2.Zero;
                _joystickMagnitude = 0F;
                try
                {
                    FairyGuiHost.TryUpdateMobileDoubleJoystickVisual(active: false, forceRun: _joystickForceRun, joystickOrigin: _joystickOrigin, joystickDelta: Vector2.Zero, joystickRadius: 0F);
                }
                catch
                {
                }
                return;
            }

            Vector2 delta = joystickTouch.Value.Position - _joystickOrigin;
            float length = delta.Length();
            _joystickMagnitude = Math.Min(1F, length / radius);

            if (length > radius && length > 0)
                delta *= radius / length;

            _joystickVector = delta / radius;

            try
            {
                FairyGuiHost.TryUpdateMobileDoubleJoystickVisual(active: true, forceRun: _joystickForceRun, joystickOrigin: _joystickOrigin, joystickDelta: delta, joystickRadius: radius);
            }
            catch
            {
            }
        }

        public bool IsMouseInsideGameWindow()
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
                return true;

            // 获取当前游戏窗口的句柄
            IntPtr gameWindowHandle = Window.Handle;

            // 获取当前激活的窗口句柄
            IntPtr foregroundWindowHandle = GetForegroundWindow();

            // 判断鼠标是否在游戏窗口内
            bool isMouseInGameWindow = (gameWindowHandle == foregroundWindowHandle);


            return isMouseInGameWindow;

        }
        private void Event(MirControl control)
        {
            if (control == null || control.Controls == null) return;
            foreach (var item in control.Controls.ToList())
            {
                item.Event();
                Event(item);
            }
        }


        protected override void Draw(GameTime gameTime)
        {
            if (Settings.DebugMode)
                SpriteBatchScope.ResetFrameMetrics();

            GraphicsDevice.Clear(Color.Black);

            UpdateTime();
            UpdateEnviroment();

            SpriteBatchScope.Begin();
            RenderEnvironment();
            SpriteBatchScope.End();

#if ANDROID || IOS
            FairyGuiHost.Draw(gameTime);
#endif

            if (Settings.DebugMode)
            {
                SpriteBatchScope.Begin();
                DrawDebugOverlay();
                SpriteBatchScope.End();
            }

            if (Settings.DebugMode)
            {
                _debugLastSpriteBatchBeginCalls = SpriteBatchScope.FrameBeginCalls;
                _debugLastSpriteBatchEndCalls = SpriteBatchScope.FrameEndCalls;
                _debugLastSpriteBatchStateChanges = SpriteBatchScope.FrameStateChanges;
            }
            base.Draw(gameTime);
        }

        private static DynamicSpriteFont GetDebugFont()
        {
            if (_debugFont != null)
                return _debugFont;

            if (fontSystem == null)
                return null;

            _debugFont = fontSystem.GetFont(50);
            return _debugFont;
        }

        private static void DrawDebugOverlay()
        {
            DynamicSpriteFont font = GetDebugFont();
            if (font == null)
                return;

            var pointer = currentMouseState.Position;

             spriteBatch.DrawString(font, $"Pointer: {pointer.X},{pointer.Y}", new Vector2(0, 0), Color.White);
             spriteBatch.DrawString(font, $"FPS: {FPS}", new Vector2(0, 50), Color.White);
             spriteBatch.DrawString(font, $"SpriteBatch(B/E/S): {_debugLastSpriteBatchBeginCalls}/{_debugLastSpriteBatchEndCalls}/{_debugLastSpriteBatchStateChanges}", new Vector2(0, 100), Color.Yellow);

             string debugText = DebugText ?? string.Empty;
             if (!string.IsNullOrWhiteSpace(debugText))
                 spriteBatch.DrawString(font, debugText, new Vector2(0, 150), Color.Cyan);
         }

        private void HandleActivated(object sender, EventArgs e)
        {
            NotifyRuntimeActivated();
        }

        private void HandleDeactivated(object sender, EventArgs e)
        {
            NotifyRuntimeDeactivated();
        }

        public void NotifyRuntimeActivated()
        {
            bool wasSuspended = RuntimeSuspended;

            RuntimeSuspended = false;
            ResetTouchInputState();
            ApplyTimingConfiguration();
            NextPing = Time + 10000;
            _nextSuspendedNetworkProcessTime = 0;
            ClientResourceLayout.RefreshPackageStateSnapshot();

            if (wasSuspended && Environment.OSVersion.Platform != PlatformID.Win32NT)
                SoundManager.ResumeAudio();
        }

        public void NotifyRuntimeDeactivated()
        {
            bool wasSuspended = RuntimeSuspended;

            RuntimeSuspended = true;
            ResetTouchInputState();
            ApplyTimingConfiguration();
            _nextSuspendedNetworkProcessTime = 0;
            ClientResourceLayout.RefreshPackageStateSnapshot();

            if (!wasSuspended && Environment.OSVersion.Platform != PlatformID.Win32NT)
                SoundManager.SuspendAudio();
        }

        private void ApplyTimingConfiguration()
        {
            int targetFps;
            if (RuntimeSuspended)
            {
                targetFps = Math.Max(5, Settings.BackgroundMaxFPS);
            }
            else
            {
                targetFps = Settings.FPSCap ? Math.Max(15, Settings.MaxFPS) : 60;
            }

            IsFixedTimeStep = RuntimeSuspended || Settings.FPSCap;
            TargetElapsedTime = TimeSpan.FromSeconds(1D / targetFps);
            InactiveSleepTime = RuntimeSuspended ? TimeSpan.FromMilliseconds(250) : TimeSpan.FromMilliseconds(33);
        }

        private static void UpdateTime()
        {
            Time = Timer.ElapsedMilliseconds;
        }
        private static void UpdateEnviroment()
        {

            if (Time >= _fpsTime)
            {
                _fpsTime = Time + 1000;
                FPS = _fps;
                _fps = 0;
                DXManager.Clean(); // Clean once a second.
            }
            else
                _fps++;

            if (RuntimeSuspended)
            {
                if (_nextSuspendedNetworkProcessTime == 0 || Time >= _nextSuspendedNetworkProcessTime)
                {
                    _nextSuspendedNetworkProcessTime = Time + Settings.BackgroundNetworkTickMs;
                    Network.Process();
                }

                return;
            }

            // 优先处理网络：避免首次启动/资源回填时 I/O 阻塞导致握手与 KeepAlive 延迟。
            Network.Process();

            int resourcePollIntervalMs = 0;
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
            {
                bool inLoginHandshake = MirScene.ActiveScene is LoginScene && !Network.Connected;
                resourcePollIntervalMs = inLoginHandshake ? 1000 : 200;
            }

            if (resourcePollIntervalMs <= 0 || Time >= _nextMobileAsyncResourcePollTime)
            {
                _nextMobileAsyncResourcePollTime = resourcePollIntervalMs <= 0 ? 0 : Time + resourcePollIntervalMs;
                AsynDownLoadResources.CreateInstance().TryNotifyExisting();
            }

            if (MirScene.ActiveScene != null)
                MirScene.ActiveScene.Process();

            for (int i = 0; i < MirAnimatedControl.Animations.Count; i++)
                MirAnimatedControl.Animations[i].UpdateOffSet();

            //for (int i = 0; i < MirAnimatedButton.Animations.Count; i++)
            //    MirAnimatedButton.Animations[i].UpdateOffSet();

            //CreateHintLabel();

            //if (Settings.DebugMode)
            //{
            //    CreateDebugLabel();
            //}
        }

        private static void RenderEnvironment()
        {
            //try
            //{
            //if (DXManager.DeviceLost)
            //{
            //    DXManager.AttemptReset();
            //    Thread.Sleep(1);
            //    return;
            //}

            //DXManager.Device.Clear(ClearFlags.Target, Color.CornflowerBlue, 0, 0);
            //DXManager.Device.BeginScene();
            //DXManager.Sprite.Begin(SpriteFlags.AlphaBlend);
            //DXManager.SetSurface(DXManager.MainSurface);

            if (MirScene.ActiveScene != null)
                MirScene.ActiveScene.Draw();

            //DXManager.Sprite.End();
            //DXManager.Device.EndScene();
            //DXManager.Device.Present();
            //}
            //catch (Direct3D9Exception ex)
            //{
            //    DXManager.DeviceLost = true;
            //}
            //catch (Exception ex)
            //{
            //    SaveError(ex.ToString());

            //    DXManager.AttemptRecovery();
            //}
        }

            private static void MirrorMobileLogToPlatformOutput(string tag, string message, bool error)
            {
                if (string.IsNullOrWhiteSpace(message))
                    return;

#if ANDROID
                try
                {
                    const int chunkSize = 3800;
                    for (int offset = 0; offset < message.Length; offset += chunkSize)
                    {
                        string chunk = message.Substring(offset, Math.Min(chunkSize, message.Length - offset));
                        if (error)
                            Android.Util.Log.Error(tag, chunk);
                        else
                            Android.Util.Log.Debug(tag, chunk);
                    }
                }
                catch
                {
                }
#elif IOS
                try
                {
                    System.Diagnostics.Debug.WriteLine($"{tag}: {message}");
                }
                catch
                {
                }
#endif
            }

	        public static void SaveError(string message)
	        {
	            if (string.IsNullOrWhiteSpace(message))
	                return;

                if (Environment.OSVersion.Platform != PlatformID.Win32NT)
                {
                    MirrorMobileLogToPlatformOutput("LomMir2-Err", message, error: true);
                    EnsureMobileLogWriterStarted();
                    TryEnqueueMobileLog(_mobileErrorLogQueue, ref _mobileErrorLogQueueCount, MobileErrorLogMaxQueuedLines, message);
                    return;
                }

            try
            {
                lock (_errorLogLock)
                {
                    string logPath = Path.Combine(ClientResourceLayout.RuntimeRoot, "MobileErrors.log");
                    Directory.CreateDirectory(Path.GetDirectoryName(logPath) ?? ClientResourceLayout.RuntimeRoot);

                    using var writer = new StreamWriter(logPath, append: true, Utf8NoBom);
                    writer.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}");
                }
	            }
	            catch
	            {
	            }
	        }

	        public static void SaveLog(string message)
	        {
	            if (string.IsNullOrWhiteSpace(message))
	                return;

                if (Environment.OSVersion.Platform != PlatformID.Win32NT)
                {
                    MirrorMobileLogToPlatformOutput("LomMir2", message, error: false);
                    EnsureMobileLogWriterStarted();
                    TryEnqueueMobileLog(_mobileRuntimeLogQueue, ref _mobileRuntimeLogQueueCount, MobileRuntimeLogMaxQueuedLines, message);
                    return;
                }

	            try
	            {
	                lock (_errorLogLock)
	                {
	                    string logPath = Path.Combine(ClientResourceLayout.RuntimeRoot, "MobileRuntime.log");
	                    Directory.CreateDirectory(Path.GetDirectoryName(logPath) ?? ClientResourceLayout.RuntimeRoot);

	                    using var writer = new StreamWriter(logPath, append: true, Utf8NoBom);
	                    writer.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}");
	                }
	            }
	            catch
	            {
	            }
	        }

	        private static void EnsureExceptionLoggingInstalled()
	        {
	            lock (_exceptionLogGate)
	            {
	                if (_exceptionLogInstalled)
	                    return;

	                _exceptionLogInstalled = true;
	            }

	            try
	            {
	                AppDomain.CurrentDomain.UnhandledException += (_, args) =>
	                {
	                    try
	                    {
	                        SaveError($"UnhandledException: {args.ExceptionObject}");
	                    }
	                    catch
	                    {
	                    }
	                };

	                TaskScheduler.UnobservedTaskException += (_, args) =>
	                {
	                    try
	                    {
	                        SaveError($"UnobservedTaskException: {args.Exception}");
	                    }
	                    catch
	                    {
	                    }

	                    try
	                    {
	                        args.SetObserved();
	                    }
	                    catch
	                    {
	                    }
	                };
	            }
	            catch
	            {
	            }
	        }

            private static void EnsureMobileLogWriterStarted()
            {
                if (_mobileLogWriterStarted)
                    return;

                lock (_mobileLogWriterGate)
                {
                    if (_mobileLogWriterStarted)
                        return;

                    _mobileLogWriterStarted = true;
                    _ = Task.Run(MobileLogWriterLoop);
                }
            }

            private static void MobileLogWriterLoop()
            {
                while (true)
                {
                    bool wroteAny = false;

                    wroteAny |= FlushMobileLogQueue(_mobileRuntimeLogQueue, ref _mobileRuntimeLogQueueCount, Path.Combine(ClientResourceLayout.RuntimeRoot, "MobileRuntime.log"));
                    wroteAny |= FlushMobileLogQueue(_mobileErrorLogQueue, ref _mobileErrorLogQueueCount, Path.Combine(ClientResourceLayout.RuntimeRoot, "MobileErrors.log"));

                    if (!wroteAny)
                        _mobileLogWakeup.WaitOne(500);
                }
            }

            private static bool TryEnqueueMobileLog(ConcurrentQueue<string> queue, ref int counter, int maxQueuedLines, string message)
            {
                if (queue == null || string.IsNullOrWhiteSpace(message))
                    return false;

                int current = Interlocked.Increment(ref counter);
                if (maxQueuedLines > 0 && current > maxQueuedLines)
                {
                    Interlocked.Decrement(ref counter);
                    return false;
                }

                queue.Enqueue($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}");
                _mobileLogWakeup.Set();
                return true;
            }

            private static bool FlushMobileLogQueue(ConcurrentQueue<string> queue, ref int counter, string logPath)
            {
                if (queue == null || queue.IsEmpty)
                    return false;

                var builder = new StringBuilder(4 * 1024);
                int flushed = 0;

                while (flushed < MobileLogMaxLinesPerFlush && queue.TryDequeue(out string line))
                {
                    Interlocked.Decrement(ref counter);
                    builder.AppendLine(line);
                    flushed++;
                }

                if (flushed <= 0)
                    return false;

                try
                {
                    lock (_errorLogLock)
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(logPath) ?? ClientResourceLayout.RuntimeRoot);
                        File.AppendAllText(logPath, builder.ToString(), Utf8NoBom);
                    }
                }
                catch
                {
                }

                return true;
            }
	    }
}
