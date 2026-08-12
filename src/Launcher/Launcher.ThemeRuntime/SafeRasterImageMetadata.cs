using System.Buffers.Binary;

namespace Launcher.ThemeRuntime;

internal static class SafeRasterImageMetadata
{
    public static bool TryGetDimensions(ReadOnlySpan<byte> data, out int width, out int height)
    {
        width = height = 0;
        if (data.Length >= 24 && data[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
        {
            width = BinaryPrimitives.ReadInt32BigEndian(data[16..20]); height = BinaryPrimitives.ReadInt32BigEndian(data[20..24]);
            return width > 0 && height > 0;
        }
        if (data.Length >= 26 && data[0] == (byte)'B' && data[1] == (byte)'M')
        {
            width = BinaryPrimitives.ReadInt32LittleEndian(data[18..22]);
            int signedHeight = BinaryPrimitives.ReadInt32LittleEndian(data[22..26]); height = signedHeight == int.MinValue ? 0 : Math.Abs(signedHeight);
            return width > 0 && height > 0;
        }
        if (data.Length < 4 || data[0] != 0xFF || data[1] != 0xD8) return false;
        int offset = 2;
        while (offset + 4 <= data.Length)
        {
            if (data[offset++] != 0xFF) continue;
            byte marker; do { if (offset >= data.Length) return false; marker = data[offset++]; } while (marker == 0xFF);
            if (marker is 0xD8 or 0xD9 || marker is >= 0xD0 and <= 0xD7) continue;
            if (offset + 2 > data.Length) return false;
            int length = BinaryPrimitives.ReadUInt16BigEndian(data[offset..(offset + 2)]);
            if (length < 2 || offset + length > data.Length) return false;
            if (IsStartOfFrame(marker) && length >= 7)
            {
                height = BinaryPrimitives.ReadUInt16BigEndian(data[(offset + 3)..(offset + 5)]);
                width = BinaryPrimitives.ReadUInt16BigEndian(data[(offset + 5)..(offset + 7)]);
                return width > 0 && height > 0;
            }
            offset += length;
        }
        return false;
    }

    private static bool IsStartOfFrame(byte marker) => marker is >= 0xC0 and <= 0xC3 or >= 0xC5 and <= 0xC7 or >= 0xC9 and <= 0xCB or >= 0xCD and <= 0xCF;
}
