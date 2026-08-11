using System.Text;
using System.Diagnostics;
using Launcher.PlayerShell;
using Xunit;

namespace Launcher.PlayerShellIntegration.Windows;

public sealed class PlayerPayloadPackageTests
{
    [Fact]
    public void DownloadedEntryMovesToManagedProjectDirectoryAndOriginalCanForward()
    {
        string root = CreateTemporaryRoot();
        try
        {
            string shell = Path.Combine(root, "download.exe"); File.WriteAllBytes(shell, "MZ-test-shell"u8.ToArray());
            string payload = Directory.CreateDirectory(Path.Combine(root, "payload")).FullName; File.WriteAllText(Path.Combine(payload, "Client.exe"), "client");
            string source = Path.Combine(root, "玩家下载入口.exe"); PlayerPayloadInfo info = PlayerPayloadPackage.Create(shell, payload, source, "Client.exe");
            string managedRoot = Directory.CreateDirectory(Path.Combine(root, "managed")).FullName;
            string managed = PlayerManagedEntry.Ensure(source, "project-1", managedRoot, info);
            Assert.NotEqual(source, managed);
            Assert.Equal(info.Sha256, PlayerPayloadPackage.Verify(managed).Sha256);
            Assert.Equal(managed, PlayerManagedEntry.Ensure(source, "project-1", managedRoot, info));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
    [Fact]
    public void 玩家入口重命名后仍能校验并解包全部载荷()
    {
        string root = CreateTemporaryRoot();
        try
        {
            string shell = Path.Combine(root, "shell.exe");
            string payload = Path.Combine(root, "payload");
            string output = Path.Combine(root, "原始名称.exe");
            string renamed = Path.Combine(root, "玩家任意改名.exe");
            string extracted = Path.Combine(root, "extracted");
            Directory.CreateDirectory(Path.Combine(payload, "Data"));
            File.WriteAllBytes(shell, Encoding.ASCII.GetBytes("MZ-test-shell"));
            File.WriteAllText(Path.Combine(payload, "Client.exe"), "client-binary", Encoding.UTF8);
            File.WriteAllText(Path.Combine(payload, "Data", "theme.json"), "{\"name\":\"内置主题\"}", Encoding.UTF8);

            PlayerPayloadPackage.Create(shell, payload, output, "Client.exe");
            File.Move(output, renamed);

            PlayerPayloadInfo info = PlayerPayloadPackage.Verify(renamed);
            PlayerPayloadPackage.ExtractVerified(renamed, extracted);

            Assert.Equal("Client.exe", info.EntryPoint);
            Assert.Equal(2, info.FileCount);
            Assert.Equal("client-binary", File.ReadAllText(Path.Combine(extracted, "Client.exe"), Encoding.UTF8));
            Assert.Equal("{\"name\":\"内置主题\"}", File.ReadAllText(Path.Combine(extracted, "Data", "theme.json"), Encoding.UTF8));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void 已解包载荷被修改后必须拒绝复用()
    {
        string root = CreateTemporaryRoot();
        try
        {
            string shell = Path.Combine(root, "shell.exe");
            string payload = Path.Combine(root, "payload");
            string output = Path.Combine(root, "Player.exe");
            string extracted = Path.Combine(root, "extracted");
            Directory.CreateDirectory(payload);
            File.WriteAllBytes(shell, Encoding.ASCII.GetBytes("MZ-test-shell"));
            File.WriteAllText(Path.Combine(payload, "Client.exe"), "client-binary", Encoding.UTF8);
            PlayerPayloadPackage.Create(shell, payload, output, "Client.exe");
            PlayerPayloadPackage.ExtractVerified(output, extracted);
            File.AppendAllText(Path.Combine(extracted, "Client.exe"), "tampered", Encoding.UTF8);

            Assert.Throws<InvalidDataException>(() => PlayerPayloadPackage.VerifyExtracted(output, extracted));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void 超过八十MiB时拒绝生成且不遗留正式入口()
    {
        string root = CreateTemporaryRoot();
        try
        {
            string shell = Path.Combine(root, "shell.exe");
            string payload = Path.Combine(root, "payload");
            string output = Path.Combine(root, "Player.exe");
            Directory.CreateDirectory(payload);
            using (FileStream stream = File.Create(shell)) stream.SetLength(PlayerPayloadPackage.MaximumPlayerExecutableBytes);
            File.WriteAllText(Path.Combine(payload, "Client.exe"), "client-binary", Encoding.UTF8);

            Assert.Throws<InvalidDataException>(() => PlayerPayloadPackage.Create(shell, payload, output, "Client.exe"));
            Assert.False(File.Exists(output));
            Assert.Empty(Directory.EnumerateFiles(root, "Player.exe.*"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void 解包拒绝经过目标目录内的Junction且不写出边界()
    {
        string root = CreateTemporaryRoot();
        string? junction = null;
        try
        {
            string shell = Path.Combine(root, "shell.exe");
            string payload = Path.Combine(root, "payload");
            string output = Path.Combine(root, "Player.exe");
            string extracted = Path.Combine(root, "extracted");
            string outside = Path.Combine(root, "outside");
            Directory.CreateDirectory(Path.Combine(payload, "Data"));
            Directory.CreateDirectory(extracted);
            Directory.CreateDirectory(outside);
            File.WriteAllBytes(shell, Encoding.ASCII.GetBytes("MZ-test-shell"));
            File.WriteAllText(Path.Combine(payload, "Client.exe"), "client-binary", Encoding.UTF8);
            File.WriteAllText(Path.Combine(payload, "Data", "theme.json"), "theme", Encoding.UTF8);
            PlayerPayloadPackage.Create(shell, payload, output, "Client.exe");

            junction = Path.Combine(extracted, "Data");
            using Process process = Process.Start(new ProcessStartInfo("cmd.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                ArgumentList = { "/d", "/c", "mklink", "/J", junction, outside },
            }) ?? throw new InvalidOperationException("无法启动 junction 夹具");
            process.WaitForExit();
            Assert.Equal(0, process.ExitCode);

            Assert.Throws<InvalidDataException>(() => PlayerPayloadPackage.ExtractVerified(output, extracted));
            Assert.Empty(Directory.EnumerateFileSystemEntries(outside));
        }
        finally
        {
            if (junction is not null && Directory.Exists(junction)) Directory.Delete(junction);
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTemporaryRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "LyoCrystalPlayerShellTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
