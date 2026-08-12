using System.Text.Json;
using System.Text.Json.Serialization;
using System.Diagnostics;
using Launcher.PlayerShell;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static int Main(string[] args)
    {
        try
        {
            if (args.Length == 6 && string.Equals(args[0], "create", StringComparison.Ordinal))
            {
                Create(args[1], args[2], args[3], args[4], args[5]);
                return 0;
            }
            if (args.Length == 2 && string.Equals(args[0], "verify", StringComparison.Ordinal))
            {
                Print(PlayerPayloadPackage.Verify(args[1]));
                return 0;
            }
            if (args.Length == 5 && string.Equals(args[0], "append-verified-shell", StringComparison.Ordinal))
            {
                AppendVerifiedShell(args[1], args[2], args[3], args[4]);
                return 0;
            }
            throw new ArgumentException(
                "用法：\n" +
                "  create <预构建外壳.exe> <载荷目录> <玩家入口.exe> <入口相对路径> <品牌.json>\n" +
                "  append-verified-shell <已品牌外壳.exe> <载荷目录> <玩家入口.exe> <入口相对路径>\n" +
                "  verify <玩家入口.exe>");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }
    }

    private static void Create(string shell, string payloadDirectory, string output, string entryPoint, string brandPath)
    {
        PlayerExecutableBrand brand = JsonSerializer.Deserialize<PlayerExecutableBrand>(File.ReadAllText(Path.GetFullPath(brandPath)), JsonOptions)
            ?? throw new InvalidDataException("品牌配置为空");
        string brandedShell = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output))!, ".branded-shell-" + Guid.NewGuid().ToString("N") + ".exe");
        try
        {
            NativeExecutableBranding.CreateBrandedCopy(shell, brandedShell, brand);
            PlayerPayloadInfo info = PlayerPayloadPackage.Create(brandedShell, payloadDirectory, output, entryPoint);
            RunGeneratedExecutableSmoke(output);
            Print(info);
        }
        catch
        {
            if (File.Exists(output)) File.Delete(output);
            throw;
        }
        finally
        {
            if (File.Exists(brandedShell)) File.Delete(brandedShell);
        }
    }

    private static void AppendVerifiedShell(string shell, string payloadDirectory, string output, string entryPoint)
    {
        try
        {
            PlayerPayloadInfo info = PlayerPayloadPackage.Create(shell, payloadDirectory, output, entryPoint);
            RunGeneratedExecutableSmoke(output);
            Print(info);
        }
        catch
        {
            if (File.Exists(output)) File.Delete(output);
            throw;
        }
    }

    private static void RunGeneratedExecutableSmoke(string path)
    {
        using Process process = Process.Start(new ProcessStartInfo(Path.GetFullPath(path))
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(path))!,
            ArgumentList = { "--shell-smoke" },
        }) ?? throw new InvalidOperationException("无法启动生成后的玩家入口冒烟验证");
        if (!process.WaitForExit(15_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("生成后的玩家入口冒烟验证超时");
        }
        if (process.ExitCode != 0)
            throw new InvalidDataException("生成后的玩家入口冒烟验证失败，退出码 " + process.ExitCode);
    }

    private static void Print(PlayerPayloadInfo info)
    {
        Console.WriteLine($"玩家入口校验通过：入口={info.EntryPoint}；文件={info.FileCount}；压缩载荷={info.CompressedSize} 字节；SHA-256={info.Sha256}");
    }
}
