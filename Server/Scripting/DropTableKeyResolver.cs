using System.IO;

namespace Server.Scripting
{
    internal static class DropTableKeyResolver
    {
        public static bool TryResolve(string path, out string key)
        {
            key = string.Empty;

            if (string.IsNullOrWhiteSpace(path))
                return false;

            try
            {
                var dropRoot = Path.GetFullPath(Settings.DropPath);
                var fullPath = Path.GetFullPath(path);

                var relative = Path.GetRelativePath(dropRoot, fullPath);

                if (relative.StartsWith("..", StringComparison.Ordinal) ||
                    relative.StartsWith("../", StringComparison.Ordinal) ||
                    relative.StartsWith("..\\", StringComparison.Ordinal))
                {
                    return false;
                }

                relative = relative.Replace('\\', '/');

                if (relative.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                {
                    relative = relative.Substring(0, relative.Length - 4);
                }

                if (string.IsNullOrWhiteSpace(relative) || relative == ".")
                    return false;

                return LogicKey.TryNormalize($"Drops/{relative}", out key);
            }
            catch
            {
                return false;
            }
        }
    }
}

