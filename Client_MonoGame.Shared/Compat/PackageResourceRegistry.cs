using System;
using System.Collections.Generic;
using System.IO;

namespace MonoShare;

internal static class PackageResourceRegistry
{
    private static readonly object Gate = new();
    private static Func<string, Stream?>? _streamOpener;
    private static readonly IReadOnlyDictionary<string, string> SharedSoundAliasByFileName =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["UILoadingRes_r7r823.wav"] = "Sound/Log-in-long2.wav",
            ["UIRes_vth6j.wav"] = "Sound/sellect-loop2.wav",
        };
    private static readonly (string SourcePrefix, string[] Aliases)[] PathAliasRules =
    {
        ("Assets/UI/复古/自定义组件_fui.bytes", new[] { "Assets/UI/fui-retro/custom-components_fui.bytes", "Assets/UI/澶嶅彜/自定义组件_fui.bytes" }),
        ("Assets/UI/fui-retro/custom-components_fui.bytes", new[] { "Assets/UI/复古/自定义组件_fui.bytes", "Assets/UI/澶嶅彜/自定义组件_fui.bytes" }),
        ("Assets/UI/澶嶅彜/自定义组件_fui.bytes", new[] { "Assets/UI/复古/自定义组件_fui.bytes", "Assets/UI/fui-retro/custom-components_fui.bytes" }),
        ("Assets/UI/复古/", new[] { "Assets/UI/fui-retro/", "Assets/UI/澶嶅彜/" }),
        ("Assets/UI/fui-retro/", new[] { "Assets/UI/复古/", "Assets/UI/澶嶅彜/" }),
        ("Assets/UI/澶嶅彜/", new[] { "Assets/UI/复古/", "Assets/UI/fui-retro/" }),
    };

    public static void Configure(Func<string, Stream?> streamOpener)
    {
        lock (Gate)
        {
            _streamOpener = streamOpener;
        }
    }

    public static Stream OpenRequired(string relativePath)
    {
        if (TryOpen(relativePath, out Stream? stream) && stream != null)
            return stream;

        throw new FileNotFoundException($"未找到包内资源：{Normalize(relativePath)}", Normalize(relativePath));
    }

    public static bool TryOpen(string relativePath, out Stream? stream)
    {
        stream = null;

        string normalizedPath = Normalize(relativePath);
        if (string.IsNullOrWhiteSpace(normalizedPath))
            return false;

        string[] candidates = ExpandCandidates(normalizedPath);

        Func<string, Stream?>? opener;
        lock (Gate)
        {
            opener = _streamOpener;
        }

        if (opener != null)
        {
            for (int i = 0; i < candidates.Length; i++)
            {
                try
                {
                    stream = opener(candidates[i]);
                    if (stream != null)
                        return true;
                }
                catch
                {
                }
            }
        }

        for (int i = 0; i < candidates.Length; i++)
        {
            string candidatePath = candidates[i].Replace('/', Path.DirectorySeparatorChar);
            string[] probes =
            {
                Path.Combine(AppContext.BaseDirectory, candidatePath),
                Path.Combine(Environment.CurrentDirectory, candidatePath),
            };

            for (int probeIndex = 0; probeIndex < probes.Length; probeIndex++)
            {
                string probe = probes[probeIndex];
                if (!File.Exists(probe))
                    continue;

                stream = File.OpenRead(probe);
                return true;
            }
        }

        return false;
    }

    public static bool TryResolveSharedSoundAliasPath(string relativePath, out string aliasRelativePath)
    {
        aliasRelativePath = string.Empty;

        string normalizedPath = Normalize(relativePath);
        if (string.IsNullOrWhiteSpace(normalizedPath))
            return false;

        string fileName = Path.GetFileName(normalizedPath);
        if (string.IsNullOrWhiteSpace(fileName))
            return false;

        if (!SharedSoundAliasByFileName.TryGetValue(fileName, out string? aliasPath) || string.IsNullOrWhiteSpace(aliasPath))
            return false;

        aliasRelativePath = Normalize(aliasPath);
        return true;
    }

    public static bool TryEnsureSharedSoundAliasAvailable(string relativePath, out string availablePath)
    {
        availablePath = string.Empty;

        if (!TryResolveSharedSoundAliasPath(relativePath, out string aliasRelativePath))
            return false;

        string aliasAbsolutePath = ClientResourceLayout.ResolvePath(aliasRelativePath);
        if (File.Exists(aliasAbsolutePath))
        {
            availablePath = aliasAbsolutePath;
            return true;
        }

        if (ClientResourceLayout.TryHydrateFileFromPackage(aliasRelativePath, out string hydratedPath) && File.Exists(hydratedPath))
        {
            availablePath = hydratedPath;
            return true;
        }

        return false;
    }

    private static string Normalize(string relativePath)
    {
        return string.IsNullOrWhiteSpace(relativePath)
            ? string.Empty
            : relativePath.Replace('\\', '/').TrimStart('/');
    }

    private static string[] ExpandCandidates(string normalizedPath)
    {
        for (int i = 0; i < PathAliasRules.Length; i++)
        {
            (string sourcePrefix, string[] aliases) = PathAliasRules[i];
            if (!normalizedPath.StartsWith(sourcePrefix, StringComparison.OrdinalIgnoreCase))
                continue;

            string suffix = normalizedPath.Substring(sourcePrefix.Length);
            var candidates = new List<string>(aliases.Length + 1) { normalizedPath };
            for (int aliasIndex = 0; aliasIndex < aliases.Length; aliasIndex++)
            {
                string aliasPath = aliases[aliasIndex] + suffix;
                bool exists = false;
                for (int existingIndex = 0; existingIndex < candidates.Count; existingIndex++)
                {
                    if (!string.Equals(candidates[existingIndex], aliasPath, StringComparison.OrdinalIgnoreCase))
                        continue;

                    exists = true;
                    break;
                }

                if (!exists)
                    candidates.Add(aliasPath);
            }

            return candidates.ToArray();
        }

        return new[] { normalizedPath };
    }
}
