using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Server;
using Server.MirEnvir;
using Server.MirDatabase;
using Server.MirObjects;
using Server.MirNetwork;
using Server.Persistence;
using Server.Persistence.Sql;
using Server.Security;
using Shared.Security;
using Server.Operations;
using Server.Scripting;
using Server.Scripting.Variables;
using Xunit;

namespace Base05.Tests;

[Collection("TLS环境")]
public sealed class ServerLifecycleSmokeTests : IDisposable
{
    private readonly string _secretRoot = Path.Combine(Path.GetTempPath(), "LyoCrystalLifecycleSecrets-" + Guid.NewGuid().ToString("N"));
    private readonly IDisposable _secretScope;

    public ServerLifecycleSmokeTests()
    {
        _secretScope = ProtectedSecretStore.UseTestRoot(_secretRoot);
    }

    public void Dispose()
    {
        _secretScope.Dispose();
        if (Directory.Exists(_secretRoot)) Directory.Delete(_secretRoot, true);
    }
    [Fact]
    public void Minimal_server_start_stop_is_isolated_and_repeatable()
    {
        var envir = new Envir();
        var options = new EnvirStartOptions
        {
            EnforceProductionSecurity = false,
            LoadResources = false,
            BindNetwork = false,
            StartScripts = false,
            StartHttp = false,
            SaveOnStop = false,
            Multithreaded = false,
        };

        envir.Start(options);
        try
        {
            var startupCompleted = SpinWait.SpinUntil(
                () => envir.StartState is EnvirStartState.Ready or EnvirStartState.Failed,
                TimeSpan.FromSeconds(2));

            Assert.True(startupCompleted, "服务器启动未在有界时间内完成。");
            Assert.Equal(EnvirStartState.Ready, envir.StartState);
            Assert.Null(envir.StartFailure);
            Assert.True(envir.Running);
            Assert.False(envir.IsNetworkBound);
        }
        finally
        {
            envir.Stop();
        }

        Assert.False(envir.Running);
        Assert.Equal(EnvirStartState.Stopped, envir.StartState);

        envir.Stop();
        Assert.False(envir.Running);
    }

    [Fact]
    public void 翎风Txt灰度候选在真实服务端生命周期内可重启并关闭回滚()
    {
        bool oldTxtEnabled = Settings.TxtScriptsEnabled;
        string oldTxtPath = Settings.TxtScriptsPath;
        TxtScriptLayout oldLayout = Settings.TxtScriptsLayout;
        long oldMaxFileBytes = Settings.TxtScriptsMaxFileBytes;
        bool oldHotReload = Settings.TxtScriptsHotReloadEnabled;
        bool oldCSharpEnabled = Settings.CSharpScriptsEnabled;
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        bool oldStrict = Settings.TxtScriptsStrictCompatibility;
        string contentRoot = Path.Combine(
            RepositoryRoot(), "Configs", "LingFengTxtPilot", "Content");
        string firstDigest = string.Empty;

        try
        {
            Settings.CSharpScriptsEnabled = false;
            Settings.TxtScriptsEnabled = true;
            Settings.TxtScriptsPath = contentRoot;
            Settings.TxtScriptsLayout = TxtScriptLayout.LyoCrystal;
            Settings.TxtScriptsMaxFileBytes = 1024 * 1024;
            Settings.TxtScriptsHotReloadEnabled = false;
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            Settings.TxtScriptsStrictCompatibility = true;

            for (int cycle = 0; cycle < 2; cycle++)
            {
                var envir = new Envir();
                try
                {
                    envir.Start(IsolatedStartOptions());
                    Assert.True(SpinWait.SpinUntil(
                        () => envir.StartState is EnvirStartState.Ready or EnvirStartState.Failed,
                        TimeSpan.FromSeconds(5)));
                    Assert.Equal(EnvirStartState.Ready, envir.StartState);
                    Assert.Null(envir.StartFailure);
                    Assert.Equal(3, envir.TextFileProvider.GetAll().Count);
                    Assert.Empty(TxtScriptSnapshotValidator.Validate(envir.TextFileProvider));
                    Assert.NotNull(envir.TxtScriptSnapshot);
                    Assert.True(envir.TxtScriptSnapshot.LoadMilliseconds < 5000);
                    if (cycle == 0)
                        firstDigest = envir.TxtScriptSnapshot.Digest;
                    else
                        Assert.Equal(firstDigest, envir.TxtScriptSnapshot.Digest);
                }
                finally
                {
                    envir.Stop();
                }
            }

            Settings.TxtScriptsEnabled = false;
            var rollbackEnvir = new Envir();
            try
            {
                rollbackEnvir.Start(IsolatedStartOptions());
                Assert.True(SpinWait.SpinUntil(
                    () => rollbackEnvir.StartState is EnvirStartState.Ready or EnvirStartState.Failed,
                    TimeSpan.FromSeconds(5)));
                Assert.Equal(EnvirStartState.Ready, rollbackEnvir.StartState);
                Assert.Null(rollbackEnvir.TextFileProvider);
                Assert.Null(rollbackEnvir.TxtScriptSnapshot);
            }
            finally
            {
                rollbackEnvir.Stop();
            }
        }
        finally
        {
            Settings.TxtScriptsEnabled = oldTxtEnabled;
            Settings.TxtScriptsPath = oldTxtPath;
            Settings.TxtScriptsLayout = oldLayout;
            Settings.TxtScriptsMaxFileBytes = oldMaxFileBytes;
            Settings.TxtScriptsHotReloadEnabled = oldHotReload;
            Settings.CSharpScriptsEnabled = oldCSharpEnabled;
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
            Settings.TxtScriptsStrictCompatibility = oldStrict;
        }
    }

