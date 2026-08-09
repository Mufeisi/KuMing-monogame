using System.Diagnostics;
using System.Reflection;
using Server;
using Server.MirDatabase;
using Server.MirEnvir;
using Server.Persistence;
using Server.Persistence.Sql;
using Server.Security;
using Xunit;
using Xunit.Abstractions;

namespace Base05.Tests;

public sealed class ProductionRpoPolicyTests
{
    private const string ChildModeVariable = "LYOCRYSTAL_DB05_FAULT_CHILD";
    private const string DatabasePathVariable = "LYOCRYSTAL_DB05_DATABASE_PATH";
    private const string MarkerPathVariable = "LYOCRYSTAL_DB05_MARKER_PATH";
    private readonly ITestOutputHelper _output;

    public ProductionRpoPolicyTests(ITestOutputHelper output) => _output = output;

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public void 正式保存间隔接受一分钟和五分钟边界(int minutes)
    {
        ProductionRpoPolicy.ValidateSaveDelay(minutes, enforceProductionMaximum: true);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(60)]
    public void 正式保存间隔拒绝越界值(int minutes)
    {
        Assert.Throws<InvalidOperationException>(() =>
            ProductionRpoPolicy.ValidateSaveDelay(minutes, enforceProductionMaximum: true));
    }

    [Fact]
    public void 测试服只放宽上限但仍拒绝零和负数()
    {
        ProductionRpoPolicy.ValidateSaveDelay(60, enforceProductionMaximum: false);
        Assert.Throws<InvalidOperationException>(() =>
            ProductionRpoPolicy.ValidateSaveDelay(0, enforceProductionMaximum: false));
    }

    [Fact]
    public void 配置上下文在载入与启动前使用同一正式或测试服口径()
    {
        int oldSaveDelay = Settings.SaveDelay;
        bool oldTestServer = Settings.TestServer;
        try
        {
            Settings.SaveDelay = 60;
            Settings.TestServer = true;
            ProductionRpoPolicy.ValidateConfiguredSaveDelay();

            Settings.TestServer = false;
            Assert.Throws<InvalidOperationException>(ProductionRpoPolicy.ValidateConfiguredSaveDelay);
        }
        finally
        {
            Settings.SaveDelay = oldSaveDelay;
            Settings.TestServer = oldTestServer;
        }
    }

