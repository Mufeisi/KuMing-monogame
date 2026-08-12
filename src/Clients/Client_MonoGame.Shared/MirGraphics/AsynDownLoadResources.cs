using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MonoShare.MirGraphics
{
    internal sealed class AsynDownLoadResources
    {
        private static readonly Lazy<AsynDownLoadResources> Instance =
            new Lazy<AsynDownLoadResources>(() => new AsynDownLoadResources());

        private readonly object _gate = new object();
        private readonly Dictionary<string, List<Action>> _pendingCallbacks =
            new Dictionary<string, List<Action>>(StringComparer.OrdinalIgnoreCase);

        private AsynDownLoadResources()
        {
        }

        public static AsynDownLoadResources CreateInstance()
        {
            return Instance.Value;
        }

        public int PendingCount
        {
            get
            {
                lock (_gate)
                {
                    return _pendingCallbacks.Count;
                }
            }
        }

        public void Add(string resourcePath, Action callback)
        {
            if (string.IsNullOrWhiteSpace(resourcePath))
            {
                callback?.Invoke();
                return;
            }

            var normalizedPath = NormalizePath(resourcePath);
            if (File.Exists(normalizedPath))
            {
                ClientResourceLayout.MarkMissingPackageResolved(normalizedPath);
                callback?.Invoke();
                return;
            }

            if (ClientResourceLayout.TryHydrateFileFromPackage(normalizedPath, out string availablePath)
                && File.Exists(availablePath))
            {
                ClientResourceLayout.MarkMissingPackageResolved(availablePath);
                callback?.Invoke();
                return;
            }

            ClientResourceLayout.ReportMissingPackageRequest(normalizedPath);

            lock (_gate)
            {
                if (!_pendingCallbacks.TryGetValue(normalizedPath, out var callbacks))
                {
                    callbacks = new List<Action>();
                    _pendingCallbacks[normalizedPath] = callbacks;
                }

                if (callback != null && !callbacks.Contains(callback))
                    callbacks.Add(callback);
            }
        }

        public string[] GetPendingResources()
        {
            lock (_gate)
            {
                return _pendingCallbacks.Keys
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
        }

        public void TryNotify(string resourcePath)
        {
            if (string.IsNullOrWhiteSpace(resourcePath))
                return;

            var normalizedPath = NormalizePath(resourcePath);
            List<Action> callbacks = null;

            lock (_gate)
            {
                if (_pendingCallbacks.TryGetValue(normalizedPath, out callbacks))
                    _pendingCallbacks.Remove(normalizedPath);
            }

            if (callbacks == null)
                return;

            for (var i = 0; i < callbacks.Count; i++)
            {
                callbacks[i]?.Invoke();
            }
        }

        public void TryNotifyExisting()
        {
            ClientResourceLayout.TryApplyBundleInboxIfDue();
            ClientResourceLayout.ProcessPendingPackageRequestsIfDue();
            BootstrapPackageDownloader.TryDownloadPendingPackagesIfDue();

            string[] pendingPaths;

            lock (_gate)
            {
                pendingPaths = _pendingCallbacks.Keys
                    .ToArray();
            }

            var readyPaths = new List<string>();

            for (var i = 0; i < pendingPaths.Length; i++)
            {
                string pendingPath = pendingPaths[i];
                if (File.Exists(pendingPath))
                {
                    ClientResourceLayout.MarkMissingPackageResolved(pendingPath);
                    readyPaths.Add(pendingPath);
                    continue;
                }

                if (ClientResourceLayout.TryHydrateFileFromPackage(pendingPath, out string availablePath)
                    && File.Exists(availablePath))
                {
                    ClientResourceLayout.MarkMissingPackageResolved(availablePath);
                    ClientResourceLayout.RefreshPackageStateSnapshot();
                    readyPaths.Add(pendingPath);
                    continue;
                }

                ClientResourceLayout.ReportMissingPackageRequest(pendingPath);
            }

            for (var i = 0; i < readyPaths.Count; i++)
                TryNotify(readyPaths[i]);
        }

        private static string NormalizePath(string resourcePath)
        {
            if (string.IsNullOrWhiteSpace(resourcePath))
                return string.Empty;

            return resourcePath.Replace('/', Path.DirectorySeparatorChar);
        }
    }
}
