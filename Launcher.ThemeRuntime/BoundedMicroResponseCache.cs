using System.Security.Cryptography;
using System.Text;

namespace Launcher.ThemeRuntime;

/// <summary>只缓存可重新下载的微端 HTTP 响应；容量淘汰绝不触碰 Data、Map 或 Sound。</summary>
public sealed class BoundedMicroResponseCache
{
    private static readonly byte[] Magic = "LYOMRC01"u8.ToArray();
    private readonly string _root;
    private readonly string _clientRoot;
    private readonly long _limitBytes;
    private readonly object _sync = new();

    public BoundedMicroResponseCache(string clientDirectory, int limitMiB)
    {
        _clientRoot = Path.GetFullPath(clientDirectory).TrimEnd(Path.DirectorySeparatorChar);
        _root = Path.GetFullPath(Path.Combine(_clientRoot, "Cache", "MicroResponses"));
        if (!_root.StartsWith(_clientRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("微端缓存目录越界");
        _limitBytes = Math.Clamp(limitMiB, 256, 16384) * 1024L * 1024L;
    }

    public bool TryRead(string key, TimeSpan maximumAge, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        string path = GetPath(key);
        try
        {
            if (!IsPathChainSafe()) return false;
            if (!File.Exists(path)) return false;
            var info = new FileInfo(path);
            if (info.Length is <= 40 or > 64L * 1024 * 1024 + 40 || DateTime.UtcNow - info.CreationTimeUtc > maximumAge)
            {
                File.Delete(path);
                return false;
            }
            byte[] stored = File.ReadAllBytes(path);
            if (!stored.AsSpan(0, 8).SequenceEqual(Magic)) { File.Delete(path); return false; }
            bytes = stored[40..];
            if (!CryptographicOperations.FixedTimeEquals(stored.AsSpan(8, 32), SHA256.HashData(bytes)))
            {
                File.Delete(path);
                bytes = Array.Empty<byte>();
                return false;
            }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    public bool Write(string key, byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length == 0 || bytes.LongLength > Math.Min(_limitBytes, 64L * 1024 * 1024)) return false;
        lock (_sync)
        {
            string temporary = string.Empty;
            try
            {
                if (!IsPathChainSafe()) return false;
                Directory.CreateDirectory(_root);
                if (!IsPathChainSafe()) return false;
                if (TrimCore() > _limitBytes) return false;
                string path = GetPath(key);
                temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
                byte[] stored = new byte[40 + bytes.Length];
                Magic.CopyTo(stored, 0);
                SHA256.HashData(bytes).CopyTo(stored, 8);
                bytes.CopyTo(stored, 40);
                File.WriteAllBytes(temporary, stored);
                File.Move(temporary, path, overwrite: true);
                if (TrimCore() > _limitBytes)
                {
                    try { File.Delete(path); } catch { }
                    return false;
                }
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
            finally { if (!string.IsNullOrEmpty(temporary)) try { File.Delete(temporary); } catch { } }
        }
    }

    public void Invalidate(string key)
    {
        lock (_sync) try { if (IsPathChainSafe()) File.Delete(GetPath(key)); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    public long Trim()
    {
        lock (_sync) return TrimCore();
    }

    private long TrimCore()
    {
        if (!Directory.Exists(_root)) return 0;
        if (!IsPathChainSafe()) return 0;
        foreach (string temporary in Directory.EnumerateFiles(_root, "*.tmp-*", SearchOption.TopDirectoryOnly))
            try { File.Delete(temporary); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        FileInfo[] files = Directory.EnumerateFiles(_root, "*", SearchOption.TopDirectoryOnly)
            .Select(path => new FileInfo(path))
            .Where(info => (info.Attributes & FileAttributes.ReparsePoint) == 0)
            .OrderByDescending(info => info.CreationTimeUtc)
            .ToArray();
        long retained = 0;
        foreach (FileInfo file in files)
        {
            if (retained <= _limitBytes - file.Length) retained += file.Length;
            else
            {
                try { file.Delete(); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { retained += file.Exists ? file.Length : 0; }
            }
        }
        return retained;
    }

    private string GetPath(string key)
    {
        string name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant() + ".bin";
        return Path.Combine(_root, name);
    }

    private bool IsPathChainSafe()
    {
        for (string? current = _root; current is not null && current.StartsWith(_clientRoot, StringComparison.OrdinalIgnoreCase); current = Path.GetDirectoryName(current))
        {
            if (Directory.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return false;
            if (string.Equals(current, _clientRoot, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}
