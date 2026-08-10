using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Launcher.PlayerShell;

public sealed record PlayerPayloadInfo(string EntryPoint, int FileCount, long CompressedSize, string Sha256);

public static class PlayerPayloadPackage
{
    public const long MaximumPlayerExecutableBytes = 80L * 1024 * 1024;
    private const string ManifestName = ".lyocrystal/player-payload.json";
    private const string ManifestFormat = "lyocrystal-player-payload-v1";
    private const int TrailerSize = 96;
    private const int FormatVersion = 1;
    private static readonly DateTimeOffset DeterministicZipTimestamp = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly byte[] TrailerMagic = CreateMagic();
    private static readonly UTF8Encoding Utf8NoBom = new(false, true);

    public static PlayerPayloadInfo Create(string shellPath, string payloadDirectory, string outputPath, string entryPoint)
    {
        shellPath = RequireExistingFile(shellPath, nameof(shellPath));
        payloadDirectory = RequireExistingDirectory(payloadDirectory, nameof(payloadDirectory));
        outputPath = Path.GetFullPath(outputPath ?? throw new ArgumentNullException(nameof(outputPath)));
        entryPoint = NormalizeRelativePath(entryPoint, nameof(entryPoint));
        if (File.Exists(outputPath)) throw new IOException("玩家入口输出已存在，拒绝覆盖");
        if (IsWithinDirectory(outputPath, payloadDirectory)) throw new ArgumentException("玩家入口输出不得位于载荷目录内", nameof(outputPath));

        List<PayloadFile> files = Directory.EnumerateFiles(payloadDirectory, "*", SearchOption.AllDirectories)
            .Select(path => CreatePayloadFile(payloadDirectory, path))
            .OrderBy(item => item.Path, StringComparer.Ordinal)
            .ToList();
        if (files.Count == 0) throw new InvalidDataException("玩家入口载荷目录为空");
        if (!files.Any(item => string.Equals(item.Path, entryPoint, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("玩家入口点不在载荷目录中");
        if (files.Any(item => string.Equals(item.Path, ManifestName, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("载荷目录使用了保留清单路径");

        string? outputDirectory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outputDirectory)) Directory.CreateDirectory(outputDirectory);
        string temporaryPath = outputPath + ".tmp-" + Guid.NewGuid().ToString("N");
        string payloadTemporaryPath = outputPath + ".payload-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var payload = new FileStream(payloadTemporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.SequentialScan))
            {
                using (var archive = new ZipArchive(payload, ZipArchiveMode.Create, leaveOpen: true, Utf8NoBom))
                {
                    foreach (PayloadFile file in files)
                    {
                        ZipArchiveEntry entry = archive.CreateEntry(file.Path, CompressionLevel.Optimal);
                        entry.LastWriteTime = DeterministicZipTimestamp;
                        using Stream destination = entry.Open();
                        using FileStream source = new(file.SourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.SequentialScan);
                        source.CopyTo(destination);
                    }

                    var manifest = new PayloadManifest
                    {
                        Format = ManifestFormat,
                        EntryPoint = entryPoint,
                        Files = files.Select(item => new PayloadManifestFile
                        {
                            Path = item.Path,
                            Size = item.Size,
                            Sha256 = item.Sha256,
                        }).ToList(),
                    };
                    ZipArchiveEntry manifestEntry = archive.CreateEntry(ManifestName, CompressionLevel.Optimal);
                    manifestEntry.LastWriteTime = DeterministicZipTimestamp;
                    using Stream manifestStream = manifestEntry.Open();
                    JsonSerializer.Serialize(manifestStream, manifest, PlayerPayloadJsonContext.Default.PayloadManifest);
                }
                payload.Flush(flushToDisk: true);
            }

            using (var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 128 * 1024, FileOptions.SequentialScan))
            {
                using (FileStream shell = File.OpenRead(shellPath)) shell.CopyTo(output);
                long payloadOffset = output.Position;
                using (var payload = new FileStream(payloadTemporaryPath, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.SequentialScan))
                {
                    payload.CopyTo(output);
                }
                long payloadLength = output.Position - payloadOffset;
                output.Flush(flushToDisk: true);
                byte[] payloadHash = HashSlice(output, payloadOffset, payloadLength);
                output.Position = output.Length;
                WriteTrailer(output, payloadOffset, payloadLength, payloadHash);
                output.Flush(flushToDisk: true);
            }

            PlayerPayloadInfo info = Verify(temporaryPath);
            if (new FileInfo(temporaryPath).Length > MaximumPlayerExecutableBytes)
                throw new InvalidDataException("玩家入口超过 80 MiB 上限");
            File.Move(temporaryPath, outputPath);
            return info;
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            if (File.Exists(payloadTemporaryPath)) File.Delete(payloadTemporaryPath);
        }
    }

    public static PlayerPayloadInfo Verify(string executablePath)
    {
        executablePath = RequireExistingFile(executablePath, nameof(executablePath));
        using FileStream executable = new(executablePath, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.RandomAccess);
        Trailer trailer = ReadTrailer(executable);
        byte[] actualHash = HashSlice(executable, trailer.Offset, trailer.Length);
        if (!CryptographicOperations.FixedTimeEquals(actualHash, trailer.Sha256))
            throw new InvalidDataException("玩家入口载荷 SHA-256 校验失败");
        PayloadManifest manifest = ReadAndValidateManifest(executable, trailer);
        return new PlayerPayloadInfo(
            manifest.EntryPoint,
            manifest.Files.Count,
            trailer.Length,
            Convert.ToHexString(actualHash).ToLowerInvariant());
    }

    public static PlayerPayloadInfo ExtractVerified(string executablePath, string destinationDirectory)
    {
        executablePath = RequireExistingFile(executablePath, nameof(executablePath));
        destinationDirectory = Path.GetFullPath(destinationDirectory ?? throw new ArgumentNullException(nameof(destinationDirectory)));
        Directory.CreateDirectory(destinationDirectory);
        string destinationPrefix = destinationDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

        using FileStream executable = new(executablePath, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.RandomAccess);
        Trailer trailer = ReadTrailer(executable);
        byte[] actualHash = HashSlice(executable, trailer.Offset, trailer.Length);
        if (!CryptographicOperations.FixedTimeEquals(actualHash, trailer.Sha256))
            throw new InvalidDataException("玩家入口载荷 SHA-256 校验失败");
        PayloadManifest manifest = ReadAndValidateManifest(executable, trailer);
        Dictionary<string, PayloadManifestFile> expected = manifest.Files.ToDictionary(item => item.Path, StringComparer.OrdinalIgnoreCase);

        using var slice = new ReadOnlySliceStream(executable, trailer.Offset, trailer.Length, leaveOpen: true);
        using var archive = new ZipArchive(slice, ZipArchiveMode.Read, leaveOpen: false, Utf8NoBom);
        var extracted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            if (string.Equals(entry.FullName, ManifestName, StringComparison.OrdinalIgnoreCase)) continue;
            string relativePath = NormalizeRelativePath(entry.FullName, "载荷条目");
            if (!expected.TryGetValue(relativePath, out PayloadManifestFile? expectedFile))
                throw new InvalidDataException("玩家入口载荷包含未登记文件");
            if (!extracted.Add(relativePath)) throw new InvalidDataException("玩家入口载荷包含重复文件");
            if (entry.Length != expectedFile.Size) throw new InvalidDataException("玩家入口载荷文件大小不一致");

            string targetPath = Path.GetFullPath(Path.Combine(destinationDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!targetPath.StartsWith(destinationPrefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("玩家入口载荷路径越出目标目录");
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            string temporaryPath = targetPath + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                using Stream source = entry.Open();
                using (var target = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.SequentialScan))
                {
                    source.CopyTo(target);
                    target.Flush(flushToDisk: true);
                }
                string hash = HashFile(temporaryPath);
                if (!string.Equals(hash, expectedFile.Sha256, StringComparison.Ordinal))
                    throw new InvalidDataException("玩家入口载荷文件 SHA-256 校验失败");
                File.Move(temporaryPath, targetPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
        }
        if (extracted.Count != expected.Count) throw new InvalidDataException("玩家入口载荷缺少清单文件");

        return new PlayerPayloadInfo(
            manifest.EntryPoint,
            manifest.Files.Count,
            trailer.Length,
            Convert.ToHexString(actualHash).ToLowerInvariant());
    }

    public static PlayerPayloadInfo VerifyExtracted(string executablePath, string destinationDirectory)
    {
        executablePath = RequireExistingFile(executablePath, nameof(executablePath));
        destinationDirectory = RequireExistingDirectory(destinationDirectory, nameof(destinationDirectory));
        string destinationPrefix = destinationDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        using FileStream executable = new(executablePath, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.RandomAccess);
        Trailer trailer = ReadTrailer(executable);
        byte[] actualHash = HashSlice(executable, trailer.Offset, trailer.Length);
        if (!CryptographicOperations.FixedTimeEquals(actualHash, trailer.Sha256))
            throw new InvalidDataException("玩家入口载荷 SHA-256 校验失败");
        PayloadManifest manifest = ReadAndValidateManifest(executable, trailer);
        foreach (PayloadManifestFile file in manifest.Files)
        {
            string path = Path.GetFullPath(Path.Combine(destinationDirectory, file.Path.Replace('/', Path.DirectorySeparatorChar)));
            if (!path.StartsWith(destinationPrefix, StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
                throw new InvalidDataException("已解包玩家载荷缺少清单文件");
            if (new FileInfo(path).Length != file.Size || !string.Equals(HashFile(path), file.Sha256, StringComparison.Ordinal))
                throw new InvalidDataException("已解包玩家载荷文件校验失败");
        }
        return new PlayerPayloadInfo(
            manifest.EntryPoint,
            manifest.Files.Count,
            trailer.Length,
            Convert.ToHexString(actualHash).ToLowerInvariant());
    }

    private static PayloadManifest ReadAndValidateManifest(FileStream executable, Trailer trailer)
    {
        using var slice = new ReadOnlySliceStream(executable, trailer.Offset, trailer.Length, leaveOpen: true);
        using var archive = new ZipArchive(slice, ZipArchiveMode.Read, leaveOpen: false, Utf8NoBom);
        ZipArchiveEntry? entry = archive.Entries.SingleOrDefault(item => string.Equals(item.FullName, ManifestName, StringComparison.Ordinal));
        if (entry == null || entry.Length <= 0 || entry.Length > 8 * 1024 * 1024)
            throw new InvalidDataException("玩家入口载荷清单缺失或过大");
        PayloadManifest manifest;
        try
        {
            using Stream stream = entry.Open();
            manifest = JsonSerializer.Deserialize(stream, PlayerPayloadJsonContext.Default.PayloadManifest)
                ?? throw new InvalidDataException("玩家入口载荷清单为空");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("玩家入口载荷清单 JSON 无效", ex);
        }
        if (!string.Equals(manifest.Format, ManifestFormat, StringComparison.Ordinal))
            throw new InvalidDataException("玩家入口载荷格式不受支持");
        manifest.EntryPoint = NormalizeRelativePath(manifest.EntryPoint, "入口点");
        if (manifest.Files is not { Count: > 0 and <= 100_000 }) throw new InvalidDataException("玩家入口载荷文件数量无效");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (PayloadManifestFile file in manifest.Files)
        {
            file.Path = NormalizeRelativePath(file.Path, "载荷文件");
            if (!names.Add(file.Path)) throw new InvalidDataException("玩家入口载荷清单包含重复文件");
            if (file.Size < 0 || file.Sha256?.Length != 64 || file.Sha256.Any(character => !Uri.IsHexDigit(character)) || file.Sha256.Any(char.IsUpper))
                throw new InvalidDataException("玩家入口载荷文件摘要无效");
        }
        if (!names.Contains(manifest.EntryPoint)) throw new InvalidDataException("玩家入口点不在载荷清单中");
        return manifest;
    }

    private static PayloadFile CreatePayloadFile(string root, string path)
    {
        FileAttributes attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("玩家入口载荷不允许重解析点");
        string relativePath = NormalizeRelativePath(Path.GetRelativePath(root, path), "载荷文件");
        var info = new FileInfo(path);
        return new PayloadFile(path, relativePath, info.Length, HashFile(path));
    }

    private static string HashFile(string path)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.SequentialScan);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static byte[] HashSlice(FileStream stream, long offset, long length)
    {
        stream.Position = offset;
        using var slice = new ReadOnlySliceStream(stream, offset, length, leaveOpen: true);
        return SHA256.HashData(slice);
    }

    private static Trailer ReadTrailer(FileStream stream)
    {
        if (stream.Length < TrailerSize + 2) throw new InvalidDataException("玩家入口没有有效载荷尾标");
        byte[] trailer = new byte[TrailerSize];
        stream.Position = stream.Length - TrailerSize;
        stream.ReadExactly(trailer);
        if (!trailer.AsSpan(0, TrailerMagic.Length).SequenceEqual(TrailerMagic))
            throw new InvalidDataException("玩家入口载荷尾标无效");
        if (BinaryPrimitives.ReadInt32LittleEndian(trailer.AsSpan(24, 4)) != FormatVersion)
            throw new InvalidDataException("玩家入口载荷版本不受支持");
        long offset = BinaryPrimitives.ReadInt64LittleEndian(trailer.AsSpan(32, 8));
        long length = BinaryPrimitives.ReadInt64LittleEndian(trailer.AsSpan(40, 8));
        if (offset < 2 || length <= 0 || offset > stream.Length - TrailerSize || length != stream.Length - TrailerSize - offset)
            throw new InvalidDataException("玩家入口载荷边界无效");
        return new Trailer(offset, length, trailer.AsSpan(48, 32).ToArray());
    }

    private static void WriteTrailer(Stream stream, long offset, long length, byte[] sha256)
    {
        Span<byte> trailer = stackalloc byte[TrailerSize];
        TrailerMagic.CopyTo(trailer);
        BinaryPrimitives.WriteInt32LittleEndian(trailer.Slice(24, 4), FormatVersion);
        BinaryPrimitives.WriteInt64LittleEndian(trailer.Slice(32, 8), offset);
        BinaryPrimitives.WriteInt64LittleEndian(trailer.Slice(40, 8), length);
        sha256.CopyTo(trailer.Slice(48, 32));
        stream.Write(trailer);
    }

    private static string NormalizeRelativePath(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("相对路径为空", parameterName);
        string normalized = value.Replace('\\', '/').Trim('/');
        if (Path.IsPathRooted(value) || normalized.Length == 0 || normalized.Split('/').Any(segment => segment is "" or "." or ".."))
            throw new InvalidDataException("玩家入口载荷包含非法相对路径");
        return normalized;
    }

    private static string RequireExistingFile(string? path, string parameterName)
    {
        string fullPath = Path.GetFullPath(path ?? throw new ArgumentNullException(parameterName));
        if (!File.Exists(fullPath)) throw new FileNotFoundException("文件不存在", fullPath);
        return fullPath;
    }

    private static string RequireExistingDirectory(string? path, string parameterName)
    {
        string fullPath = Path.GetFullPath(path ?? throw new ArgumentNullException(parameterName));
        if (!Directory.Exists(fullPath)) throw new DirectoryNotFoundException(fullPath);
        return fullPath;
    }

    private static bool IsWithinDirectory(string path, string directory)
    {
        string prefix = directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static byte[] CreateMagic()
    {
        byte[] magic = new byte[24];
        Encoding.ASCII.GetBytes("LyoCrystal.Player.v1").CopyTo(magic, 0);
        return magic;
    }

    private sealed record PayloadFile(string SourcePath, string Path, long Size, string Sha256);
    private sealed record Trailer(long Offset, long Length, byte[] Sha256);

}

internal sealed class PayloadManifest
{
    public string Format { get; set; } = string.Empty;
    public string EntryPoint { get; set; } = string.Empty;
    public List<PayloadManifestFile> Files { get; set; } = new();
}

internal sealed class PayloadManifestFile
{
    public string Path { get; set; } = string.Empty;
    public long Size { get; set; }
    public string Sha256 { get; set; } = string.Empty;
}