    [Fact]
    public void 翎风Txt灰度NPC在真实服务端生命周期内执行并产生百分位指标()
    {
        bool oldTxtEnabled = Settings.TxtScriptsEnabled;
        string oldTxtPath = Settings.TxtScriptsPath;
        TxtScriptLayout oldLayout = Settings.TxtScriptsLayout;
        long oldMaxFileBytes = Settings.TxtScriptsMaxFileBytes;
        bool oldHotReload = Settings.TxtScriptsHotReloadEnabled;
        bool oldCSharpEnabled = Settings.CSharpScriptsEnabled;
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        bool oldStrict = Settings.TxtScriptsStrictCompatibility;
        bool oldMetricsEnabled = Settings.ScriptsRuntimeMetricsEnabled;
        int oldAutoDumpSeconds = Settings.ScriptsRuntimeMetricsAutoDumpSeconds;
        Envir envir = Envir.Main;

        try
        {
            Settings.CSharpScriptsEnabled = false;
            Settings.TxtScriptsEnabled = true;
            Settings.TxtScriptsPath = Path.Combine(
                RepositoryRoot(), "Configs", "LingFengTxtPilot", "Content");
            Settings.TxtScriptsLayout = TxtScriptLayout.LyoCrystal;
            Settings.TxtScriptsMaxFileBytes = 1024 * 1024;
            Settings.TxtScriptsHotReloadEnabled = false;
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            Settings.TxtScriptsStrictCompatibility = true;
            Settings.ScriptsRuntimeMetricsEnabled = true;
            Settings.ScriptsRuntimeMetricsAutoDumpSeconds = 0;
            ScriptRuntimeMetrics.Clear();

            envir.Start(IsolatedStartOptions());
            Assert.True(SpinWait.SpinUntil(
                () => envir.StartState is EnvirStartState.Ready or EnvirStartState.Failed,
                TimeSpan.FromSeconds(5)));
            Assert.Equal(EnvirStartState.Ready, envir.StartState);

            NPCScript script = NPCScript.GetOrAdd(0, "TXT灰度向导", NPCScriptType.Normal);
            Assert.Contains(script.NPCPages, page => page.Key == NPCScript.MainKey);
            var player = new PlayerObject
            {
                Info = new CharacterInfo { Name = "TXT灰度指标玩家" },
                Account = new AccountInfo()
            };

            for (int index = 0; index < 100; index++)
                script.Call(player, 0, NPCScript.MainKey);

            ScriptRuntimeMetrics.EntrySnapshot metric = Assert.Single(
                ScriptRuntimeMetrics.CreateSnapshot().Entries,
                entry => entry.Key.Contains("TXT灰度向导", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(100, metric.Count);
            Assert.Equal(100, metric.RecentSampleCount);
            Assert.True(metric.P95Milliseconds >= 0);
            Assert.True(metric.P99Milliseconds >= metric.P95Milliseconds);
            Assert.True(metric.MaximumMilliseconds >= metric.P99Milliseconds);
        }
        finally
        {
            envir.Stop();
            ScriptRuntimeMetrics.Clear();
            Settings.TxtScriptsEnabled = oldTxtEnabled;
            Settings.TxtScriptsPath = oldTxtPath;
            Settings.TxtScriptsLayout = oldLayout;
            Settings.TxtScriptsMaxFileBytes = oldMaxFileBytes;
            Settings.TxtScriptsHotReloadEnabled = oldHotReload;
            Settings.CSharpScriptsEnabled = oldCSharpEnabled;
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
            Settings.TxtScriptsStrictCompatibility = oldStrict;
            Settings.ScriptsRuntimeMetricsEnabled = oldMetricsEnabled;
            Settings.ScriptsRuntimeMetricsAutoDumpSeconds = oldAutoDumpSeconds;
        }
    }

    private static EnvirStartOptions IsolatedStartOptions() => new()
    {
        EnforceProductionSecurity = false,
        LoadResources = false,
        BindNetwork = false,
        StartScripts = false,
        StartHttp = false,
        SaveOnStop = false,
        Multithreaded = false,
    };

    private static string RepositoryRoot()
    {
        DirectoryInfo current = new(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(
                    current.FullName, "Docs", "design", "scripting", "翎风TXT脚本兼容迁移实施规格.md")))
                return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("未找到 LyoCrystal 仓库根目录。");
    }

    [Fact]
    public void 真实服务器生命周期内变量声明可原子热重载且失败保留旧版本()
    {
        string scriptsRoot = Path.Combine(Path.GetTempPath(), "LyoCrystalVar01Scripts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scriptsRoot);
        string scriptPath = Path.Combine(scriptsRoot, "Variables.cs");
        bool oldEnabled = Settings.CSharpScriptsEnabled;
        string oldPath = Settings.CSharpScriptsPath;
        bool oldHotReload = Settings.CSharpScriptsHotReloadEnabled;
        bool oldPushMode = Settings.CSharpScriptsPushModeEnabled;
        var envir = new Envir();

        try
        {
            WriteVariableScript(scriptPath, "Decimal", "1.0", includeBonus: false);
            Settings.CSharpScriptsEnabled = true;
            Settings.CSharpScriptsPath = scriptsRoot;
            Settings.CSharpScriptsHotReloadEnabled = false;
            Settings.CSharpScriptsPushModeEnabled = false;

            envir.Start(new EnvirStartOptions
            {
                EnforceProductionSecurity = false,
                LoadResources = false,
                BindNetwork = false,
                StartScripts = true,
                StartHttp = false,
                SaveOnStop = false,
                Multithreaded = false,
            });
            Assert.True(SpinWait.SpinUntil(
                () => envir.StartState is EnvirStartState.Ready or EnvirStartState.Failed,
                TimeSpan.FromSeconds(5)));
            Assert.Equal(EnvirStartState.Ready, envir.StartState);
            Assert.Equal(ScriptVariableKind.Decimal, envir.CSharpScripts.CurrentRegistry
                .VariableDeclarations.GetRequired(ScriptVariableScope.P, "Rate").Kind);
            Assert.Throws<InvalidOperationException>(() => envir.CSharpScripts.CurrentRegistry.RegisterVariable(
                ScriptVariableScope.P, "OutOfBand", ScriptVariableKind.Decimal, "0"));

            var owner = new object();
            var context = ScriptVariableContext.ForConversation(owner, 100);
            Assert.True(envir.CSharpScripts.VariableCommands
                .Mutate(context, "P.Rate", "MOV", "2.5").Success);

            long compatibleBaseVersion = envir.CSharpScripts.Version;
            WriteVariableScript(scriptPath, "Decimal", "3.0", includeBonus: true);
            envir.CSharpScripts.Reload();
            Assert.True(envir.CSharpScripts.Version > compatibleBaseVersion);
            Assert.Equal("2.5", envir.CSharpScripts.VariableCommands.Format(context, "P.Rate").Text);
            Assert.Equal("0.5", envir.CSharpScripts.VariableCommands.Format(context, "P.Bonus").Text);

            long incompatibleBaseVersion = envir.CSharpScripts.Version;
            WriteVariableScript(scriptPath, "Integer", "1", includeBonus: false);
            envir.CSharpScripts.Reload();
            Assert.Equal(incompatibleBaseVersion, envir.CSharpScripts.Version);
            Assert.Equal(ScriptVariableKind.Decimal, envir.CSharpScripts.CurrentRegistry
                .VariableDeclarations.GetRequired(ScriptVariableScope.P, "Rate").Kind);
            Assert.Contains("不能修改变量类型", envir.CSharpScripts.LastError, StringComparison.Ordinal);
        }
        finally
        {
            envir.Stop();
            Settings.CSharpScriptsEnabled = oldEnabled;
            Settings.CSharpScriptsPath = oldPath;
            Settings.CSharpScriptsHotReloadEnabled = oldHotReload;
            Settings.CSharpScriptsPushModeEnabled = oldPushMode;
            Directory.Delete(scriptsRoot, recursive: true);
        }
    }

    private static void WriteVariableScript(
        string path,
        string kind,
        string defaultValue,
        bool includeBonus)
    {
        string bonus = includeBonus
            ? "registry.RegisterVariable(ScriptVariableScope.P, \"Bonus\", ScriptVariableKind.Decimal, \"0.5\");"
            : string.Empty;
        File.WriteAllText(path, $$"""
            using Server.Scripting;
            using Server.Scripting.Variables;

            public sealed class RuntimeVariableDeclarations : IScriptModule
            {
                public void Register(ScriptRegistry registry)
                {
                    registry.RegisterVariable(
                        ScriptVariableScope.P, "Rate", ScriptVariableKind.{{kind}}, "{{defaultValue}}");
                    {{bonus}}
                }
            }
            """);
    }

    [Fact]
    public void Sqlite关服先在主线程捕获四个最终快照并排空写队列()
    {
        bool oldBlockShutdown = Settings.BlockShutdownOnSaveFailures;
        var persistence = new ShutdownRecordingPersistence();
        var envir = new Envir();
        typeof(Envir).GetField("_persistence", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(envir, persistence);
        int callerThreadId = Environment.CurrentManagedThreadId;

        try
        {
            Settings.BlockShutdownOnSaveFailures = false;
            envir.Start(new EnvirStartOptions
            {
                EnforceProductionSecurity = false,
                LoadResources = false,
                BindNetwork = false,
                StartScripts = false,
                StartHttp = false,
                SaveOnStop = true,
                Multithreaded = false,
            });
            Assert.True(SpinWait.SpinUntil(
                () => envir.StartState is EnvirStartState.Ready or EnvirStartState.Failed,
                TimeSpan.FromSeconds(2)));
            Assert.Equal(EnvirStartState.Ready, envir.StartState);

            envir.Stop();

            Assert.Equal(new[] { "ScriptVariables", "Accounts", "Guilds", "Goods", "Conquests", "Drain" }, persistence.Events);
            Assert.DoesNotContain(callerThreadId, persistence.SaveThreadIds);
            Assert.Single(persistence.SaveThreadIds.Distinct());
            Assert.False(envir.Running);
        }
        finally
        {
            Settings.BlockShutdownOnSaveFailures = oldBlockShutdown;
            envir.Stop();
        }
    }

    [Fact]
    public void Sqlite最终保存连续失败时取消关服恢复成功后允许重试()
    {
        bool oldBlockShutdown = Settings.BlockShutdownOnSaveFailures;
        int oldThreshold = Settings.BlockShutdownOnSaveFailuresThreshold;
        var persistence = new ShutdownRecordingPersistence { FailAccounts = true };
        var envir = new Envir();
        typeof(Envir).GetField("_persistence", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(envir, persistence);

        try
        {
            Settings.BlockShutdownOnSaveFailures = true;
            Settings.BlockShutdownOnSaveFailuresThreshold = 1;
            envir.Start(new EnvirStartOptions
            {
                EnforceProductionSecurity = false,
                LoadResources = false,
                BindNetwork = false,
                StartScripts = false,
                StartHttp = false,
                SaveOnStop = true,
                Multithreaded = false,
            });
            Assert.True(SpinWait.SpinUntil(
                () => envir.StartState is EnvirStartState.Ready or EnvirStartState.Failed,
                TimeSpan.FromSeconds(2)));
            Assert.Equal(EnvirStartState.Ready, envir.StartState);

            envir.Stop();

            Assert.True(envir.Running);

            persistence.FailAccounts = false;
            SqlSaveResilience.ReportSuccess(DatabaseProviderKind.Sqlite, SqlSaveDomain.Accounts);
            envir.Stop();
            Assert.False(envir.Running);
        }
        finally
        {
            persistence.FailAccounts = false;
            SqlSaveResilience.ReportSuccess(DatabaseProviderKind.Sqlite, SqlSaveDomain.Accounts);
            Settings.BlockShutdownOnSaveFailures = oldBlockShutdown;
            Settings.BlockShutdownOnSaveFailuresThreshold = oldThreshold;
            envir.Stop();
        }
    }

    [Fact]
    public void Sqlite最终保存已排空时资源清理不再追加商品保存()
    {
        var persistence = new ShutdownRecordingPersistence();
        var envir = new Envir();
        Type envirType = typeof(Envir);
        envirType.GetField("_persistence", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(envir, persistence);
        envirType.GetField("_shutdownSavePrepared", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(envir, 1);

        envirType.GetMethod("StopEnvir", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(envir, null);

        Assert.Empty(persistence.Events);
    }

    [Fact]
    public async Task Sqlite关服排空期间入队的旧主线程工作被取消且重启不执行()
    {
        bool oldBlockShutdown = Settings.BlockShutdownOnSaveFailures;
        var persistence = new ShutdownRecordingPersistence { BlockDrain = true };
        var envir = new Envir();
        typeof(Envir).GetField("_persistence", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(envir, persistence);
        bool staleWorkExecuted = false;

        try
        {
            Settings.BlockShutdownOnSaveFailures = false;
            envir.Start(CreateSqliteShutdownTestOptions(saveOnStop: true));
            Assert.True(SpinWait.SpinUntil(() => envir.StartState == EnvirStartState.Ready, TimeSpan.FromSeconds(2)));

            Task stop = Task.Run(envir.Stop);
            Assert.True(persistence.DrainStarted.Wait(TimeSpan.FromSeconds(5)));
            Task<bool> staleWork = Task.Run(() => envir.InvokeOnMainThread(() =>
            {
                staleWorkExecuted = true;
                return true;
            }));
            Assert.True(SpinWait.SpinUntil(() => envir.PendingMainThreadWorkCount == 1, TimeSpan.FromSeconds(5)));

            persistence.ReleaseDrain.Set();
            await stop.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(await staleWork.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.False(staleWorkExecuted);

            persistence.BlockDrain = false;
            envir.Start(CreateSqliteShutdownTestOptions(saveOnStop: false));
            Assert.True(SpinWait.SpinUntil(() => envir.StartState == EnvirStartState.Ready, TimeSpan.FromSeconds(2)));
            await Task.Delay(50);
            Assert.False(staleWorkExecuted);
        }
        finally
        {
            persistence.ReleaseDrain.Set();
            Settings.BlockShutdownOnSaveFailures = oldBlockShutdown;
            envir.Stop();
        }
    }

    private static EnvirStartOptions CreateSqliteShutdownTestOptions(bool saveOnStop)
    {
        return new EnvirStartOptions
        {
            EnforceProductionSecurity = false,
            LoadResources = false,
            BindNetwork = false,
            StartScripts = false,
            StartHttp = false,
            SaveOnStop = saveOnStop,
            Multithreaded = false,
        };
    }

    private sealed class ShutdownRecordingPersistence : IServerPersistence, IPendingSaveCoordinator
    {
        private readonly List<string> _events = new();
        private readonly List<int> _saveThreadIds = new();

        public DatabaseProviderKind Provider => DatabaseProviderKind.Sqlite;
        public IReadOnlyList<string> Events => _events;
        public IReadOnlyList<int> SaveThreadIds => _saveThreadIds;
        public bool FailAccounts { get; set; }
        public bool BlockDrain { get; set; }
        public ManualResetEventSlim DrainStarted { get; } = new(false);
        public ManualResetEventSlim ReleaseDrain { get; } = new(false);

        public bool LoadWorld(Envir envir) => true;
        public void SaveWorld(Envir envir) { }
        public void LoadScriptVariables(Envir envir) { }
        public void SaveScriptVariables(Envir envir) => Record("ScriptVariables");
        public void LoadAccounts(Envir envir) { }
        public void BeginSaveAccounts(Envir envir) { }
        public void SaveAccounts(Envir envir)
        {
            Record("Accounts");
            if (FailAccounts)
            {
                SqlSaveResilience.ReportFailure(
                    DatabaseProviderKind.Sqlite,
                    SqlSaveDomain.Accounts,
                    new IOException("测试最终保存失败"),
                    operation: "ShutdownTest");
            }
        }
        public void LoadGuilds(Envir envir) { }
        public void SaveGuilds(Envir envir, bool forced) => Record("Guilds");
        public void SaveGoods(Envir envir, bool forced) => Record("Goods");
        public void LoadConquests(Envir envir) { }
        public void SaveConquests(Envir envir, bool forced) => Record("Conquests");
        public void SaveArchivedCharacter(Envir envir, CharacterInfo info) { }
        public CharacterInfo GetArchivedCharacter(Envir envir, string name) => null;

        public void DrainPendingSaves()
        {
            _events.Add("Drain");
            if (!BlockDrain) return;
            DrainStarted.Set();
            Assert.True(ReleaseDrain.Wait(TimeSpan.FromSeconds(5)));
        }

        private void Record(string name)
        {
            _events.Add(name);
            _saveThreadIds.Add(Environment.CurrentManagedThreadId);
        }
    }

    [Fact]
    public void 无游戏监听器启动失败后可重试且不进入Ready()
    {
        string oldAddress = Settings.IPAddress;
        bool oldTls = Settings.TlsEnabled;
        bool oldLegacy = Settings.AllowLegacyV1;
        var envir = new Envir();
        var failOptions = new EnvirStartOptions
        {
            EnforceProductionSecurity = false,
            LoadResources = false,
            BindNetwork = true,
            StartScripts = false,
            StartHttp = false,
            SaveOnStop = false,
            Multithreaded = false,
        };
        try
        {
            Settings.IPAddress = "203.0.113.10";
            Settings.TlsEnabled = false;
            Settings.AllowLegacyV1 = true;
            envir.Start(failOptions);
            Assert.True(SpinWait.SpinUntil(() => envir.StartState == EnvirStartState.Failed, TimeSpan.FromSeconds(2)));
            Assert.False(envir.Running);
            Assert.Contains("没有可用的游戏监听器", envir.StartFailure?.Message);

            envir.Stop();
            envir.Start(new EnvirStartOptions
            {
                EnforceProductionSecurity = false,
                LoadResources = false,
                BindNetwork = false,
                StartScripts = false,
                StartHttp = false,
                SaveOnStop = false,
                Multithreaded = false,
            });
            Assert.True(SpinWait.SpinUntil(() => envir.StartState == EnvirStartState.Ready, TimeSpan.FromSeconds(2)));
            Assert.True(envir.Running);
        }
        finally
        {
            envir.Stop();
            Settings.IPAddress = oldAddress;
            Settings.TlsEnabled = oldTls;
            Settings.AllowLegacyV1 = oldLegacy;
        }
    }

    [Fact]
    public void 真实Server错误PFX密码失败后修正可重启()
    {
        using var scope = new ServerNetworkScope();
        scope.SetCertificatePassword("wrong-password");
        scope.Start();
        Assert.True(SpinWait.SpinUntil(() => scope.ServerEnvironment.StartState == EnvirStartState.Failed, TimeSpan.FromSeconds(8)), scope.ServerEnvironment.StartFailure?.ToString());
        Assert.False(scope.ServerEnvironment.Running);

        scope.Stop();
        scope.SetCertificatePassword(scope.CertificatePassword);
        scope.Start();
        Assert.True(SpinWait.SpinUntil(() => scope.ServerEnvironment.StartState == EnvirStartState.Ready, TimeSpan.FromSeconds(8)));
        Assert.True(scope.ServerEnvironment.Running);
        Assert.True(scope.ServerEnvironment.IsNetworkBound);
    }

    [Fact]
    public async Task 真实ServerTLS路径完成KeepAlive()
    {
        using var scope = new ServerNetworkScope();
        scope.SetCertificatePassword(scope.CertificatePassword);
        scope.Start();
        Assert.True(SpinWait.SpinUntil(() => scope.ServerEnvironment.StartState == EnvirStartState.Ready, TimeSpan.FromSeconds(8)));
        Assert.True(scope.GetListener("_tlsListener")?.Server.IsBound == true);
        Assert.True(await SendKeepAliveAndObserveAsync(scope.ServerEnvironment, scope.TlsPort, useTls: true, scope.Certificate));
    }

    [Fact]
    public async Task 真实Server停止重启取消旧TLS握手代次()
    {
        using var scope = new ServerNetworkScope();
        scope.SetCertificatePassword(scope.CertificatePassword);
        scope.Start();
        Assert.True(SpinWait.SpinUntil(() => scope.ServerEnvironment.StartState == EnvirStartState.Ready, TimeSpan.FromSeconds(8)));
        using var slowClient = new TcpClient();
        await slowClient.ConnectAsync(IPAddress.Loopback, scope.TlsPort);
        scope.Stop();
        scope.Start();
        Assert.True(SpinWait.SpinUntil(() => scope.ServerEnvironment.StartState == EnvirStartState.Ready, TimeSpan.FromSeconds(8)));
        Assert.True(await SendKeepAliveAndObserveAsync(scope.ServerEnvironment, scope.TlsPort, useTls: true, scope.Certificate));
    }

    [Fact]
    public async Task 真实Server回环V1路径完成KeepAlive()
    {
        using var scope = new ServerNetworkScope(tlsEnabled: false);
        scope.Start();
        Assert.True(SpinWait.SpinUntil(() => scope.ServerEnvironment.StartState == EnvirStartState.Ready, TimeSpan.FromSeconds(8)));
        Assert.True(scope.GetListener("_listener")?.Server.IsBound == true);
        Assert.True(await SendKeepAliveAndObserveAsync(scope.ServerEnvironment, scope.LegacyPort, useTls: false, scope.Certificate));
    }

    [Fact]
    public async Task 真实Server超大声明帧由主循环断开并留下单条帧证据()
    {
        using var scope = new ServerNetworkScope(tlsEnabled: false);
        string policyPath = Path.Combine(scope.Directory, "gateway-governance.json");
        var governance = new GatewayTrafficGovernance(policyPath, auditSink: _ => { });
        GatewayGovernancePolicy baseline = governance.CaptureSnapshot().Policy;
        governance.SetPolicy(new GatewayGovernanceChangeRequest
        {
            ExpectedRevision = baseline.Revision,
            Mode = GatewayGovernanceMode.Enforce,
            MaximumPacketBytes = 1024,
            Rules = baseline.Rules,
            Reason = "真实网络超大封包测试",
        }, "test-admin");
        scope.SetGatewayGovernance(governance);
        scope.Start();
        Assert.True(SpinWait.SpinUntil(() => scope.ServerEnvironment.StartState == EnvirStartState.Ready, TimeSpan.FromSeconds(8)));

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, scope.LegacyPort);
        await using NetworkStream stream = client.GetStream();
        byte[] connected = await ReadFrameAsync(stream, TimeSpan.FromSeconds(4));
        Assert.Equal((short)ServerPacketIds.Connected, BitConverter.ToInt16(connected, 2));
        MirConnection connection = null;
        Assert.True(SpinWait.SpinUntil(() =>
        {
            lock (scope.ServerEnvironment.Connections)
                connection = scope.ServerEnvironment.Connections.FirstOrDefault(value => value.Connected);
            return connection != null;
        }, TimeSpan.FromSeconds(4)));

        byte[] oversizedHeader = new byte[4];
        BitConverter.GetBytes((ushort)1025).CopyTo(oversizedHeader, 0);
        BitConverter.GetBytes((short)ClientPacketIds.KeepAlive).CopyTo(oversizedHeader, 2);
        await stream.WriteAsync(oversizedHeader.AsMemory(0, 2));
        await stream.FlushAsync();
        await Task.Delay(50);
        await stream.WriteAsync(oversizedHeader.AsMemory(2, 2));
        await stream.FlushAsync();

        Assert.True(SpinWait.SpinUntil(() => !connection.Connected, TimeSpan.FromSeconds(4)));
        GatewayGovernanceEvidence evidence = Assert.Single(governance.CaptureSnapshot().RecentEvidence);
        Assert.Equal(GatewayTrafficCategory.OversizedPacket, evidence.Category);
        Assert.Equal(1025, evidence.Observed);
    }

    [Fact]
    public void 真实Server端口占用失败释放后可重启()
    {
        using var scope = new ServerNetworkScope();
        scope.SetCertificatePassword(scope.CertificatePassword);
        using var occupied = new TcpListener(IPAddress.Loopback, scope.TlsPort);
        occupied.Start();

        scope.Start();
        Assert.True(SpinWait.SpinUntil(() => scope.ServerEnvironment.StartState == EnvirStartState.Failed, TimeSpan.FromSeconds(8)), scope.ServerEnvironment.StartFailure?.ToString());
        Assert.False(scope.ServerEnvironment.Running);
        scope.Stop();

        occupied.Stop();
        scope.Start();
        Assert.True(SpinWait.SpinUntil(() => scope.ServerEnvironment.StartState == EnvirStartState.Ready, TimeSpan.FromSeconds(8)));
        Assert.True(scope.ServerEnvironment.Running);
    }

    [Fact]
    public async Task 真实ServerMaxUser与MaxIP准入在同一临界区生效()
    {
        using var userScope = new ServerNetworkScope(maxUser: 1, maxIp: 5, ipBlockSeconds: 0);
        Envir.IPBlocks.Clear();
        userScope.SetCertificatePassword(userScope.CertificatePassword);
        userScope.Start();
        Assert.True(SpinWait.SpinUntil(() => userScope.ServerEnvironment.StartState == EnvirStartState.Ready, TimeSpan.FromSeconds(8)), userScope.ServerEnvironment.StartFailure?.ToString());
        var first = await userScope.ConnectTlsClientAsync();
        using var firstClient = first.Client;
        using var firstSsl = first.Ssl;
        Assert.True(SpinWait.SpinUntil(() => userScope.ServerEnvironment.Connections.Count(c => c.Connected) == 1, TimeSpan.FromSeconds(4)));
        var second = await userScope.ConnectTlsClientAsync();
        using var secondClient = second.Client;
        using var secondSsl = second.Ssl;
        Assert.True(SpinWait.SpinUntil(() => userScope.ServerEnvironment.Connections.Count(c => c.Connected) == 1, TimeSpan.FromSeconds(2)));
        userScope.Stop();

        using var ipScope = new ServerNetworkScope(maxUser: 5, maxIp: 1, ipBlockSeconds: 0);
        Envir.IPBlocks.Clear();
        ipScope.SetCertificatePassword(ipScope.CertificatePassword);
        ipScope.Start();
        Assert.True(SpinWait.SpinUntil(() => ipScope.ServerEnvironment.StartState == EnvirStartState.Ready, TimeSpan.FromSeconds(8)), ipScope.ServerEnvironment.StartFailure?.ToString());
        var ipFirst = await ipScope.ConnectTlsClientAsync();
        using var ipFirstClient = ipFirst.Client;
        using var ipFirstSsl = ipFirst.Ssl;
        Assert.True(SpinWait.SpinUntil(() => ipScope.ServerEnvironment.Connections.Count(c => c.Connected) == 1, TimeSpan.FromSeconds(4)));
        var ipSecond = await ipScope.ConnectTlsClientAsync();
        using var ipSecondClient = ipSecond.Client;
        using var ipSecondSsl = ipSecond.Ssl;
        Assert.True(SpinWait.SpinUntil(() => ipScope.ServerEnvironment.Connections.Count(c => c.Connected) == 1, TimeSpan.FromSeconds(2)));
    }

    private sealed class ServerNetworkScope : IDisposable
    {
        public const string DefaultCertificatePassword = "c3-test-password";
        public readonly string Directory;
        public readonly string CertificatePath;
        public readonly string CertificatePassword = DefaultCertificatePassword;
        public readonly int LegacyPort;
        public readonly int TlsPort;
        public readonly X509Certificate2 Certificate;
        public readonly Envir ServerEnvironment;

        private readonly string _oldAddress;
        private readonly ushort _oldPort;
        private readonly ushort _oldTlsPort;
        private readonly bool _oldTls;
        private readonly bool _oldLegacy;
        private readonly string _oldCertificatePath;
        private readonly string _oldProvider;
        private readonly string _oldSqlitePath;
        private readonly bool _oldAutoApply;
        private readonly bool _oldAutoImport;
        private readonly bool _oldPacketDirection;
        private readonly ushort _oldMaxUser;
        private readonly ushort _oldMaxIP;
        private readonly int _oldIPBlockSeconds;
        private readonly bool _hadLoopbackBlock;
        private readonly DateTime _oldLoopbackBlock;
        private GatewayTrafficGovernance _gatewayGovernance;

        public ServerNetworkScope(bool tlsEnabled = true, ushort maxUser = 500, ushort maxIp = 5, int ipBlockSeconds = 5)
        {
            Directory = CreateTempDirectory();
            CertificatePath = Path.Combine(Directory, "server.pfx");
            LegacyPort = GetFreePort();
            TlsPort = GetFreePort();
            if (LegacyPort == TlsPort) TlsPort = GetFreePort();
            Certificate = CreateCertificate();
            File.WriteAllBytes(CertificatePath, Certificate.Export(X509ContentType.Pfx, CertificatePassword));

            _oldAddress = Settings.IPAddress;
            _oldPort = Settings.Port;
            _oldTlsPort = Settings.TlsPort;
            _oldTls = Settings.TlsEnabled;
            _oldLegacy = Settings.AllowLegacyV1;
            _oldCertificatePath = Settings.TlsCertificatePath;
            _oldProvider = Settings.DatabaseProvider;
            _oldSqlitePath = Settings.SqlitePath;
            _oldAutoApply = Settings.AutoApplySchemaOnStartup;
            _oldAutoImport = Settings.AutoImportLegacyOnEmpty;
            _oldPacketDirection = Packet.IsServer;
            // 测试宿主不经过 Server.MirForms.Program，必须显式复现生产端的协议方向初始化。
            Packet.IsServer = true;
            _hadLoopbackBlock = Envir.IPBlocks.TryRemove("127.0.0.1", out _oldLoopbackBlock);
            _oldMaxUser = Settings.MaxUser;
            _oldMaxIP = Settings.MaxIP;
            _oldIPBlockSeconds = Settings.IPBlockSeconds;

            Settings.IPAddress = "127.0.0.1";
            Settings.Port = (ushort)LegacyPort;
            Settings.TlsPort = (ushort)TlsPort;
            Settings.TlsEnabled = tlsEnabled;
            Settings.AllowLegacyV1 = true;
            Settings.TlsCertificatePath = CertificatePath;
            Settings.DatabaseProvider = "Sqlite";
            Settings.SqlitePath = Path.Combine(Directory, "server.db");
            Settings.AutoApplySchemaOnStartup = true;
            Settings.AutoImportLegacyOnEmpty = false;
            Settings.MaxUser = maxUser;
            Settings.MaxIP = maxIp;
            Settings.IPBlockSeconds = ipBlockSeconds;

            ServerEnvironment = new Envir();
            typeof(Envir).GetField("StatusPortEnabled", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(ServerEnvironment, false);
        }

        public EnvirStartOptions StartOptions => new EnvirStartOptions
        {
            EnforceProductionSecurity = false,
            LoadResources = false,
            BindNetwork = true,
            StartScripts = false,
            StartHttp = false,
            SaveOnStop = false,
            Multithreaded = false,
            GatewayGovernance = _gatewayGovernance,
        };

        public void SetCertificatePassword(string password)
        {
            ProtectedSecretStore.Write(ProtectedSecretStore.TlsCertificatePassword, password);
        }

        public void SetGatewayGovernance(GatewayTrafficGovernance governance) => _gatewayGovernance = governance;

        public void Start() => ServerEnvironment.Start(StartOptions);

        public async Task<(TcpClient Client, SslStream Ssl)> ConnectTlsClientAsync()
        {
            var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, TlsPort);
            var ssl = new SslStream(client.GetStream(), leaveInnerStreamOpen: false);
            var options = TlsClientPolicy.CreateOptions("localhost");
            options.CertificateChainPolicy = new X509ChainPolicy
            {
                TrustMode = X509ChainTrustMode.CustomRootTrust,
                RevocationMode = X509RevocationMode.NoCheck,
            };
            options.CertificateChainPolicy.CustomTrustStore.Add(Certificate);
            await ssl.AuthenticateAsClientAsync(options);
            return (client, ssl);
        }

        public void Stop() => ServerEnvironment.Stop();

        public TcpListener GetListener(string fieldName) =>
            (TcpListener)typeof(Envir).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(ServerEnvironment);

        public void Dispose()
        {
            ServerEnvironment.Stop();
            Packet.IsServer = _oldPacketDirection;
            Settings.IPAddress = _oldAddress;
            Settings.Port = _oldPort;
            Settings.TlsPort = _oldTlsPort;
            Settings.TlsEnabled = _oldTls;
            Settings.AllowLegacyV1 = _oldLegacy;
            Settings.TlsCertificatePath = _oldCertificatePath;
            Settings.DatabaseProvider = _oldProvider;
            Settings.SqlitePath = _oldSqlitePath;
            Settings.AutoApplySchemaOnStartup = _oldAutoApply;
            Settings.AutoImportLegacyOnEmpty = _oldAutoImport;
            Settings.MaxUser = _oldMaxUser;
            Settings.MaxIP = _oldMaxIP;
            Settings.IPBlockSeconds = _oldIPBlockSeconds;
            Envir.IPBlocks.TryRemove("127.0.0.1", out _);
            if (_hadLoopbackBlock) Envir.IPBlocks["127.0.0.1"] = _oldLoopbackBlock;
            Certificate.Dispose();
            TryDeleteDirectory(Directory);
        }
    }

    private static async Task<bool> SendKeepAliveAndObserveAsync(Envir envir, int port, bool useTls, X509Certificate2 certificate)
    {
        TcpClient client = null;
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                client = new TcpClient();
                await client.ConnectAsync(IPAddress.Loopback, port);
                break;
            }
            catch (SocketException) when (attempt < 40)
            {
                client?.Dispose();
                client = null;
                await Task.Delay(50);
            }
        }
        using (client)
        {
            using var ssl = useTls ? new SslStream(client.GetStream(), leaveInnerStreamOpen: false) : null;
            Stream stream = client.GetStream();
            if (ssl != null)
            {
                var options = TlsClientPolicy.CreateOptions("localhost");
                options.CertificateChainPolicy = new X509ChainPolicy
                {
                    TrustMode = X509ChainTrustMode.CustomRootTrust,
                    RevocationMode = X509RevocationMode.NoCheck,
                };
                options.CertificateChainPolicy.CustomTrustStore.Add(certificate);
                await ssl.AuthenticateAsClientAsync(options);
                stream = ssl;
            }

            bool previous = Packet.IsServer;
            Packet.IsServer = true;
            try
            {
                byte[] packet = new ClientPackets.KeepAlive { Time = 42 }.GetPacketBytes().ToArray();
                await stream.WriteAsync(packet);
                await stream.FlushAsync();
                MirConnection connection = null;
                Assert.True(SpinWait.SpinUntil(() =>
                {
                    lock (envir.Connections)
                        connection = envir.Connections.FirstOrDefault(item => item.Connected);
                    return connection != null;
                }, TimeSpan.FromSeconds(4)));

                bool processed = SpinWait.SpinUntil(
                    () => connection.ReceiveQueueHighWater > 0 && connection.ReceiveQueueDepth == 0,
                    TimeSpan.FromSeconds(4));
                connection.Disconnect(0);
                return processed;
            }
            finally
            {
                Packet.IsServer = previous;
            }
        }
    }

    private static async Task<byte[]> ReadFrameAsync(Stream stream, TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        byte[] header = new byte[4];
        await stream.ReadExactlyAsync(header, cancellation.Token);
        int length = BitConverter.ToUInt16(header, 0);
        if (length < 4) throw new InvalidDataException($"收到非法帧长度：{length}");
        byte[] frame = new byte[length];
        Buffer.BlockCopy(header, 0, frame, 0, header.Length);
        if (length > header.Length)
            await stream.ReadExactlyAsync(frame.AsMemory(header.Length, length - header.Length), cancellation.Token);
        return frame;
    }


    private static int GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static X509Certificate2 CreateCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("localhost");
        request.CertificateExtensions.Add(san.Build());
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "lyocrystal-c3-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            Directory.Delete(path, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"临时测试目录清理失败，保留路径：{path}（{ex.GetType().Name}）");
        }
    }
}
