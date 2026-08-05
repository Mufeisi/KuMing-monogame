using System.Collections.Concurrent;

namespace Server.Library.Utils
{
    internal static class MicroLibraryReader
    {
        private sealed class CachedLibrary
        {
            public DateTime LastWriteTimeUtc;
            public long FileLength;
            public int Version;
            public int Count;
            public int HeaderLength;
            public byte[] HeaderBytes;
            public int[] IndexList;
        }

        private static readonly ConcurrentDictionary<string, CachedLibrary> Cache =
            new ConcurrentDictionary<string, CachedLibrary>(StringComparer.OrdinalIgnoreCase);

        public static byte[] TryCreateHeaderPayload(string filePath)
        {
            var library = TryGetOrLoadLibrary(filePath);
            if (library == null)
                return null;

            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            writer.Write(library.FileLength);
            writer.Write(library.HeaderLength);
            writer.Write(library.HeaderBytes);
            writer.Flush();
            return ms.ToArray();
        }

        public static byte[] TryCreateImagePayload(string filePath, int index)
        {
            var library = TryGetOrLoadLibrary(filePath);
            if (library == null)
                return null;

            if (index < 0 || index >= library.Count)
                return null;

            var position = library.IndexList[index];
            if (position <= 0)
                return null;

            try
            {
                using var stream = File.OpenRead(filePath);
                using var reader = new BinaryReader(stream);

                if (position >= stream.Length)
                    return null;

                stream.Seek(position, SeekOrigin.Begin);

                // Layer 1 header (17 bytes)
                _ = reader.ReadInt16(); // Width
                _ = reader.ReadInt16(); // Height
                _ = reader.ReadInt16(); // X
                _ = reader.ReadInt16(); // Y
                _ = reader.ReadInt16(); // ShadowX
                _ = reader.ReadInt16(); // ShadowY
                var shadow = reader.ReadByte();
                var imageLength = reader.ReadInt32();

                if (imageLength < 0)
                    return null;

                bool hasMask = (shadow & 0x80) != 0;

                long blockLength = 17L + imageLength;

                if (hasMask)
                {
                    // Skip layer 1 bytes, then parse layer 2 header (12 bytes) to get mask byte length.
                    stream.Seek(imageLength, SeekOrigin.Current);

                    _ = reader.ReadInt16(); // MaskWidth
                    _ = reader.ReadInt16(); // MaskHeight
                    _ = reader.ReadInt16(); // MaskX
                    _ = reader.ReadInt16(); // MaskY
                    var maskLength = reader.ReadInt32();

                    if (maskLength < 0)
                        return null;

                    blockLength += 12L + maskLength;
                }

                if (blockLength <= 0 || blockLength > int.MaxValue)
                    return null;

                if ((long)position + blockLength > stream.Length)
                    return null;

                stream.Seek(position, SeekOrigin.Begin);
                var imageBytes = new byte[blockLength];
                var read = stream.Read(imageBytes, 0, (int)blockLength);
                if (read != (int)blockLength)
                    return null;

                using var payloadStream = new MemoryStream();
                using var writer = new BinaryWriter(payloadStream);
                writer.Write(position);
                writer.Write((int)blockLength);
                writer.Write(imageBytes);
                writer.Flush();
                return payloadStream.ToArray();
            }
            catch
            {
                return null;
            }
        }

        private static CachedLibrary TryGetOrLoadLibrary(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return null;

            try
            {
                var info = new FileInfo(filePath);
                if (!info.Exists)
                    return null;

                var lastWriteTimeUtc = info.LastWriteTimeUtc;
                var length = info.Length;

                if (Cache.TryGetValue(filePath, out var cached)
                    && cached.FileLength == length
                    && cached.LastWriteTimeUtc == lastWriteTimeUtc)
                {
                    return cached;
                }

                var loaded = LoadLibrary(filePath, length, lastWriteTimeUtc);
                if (loaded == null)
                    return null;

                Cache[filePath] = loaded;
                return loaded;
            }
            catch
            {
                return null;
            }
        }

        private static CachedLibrary LoadLibrary(string filePath, long fileLength, DateTime lastWriteTimeUtc)
        {
            using var stream = File.OpenRead(filePath);
            using var reader = new BinaryReader(stream);

            var version = reader.ReadInt32();
            if (version < 2)
                return null;

            var count = reader.ReadInt32();
            if (count < 0)
                return null;

            if (count > (int.MaxValue - 16) / 4)
                return null;

            int frameSeek = 0;
            var headerLength = 8 + (count * 4);

            if (version >= 3)
            {
                frameSeek = reader.ReadInt32();
                headerLength += 4;
            }

            if (headerLength <= 0 || headerLength > stream.Length)
                return null;

            var indexList = new int[count];
            for (var i = 0; i < count; i++)
            {
                indexList[i] = reader.ReadInt32();
            }

            byte[] headerBytes;
            using (var headerStream = new MemoryStream(headerLength))
            using (var headerWriter = new BinaryWriter(headerStream))
            {
                headerWriter.Write(version);
                headerWriter.Write(count);
                if (version >= 3)
                    headerWriter.Write(frameSeek);
                for (var i = 0; i < indexList.Length; i++)
                    headerWriter.Write(indexList[i]);
                headerWriter.Flush();
                headerBytes = headerStream.ToArray();
            }

            if (headerBytes.Length != headerLength)
                return null;

            return new CachedLibrary
            {
                LastWriteTimeUtc = lastWriteTimeUtc,
                FileLength = fileLength,
                Version = version,
                Count = count,
                HeaderLength = headerLength,
                HeaderBytes = headerBytes,
                IndexList = indexList,
            };
        }
    }
}

