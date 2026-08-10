using System.Text;
using Launcher.PlayerShell;
using Xunit;

namespace Launcher.PlayerShellIntegration.Windows;

public sealed class PlayerPayloadPackageTests
{
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

    private static string CreateTemporaryRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "LyoCrystalPlayerShellTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
