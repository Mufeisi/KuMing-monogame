namespace LyoCrystal.MicroGateway;

internal static class MicroLibraryReader
{
    private sealed record CachedLibrary(
        DateTime LastWriteTimeUtc,
        long FileLength,
        int Count,
        int HeaderLength,
        byte[] HeaderBytes,
        int[] IndexList);

    public static byte[]? TryCreateHeaderPayload(string filePath, int maximumPayloadBytes)
    {
        CachedLibrary? library = TryGetOrLoad(filePath, maximumPayloadBytes - 12);
        if (library is null) return null;
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(library.FileLength);
        writer.Write(library.HeaderLength);
        writer.Write(library.HeaderBytes);
        return stream.ToArray();
    }

    public static byte[]? TryCreateImagePayload(string filePath, int index, int maximumPayloadBytes)
    {
        CachedLibrary? library = TryGetOrLoad(filePath, maximumPayloadBytes);
        if (library is null || index < 0 || index >= library.Count) return null;
        int position = library.IndexList[index];
        if (position <= 0) return null;
        try
        {
            using var stream = File.OpenRead(filePath);
            using var reader = new BinaryReader(stream);
            if (position >= stream.Length) return null;
            stream.Position = position;
            _ = reader.ReadInt16(); _ = reader.ReadInt16(); _ = reader.ReadInt16();
            _ = reader.ReadInt16(); _ = reader.ReadInt16(); _ = reader.ReadInt16();
            byte shadow = reader.ReadByte();
            int imageLength = reader.ReadInt32();
            if (imageLength < 0) return null;
            long blockLength = 17L + imageLength;
            if ((shadow & 0x80) != 0)
            {
                stream.Seek(imageLength, SeekOrigin.Current);
                _ = reader.ReadInt16(); _ = reader.ReadInt16(); _ = reader.ReadInt16(); _ = reader.ReadInt16();
                int maskLength = reader.ReadInt32();
                if (maskLength < 0) return null;
                blockLength += 12L + maskLength;
            }
            if (blockLength <= 0 || blockLength > maximumPayloadBytes - 8L || position + blockLength > stream.Length) return null;
            stream.Position = position;
            byte[] image = new byte[(int)blockLength];
            stream.ReadExactly(image);
            using var payload = new MemoryStream();
            using var writer = new BinaryWriter(payload);
            writer.Write(position);
            writer.Write((int)blockLength);
            writer.Write(image);
            return payload.ToArray();
        }
        catch { return null; }
    }

    private static CachedLibrary? TryGetOrLoad(string filePath, int maximumHeaderBytes)
    {
        try
        {
            var info = new FileInfo(filePath);
            if (!info.Exists) return null;
            using var stream = File.OpenRead(filePath);
            using var reader = new BinaryReader(stream);
            int version = reader.ReadInt32();
            if (version < 2) return null;
            int count = reader.ReadInt32();
            if (count < 0 || count > (int.MaxValue - 16) / 4) return null;
            int frameSeek = 0;
            int headerLength = 8 + count * 4;
            if (version >= 3) { frameSeek = reader.ReadInt32(); headerLength += 4; }
            if (headerLength <= 0 || headerLength > maximumHeaderBytes || headerLength > stream.Length) return null;
            int[] indexes = new int[count];
            for (int i = 0; i < count; i++) indexes[i] = reader.ReadInt32();
            using var header = new MemoryStream(headerLength);
            using (var writer = new BinaryWriter(header, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(version); writer.Write(count);
                if (version >= 3) writer.Write(frameSeek);
                foreach (int value in indexes) writer.Write(value);
            }
            return new CachedLibrary(info.LastWriteTimeUtc, info.Length, count, headerLength, header.ToArray(), indexes);
        }
        catch { return null; }
    }
}
