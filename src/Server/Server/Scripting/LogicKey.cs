namespace Server.Scripting
{
    public static class LogicKey
    {
        public static bool TryNormalize(string keyOrPath, out string normalizedKey)
        {
            normalizedKey = string.Empty;

            if (keyOrPath == null) return false;

            var key = keyOrPath.Trim();
            if (key.Length == 0) return false;

            key = key.Replace('\\', '/');

            while (key.Contains("//"))
            {
                key = key.Replace("//", "/");
            }

            if (key.StartsWith("./", StringComparison.Ordinal))
            {
                key = key.Substring(2);
            }

            key = key.TrimStart('/');

            if (key.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            {
                key = key.Substring(0, key.Length - 4);
            }
            else if (key.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                key = key.Substring(0, key.Length - 3);
            }

            if (key.Length == 0) return false;
            if (key.EndsWith("/", StringComparison.Ordinal)) return false;

            if (key.StartsWith("//", StringComparison.Ordinal)) return false;

            if (key.Length >= 3 && char.IsLetter(key[0]) && key[1] == ':' && key[2] == '/')
            {
                return false;
            }

            var segments = key.Split('/');
            for (var i = 0; i < segments.Length; i++)
            {
                if (segments[i] == "..") return false;
                if (segments[i].Length == 0) return false;
            }

            normalizedKey = key.ToLowerInvariant();
            return true;
        }

        public static string NormalizeOrThrow(string keyOrPath)
        {
            if (!TryNormalize(keyOrPath, out var normalized))
                throw new ArgumentException("无效的逻辑 Key 或路径。", nameof(keyOrPath));

            return normalized;
        }
    }
}

