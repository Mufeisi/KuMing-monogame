using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Launcher.PlayerShell;

public sealed class PlayerExecutableBrand
{
    public string ProductName { get; init; } = "LyoCrystal 玩家入口";
    public string FileDescription { get; init; } = "LyoCrystal 玩家入口";
    public string CompanyName { get; init; } = string.Empty;
    public string LegalCopyright { get; init; } = string.Empty;
    public string FileVersion { get; init; } = "1.0.0.0";
    public string ProductVersion { get; init; } = "1.0.0.0";
    public string? IconPath { get; init; }
}

[SupportedOSPlatform("windows")]
public static class NativeExecutableBranding
{
    private const int RtIcon = 3;
    private const int RtGroupIcon = 14;
    private const int RtVersion = 16;
    private const ushort NeutralLanguage = 0;

    public static void CreateBrandedCopy(string templatePath, string outputPath, PlayerExecutableBrand brand)
    {
        ArgumentNullException.ThrowIfNull(brand);
        templatePath = Path.GetFullPath(templatePath ?? throw new ArgumentNullException(nameof(templatePath)));
        outputPath = Path.GetFullPath(outputPath ?? throw new ArgumentNullException(nameof(outputPath)));
        if (!File.Exists(templatePath)) throw new FileNotFoundException("预构建玩家外壳不存在", templatePath);
        if (File.Exists(outputPath)) throw new IOException("品牌玩家入口输出已存在，拒绝覆盖");
        ValidateText(brand.ProductName, nameof(brand.ProductName));
        ValidateText(brand.FileDescription, nameof(brand.FileDescription));
        ValidateText(brand.CompanyName, nameof(brand.CompanyName), allowEmpty: true);
        ValidateText(brand.LegalCopyright, nameof(brand.LegalCopyright), allowEmpty: true);
        Version fileVersion = ParseFourPartVersion(brand.FileVersion, nameof(brand.FileVersion));
        Version productVersion = ParseFourPartVersion(brand.ProductVersion, nameof(brand.ProductVersion));
        IconFile? icon = string.IsNullOrWhiteSpace(brand.IconPath) ? null : ReadIcon(brand.IconPath!);

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        File.Copy(templatePath, outputPath, overwrite: false);
        nint update = BeginUpdateResourceW(outputPath, false);
        if (update == 0)
        {
            File.Delete(outputPath);
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法打开玩家外壳资源");
        }

        bool committed = false;
        try
        {
            byte[] versionResource = BuildVersionResource(brand, fileVersion, productVersion);
            Update(update, RtVersion, 1, NeutralLanguage, versionResource);
            if (icon != null)
            {
                for (int index = 0; index < icon.Images.Count; index++)
                    Update(update, RtIcon, index + 1, NeutralLanguage, icon.Images[index].Data);
                Update(update, RtGroupIcon, 1, NeutralLanguage, BuildIconGroup(icon));
            }

            if (!EndUpdateResourceW(update, false))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法提交玩家外壳品牌资源");
            committed = true;
        }
        finally
        {
            if (!committed)
            {
                EndUpdateResourceW(update, true);
                if (File.Exists(outputPath)) File.Delete(outputPath);
            }
        }
    }

    private static byte[] BuildVersionResource(PlayerExecutableBrand brand, Version fileVersion, Version productVersion)
    {
        byte[] fixedInfo = new byte[52];
        uint[] words =
        [
            0xFEEF04BD, 0x00010000,
            VersionHigh(fileVersion), VersionLow(fileVersion),
            VersionHigh(productVersion), VersionLow(productVersion),
            0x0000003F, 0,
            0x00040004, 1, 0, 0, 0,
        ];
        for (int index = 0; index < words.Length; index++)
            BinaryPrimitives.WriteUInt32LittleEndian(fixedInfo.AsSpan(index * 4, 4), words[index]);

        byte[] stringTable = BuildBlock("040904B0", 0, 1, [],
        [
            BuildString("CompanyName", brand.CompanyName),
            BuildString("FileDescription", brand.FileDescription),
            BuildString("FileVersion", brand.FileVersion),
            BuildString("InternalName", brand.ProductName),
            BuildString("LegalCopyright", brand.LegalCopyright),
            BuildString("OriginalFilename", "Player.exe"),
            BuildString("ProductName", brand.ProductName),
            BuildString("ProductVersion", brand.ProductVersion),
        ]);
        byte[] stringFileInfo = BuildBlock("StringFileInfo", 0, 1, [], [stringTable]);
        byte[] translation = new byte[4];
        BinaryPrimitives.WriteUInt16LittleEndian(translation.AsSpan(0, 2), 0x0409);
        BinaryPrimitives.WriteUInt16LittleEndian(translation.AsSpan(2, 2), 0x04B0);
        byte[] variable = BuildBlock("Translation", 4, 0, translation, []);
        byte[] variableFileInfo = BuildBlock("VarFileInfo", 0, 1, [], [variable]);
        return BuildBlock("VS_VERSION_INFO", (ushort)fixedInfo.Length, 0, fixedInfo, [stringFileInfo, variableFileInfo]);
    }

