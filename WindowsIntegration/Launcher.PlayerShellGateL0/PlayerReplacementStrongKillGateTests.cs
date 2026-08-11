using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Launcher.PlayerShellReplacementWorker;
using Shared.Security;
using Xunit;

namespace Launcher.PlayerShellGateL0.Windows;

public sealed class PlayerReplacementStrongKillGateTests
{
    [Fact]
    public void 两个真实玩家EXE在三个确定替换点强停后均可恢复()
    {
        string v1 = RequireGateExecutable("LYOCRYSTAL_GATE_L0_V1");
        string v2 = RequireGateExecutable("LYOCRYSTAL_GATE_L0_V2");
        string v1Hash = Hash(v1);
        string v2Hash = Hash(v2);
        Assert.NotEqual(v1Hash, v2Hash);
        Assert.True(new FileInfo(v1).Length <= 80L * 1024 * 1024);
        Assert.True(new FileInfo(v2).Length <= 80L * 1024 * 1024);

        string root = Path.Combine(Path.GetTempPath(), "LyoCrystalGateL0", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            string publicKey = Convert.ToBase64String(signer.ExportSubjectPublicKeyInfo());
            RunCase(root, "before", "BeforeApplying", v1, v2, v1Hash, v2Hash, signer, publicKey);
            RunCase(root, "during", "AfterApplyingJournalPersisted", v1, v2, v1Hash, v2Hash, signer, publicKey);
            RunCase(root, "after", "AfterAtomicReplace", v1, v2, v1Hash, v2Hash, signer, publicKey);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void RunCase(
        string root,
        string name,
        string interruptionPoint,
        string v1,
        string v2,
        string v1Hash,
        string v2Hash,
        ECDsa signer,
        string publicKey)
    {
        string caseRoot = Path.Combine(root, name);
        Directory.CreateDirectory(caseRoot);
        string target = Path.Combine(caseRoot, "Player.exe");
        string staged = target + ".new";
        string previous = target + ".previous";
        string journal = Path.Combine(caseRoot, "player-replacement.json");
        string reached = Path.Combine(caseRoot, "reached");
        string result = Path.Combine(caseRoot, "result");
        File.Copy(v1, target);
        File.Copy(v2, staged);
        File.WriteAllText(journal, CreateSignedJournal(staged, signer), Encoding.UTF8);

        using Process interrupted = StartWorker(journal, target, publicKey, interruptionPoint, reached, result);
        Assert.True(SpinWait.SpinUntil(() => File.Exists(reached), TimeSpan.FromSeconds(15)), $"未到达强停点 {interruptionPoint}");
        Assert.False(interrupted.HasExited, "强停前工作进程已退出");
        interrupted.Kill(entireProcessTree: true);
        Assert.True(interrupted.WaitForExit(10_000), "强停工作进程未退出");
        Assert.False(File.Exists(result), "强停工作进程错误地完成了替换");

        if (string.Equals(interruptionPoint, "AfterAtomicReplace", StringComparison.Ordinal))
        {
            Assert.Equal(v2Hash, Hash(target));
            Assert.Equal(v1Hash, Hash(previous));
            Assert.False(File.Exists(staged));
        }
        else
        {
            Assert.Equal(v1Hash, Hash(target));
            Assert.Equal(v2Hash, Hash(staged));
            Assert.False(File.Exists(previous));
        }

        using Process recovery = StartWorker(journal, target, publicKey, "None", reached + ".recovery", result);
        Assert.True(recovery.WaitForExit(30_000), "恢复工作进程超时");
        Assert.Equal(0, recovery.ExitCode);
        Assert.True(File.Exists(result), "恢复工作进程未写入结果");
        Assert.Equal(v2Hash, Hash(target));
        Assert.Equal(v1Hash, Hash(previous));
        Assert.False(File.Exists(staged));
        Assert.Contains("\"Status\": \"Committed\"", File.ReadAllText(journal, Encoding.UTF8));
    }

    private static Process StartWorker(
        string journal,
        string target,
        string publicKey,
        string interruptionPoint,
        string reached,
        string result)
    {
        string worker = typeof(ReplacementWorkerMarker).Assembly.Location;
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add(worker);
        start.ArgumentList.Add(journal);
        start.ArgumentList.Add(target);
        start.ArgumentList.Add(publicKey);
        start.ArgumentList.Add(interruptionPoint);
        start.ArgumentList.Add(reached);
        start.ArgumentList.Add(result);
        return Process.Start(start) ?? throw new InvalidOperationException("无法启动强停工作进程");
    }

    private static string CreateSignedJournal(string stagedPath, ECDsa signer)
    {
        byte[] bytes = File.ReadAllBytes(stagedPath);
        var manifest = new BootstrapSignedManifest
        {
            Format = BootstrapManifestSignaturePolicy.Format,
            Algorithm = BootstrapManifestSignaturePolicy.Algorithm,
            KeyId = "player-gate-l0",
            Sequence = 1,
            GeneratedAtUtc = "2026-08-11T00:00:00Z",
            ResourceVersion = "player-gate-l0-v2",
            MinimumClientVersion = "1.0.0",
            Packages =
            [
                new BootstrapSignedPackage
                {
                    Name = "player-entry",
                    Size = bytes.Length,
                    Sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
                },
            ],
        };
        manifest.Signature = Convert.ToBase64String(signer.SignData(
            BootstrapManifestSignaturePolicy.BuildCanonicalPayload(manifest),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
        return JsonSerializer.Serialize(new
        {
            Format = "lyocrystal-player-replacement-v1",
            PackageName = "player-entry",
            SignedManifestJson = JsonSerializer.Serialize(manifest),
            Status = "Prepared",
        });
    }

    private static string RequireGateExecutable(string variable)
    {
        string? path = Environment.GetEnvironmentVariable(variable);
        Assert.False(string.IsNullOrWhiteSpace(path), $"缺少门禁环境变量 {variable}");
        path = Path.GetFullPath(path!);
        Assert.True(File.Exists(path), $"门禁 EXE 不存在：{path}");
        return path;
    }

    private static string Hash(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
}