    [Fact]
    public async Task 真实自动保存提交后在下一截止前一毫秒强停并重启只丢失未提交窗口()
    {
        if (string.Equals(Environment.GetEnvironmentVariable(ChildModeVariable), "1", StringComparison.Ordinal))
        {
            RunFaultChild();
            return;
        }

        string root = Path.Combine(Path.GetTempPath(), "base05-db05-fault-" + Guid.NewGuid().ToString("N"));
        string databasePath = Path.Combine(root, "server.db");
        string markerPath = Path.Combine(root, "ready.txt");
        string stdoutPath = Path.Combine(root, "child.stdout.txt");
        string stderrPath = Path.Combine(root, "child.stderr.txt");
        Directory.CreateDirectory(root);
        Process? child = null;
        try
        {
            string projectPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Base05.Tests.csproj"));
            var startInfo = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            startInfo.ArgumentList.Add("test");
            startInfo.ArgumentList.Add(projectPath);
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add("Release");
            startInfo.ArgumentList.Add("--no-build");
            startInfo.ArgumentList.Add("--no-restore");
            startInfo.ArgumentList.Add("--filter");
            startInfo.ArgumentList.Add("FullyQualifiedName=Base05.Tests.ProductionRpoPolicyTests.真实自动保存提交后在下一截止前一毫秒强停并重启只丢失未提交窗口");
            startInfo.Environment[ChildModeVariable] = "1";
            startInfo.Environment[DatabasePathVariable] = databasePath;
            startInfo.Environment[MarkerPathVariable] = markerPath;

            child = Process.Start(startInfo) ?? throw new InvalidOperationException("无法启动 DB-05 故障注入子进程");
            Task<string> stdout = child.StandardOutput.ReadToEndAsync();
            Task<string> stderr = child.StandardError.ReadToEndAsync();

            Assert.True(
                SpinWait.SpinUntil(() => File.Exists(markerPath) || child.HasExited, TimeSpan.FromSeconds(30)),
                "DB-05 子进程未在有界时间内完成首次自动保存");
            if (child.HasExited)
                throw new Xunit.Sdk.XunitException($"DB-05 子进程提前退出：{child.ExitCode}\n{await stdout}\n{await stderr}");

            string[] marker = File.ReadAllLines(markerPath);
            long lastCommittedAt = ReadMarker(marker, "LAST_COMMITTED_LOGICAL_MS");
            long crashAt = ReadMarker(marker, "CRASH_LOGICAL_MS");
            long committedGeneration = ReadMarker(marker, "COMMITTED_GENERATION");

            child.Kill(entireProcessTree: true);
            Assert.True(child.WaitForExit(10000), "DB-05 故障注入子进程未能强制终止");
            Assert.NotEqual(0, child.ExitCode);
            File.WriteAllText(stdoutPath, await stdout);
            File.WriteAllText(stderrPath, await stderr);

            var persistence = new SqlServerPersistence(DatabaseProviderKind.Sqlite, new SqlDatabaseOptions { SqlitePath = databasePath });
            var restarted = new Envir();
            persistence.LoadAccounts(restarted);
            AccountInfo account = Assert.Single(restarted.AccountList);

            long unpersistedWindow = crashAt - lastCommittedAt;
            Assert.Equal(100u, account.Gold);
            Assert.Equal(299999, unpersistedWindow);
            Assert.True(unpersistedWindow < TimeSpan.FromMinutes(5).TotalMilliseconds);
            Assert.True(committedGeneration > 0);
            _output.WriteLine($"FAULT_PROCESS_ID={child.Id}");
            _output.WriteLine($"FAULT_PROCESS_EXIT_CODE={child.ExitCode}");
            _output.WriteLine($"LAST_COMMITTED_LOGICAL_MS={lastCommittedAt}");
            _output.WriteLine($"CRASH_LOGICAL_MS={crashAt}");
            _output.WriteLine($"UNPERSISTED_WINDOW_MS={unpersistedWindow}");
            _output.WriteLine($"RESTARTED_ACCOUNT_GOLD={account.Gold}");
            _output.WriteLine($"COMMITTED_GENERATION={committedGeneration}");
        }
        finally
        {
            if (child is { HasExited: false })
            {
                try { child.Kill(entireProcessTree: true); child.WaitForExit(10000); } catch { }
            }
            child?.Dispose();
            SqliteConnectionPoolCleanup();
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static void RunFaultChild()
    {
        string databasePath = Environment.GetEnvironmentVariable(DatabasePathVariable)
            ?? throw new InvalidOperationException("DB-05 子进程缺少数据库路径");
        string markerPath = Environment.GetEnvironmentVariable(MarkerPathVariable)
            ?? throw new InvalidOperationException("DB-05 子进程缺少标记路径");
        const long firstDeadline = 300000;
        const long crashAt = 599999;
        long simulatedTime = 0;
        Settings.SaveDelay = ProductionRpoPolicy.MaximumSaveDelayMinutes;

        var persistence = new SqlServerPersistence(DatabaseProviderKind.Sqlite, new SqlDatabaseOptions { SqlitePath = databasePath });
        var environment = new Envir();
        var account = new AccountInfo { Index = 51001, AccountID = "db05-fault", UserName = "DB05", Gold = 100 };
        environment.AccountList.Add(account);
        typeof(Envir).GetField("_persistence", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(environment, persistence);
        environment.Start(new EnvirStartOptions
        {
            EnforceProductionSecurity = false,
            LoadResources = false,
            BindNetwork = false,
            StartScripts = false,
            StartHttp = false,
            SaveOnStop = true,
            Multithreaded = false,
            ElapsedMillisecondsProvider = () => Interlocked.Read(ref simulatedTime),
        });

        if (!SpinWait.SpinUntil(() => environment.StartState == EnvirStartState.Ready, TimeSpan.FromSeconds(5)))
            throw new InvalidOperationException($"DB-05 子进程宿主未进入 Ready：{environment.StartFailure}");

        Interlocked.Exchange(ref simulatedTime, firstDeadline);
        if (!SpinWait.SpinUntil(
                () => persistence.GetLastCommittedGeneration(SqlSaveDomain.Accounts) > 0,
                TimeSpan.FromSeconds(10)))
            throw new TimeoutException("DB-05 首次真实 SQLite 自动保存未提交");
        ((IPendingSaveCoordinator)persistence).DrainPendingSaves();
        long committedGeneration = persistence.GetLastCommittedGeneration(SqlSaveDomain.Accounts);

        environment.InvokeOnMainThread(() =>
        {
            account.Gold = 777;
            return true;
        });
        Interlocked.Exchange(ref simulatedTime, crashAt);
        Thread.Sleep(100);
        var verifier = new SqlServerPersistence(DatabaseProviderKind.Sqlite, new SqlDatabaseOptions { SqlitePath = databasePath });
        var beforeCrash = new Envir();
        verifier.LoadAccounts(beforeCrash);
        if (beforeCrash.AccountList.Single().Gold != 100)
            throw new InvalidOperationException("下一截止前发生了非预期自动保存");

        string markerPartialPath = markerPath + ".partial";
        File.WriteAllLines(markerPartialPath,
        [
            $"LAST_COMMITTED_LOGICAL_MS={firstDeadline}",
            $"CRASH_LOGICAL_MS={crashAt}",
            $"COMMITTED_GENERATION={committedGeneration}",
            "MEMORY_GOLD=777",
            "DATABASE_GOLD=100",
        ]);
        File.Move(markerPartialPath, markerPath);
        Thread.Sleep(Timeout.Infinite);
    }

    private static long ReadMarker(IEnumerable<string> lines, string name)
    {
        string prefix = name + "=";
        string line = lines.Single(item => item.StartsWith(prefix, StringComparison.Ordinal));
        return long.Parse(line[prefix.Length..]);
    }

    private static void SqliteConnectionPoolCleanup()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    }
}
