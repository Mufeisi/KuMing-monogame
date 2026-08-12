using System.Diagnostics;
using System.Runtime.InteropServices;
using Launcher.PlayerShell;
using Xunit;

namespace Launcher.PlayerShellIntegration.Windows;

public sealed class NativeExecutableBrandingTests
{
    [Fact]
    public void Windows可以读取生成后玩家入口的品牌与版本资源()
    {
        string root = Path.Combine(Path.GetTempPath(), "LyoCrystalBrandingTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string source = Environment.ProcessPath ?? throw new InvalidOperationException("测试宿主路径为空");
            string output = Path.Combine(root, "品牌玩家入口.exe");
            var brand = new PlayerExecutableBrand
            {
                ProductName = "清洁重实现启动器",
                FileDescription = "玩家专属入口",
                CompanyName = "测试 GM",
                LegalCopyright = "Copyright 2026",
                FileVersion = "1.2.3.4",
                ProductVersion = "5.6.7.8",
                IconPath = Path.Combine(AppContext.BaseDirectory, "TestAssets", "MIR2.ICO"),
            };

            NativeExecutableBranding.CreateBrandedCopy(source, output, brand);

            FileVersionInfo version = FileVersionInfo.GetVersionInfo(output);
            Assert.Equal(brand.ProductName, version.ProductName);
            Assert.Equal(brand.FileDescription, version.FileDescription);
            Assert.Equal(brand.CompanyName, version.CompanyName);
            Assert.Equal(brand.LegalCopyright, version.LegalCopyright);
            Assert.Equal(brand.FileVersion, version.FileVersion);
            Assert.Equal(brand.ProductVersion, version.ProductVersion);
            Assert.True(ExtractIconExW(output, -1, 0, 0, 0) > 0);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconExW(string file, int iconIndex, nint largeIcons, nint smallIcons, uint iconCount);
}
