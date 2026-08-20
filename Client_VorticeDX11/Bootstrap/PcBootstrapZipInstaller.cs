using System;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace Client.Bootstrap
{
    internal static class PcBootstrapZipInstaller
    {
        public static string ExtractZipToStaging(string localZipPath, string packageName)
        {
            if (string.IsNullOrWhiteSpace(localZipPath))
                throw new ArgumentException("zip 路径为空。", nameof(localZipPath));
            if (!File.Exists(localZipPath))
                throw new FileNotFoundException("zip 文件不存在。", localZipPath);

            string safe = MakeSafeFileName(packageName);
            string stagingRoot = Path.Combine(PcBootstrapLayout.BundleStagingRoot, $"{DateTime.UtcNow:yyyyMMdd_HHmmss}_{safe}");

            if (Directory.Exists(stagingRoot))
                Directory.Delete(stagingRoot, recursive: true);

            Directory.CreateDirectory(stagingRoot);

            SafeExtractZip(localZipPath, stagingRoot);
            return stagingRoot;
        }

        public static int InstallExtractedPackageToClient(string stagingRoot, string packageName)
        {
            if (string.IsNullOrWhiteSpace(stagingRoot) || !Directory.Exists(stagingRoot))
                throw new DirectoryNotFoundException("staging 目录不存在。");

            string packRoot = Path.Combine(stagingRoot, "Packages", packageName);
            if (!Directory.Exists(packRoot))
                throw new DirectoryNotFoundException($"未检测到分包根目录：{packRoot}");

            string clientRoot = PcBootstrapLayout.ClientRoot;
            int installed = 0;

            foreach (string sourcePath in Directory.GetFiles(packRoot, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(packRoot, sourcePath);
                if (string.IsNullOrWhiteSpace(relative))
                    continue;

                string normalized = NormalizeRelativePath(relative);

                if (!IsAllowedInstallPath(normalized))
                    continue;

                if (string.Equals(normalized, "Mir2Config.ini", StringComparison.OrdinalIgnoreCase))
                    continue;

                string destPath = Path.GetFullPath(Path.Combine(clientRoot, normalized.Replace('/', Path.DirectorySeparatorChar)));
                if (!destPath.StartsWith(clientRoot, StringComparison.OrdinalIgnoreCase))
                    continue;

                string destDir = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrWhiteSpace(destDir))
                    Directory.CreateDirectory(destDir);

                File.Copy(sourcePath, destPath, overwrite: true);
                installed++;
            }

            return installed;
        }

        private static void SafeExtractZip(string zipPath, string destinationDirectory)
        {
            string destRoot = Path.GetFullPath(destinationDirectory);

            using ZipArchive archive = ZipFile.OpenRead(zipPath);
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                string entryName = entry.FullName ?? string.Empty;
                if (string.IsNullOrWhiteSpace(entryName))
                    continue;

                entryName = entryName.Replace('\\', '/');

                // directory
                if (entryName.EndsWith("/", StringComparison.Ordinal))
                    continue;

                string safeRelative = NormalizeRelativePath(entryName);
                if (string.IsNullOrWhiteSpace(safeRelative))
                    continue;

                if (safeRelative.StartsWith("../", StringComparison.Ordinal) || safeRelative.Contains("/../", StringComparison.Ordinal))
                    throw new InvalidDataException($"ZipSlip 风险路径：{entryName}");

                string targetPath = Path.GetFullPath(Path.Combine(destRoot, safeRelative.Replace('/', Path.DirectorySeparatorChar)));
                if (!targetPath.StartsWith(destRoot, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"ZipSlip 风险路径：{entryName}");

                string targetDir = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrWhiteSpace(targetDir))
                    Directory.CreateDirectory(targetDir);

                entry.ExtractToFile(targetPath, overwrite: true);
            }
        }

        private static string NormalizeRelativePath(string relative)
        {
            string normalized = (relative ?? string.Empty)
                .Replace('\\', '/')
                .TrimStart('/');

            while (normalized.StartsWith("./", StringComparison.Ordinal))
                normalized = normalized.Substring(2);

            return normalized;
        }

        private static bool IsAllowedInstallPath(string relative)
        {
            if (string.IsNullOrWhiteSpace(relative))
                return false;

            if (relative.StartsWith("Data/", StringComparison.OrdinalIgnoreCase))
                return true;
            if (relative.StartsWith("Map/", StringComparison.OrdinalIgnoreCase))
                return true;
            if (relative.StartsWith("Sound/", StringComparison.OrdinalIgnoreCase))
                return true;

            // root files
            if (!relative.Contains('/', StringComparison.Ordinal) &&
                relative.EndsWith(".ini", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        private static string MakeSafeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "unknown";

            char[] invalid = Path.GetInvalidFileNameChars();
            var filtered = name.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
            string result = new string(filtered).Trim();
            return string.IsNullOrWhiteSpace(result) ? "unknown" : result;
        }
    }
}

