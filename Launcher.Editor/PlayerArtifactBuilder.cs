using System.Diagnostics;
using System.Drawing.Imaging;
using System.IO.Compression;
using System.Text.Json;
using Launcher.PlayerShell;
using Launcher.ThemeRuntime;
using Shared.Security;

namespace LyoCrystal.LauncherEditor;

public static class PlayerArtifactBuilder
{
    private const string ShellResource = "LyoCrystal.LauncherEditor.PlayerShell.exe";
    private const string PayloadResource = "LyoCrystal.LauncherEditor.PlayerPayload.zip";

    public static bool RequiresMicroCredential(EditorProject project) =>
        project.Snapshot.DefaultMicro.Enabled || project.Snapshot.Servers.Any(server => server.MicroOverride?.Enabled == true);

    public static PlayerPayloadInfo Create(EditorProject project, string projectRoot, string outputExe, string? microCode, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(project);
        project.SynchronizeMicroIdentity();
        project.Snapshot.WindowTitle = project.Brand.WindowTitle;
        project.Snapshot.TaskbarName = project.Brand.TaskbarName;
        project.Snapshot.TrustedReleaseKeys = project.Release.RetiredPublicKeys.TakeLast(2).Concat(new[]
        {
            new BootstrapManifestTrustedKey { KeyId = project.Release.CurrentKeyId, SubjectPublicKeyInfo = project.Release.CurrentPublicKey, NotBeforeSequence = project.Release.CurrentKeyNotBeforeSequence },
            new BootstrapManifestTrustedKey { KeyId = project.Release.NextKeyId, SubjectPublicKeyInfo = project.Release.NextPublicKey, NotBeforeSequence = project.Release.NextKeyNotBeforeSequence },
        }).ToList();
        LauncherSnapshotValidator.Validate(project.Snapshot);
        string output = Path.GetFullPath(outputExe);
        if (File.Exists(output)) throw new IOException("玩家 EXE 已存在，拒绝覆盖");
        using Stream shellResource = typeof(PlayerArtifactBuilder).Assembly.GetManifestResourceStream(ShellResource) ?? throw new InvalidOperationException("当前编辑器未内置玩家外壳模板，请使用正式发布版编辑器");
        using Stream payloadResource = typeof(PlayerArtifactBuilder).Assembly.GetManifestResourceStream(PayloadResource) ?? throw new InvalidOperationException("当前编辑器未内置玩家客户端模板，请使用正式发布版编辑器");
        string buildRoot = Path.Combine(Path.GetDirectoryName(output)!, ".lyocrystal-build-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(buildRoot);
        try
        {
            string shell = Path.Combine(buildRoot, "player-shell.exe");
            using (var target = new FileStream(shell, FileMode.CreateNew, FileAccess.Write, FileShare.None)) shellResource.CopyTo(target);
            string payload = Path.Combine(buildRoot, "payload"); Directory.CreateDirectory(payload);
            ExtractTemplate(payloadResource, payload, cancellationToken);
            string builtIn = Path.Combine(payload, "Launcher", "BuiltIn"); Directory.CreateDirectory(builtIn);
            CopyProjectAssets(projectRoot, builtIn, project.Snapshot, cancellationToken);
            File.WriteAllBytes(Path.Combine(builtIn, "launcher-snapshot.json"), JsonSerializer.SerializeToUtf8Bytes(project.Snapshot, LauncherSnapshotJsonContext.Default.LauncherSnapshot));
            string credential = Path.Combine(builtIn, "micro.credential");
            bool microRequired = RequiresMicroCredential(project);
            if (microRequired)
            {
                if (string.IsNullOrWhiteSpace(microCode)) throw new InvalidOperationException("启用微端时必须输入访问密码；密码只用于本次生成，不写入项目文件");
                File.WriteAllBytes(credential, MicroCredentialEnvelope.Create(project.Snapshot.ProjectId, microCode));
            }
            else if (File.Exists(credential)) File.Delete(credential);
            string? icon = PrepareIcon(project.Brand.IconPath, buildRoot);
            string branded = Path.Combine(buildRoot, "branded-shell.exe");
            NativeExecutableBranding.CreateBrandedCopy(shell, branded, new PlayerExecutableBrand
            {
                ProductName = project.Brand.ProductName, FileDescription = project.Brand.FileDescription, CompanyName = project.Brand.CompanyName,
                LegalCopyright = project.Brand.Copyright, FileVersion = project.Brand.FileVersion, ProductVersion = project.Brand.ProductVersion, IconPath = icon,
            });
            PlayerPayloadInfo info = PlayerPayloadPackage.Create(branded, payload, output, "Client.exe");
            cancellationToken.ThrowIfCancellationRequested();
            if (new FileInfo(output).Length > PlayerPayloadPackage.MaximumPlayerExecutableBytes) throw new InvalidDataException("玩家入口超过 80 兆字节上限");
            using Process process = Process.Start(new ProcessStartInfo(output) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(output)!, ArgumentList = { "--shell-smoke" } }) ?? throw new InvalidOperationException("无法启动生成后的玩家入口");
            WaitForExit(process, TimeSpan.FromSeconds(15), cancellationToken, "玩家入口生成后验证超时");
            if (process.ExitCode != 0) throw new InvalidDataException("玩家入口生成后验证失败，退出码 " + process.ExitCode);
            string renderOutput = Path.Combine(buildRoot, "render-smoke");
            using Process render = Process.Start(new ProcessStartInfo(output) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(output)!, ArgumentList = { "--theme-render-smoke", renderOutput } }) ?? throw new InvalidOperationException("无法启动生成后的主题验证");
            WaitForExit(render, TimeSpan.FromSeconds(120), cancellationToken, "玩家入口主题验证超时");
            string[] rendered = Directory.Exists(renderOutput) ? Directory.EnumerateFiles(renderOutput, "*.png").ToArray() : Array.Empty<string>();
            if (render.ExitCode != 0 || rendered.Length != 12) throw new InvalidDataException("玩家入口主题生成后验证失败");
            foreach (string image in rendered) using (Image.FromFile(image)) { }
            return info;
        }
        catch { if (File.Exists(output)) File.Delete(output); throw; }
        finally { try { if (Directory.Exists(buildRoot)) Directory.Delete(buildRoot, true); } catch { } }
    }

    private static void ExtractTemplate(Stream source, string destination, CancellationToken cancellationToken)
    {
        using var archive = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true);
        if (archive.Entries.Count is < 1 or > 5000) throw new InvalidDataException("玩家客户端模板文件数量无效");
        string prefix = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        long total = 0;
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(entry.Name)) continue;
            total = checked(total + entry.Length); if (total > 2L * 1024 * 1024 * 1024) throw new InvalidDataException("玩家客户端模板展开大小过大");
            string target = Path.GetFullPath(Path.Combine(destination, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
            if (!target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("玩家客户端模板路径越界");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using Stream input = entry.Open(); using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None); input.CopyTo(output, 81920); cancellationToken.ThrowIfCancellationRequested();
        }
    }

    private static void CopyProjectAssets(string projectRoot, string builtIn, LauncherSnapshot snapshot, CancellationToken cancellationToken)
    {
        string source = Path.GetFullPath(projectRoot);
        if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("项目素材根不得为重解析点");
        string targetPrefix = Path.GetFullPath(builtIn).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string[] references = new[] { snapshot.Theme.BackgroundImage, snapshot.Theme.LaunchButtonImage, snapshot.Theme.LaunchButtonHoverImage, snapshot.Theme.LaunchButtonPressedImage, snapshot.Theme.LaunchButtonDisabledImage }
            .Concat(snapshot.Theme.Controls.Select(control => control.BackgroundImage)).Concat(snapshot.Announcements.Select(item => item.Image))
            .Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (references.Length > 256) throw new InvalidDataException("主题引用素材数量超过 256 个");
        long total = 0;
        foreach (string reference in references)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string file = LauncherSnapshotValidator.ResolveAsset(projectRoot, reference);
            if (string.IsNullOrEmpty(file)) continue;
            if (!File.Exists(file)) throw new FileNotFoundException("主题引用素材不存在", file);
            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("项目素材不得包含重解析点");
            long length = new FileInfo(file).Length;
            if (length > 16L * 1024 * 1024 || (total = checked(total + length)) > 64L * 1024 * 1024) throw new InvalidDataException("主题素材超过单文件或总量限制");
            string relative = Path.GetRelativePath(source, file);
            string current = source;
            foreach (string part in Path.GetDirectoryName(relative)?.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) ?? Array.Empty<string>()) { current = Path.Combine(current, part); if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("项目素材不得经过重解析点"); }
            string target = Path.GetFullPath(Path.Combine(builtIn, relative));
            if (!target.StartsWith(targetPrefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("主题素材目标路径越界");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file, target, overwrite: true);
        }
    }

    private static void WaitForExit(Process process, TimeSpan timeout, CancellationToken cancellationToken, string timeoutMessage)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        try
        {
            while (!process.WaitForExit(200))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (DateTime.UtcNow >= deadline) throw new TimeoutException(timeoutMessage);
            }
        }
        catch
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            throw;
        }
    }

    private static string? PrepareIcon(string configured, string buildRoot)
    {
        if (string.IsNullOrWhiteSpace(configured)) return null;
        string source = Path.GetFullPath(configured);
        if (!File.Exists(source)) throw new FileNotFoundException("品牌图标不存在", source);
        if (Path.GetExtension(source).Equals(".ico", StringComparison.OrdinalIgnoreCase)) return source;
        if (!Path.GetExtension(source).Equals(".png", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("品牌图标仅支持 ICO 或 PNG");
        using Image image = Image.FromFile(source); using var bitmap = new Bitmap(image, new Size(256, 256)); using var png = new MemoryStream(); bitmap.Save(png, ImageFormat.Png);
        byte[] data = png.ToArray(); string target = Path.Combine(buildRoot, "brand.ico");
        using var output = new BinaryWriter(File.Create(target)); output.Write((ushort)0); output.Write((ushort)1); output.Write((ushort)1); output.Write((byte)0); output.Write((byte)0); output.Write((byte)0); output.Write((byte)0); output.Write((ushort)1); output.Write((ushort)32); output.Write(data.Length); output.Write(22); output.Write(data);
        return target;
    }
}
