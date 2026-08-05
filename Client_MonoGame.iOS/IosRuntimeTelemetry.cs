#if REAL_IOS
using System;
using System.Net;
using CoreFoundation;
using Foundation;
using MonoShare;
using SystemConfiguration;
using UIKit;

namespace Client_MonoGame.iOS;

internal static class IosRuntimeTelemetry
{
    private static readonly object Gate = new object();

    private static bool _started;
    private static NetworkReachability _defaultRouteReachability;
    private static NSObject _batteryLevelObserver;
    private static NSObject _batteryStateObserver;
    private static bool? _lastNetworkAvailable;
    private static string _lastBatterySnapshot = string.Empty;

    public static void Start()
    {
        lock (Gate)
        {
            if (_started)
                return;

            _started = true;
        }

        try
        {
            StartNetworkReachability();
        }
        catch (Exception ex)
        {
            CMain.SaveError($"iOS Telemetry: 启动网络监听失败：{ex.Message}");
        }

        try
        {
            StartBatteryMonitoring();
        }
        catch (Exception ex)
        {
            CMain.SaveError($"iOS Telemetry: 启动电量监听失败：{ex.Message}");
        }
    }

    public static void Stop()
    {
        lock (Gate)
        {
            if (!_started)
                return;

            _started = false;
        }

        try
        {
            StopNetworkReachability();
        }
        catch
        {
        }

        try
        {
            StopBatteryMonitoring();
        }
        catch
        {
        }
    }

    private static void StartNetworkReachability()
    {
        if (_defaultRouteReachability != null)
            return;

        _defaultRouteReachability = new NetworkReachability(new IPAddress(0));
        _defaultRouteReachability.SetNotification(OnReachabilityChanged);
        _defaultRouteReachability.Schedule(CFRunLoop.Main, CFRunLoop.ModeDefault);

        if (_defaultRouteReachability.TryGetFlags(out NetworkReachabilityFlags flags))
        {
            LogReachability(flags, source: "initial");
        }
        else
        {
            CMain.SaveLog("iOS Network: 初始状态读取失败。");
        }
    }

    private static void StopNetworkReachability()
    {
        if (_defaultRouteReachability == null)
            return;

        try
        {
            _defaultRouteReachability.Unschedule(CFRunLoop.Main, CFRunLoop.ModeDefault);
        }
        catch
        {
        }

        try
        {
            _defaultRouteReachability.Dispose();
        }
        catch
        {
        }

        _defaultRouteReachability = null;
        _lastNetworkAvailable = null;
    }

    private static void OnReachabilityChanged(NetworkReachabilityFlags flags)
    {
        LogReachability(flags, source: "change");
    }

    private static void LogReachability(NetworkReachabilityFlags flags, string source)
    {
        bool available = IsReachableWithoutRequiringConnection(flags);
        if (_lastNetworkAvailable.HasValue && _lastNetworkAvailable.Value == available && !string.Equals(source, "initial", StringComparison.OrdinalIgnoreCase))
            return;

        _lastNetworkAvailable = available;
        string state = available ? "Online" : "Offline";
        CMain.SaveLog($"iOS Network: {state} Source={source} Flags={flags}");
    }

    private static bool IsReachableWithoutRequiringConnection(NetworkReachabilityFlags flags)
    {
        bool isReachable = (flags & NetworkReachabilityFlags.Reachable) != 0;
        bool noConnectionRequired = (flags & NetworkReachabilityFlags.ConnectionRequired) == 0;

        if ((flags & NetworkReachabilityFlags.IsWWAN) != 0)
            noConnectionRequired = true;

        return isReachable && noConnectionRequired;
    }

    private static void StartBatteryMonitoring()
    {
        UIDevice.CurrentDevice.BatteryMonitoringEnabled = true;

        _batteryLevelObserver = UIDevice.Notifications.ObserveBatteryLevelDidChange((sender, args) => LogBatterySnapshot("level"));
        _batteryStateObserver = UIDevice.Notifications.ObserveBatteryStateDidChange((sender, args) => LogBatterySnapshot("state"));

        LogBatterySnapshot("initial");
    }

    private static void StopBatteryMonitoring()
    {
        try
        {
            _batteryLevelObserver?.Dispose();
            _batteryStateObserver?.Dispose();
        }
        catch
        {
        }

        _batteryLevelObserver = null;
        _batteryStateObserver = null;
        _lastBatterySnapshot = string.Empty;

        try
        {
            UIDevice.CurrentDevice.BatteryMonitoringEnabled = false;
        }
        catch
        {
        }
    }

    private static void LogBatterySnapshot(string source)
    {
        UIDevice device = UIDevice.CurrentDevice;

        string levelText = device.BatteryLevel < 0F
            ? "Unknown"
            : $"{Math.Round(device.BatteryLevel * 100F):0}%";
        string stateText = device.BatteryState.ToString();
        string snapshot = $"{stateText}|{levelText}";

        if (string.Equals(_lastBatterySnapshot, snapshot, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(source, "initial", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _lastBatterySnapshot = snapshot;
        CMain.SaveLog($"iOS Battery: State={stateText} Level={levelText} Source={source}");
    }
}
#endif
