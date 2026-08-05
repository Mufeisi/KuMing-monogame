using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace MonoShare
{
    internal static class BootstrapPackageUpdateRuntime
    {
        private static readonly object Gate = new object();
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        private static readonly JsonSerializerOptions JsonReadOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        private static readonly JsonSerializerOptions JsonWriteOptions = new JsonSerializerOptions { WriteIndented = true };

        public const string PackageIndexFileName = "bootstrap-package-index.json";
        public const string BundleDownloadMetaFileName = "bundle-download-meta.json";

        public static BootstrapPackageUpdateQueueView LoadUpdateQueue()
        {
            lock (Gate)
            {
                if (!File.Exists(ClientResourceLayout.PackageUpdateQueuePath))
                    return new BootstrapPackageUpdateQueueView();

                try
                {
                    var queue = JsonSerializer.Deserialize<BootstrapPackageUpdateQueueView>(
                                    File.ReadAllText(ClientResourceLayout.PackageUpdateQueuePath),
                                    JsonReadOptions)
                                ?? new BootstrapPackageUpdateQueueView();

                    queue.Packages ??= new List<BootstrapPackageUpdateEntryView>();
                    queue.Packages = queue.Packages
                        .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Name))
                        .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                        .Select(group => group.Last())
                        .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    return queue;
                }
                catch (Exception)
                {
                    return new BootstrapPackageUpdateQueueView();
                }
            }
        }

        public static HashSet<string> GetUpdatePackageNames()
        {
            BootstrapPackageUpdateQueueView queue = LoadUpdateQueue();
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (BootstrapPackageUpdateEntryView entry in queue.Packages ?? new List<BootstrapPackageUpdateEntryView>())
            {
                if (!string.IsNullOrWhiteSpace(entry?.Name))
                    set.Add(entry.Name);
            }

            return set;
        }

        public static bool TryGetUpdateDesiredSha256(string packageName, out string desiredSha256)
        {
            desiredSha256 = string.Empty;
            if (string.IsNullOrWhiteSpace(packageName))
                return false;

            BootstrapPackageUpdateQueueView queue = LoadUpdateQueue();
            BootstrapPackageUpdateEntryView match = (queue.Packages ?? new List<BootstrapPackageUpdateEntryView>())
                .LastOrDefault(item => string.Equals(item?.Name, packageName, StringComparison.OrdinalIgnoreCase));

            desiredSha256 = (match?.DesiredSha256 ?? string.Empty).Trim();
            return !string.IsNullOrWhiteSpace(desiredSha256);
        }

        public static void ReplaceUpdateQueue(string resourceVersion, IEnumerable<BootstrapPackageUpdateEntryView> packages)
        {
            if (packages == null)
                packages = Array.Empty<BootstrapPackageUpdateEntryView>();

            lock (Gate)
            {
                var queue = new BootstrapPackageUpdateQueueView
                {
                    CreatedAtUtc = DateTime.UtcNow.ToString("o"),
                    ResourceVersion = resourceVersion ?? string.Empty,
                    Packages = packages
                        .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Name))
                        .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                        .Select(group => NormalizeUpdateEntry(group.Last()))
                        .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                        .ToList()
                };

                WriteJsonFileAtomic(ClientResourceLayout.PackageUpdateQueuePath, queue);
            }
        }

        public static void RemovePackagesFromUpdateQueue(IEnumerable<string> packageNames)
        {
            if (packageNames == null)
                return;

            lock (Gate)
            {
                BootstrapPackageUpdateQueueView queue = LoadUpdateQueue();
                if (queue.Packages == null || queue.Packages.Count == 0)
                    return;

                var removeSet = new HashSet<string>(
                    packageNames.Where(item => !string.IsNullOrWhiteSpace(item)).Select(item => item.Trim()),
                    StringComparer.OrdinalIgnoreCase);

                if (removeSet.Count == 0)
                    return;

                int before = queue.Packages.Count;
                queue.Packages = queue.Packages
                    .Where(item => item != null && !removeSet.Contains(item.Name))
                    .ToList();

                if (queue.Packages.Count == before)
                    return;

                WriteJsonFileAtomic(ClientResourceLayout.PackageUpdateQueuePath, queue);
            }
        }

        public static BootstrapPackageVersionsSnapshotView LoadInstalledVersions()
        {
            lock (Gate)
            {
                if (!File.Exists(ClientResourceLayout.PackageVersionsPath))
                    return new BootstrapPackageVersionsSnapshotView();

                try
                {
                    var snapshot = JsonSerializer.Deserialize<BootstrapPackageVersionsSnapshotView>(
                                       File.ReadAllText(ClientResourceLayout.PackageVersionsPath),
                                       JsonReadOptions)
                                   ?? new BootstrapPackageVersionsSnapshotView();

                    snapshot.Packages ??= new List<BootstrapPackageVersionEntryView>();
                    snapshot.Packages = snapshot.Packages
                        .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Name))
                        .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                        .Select(group => group.Last())
                        .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    return snapshot;
                }
                catch (Exception)
                {
                    return new BootstrapPackageVersionsSnapshotView();
                }
            }
        }

        public static string GetInstalledSha256(string packageName)
        {
            if (string.IsNullOrWhiteSpace(packageName))
                return string.Empty;

            BootstrapPackageVersionsSnapshotView snapshot = LoadInstalledVersions();
            BootstrapPackageVersionEntryView match = (snapshot.Packages ?? new List<BootstrapPackageVersionEntryView>())
                .LastOrDefault(item => string.Equals(item?.Name, packageName, StringComparison.OrdinalIgnoreCase));

            return (match?.Sha256 ?? string.Empty).Trim();
        }

        public static HashSet<string> GetInstalledPackageNames()
        {
            BootstrapPackageVersionsSnapshotView snapshot = LoadInstalledVersions();
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (BootstrapPackageVersionEntryView entry in snapshot.Packages ?? new List<BootstrapPackageVersionEntryView>())
            {
                if (!string.IsNullOrWhiteSpace(entry?.Name))
                    set.Add(entry.Name);
            }

            return set;
        }

        public static void UpsertInstalledVersion(string packageName, string sha256, string source)
        {
            if (string.IsNullOrWhiteSpace(packageName))
                return;

            lock (Gate)
            {
                BootstrapPackageVersionsSnapshotView snapshot = LoadInstalledVersions();
                snapshot.Packages ??= new List<BootstrapPackageVersionEntryView>();

                string normalizedName = packageName.Trim();
                string normalizedSha = (sha256 ?? string.Empty).Trim();
                string normalizedSource = (source ?? string.Empty).Trim();

                BootstrapPackageVersionEntryView existing = snapshot.Packages
                    .LastOrDefault(item => string.Equals(item?.Name, normalizedName, StringComparison.OrdinalIgnoreCase));

                if (existing == null)
                {
                    snapshot.Packages.Add(new BootstrapPackageVersionEntryView
                    {
                        Name = normalizedName,
                        Sha256 = normalizedSha,
                        Source = normalizedSource,
                        InstalledAtUtc = DateTime.UtcNow.ToString("o"),
                    });
                }
                else
                {
                    existing.Sha256 = normalizedSha;
                    existing.Source = normalizedSource;
                    existing.InstalledAtUtc = DateTime.UtcNow.ToString("o");
                }

                snapshot.GeneratedAtUtc = DateTime.UtcNow.ToString("o");
                snapshot.Packages = snapshot.Packages
                    .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Name))
                    .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.Last())
                    .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                WriteJsonFileAtomic(ClientResourceLayout.PackageVersionsPath, snapshot);
            }
        }

        public static bool TryReadBundleDownloadMeta(string bundleDirectory, out BundleDownloadMetaView meta)
        {
            meta = null;
            if (string.IsNullOrWhiteSpace(bundleDirectory))
                return false;

            string path = Path.Combine(bundleDirectory, BundleDownloadMetaFileName);
            if (!File.Exists(path))
                return false;

            try
            {
                meta = JsonSerializer.Deserialize<BundleDownloadMetaView>(File.ReadAllText(path), JsonReadOptions);
                return meta != null && !string.IsNullOrWhiteSpace(meta.PackageName);
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static void TryOnBundleApplied(string bundleDirectory, BootstrapPackageApplyBundleResultView result)
        {
            if (result == null || !result.Completed)
                return;

            string[] installedPackages = (result.Install?.Packages ?? new List<BootstrapPackageInstallResultView>())
                .Where(item => item != null && item.Completed)
                .Select(item => item.PackageName)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (installedPackages.Length == 0)
                return;

            if (TryReadBundleDownloadMeta(bundleDirectory, out BundleDownloadMetaView meta))
            {
                string sha = (meta.Sha256 ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(meta.PackageName) &&
                    installedPackages.Contains(meta.PackageName, StringComparer.OrdinalIgnoreCase))
                {
                    UpsertInstalledVersion(meta.PackageName, sha, source: "download");
                }
            }

            RemovePackagesFromUpdateQueue(installedPackages);
        }

        private static BootstrapPackageUpdateEntryView NormalizeUpdateEntry(BootstrapPackageUpdateEntryView entry)
        {
            return new BootstrapPackageUpdateEntryView
            {
                Name = (entry?.Name ?? string.Empty).Trim(),
                DesiredSha256 = (entry?.DesiredSha256 ?? string.Empty).Trim(),
                Reason = (entry?.Reason ?? string.Empty).Trim(),
            };
        }

        private static void WriteJsonFileAtomic<T>(string outputPath, T payload)
        {
            if (string.IsNullOrWhiteSpace(outputPath))
                return;

            string directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            string tempPath = outputPath + ".tmp";
            string json = JsonSerializer.Serialize(payload ?? new object(), JsonWriteOptions);
            File.WriteAllText(tempPath, json ?? string.Empty, Utf8NoBom);
            File.Move(tempPath, outputPath, overwrite: true);
        }
    }

    internal sealed class BootstrapPackageUpdateQueueView
    {
        public string CreatedAtUtc { get; set; }
        public string ResourceVersion { get; set; }
        public List<BootstrapPackageUpdateEntryView> Packages { get; set; } = new List<BootstrapPackageUpdateEntryView>();
    }

    internal sealed class BootstrapPackageUpdateEntryView
    {
        public string Name { get; set; }
        public string DesiredSha256 { get; set; }
        public string Reason { get; set; }
    }

    internal sealed class BootstrapPackageVersionsSnapshotView
    {
        public string GeneratedAtUtc { get; set; }
        public List<BootstrapPackageVersionEntryView> Packages { get; set; } = new List<BootstrapPackageVersionEntryView>();
    }

    internal sealed class BootstrapPackageVersionEntryView
    {
        public string Name { get; set; }
        public string Sha256 { get; set; }
        public string Source { get; set; }
        public string InstalledAtUtc { get; set; }
    }

    internal sealed class BundleDownloadMetaView
    {
        public string PackageName { get; set; }
        public string ZipUrl { get; set; }
        public string Sha256 { get; set; }
        public string DownloadedAtUtc { get; set; }
        public string RepositoryRoot { get; set; }
    }
}

