using System.IO.Compression;
using System.Text.Json;
using Launcher.ThemeRuntime;
using Shared.Security;

namespace LyoCrystal.LauncherEditor;

public static class DeploymentPackageBuilder
{
    private const string GatewayResourceName = "LyoCrystal.LauncherEditor.MicroGateway.zip";

    public static void CreateGatewayPackage(EditorProject project, string outputZip, string? microCode = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        project.SynchronizeMicroIdentity();
        project.Snapshot.TrustedReleaseKeys = project.Release.RetiredPublicKeys.TakeLast(2).Concat(new[]
        {
            new BootstrapManifestTrustedKey { KeyId = project.Release.CurrentKeyId, SubjectPublicKeyInfo = project.Release.CurrentPublicKey, NotBeforeSequence = project.Release.CurrentKeyNotBeforeSequence },
            new BootstrapManifestTrustedKey { KeyId = project.Release.NextKeyId, SubjectPublicKeyInfo = project.Release.NextPublicKey, NotBeforeSequence = project.Release.NextKeyNotBeforeSequence },
        }).Where(item => !string.IsNullOrWhiteSpace(item.KeyId) && !string.IsNullOrWhiteSpace(item.SubjectPublicKeyInfo)).ToList();
        LauncherSnapshotValidator.Validate(project.Snapshot);
        string target = Path.GetFullPath(outputZip);
        if (!target.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("微端部署包必须使用 .zip 扩展名");
        using Stream source = typeof(DeploymentPackageBuilder).Assembly.GetManifestResourceStream(GatewayResourceName)
            ?? throw new InvalidOperationException("当前编辑器未内置微端网关模板，请使用正式发布版编辑器");
        CreateGatewayPackage(project, source, outputZip, microCode, cancellationToken);
    }

    public static void CreateGatewayPackage(EditorProject project, Stream gatewayTemplateZip, string outputZip, string? microCode = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(project);
        project.SynchronizeMicroIdentity();
        project.Snapshot.TrustedReleaseKeys = project.Release.RetiredPublicKeys.TakeLast(2).Concat(new[]
        {
            new BootstrapManifestTrustedKey { KeyId = project.Release.CurrentKeyId, SubjectPublicKeyInfo = project.Release.CurrentPublicKey, NotBeforeSequence = project.Release.CurrentKeyNotBeforeSequence },
            new BootstrapManifestTrustedKey { KeyId = project.Release.NextKeyId, SubjectPublicKeyInfo = project.Release.NextPublicKey, NotBeforeSequence = project.Release.NextKeyNotBeforeSequence },
        }).Where(item => !string.IsNullOrWhiteSpace(item.KeyId) && !string.IsNullOrWhiteSpace(item.SubjectPublicKeyInfo)).ToList();
        LauncherSnapshotValidator.Validate(project.Snapshot);
        ArgumentNullException.ThrowIfNull(gatewayTemplateZip);
        if (!gatewayTemplateZip.CanRead) throw new ArgumentException("微端网关模板不可读", nameof(gatewayTemplateZip));
        string target = Path.GetFullPath(outputZip);
        if (!target.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("微端部署包必须使用 .zip 扩展名");
        string temporary = target + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {
                CopyWithCancellation(gatewayTemplateZip, output, cancellationToken); output.Position = 0;
                using var archive = new ZipArchive(output, ZipArchiveMode.Update, leaveOpen: true);
                archive.GetEntry("gateway-project.json")?.Delete();
                ZipArchiveEntry entry = archive.CreateEntry("gateway-project.json", CompressionLevel.Optimal);
                using Stream config = entry.Open();
                JsonSerializer.Serialize(config, new
                {
                    format = "lyocrystal-micro-gateway-project-v1",
                    projectId = project.Snapshot.ProjectId,
                    listenAddress = project.Gateway.ListenAddress,
                    port = project.Gateway.Port,
                    user = project.Snapshot.DefaultMicro.User,
                    resourceVersion = project.Snapshot.DefaultMicro.ResourceVersion,
                    signingIdentity = project.Snapshot.DefaultMicro.SigningIdentity,
                    resourceDirectory = project.Gateway.ResourceDirectory,
                    launcherDirectory = "LauncherPublish",
                    cacheDirectory = project.Gateway.CacheDirectory,
                    memoryCacheMb = project.Gateway.MemoryCacheMb,
                    diskCacheMb = project.Gateway.DiskCacheMb,
                    trustedReleaseKeys = project.Snapshot.TrustedReleaseKeys,
                }, new JsonSerializerOptions { WriteIndented = true });
                if (!string.IsNullOrWhiteSpace(microCode))
                {
                    archive.GetEntry("gateway-secret.import")?.Delete();
                    ZipArchiveEntry secret = archive.CreateEntry("gateway-secret.import", CompressionLevel.NoCompression);
                    using Stream secretOutput = secret.Open(); secretOutput.Write(MicroCredentialEnvelope.Create(project.Snapshot.ProjectId, microCode));
                }
                AddLauncherPublish(archive, project, project.Release.LastPublishRoot, cancellationToken);
            }
            File.Move(temporary, target, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static void AddLauncherPublish(ZipArchive archive, EditorProject project, string publishRoot, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(publishRoot)) return;
        string root = Path.GetFullPath(publishRoot), pointer = Path.Combine(root, "current.txt");
        string source = ProjectReleasePublisher.ValidateCurrentPublishedVersion(project, root);
        string version = File.ReadAllText(pointer).Trim();
        ZipArchiveEntry pointerEntry = archive.CreateEntry("LauncherPublish/current.txt", CompressionLevel.NoCompression);
        using (StreamWriter writer = new(pointerEntry.Open(), new System.Text.UTF8Encoding(false))) writer.Write(version + "\n");
        foreach (string file in Directory.EnumerateFiles(source))
        {
            cancellationToken.ThrowIfCancellationRequested();
            ZipArchiveEntry entry = archive.CreateEntry("LauncherPublish/versions/" + version + "/" + Path.GetFileName(file), CompressionLevel.Optimal);
            using Stream input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read); using Stream output = entry.Open(); CopyWithCancellation(input, output, cancellationToken);
        }
    }

    private static void CopyWithCancellation(Stream input, Stream output, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[1024 * 1024]; int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0) { cancellationToken.ThrowIfCancellationRequested(); output.Write(buffer, 0, read); }
    }
}
