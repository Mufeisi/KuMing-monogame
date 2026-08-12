using System.Security.Cryptography;
using System.Text;
using System.Collections.Concurrent;

namespace LyoCrystal.MicroGateway;

public sealed record MicroPayloadCacheSnapshot(long Hits, long Misses, long MemoryBytes, long DiskBytes, long MemoryLimitBytes, long DiskLimitBytes);

public sealed class MicroPayloadCache
{
    private static readonly byte[] Magic = "LYOMIC01"u8.ToArray();
    private readonly string _root;
    private readonly long _memoryLimit;
    private readonly long _diskLimit;
    private readonly object _sync = new();
    private readonly ConcurrentDictionary<string, Lazy<byte[]?>> _inflight = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MemoryEntry> _memory = new(StringComparer.Ordinal);
    private long _memoryBytes;
    private long _diskBytes;
    private long _hits;
    private long _misses;
    private readonly int _maxItemBytes;
    private readonly SemaphoreSlim _generationSlots;
    private int _diskWritable = 1;

    private sealed record MemoryEntry(byte[] Bytes, DateTime LastAccessUtc);

    public MicroPayloadCache(string resourceRoot, string cacheRoot, int memoryCacheMb, int diskCacheMb)
    {
        _root = Path.GetFullPath(cacheRoot);
        string resources = Path.GetFullPath(resourceRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string cache = _root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (cache.StartsWith(resources, StringComparison.OrdinalIgnoreCase) || resources.StartsWith(cache, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("微端缓存目录必须与资源库完全分离。");
        _memoryLimit = Math.Max(0, memoryCacheMb) * 1024L * 1024L;
        _diskLimit = Math.Max(0, diskCacheMb) * 1024L * 1024L;
        _maxItemBytes = checked((int)Math.Min(64L * 1024 * 1024, Math.Max(1024L * 1024, _memoryLimit > 0 ? _memoryLimit : 16L * 1024 * 1024)));
        _generationSlots = new SemaphoreSlim(Math.Max(1, (int)Math.Min(16, Math.Max(1, _memoryLimit / _maxItemBytes))));
        RejectReparseChain(_root);
        Directory.CreateDirectory(_root);
        RejectReparseChain(_root);
        if (!TrimDisk()) _diskWritable = 0;
    }

    public byte[]? GetOrCreate(string key, Func<int, byte[]?> factory)
    {
        string digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        if (TryReadMemory(digest, out byte[]? bytes)) { Interlocked.Increment(ref _hits); return bytes; }
        Lazy<byte[]?> pending = _inflight.GetOrAdd(digest, _ => new Lazy<byte[]?>(() =>
        {
            _generationSlots.Wait();
            try
            {
                if (TryReadDisk(digest, out byte[]? diskBytes)) { Interlocked.Increment(ref _hits); return diskBytes; }
                Interlocked.Increment(ref _misses);
                byte[]? created = factory(_maxItemBytes);
                if (created is null || created.LongLength > _maxItemBytes) return null;
                StoreMemory(digest, created);
                WriteDisk(digest, created);
                return created;
            }
            finally { _generationSlots.Release(); }
        }, LazyThreadSafetyMode.ExecutionAndPublication));
        try { return pending.Value; }
        finally { _inflight.TryRemove(new KeyValuePair<string, Lazy<byte[]?>>(digest, pending)); }
    }

    public MicroPayloadCacheSnapshot GetSnapshot() => new(
        Interlocked.Read(ref _hits), Interlocked.Read(ref _misses), Interlocked.Read(ref _memoryBytes),
        Interlocked.Read(ref _diskBytes), _memoryLimit, _diskLimit);

    private bool TryReadMemory(string key, out byte[]? bytes)
    {
        lock (_sync)
        {
            if (!_memory.TryGetValue(key, out MemoryEntry? entry)) { bytes = null; return false; }
            _memory[key] = entry with { LastAccessUtc = DateTime.UtcNow };
            bytes = entry.Bytes;
            return true;
        }
    }

    private void StoreMemory(string key, byte[] bytes)
    {
        if (_memoryLimit == 0 || bytes.LongLength > _memoryLimit) return;
        lock (_sync)
        {
            if (_memory.Remove(key, out MemoryEntry? previous)) _memoryBytes -= previous.Bytes.LongLength;
            _memory[key] = new MemoryEntry(bytes, DateTime.UtcNow);
            _memoryBytes += bytes.LongLength;
            foreach (string victim in _memory.OrderBy(pair => pair.Value.LastAccessUtc).Select(pair => pair.Key).ToArray())
            {
                if (_memoryBytes <= _memoryLimit) break;
                _memoryBytes -= _memory[victim].Bytes.LongLength;
                _memory.Remove(victim);
            }
        }
    }

    private bool TryReadDisk(string key, out byte[]? bytes)
    {
        bytes = null;
        if (_diskLimit == 0) return false;
        string path = CachePath(key);
        try
        {
            RejectReparseChain(_root);
            if (!File.Exists(path) || IsReparse(path)) return false;
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            Span<byte> magic = stackalloc byte[Magic.Length];
            input.ReadExactly(magic);
            if (!magic.SequenceEqual(Magic)) throw new InvalidDataException();
            Span<byte> lengthBytes = stackalloc byte[8];
            input.ReadExactly(lengthBytes);
            long length = BitConverter.ToInt64(lengthBytes);
            if (length < 0 || length > _diskLimit || length > _maxItemBytes || input.Length != Magic.Length + 8 + 32 + length) throw new InvalidDataException();
            byte[] expected = new byte[32];
            input.ReadExactly(expected);
            bytes = new byte[checked((int)length)];
            input.ReadExactly(bytes);
            if (!CryptographicOperations.FixedTimeEquals(expected, SHA256.HashData(bytes))) throw new InvalidDataException();
            File.SetLastAccessTimeUtc(path, DateTime.UtcNow);
            StoreMemory(key, bytes);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            try { if (File.Exists(path) && !IsReparse(path)) File.Delete(path); } catch { }
            bytes = null;
            return false;
        }
    }

    private void WriteDisk(string key, byte[] bytes)
    {
        if (_diskLimit == 0 || bytes.LongLength > _diskLimit || Volatile.Read(ref _diskWritable) == 0) return;
        string path = CachePath(key);
        string temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            RejectReparseChain(_root);
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                output.Write(Magic);
                output.Write(BitConverter.GetBytes(bytes.LongLength));
                output.Write(SHA256.HashData(bytes));
                output.Write(bytes);
                output.Flush(true);
            }
            File.Move(temporary, path, true);
            if (!TrimDisk())
            {
                Interlocked.Exchange(ref _diskWritable, 0);
                try { if (File.Exists(path) && !IsReparse(path)) File.Delete(path); } catch { }
                TrimDisk();
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
        }
    }

    private bool TrimDisk()
    {
        try
        {
            RejectReparseChain(_root);
            foreach (string temporary in Directory.EnumerateFiles(_root, "*.tmp-*"))
                if (!IsReparse(temporary)) File.Delete(temporary);
            FileInfo[] files = Directory.EnumerateFiles(_root, "*.bin").Select(path => new FileInfo(path)).Where(file => !IsReparse(file.FullName)).OrderBy(file => file.LastAccessTimeUtc).ToArray();
            long total = files.Sum(file => file.Length);
            foreach (FileInfo file in files)
            {
                if (total <= _diskLimit) break;
                long length = file.Length;
                file.Delete();
                total -= length;
            }
            total = Directory.EnumerateFiles(_root).Where(path => !IsReparse(path)).Sum(path => new FileInfo(path).Length);
            Interlocked.Exchange(ref _diskBytes, total);
            return total <= _diskLimit;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            try { Interlocked.Exchange(ref _diskBytes, Directory.EnumerateFiles(_root).Where(path => !IsReparse(path)).Sum(path => new FileInfo(path).Length)); } catch { }
            return false;
        }
    }

    private string CachePath(string key) => Path.Combine(_root, key + ".bin");
    private static bool IsReparse(string path) => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static void RejectReparseChain(string fullPath)
    {
        string root = Path.GetPathRoot(fullPath) ?? throw new InvalidOperationException("缓存路径无效。");
        string current = root;
        foreach (string segment in Path.GetRelativePath(root, fullPath).Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if ((File.Exists(current) || Directory.Exists(current)) && IsReparse(current))
                throw new InvalidOperationException("缓存路径不能包含重解析点。");
        }
    }
}
