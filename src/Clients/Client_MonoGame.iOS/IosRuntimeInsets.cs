#if REAL_IOS
using System;
using System.Linq;
using Foundation;
using MonoShare;
using UIKit;

namespace Client_MonoGame.iOS;

internal static class IosRuntimeInsets
{
    private static bool _started;
    private static NSObject _keyboardWillShowObserver;
    private static NSObject _keyboardWillHideObserver;
    private static NSTimer _safeAreaTimer;

    private static int _lastSafeLeft;
    private static int _lastSafeTop;
    private static int _lastSafeRight;
    private static int _lastSafeBottom;
    private static int _lastKeyboardAvoidance;

    public static void Start()
    {
        if (_started)
            return;

        _started = true;

        try
        {
            _keyboardWillShowObserver = UIKeyboard.Notifications.ObserveWillShow((sender, args) =>
            {
                try
                {
                    UpdateKeyboardAvoidanceFromArgs(args);
                    UpdateSafeAreaNow();
                }
                catch
                {
                }
            });

            _keyboardWillHideObserver = UIKeyboard.Notifications.ObserveWillHide((sender, args) =>
            {
                try
                {
                    SetKeyboardAvoidance(0, sourceHeightPixels: 0);
                    UpdateSafeAreaNow();
                }
                catch
                {
                }
            });

            UIApplication.SharedApplication.InvokeOnMainThread(() =>
            {
                try
                {
                    UpdateSafeAreaNow();
                    _safeAreaTimer = NSTimer.CreateRepeatingScheduledTimer(TimeSpan.FromSeconds(0.75), _ => UpdateSafeAreaNow());
                }
                catch
                {
                }
            });
        }
        catch
        {
        }
    }

    private static void UpdateKeyboardAvoidanceFromArgs(UIKeyboardEventArgs args)
    {
        if (args == null)
            return;

        nfloat scale = UIScreen.MainScreen?.Scale ?? 1F;

        // iOS 提供的是 points；这里统一转换为像素，保持与 Settings.ScreenWidth/Height 的口径一致。
        CGRect endFrame = args.FrameEnd;
        nfloat screenHeight = UIScreen.MainScreen?.Bounds.Height ?? 0F;
        int sourceHeightPixels = (int)Math.Round(screenHeight * scale);
        nfloat overlapPoints = Math.Max(0F, screenHeight - endFrame.Y);
        int overlapPixels = (int)Math.Round(overlapPoints * scale);
        SetKeyboardAvoidance(overlapPixels, sourceHeightPixels);
    }

    private static void SetKeyboardAvoidance(int avoidancePixels, int sourceHeightPixels)
    {
        avoidancePixels = Math.Max(0, avoidancePixels);
        if (avoidancePixels == _lastKeyboardAvoidance)
            return;

        _lastKeyboardAvoidance = avoidancePixels;
        Settings.SetRuntimeSoftKeyboardAvoidanceHeight(avoidancePixels, Math.Max(0, sourceHeightPixels));
    }

    private static void UpdateSafeAreaNow()
    {
        try
        {
            UIApplication.SharedApplication.InvokeOnMainThread(() =>
            {
                try
                {
                    UIWindow window = UIApplication.SharedApplication.Windows?.FirstOrDefault(w => w != null && w.IsKeyWindow);
                    window ??= UIApplication.SharedApplication.KeyWindow;
                    if (window == null)
                        return;

                    nfloat scale = UIScreen.MainScreen?.Scale ?? 1F;
                    UIEdgeInsets insets = window.SafeAreaInsets;
                    CGRect bounds = window.Bounds;

                    int sourceWidthPixels = (int)Math.Round(bounds.Width * scale);
                    int sourceHeightPixels = (int)Math.Round(bounds.Height * scale);

                    int left = (int)Math.Round(insets.Left * scale);
                    int top = (int)Math.Round(insets.Top * scale);
                    int right = (int)Math.Round(insets.Right * scale);
                    int bottom = (int)Math.Round(insets.Bottom * scale);

                    if (left == _lastSafeLeft &&
                        top == _lastSafeTop &&
                        right == _lastSafeRight &&
                        bottom == _lastSafeBottom)
                    {
                        return;
                    }

                    _lastSafeLeft = left;
                    _lastSafeTop = top;
                    _lastSafeRight = right;
                    _lastSafeBottom = bottom;

                    Settings.SetRuntimeMobileSafeAreaInsets(left, top, right, bottom, sourceWidthPixels, sourceHeightPixels);
                }
                catch
                {
                }
            });
        }
        catch
        {
        }
    }
}
#endif
