using System;
using System.IO;

namespace Client.Bootstrap
{
    internal static class PcBootstrapLayout
    {
        public static string ClientRoot
        {
            get
            {
                // 用于 SmokeTest/自动化：允许在不复制 exe 的情况下，将更新/安装目标指向指定目录。
                // 默认不设置该环境变量，保持原行为（以 exe 所在目录为根）。
                string overrideRoot = (Environment.GetEnvironmentVariable("LOMMIR_PC_CLIENT_ROOT") ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(overrideRoot))
                    return NormalizeRoot(overrideRoot);

                return NormalizeRoot(AppDomain.CurrentDomain.BaseDirectory);
            }
        }

        public static string BootstrapAssetsRoot => Path.Combine(ClientRoot, "BootstrapAssets");
        public static string PackageManifestDirectory => Path.Combine(BootstrapAssetsRoot, "bootstrap-package-manifests");

        public static string CacheRoot => Path.Combine(ClientRoot, "Cache", "PC");
        public static string RuntimeRoot => Path.Combine(CacheRoot, "Runtime");

        public static string DownloadsRoot => Path.Combine(CacheRoot, "Downloads");
        public static string DownloadPackagesRoot => Path.Combine(DownloadsRoot, "Packages");
        public static string BundleStagingRoot => Path.Combine(DownloadsRoot, "BundleStaging");

        public static string DeclaredPackagesPath => Path.Combine(BootstrapAssetsRoot, "bootstrap-packages.json");
        public static string BaselinePackageIndexPath => Path.Combine(BootstrapAssetsRoot, "bootstrap-package-index.json");

        public static string VersionsPath => Path.Combine(RuntimeRoot, "BootstrapPackageVersions.json");
        public static string UpdateQueuePath => Path.Combine(RuntimeRoot, "BootstrapPackageUpdateQueue.json");
        public static string RemoteIndexCachePath => Path.Combine(RuntimeRoot, "BootstrapRemotePackageIndex.json");

        public static string PreLoginUpdateLogPath => Path.Combine(RuntimeRoot, "BootstrapPreLoginUpdate.log");

        public static void EnsureWritableDirectories()
        {
            string[] directories =
            {
                BootstrapAssetsRoot,
                PackageManifestDirectory,
                CacheRoot,
                RuntimeRoot,
                DownloadsRoot,
                DownloadPackagesRoot,
                BundleStagingRoot,
            };

            for (int i = 0; i < directories.Length; i++)
            {
                Directory.CreateDirectory(directories[i]);
            }
        }

        private static string NormalizeRoot(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return Path.GetFullPath(".");

            string full = Path.GetFullPath(path);
            return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }
}