    private static byte[] BuildString(string key, string value)
    {
        byte[] bytes = Encoding.Unicode.GetBytes(value + "\0");
        return BuildBlock(key, checked((ushort)(value.Length + 1)), 1, bytes, []);
    }

    private static byte[] BuildBlock(string key, ushort valueLength, ushort type, byte[] value, IReadOnlyList<byte[]> children)
    {
        using var stream = new MemoryStream();
        stream.Write(new byte[6]);
        stream.Write(Encoding.Unicode.GetBytes(key + "\0"));
        AlignFour(stream);
        stream.Write(value);
        AlignFour(stream);
        foreach (byte[] child in children) stream.Write(child);
        if (stream.Length > ushort.MaxValue) throw new InvalidDataException("版本资源超过 64 KiB 上限");
        byte[] result = stream.ToArray();
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(0, 2), (ushort)result.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(2, 2), valueLength);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(4, 2), type);
        return result;
    }

    private static void AlignFour(Stream stream)
    {
        while ((stream.Position & 3) != 0) stream.WriteByte(0);
    }

    private static IconFile ReadIcon(string path)
    {
        path = Path.GetFullPath(path);
        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.Length < 6 || BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(0, 2)) != 0 ||
            BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(2, 2)) != 1)
            throw new InvalidDataException("ICO 文件头无效");
        int count = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(4, 2));
        if (count is <= 0 or > 64 || bytes.Length < 6 + count * 16) throw new InvalidDataException("ICO 图像数量无效");
        var images = new List<IconImage>(count);
        for (int index = 0; index < count; index++)
        {
            ReadOnlySpan<byte> entry = bytes.AsSpan(6 + index * 16, 16);
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(entry.Slice(8, 4));
            uint offset = BinaryPrimitives.ReadUInt32LittleEndian(entry.Slice(12, 4));
            if (size == 0 || offset > bytes.Length || size > bytes.Length - offset) throw new InvalidDataException("ICO 图像边界无效");
            images.Add(new IconImage(
                entry[0], entry[1], entry[2], entry[3],
                BinaryPrimitives.ReadUInt16LittleEndian(entry.Slice(4, 2)),
                BinaryPrimitives.ReadUInt16LittleEndian(entry.Slice(6, 2)),
                bytes.AsSpan((int)offset, (int)size).ToArray()));
        }
        return new IconFile(images);
    }

    private static byte[] BuildIconGroup(IconFile icon)
    {
        byte[] group = new byte[6 + icon.Images.Count * 14];
        BinaryPrimitives.WriteUInt16LittleEndian(group.AsSpan(2, 2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(group.AsSpan(4, 2), (ushort)icon.Images.Count);
        for (int index = 0; index < icon.Images.Count; index++)
        {
            IconImage image = icon.Images[index];
            Span<byte> entry = group.AsSpan(6 + index * 14, 14);
            entry[0] = image.Width;
            entry[1] = image.Height;
            entry[2] = image.ColorCount;
            entry[3] = image.Reserved;
            BinaryPrimitives.WriteUInt16LittleEndian(entry.Slice(4, 2), image.Planes);
            BinaryPrimitives.WriteUInt16LittleEndian(entry.Slice(6, 2), image.BitCount);
            BinaryPrimitives.WriteUInt32LittleEndian(entry.Slice(8, 4), (uint)image.Data.Length);
            BinaryPrimitives.WriteUInt16LittleEndian(entry.Slice(12, 2), checked((ushort)(index + 1)));
        }
        return group;
    }

    private static unsafe void Update(nint handle, int type, int name, ushort language, byte[] data)
    {
        fixed (byte* pointer = data)
        {
            if (!UpdateResourceW(handle, (nint)type, (nint)name, language, (nint)pointer, (uint)data.Length))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法写入玩家外壳资源");
        }
    }

    private static Version ParseFourPartVersion(string value, string name)
    {
        if (!Version.TryParse(value, out Version? version) || version.Build < 0 || version.Revision < 0 ||
            version.Major > ushort.MaxValue || version.Minor > ushort.MaxValue || version.Build > ushort.MaxValue || version.Revision > ushort.MaxValue)
            throw new ArgumentException("版本必须是四段且每段为 0..65535", name);
        return version;
    }

    private static void ValidateText(string value, string name, bool allowEmpty = false)
    {
        if ((!allowEmpty && string.IsNullOrWhiteSpace(value)) || value == null || value.Length > 256 || value.Contains('\0'))
            throw new ArgumentException("品牌文本为空、过长或包含 NUL", name);
    }

    private static uint VersionHigh(Version version) => ((uint)version.Major << 16) | (uint)version.Minor;
    private static uint VersionLow(Version version) => ((uint)version.Build << 16) | (uint)version.Revision;

    private sealed record IconFile(List<IconImage> Images);
    private sealed record IconImage(byte Width, byte Height, byte ColorCount, byte Reserved, ushort Planes, ushort BitCount, byte[] Data);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint BeginUpdateResourceW(string pFileName, [MarshalAs(UnmanagedType.Bool)] bool bDeleteExistingResources);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UpdateResourceW(nint hUpdate, nint lpType, nint lpName, ushort wLanguage, nint lpData, uint cbData);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EndUpdateResourceW(nint hUpdate, [MarshalAs(UnmanagedType.Bool)] bool fDiscard);
}
