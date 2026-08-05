using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Client.Bootstrap
{
    internal static class PcBootstrapUpdateRuntime
    {
        private static readonly object Gate = new object();
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        private static readonly JsonSerializerOptions JsonReadOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        private static readonly JsonSerializerOptions JsonWriteOptions = new JsonSerializerOptions { WriteIndented = true };

        public static BootstrapPackageVersionsSnapshotView LoadInstalledVersions()
        {
            lock (Gate)
            {
                if (!File.Exists(PcBootstrapLayout.VersionsPath))
                    return new BootstrapPackageVersionsSnapshotView();

                try
                {
                    return JsonSerializer.Deserialize<BootstrapPackageVersionsSnapshotView>(
                               File.ReadAllText(PcBootstrapLayout.VersionsPath),
                               JsonReadOptions)
                           ?? new BootstrapPackageVersionsSnapshotView();
                }
                catch (Exception)
                {
                    return new BootstrapPackageVersionsSnapshotView();
                }
            }
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

        public static string GetInstalledSha256(string packageName)
        {
            if (string.IsNullOrWhiteSpace(packageName))
                return string.Empty;

            BootstrapPackageVersionsSnapshotView snapshot = LoadInstalledVersions();
            BootstrapPackageVersionEntryView match = (snapshot.Packages ?? new List<BootstrapPackageVersionEntryView>())
                .LastOrDefault(item => string.Equals(item?.Name, packageName, StringComparison.OrdinalIgnoreCase));

            return (match?.Sha256 ?? string.Empty).Trim();
        }

        public static void UpsertInstalledVersion(string packageName, string sha256, string source)
        {
            if (string.IsNullOrWhiteSpace(packageName))
                return;

            string normalizedSha = (sha256 ?? string.Empty).Trim();

            lock (Gate)
            {
                BootstrapPackageVersionsSnapshotView snapshot = LoadInstalledVersions();
                snapshot.Packages ??= new List<BootstrapPackageVersionEntryView>();

                snapshot.Packages = snapshot.Packages
                    .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Name))
                    .ToList();

                snapshot.Packages.Add(new BootstrapPackageVersionEntryView
                {
                    Name = packageName.Trim(),
                    Sha256 = normalizedSha,
                    Source = (source ?? string.Empty).Trim(),
                    InstalledAtUtc = DateTime.UtcNow.ToString("o"),
                });

                snapshot.GeneratedAtUtc = DateTime.UtcNow.ToString("o");
                snapshot.Packages = snapshot.Packages
                    .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.Last())
                    .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                WriteJsonFileAtomic(PcBootstrapLayout.VersionsPath, snapshot);
            }
        }

        public static BootstrapPackageUpdateQueueView LoadUpdateQueue()
        {
            lock (Gate)
            {
                if (!File.Exists(PcBootstrapLayout.UpdateQueuePath))
                    return new BootstrapPackageUpdateQueueView();

                try
                {
                    var queue = JsonSerializer.Deserialize<BootstrapPackageUpdateQueueView>(
                                    File.ReadAllText(PcBootstrapLayout.UpdateQueuePath),
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

        public static void ReplaceUpdateQueue(string resourceVersion, IEnumerable<BootstrapPackageUpdateEntryView> packages, string message)
        {
            packages ??= Array.Empty<BootstrapPackageUpdateEntryView>();

            lock (Gate)
            {
                var queue = new BootstrapPackageUpdateQueueView
                {
                    CreatedAtUtc = DateTime.UtcNow.ToString("o"),
                    ResourceVersion = resourceVersion ?? string.Empty,
                    Message = message ?? string.Empty,
                    Packages = packages
                        .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Name))
                        .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                        .Select(group => NormalizeUpdateEntry(group.Last()))
                        .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                        .ToList()
                };

                WriteJsonFileAtomic(PcBootstrapLayout.UpdateQueuePath, queue);
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

                queue.Packages = queue.Packages
                    .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Name) && !removeSet.Contains(item.Name))
                    .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.Last())
                    .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                WriteJsonFileAtomic(PcBootstrapLayout.UpdateQueuePath, queue);
            }
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

    internal sealed class BootstrapPackageUpdateQueueView
    {
        public string CreatedAtUtc { get; set; }
        public string ResourceVersion { get; set; }
        public string Message { get; set; }
        public List<BootstrapPackageUpdateEntryView> Packages { get; set; } = new List<BootstrapPackageUpdateEntryView>();
    }

    internal sealed class BootstrapPackageUpdateEntryView
    {
        public string Name { get; set; }
        public string DesiredSha256 { get; set; }
        public string Reason { get; set; }
    }
}

