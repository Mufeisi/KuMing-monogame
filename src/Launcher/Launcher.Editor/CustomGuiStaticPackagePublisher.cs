using System.IO.Compression;
using Shared.CustomGui;
using Shared.Release;

namespace LyoCrystal.LauncherEditor;

public static class CustomGuiStaticPackagePublisher
{
    public const string PackageName = "custom-gui";

    public static void Publish(
        string outputPath,
        CustomGuiRuntimeDocument document,
        CustomGuiResourceBindingsDocument bindings,
        BootstrapPackageManifestDocument resourceManifest)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(bindings);
        ArgumentNullException.ThrowIfNull(resourceManifest);

        CustomGuiResourceCatalog resources = CustomGuiResourceCatalog.FromBootstrapManifest(
            resourceManifest,
            bindings.Assets,
            bindings.Fonts);
        CustomGuiValidationPolicy.EnsureValid(document, resources);
        byte[] documentBytes = CustomGuiDocumentCodec.Serialize(document);
        byte[] bindingBytes = CustomGuiResourceBindingsCodec.Serialize(bindings);
        if (documentBytes.Length > CustomGuiValidationLimits.MaximumDocumentBytes)
            throw new CustomGuiValidationException("GUI05-LIMIT-001", "GUI 运行描述超过 512 KiB 上限");
        if (bindingBytes.Length > CustomGuiValidationLimits.MaximumResourceBindingsBytes)
            throw new CustomGuiValidationException("GUI05-LIMIT-001", "GUI 资源绑定超过 128 KiB 上限");

        string output = Path.GetFullPath(outputPath);
        string directory = Path.GetDirectoryName(output) ?? throw new InvalidDataException("GUI 包输出路径无效");
        Directory.CreateDirectory(directory);
        if (File.Exists(output)) throw new IOException("GUI 包已存在，拒绝覆盖不可变发布物");
        string temporary = output + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (ZipArchive archive = ZipFile.Open(temporary, ZipArchiveMode.Create))
            {
                WriteEntry(archive, CustomGuiPackageVerifier.DocumentEntryName, documentBytes);
                WriteEntry(archive, CustomGuiPackageVerifier.ResourceBindingsEntryName, bindingBytes);
            }
            if (new FileInfo(temporary).Length > CustomGuiValidationLimits.MaximumPackageBytes)
                throw new CustomGuiValidationException("GUI05-LIMIT-001", "GUI 包超过 32 MiB 上限");
            File.Move(temporary, output);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static void WriteEntry(ZipArchive archive, string name, byte[] bytes)
    {
        ZipArchiveEntry entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        entry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using Stream target = entry.Open();
        target.Write(bytes);
    }
}
