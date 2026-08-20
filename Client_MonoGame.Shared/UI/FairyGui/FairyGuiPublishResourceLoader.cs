using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using FairyGUI;

namespace MonoShare
{
    internal sealed class FairyGuiPublishResourceLoader : IFairyResourceLoader
    {
        private readonly string _variant;
        private readonly string _variantRootRelativePath;
        private readonly ConcurrentDictionary<string, byte[]> _prefetchedBytes =
            new ConcurrentDictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

        public FairyGuiPublishResourceLoader(string variant)
        {
            if (string.IsNullOrWhiteSpace(variant))
                throw new ArgumentException("variant 不能为空", nameof(variant));

            _variant = variant;
            _variantRootRelativePath = Path.Combine("Assets", "UI", _variant);
        }

        public byte[] ReadPackageBytes(string packageName)
        {
            if (string.IsNullOrWhiteSpace(packageName))
                throw new ArgumentException("packageName 不能为空", nameof(packageName));

            string fileName = packageName.EndsWith("_fui.bytes", StringComparison.OrdinalIgnoreCase)
                ? packageName
                : packageName + "_fui.bytes";

            return ReadAllBytesFromVariantDirectory(fileName);
        }

        public Stream OpenTextureStream(string textureName)
        {
            if (string.IsNullOrWhiteSpace(textureName))
                throw new ArgumentException("textureName 不能为空", nameof(textureName));

            string fileName = EnsureExtension(textureName, ".png");
            return OpenReadFromVariantDirectory(fileName);
        }

        public Stream OpenSoundStream(string soundName)
        {
            if (string.IsNullOrWhiteSpace(soundName))
                throw new ArgumentException("soundName 不能为空", nameof(soundName));

            string fileName = EnsureExtension(soundName, ".wav");
            try
            {
                return OpenReadFromVariantDirectory(fileName);
            }
            catch (FileNotFoundException) when (TryGetSharedSoundAliasPath(fileName, out string aliasPath))
            {
                return OpenReadFromClientPath(aliasPath, "BootstrapAssets/" + aliasPath.Replace('\\', '/'));
            }
        }

        public Stream OpenBinaryStream(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                throw new ArgumentException("fileName 不能为空", nameof(fileName));

            string[] candidates =
            {
                fileName,
                EnsureExtension(fileName, ".bytes"),
            };

            for (int i = 0; i < candidates.Length; i++)
            {
                try
                {
                    return OpenReadFromVariantDirectory(candidates[i]);
                }
                catch (FileNotFoundException)
                {
                }
            }

            return OpenReadFromVariantDirectory(fileName);
        }

        private Stream OpenReadFromVariantDirectory(string fileName)
        {
            string relativePath = Path.Combine(_variantRootRelativePath, fileName);
            string cacheKey = NormalizeCacheKey(relativePath);
            if (_prefetchedBytes.TryGetValue(cacheKey, out byte[] bytes) && bytes != null && bytes.Length > 0)
                return new MemoryStream(bytes, writable: false);

            return OpenReadFromClientPath(relativePath, relativePath.Replace('\\', '/'));
        }

        private static Stream OpenReadFromClientPath(string relativePath, params string[] titleContainerCandidates)
        {
            return ClientResourceLayout.OpenReadStream(relativePath, titleContainerCandidates);
        }

        private static bool TryGetSharedSoundAliasPath(string fileName, out string aliasPath)
        {
            return PackageResourceRegistry.TryResolveSharedSoundAliasPath(fileName, out aliasPath);
        }

        private byte[] ReadAllBytesFromVariantDirectory(string fileName)
        {
            string relativePath = Path.Combine(_variantRootRelativePath, fileName);
            string cacheKey = NormalizeCacheKey(relativePath);
            if (_prefetchedBytes.TryGetValue(cacheKey, out byte[] cachedBytes) && cachedBytes != null && cachedBytes.Length > 0)
                return cachedBytes;

            using Stream stream = OpenReadFromClientPath(relativePath, relativePath.Replace('\\', '/'));
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            byte[] bytes = ms.ToArray();
            if (bytes.Length > 0)
                _prefetchedBytes.TryAdd(cacheKey, bytes);

            return bytes;
        }

        internal bool TryPrefetchPackageBytes(string packageName)
        {
            if (string.IsNullOrWhiteSpace(packageName))
                return false;

            string fileName = packageName.EndsWith("_fui.bytes", StringComparison.OrdinalIgnoreCase)
                ? packageName
                : packageName + "_fui.bytes";

            return TryPrefetchVariantFile(fileName);
        }

        internal bool TryPrefetchTextureBytes(string textureName)
        {
            if (string.IsNullOrWhiteSpace(textureName))
                return false;

            return TryPrefetchVariantFile(EnsureExtension(textureName, ".png"));
        }

        internal bool TryPrefetchSoundBytes(string soundName)
        {
            if (string.IsNullOrWhiteSpace(soundName))
                return false;

            string fileName = EnsureExtension(soundName, ".wav");
            if (TryPrefetchVariantFile(fileName))
                return true;

            if (TryGetSharedSoundAliasPath(fileName, out string aliasPath))
                return TryPrefetchClientPath(aliasPath, "BootstrapAssets/" + aliasPath.Replace('\\', '/'));

            return false;
        }

        private bool TryPrefetchVariantFile(string fileName)
        {
            string relativePath = Path.Combine(_variantRootRelativePath, fileName);
            return TryPrefetchClientPath(relativePath, relativePath.Replace('\\', '/'));
        }

        private bool TryPrefetchClientPath(string relativePath, params string[] titleContainerCandidates)
        {
            string cacheKey = NormalizeCacheKey(relativePath);
            if (_prefetchedBytes.ContainsKey(cacheKey))
                return true;

            try
            {
                using Stream stream = OpenReadFromClientPath(relativePath, titleContainerCandidates);
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                byte[] bytes = ms.ToArray();
                if (bytes.Length <= 0)
                    return false;

                _prefetchedBytes.TryAdd(cacheKey, bytes);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string EnsureExtension(string fileName, string extension)
        {
            if (fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                return fileName;

            return fileName + extension;
        }

        private static string NormalizeCacheKey(string relativePath)
        {
            return (relativePath ?? string.Empty)
                .Replace('\\', '/')
                .TrimStart('/');
        }
    }
}
