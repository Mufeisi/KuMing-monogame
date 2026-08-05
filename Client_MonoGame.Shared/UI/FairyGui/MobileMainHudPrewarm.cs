using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FairyGUI;

namespace MonoShare
{
    internal static class MobileMainHudPrewarm
    {
        private const string AtlasPackageName = "UIRes";

        private static readonly object Gate = new object();

        private static bool _started;
        private static bool _completed;
        private static bool _failed;
        private static string _lastError = string.Empty;
        private static List<PackageItem> _atlasItems;
        private static List<string> _atlasFiles;
        private static int _prepared;
        private static int _nextWarmIndex;
        private static bool _completionLogged;
        private static long _startTimeMs;
        private static Task _prefetchTask;

        public static bool IsCompleted => _completed;
        public static bool HasFailed => _failed;
        public static string LastError => _lastError ?? string.Empty;
        public static int TotalAtlases => _atlasItems?.Count ?? 0;
        public static int LoadedAtlases => Math.Min(TotalAtlases, Volatile.Read(ref _prepared));

        public static void EnsureStarted()
        {
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                _completed = true;
                return;
            }

            lock (Gate)
            {
                if (_started || _completed || _failed)
                    return;

                UIPackage pkg = UIPackage.GetByName(AtlasPackageName);
                if (pkg == null)
                    return;

                try
                {
                    List<PackageItem> items = pkg.GetItems();
                    _atlasItems = new List<PackageItem>();
                    _atlasFiles = new List<string>();
                    if (items != null)
                    {
                        for (int i = 0; i < items.Count; i++)
                        {
                            PackageItem item = items[i];
                            if (item == null || item.type != PackageItemType.Atlas)
                                continue;

                            string fileName = (item.file ?? string.Empty).Trim();
                            if (string.IsNullOrWhiteSpace(fileName))
                                continue;

                            _atlasItems.Add(item);
                            if (!_atlasFiles.Contains(fileName, StringComparer.OrdinalIgnoreCase))
                                _atlasFiles.Add(fileName);
                        }
                    }

                    _prepared = 0;
                    _nextWarmIndex = 0;
                    _completionLogged = false;
                    _startTimeMs = CMain.Time;
                    _started = true;

                    if ((_atlasItems?.Count ?? 0) <= 0)
                    {
                        _completed = true;
                        return;
                    }

                    if (Settings.LogErrors)
                        CMain.SaveLog($"FairyGUI: MobileMainHud 后台预取启动，Pkg={AtlasPackageName} Atlases={_atlasFiles?.Count ?? 0}");

                    _prefetchTask = Task.Run(PrefetchAtlasBytesWorker);
                }
                catch (Exception ex)
                {
                    _failed = true;
                    _lastError = ex.Message ?? string.Empty;
                    if (Settings.LogErrors)
                        CMain.SaveError($"FairyGUI: MobileMainHud 后台预取初始化失败：{ex}");
                }
            }
        }

        public static void Tick(int maxAtlasesPerTick = 1)
        {
            EnsureStarted();
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                _completed = true;
                return;
            }

            if (maxAtlasesPerTick < 1)
                maxAtlasesPerTick = 1;

            UIPackage pkg;
            var itemsToWarm = new List<PackageItem>(maxAtlasesPerTick);

            lock (Gate)
            {
                if (!_started || _completed || _failed)
                    return;

                pkg = UIPackage.GetByName(AtlasPackageName);
                if (pkg == null)
                    return;

                while (itemsToWarm.Count < maxAtlasesPerTick
                       && _atlasItems != null
                       && _nextWarmIndex < _atlasItems.Count)
                {
                    PackageItem item = _atlasItems[_nextWarmIndex++];
                    if (item == null)
                        continue;

                    itemsToWarm.Add(item);
                }
            }

            if (itemsToWarm.Count <= 0)
            {
                TryMarkCompletedIfReady();
                return;
            }

            for (int i = 0; i < itemsToWarm.Count; i++)
            {
                PackageItem item = itemsToWarm[i];
                string fileName = (item?.file ?? string.Empty).Trim();
                Stopwatch sw = Settings.LogErrors ? Stopwatch.StartNew() : null;

                try
                {
                    _ = pkg.GetItemAsset(item);
                }
                catch (Exception ex)
                {
                    if (Settings.LogErrors)
                        CMain.SaveError($"FairyGUI: MobileMainHud 贴图预热失败 file={fileName} err={ex.Message}");
                }

                int prepared = Interlocked.Increment(ref _prepared);
                if (Settings.LogErrors)
                    CMain.SaveLog($"FairyGUI: MobileMainHud 贴图预热 {prepared}/{TotalAtlases} file={fileName} Ms={sw?.ElapsedMilliseconds ?? 0}");
            }

            TryMarkCompletedIfReady();
        }

        private static void PrefetchAtlasBytesWorker()
        {
            try
            {
                if (FairyResourceLoader.Current is not FairyGuiPublishResourceLoader loader)
                    return;

                loader.TryPrefetchPackageBytes(AtlasPackageName);

                string[] atlasFiles;
                lock (Gate)
                {
                    atlasFiles = (_atlasFiles ?? new List<string>()).ToArray();
                }

                for (int i = 0; i < atlasFiles.Length; i++)
                {
                    string fileName = atlasFiles[i];
                    if (string.IsNullOrWhiteSpace(fileName))
                        continue;

                    loader.TryPrefetchTextureBytes(fileName);

                    if (Settings.LogErrors)
                        CMain.SaveLog($"FairyGUI: MobileMainHud 后台字节预取 {i + 1}/{atlasFiles.Length} file={fileName}");
                }

                if (Settings.LogErrors)
                    CMain.SaveLog($"FairyGUI: MobileMainHud 后台字节预取完成，Pkg={AtlasPackageName} Atlases={atlasFiles.Length} TotalMs={Math.Max(0, CMain.Time - _startTimeMs)}");
            }
            catch (Exception ex)
            {
                lock (Gate)
                {
                    _failed = true;
                    _lastError = ex.Message ?? string.Empty;
                }

                if (Settings.LogErrors)
                    CMain.SaveError($"FairyGUI: MobileMainHud 后台预取失败：{ex}");
            }
        }

        private static void TryMarkCompletedIfReady()
        {
            bool shouldLog = false;
            int total = 0;

            lock (Gate)
            {
                total = _atlasItems?.Count ?? 0;
                if (_completed || total <= 0 || Volatile.Read(ref _prepared) < total)
                    return;

                _completed = true;
                if (!_completionLogged)
                {
                    _completionLogged = true;
                    shouldLog = true;
                }
            }

            if (shouldLog && Settings.LogErrors)
                CMain.SaveLog($"FairyGUI: MobileMainHud 贴图预热完成，Pkg={AtlasPackageName} Atlases={total} TotalMs={Math.Max(0, CMain.Time - _startTimeMs)}");
        }
    }
}
