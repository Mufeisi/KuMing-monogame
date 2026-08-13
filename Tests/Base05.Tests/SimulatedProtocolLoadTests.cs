using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Server;
using Server.MirDatabase;
using Server.MirEnvir;
using Server.Persistence;
using Server.Persistence.Sql;
using Server.Security;
using Shared.Diagnostics;
using Shared.Security;
using Server.Operations;
using Xunit;
using Xunit.Abstractions;

namespace Base05.Tests;

[Collection("TLS环境")]
public sealed class SimulatedProtocolLoadTests
{
    private const string ChildModeVariable = "LYOCRYSTAL_SIM_LOAD_CHILD";
    private const string ChildResultVariable = "LYOCRYSTAL_SIM_LOAD_RESULT";
    private const string Password = "LoadPass1234";
#if DEBUG
    private const string TestConfiguration = "Debug";
#else
    private const string TestConfiguration = "Release";
#endif
    private readonly ITestOutputHelper _output;

    public SimulatedProtocolLoadTests(ITestOutputHelper output) => _output = output;

    [Fact]
    [Trait("Category", "Load")]
    public async Task 模拟客户端维持连接并完成登录心跳与掉线补连()
    {
        if (string.Equals(Environment.GetEnvironmentVariable(ChildModeVariable), "1", StringComparison.Ordinal))
        {
            string resultPath = Environment.GetEnvironmentVariable(ChildResultVariable)
                ?? throw new InvalidOperationException("模拟压测子进程缺少结果路径。");
            string evidence = await RunScenarioAsync();
            string partialPath = resultPath + ".partial";
            File.WriteAllText(partialPath, evidence);
            File.Move(partialPath, resultPath);
            return;
        }

        string isolatedEvidence = await RunInIsolatedProcessAsync();
        _output.WriteLine(isolatedEvidence);
    }

    private static async Task<string> RunScenarioAsync()
    {
        int connections = ReadBoundedSetting("LYOCRYSTAL_LOAD_CONNECTIONS", 12, 1, 500);
        int active = ReadBoundedSetting("LYOCRYSTAL_LOAD_ACTIVE", 4, 1, connections);
        int durationSeconds = ReadBoundedSetting("LYOCRYSTAL_LOAD_DURATION_SECONDS", 4, 2, 600);

        using var scope = new ProtocolLoadScope(connections, active);
        scope.Start();
        Assert.True(
            SpinWait.SpinUntil(() => scope.Environment.StartState == EnvirStartState.Ready, TimeSpan.FromSeconds(15)),
            scope.Environment.StartFailure?.ToString());

        PerformanceMetrics.StartSession($"simulated-protocol-{connections}-{active}");
        await using var load = new SimulatedLoadController(
            scope.TlsPort,
            scope.Certificate,
            connections,
            active,
            AccountId);

        await load.StartAsync(TimeSpan.FromSeconds(Math.Max(30, connections / 4)));
        Assert.Equal(connections, load.CurrentConnections);
        Assert.True(load.SuccessfulLogins >= active, $"登录成功次数不足：{load.SuccessfulLogins}/{active}");

        load.Drop(0);
        await load.WaitForReplenishmentAsync(TimeSpan.FromSeconds(15));
        Assert.Equal(connections, load.CurrentConnections);
        Assert.True(load.SuccessfulLogins >= active + 1, "主动断开的登录会话没有完成重新登录。");

        int[] activeHeartbeatBaseline = load.CaptureActiveHeartbeatCounts();
        await Task.Delay(TimeSpan.FromSeconds(durationSeconds));
        load.AssertActiveHeartbeatProgress(
            activeHeartbeatBaseline,
            minimumNewReplies: Math.Max(1, durationSeconds / 2),
            maximumSilence: TimeSpan.FromSeconds(5));
        Assert.Equal(connections, load.CurrentConnections);
        Assert.Equal(active, load.CurrentActiveConnections);
        PerformanceSnapshot snapshot = PerformanceMetrics.StopSession();
        SimulatedLoadResult result = await load.StopAsync(snapshot, scope.Environment.NetworkQueueHighWater);

        Assert.Equal(connections, result.PeakConnections);
        Assert.True(result.Replenishments >= 1, "主动断开后没有补足目标连接数。");
        Assert.True(result.KeepAliveReplies >= active, "活跃会话没有完成至少一轮心跳往返。");
        Assert.True(result.ProtocolFailures == 0, $"协议失败 {result.ProtocolFailures} 次，首个错误：{load.FirstError}");
        Assert.True(result.KeepAliveP95Milliseconds < 5_000, $"心跳 p95 超出有界等待：{result.KeepAliveP95Milliseconds:F2}ms");

        return result.ToEvidenceLine();
    }

    private static async Task<string> RunInIsolatedProcessAsync()
    {
        string root = Path.Combine(Path.GetTempPath(), "lyocrystal-simload-parent-" + Guid.NewGuid().ToString("N"));
        string resultPath = Path.Combine(root, "result.txt");
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
            startInfo.ArgumentList.Add(TestConfiguration);
            startInfo.ArgumentList.Add("--no-build");
            startInfo.ArgumentList.Add("--no-restore");
            startInfo.ArgumentList.Add("--filter");
            startInfo.ArgumentList.Add(
                "FullyQualifiedName=Base05.Tests.SimulatedProtocolLoadTests.模拟客户端维持连接并完成登录心跳与掉线补连");
            startInfo.Environment[ChildModeVariable] = "1";
            startInfo.Environment[ChildResultVariable] = resultPath;

            child = Process.Start(startInfo) ?? throw new InvalidOperationException("无法启动模拟压测隔离子进程。");
            Task<string> stdout = child.StandardOutput.ReadToEndAsync();
            Task<string> stderr = child.StandardError.ReadToEndAsync();
            int durationSeconds = ReadBoundedSetting("LYOCRYSTAL_LOAD_DURATION_SECONDS", 4, 2, 600);
            TimeSpan childTimeout = TimeSpan.FromSeconds(Math.Max(240, durationSeconds + 180));
            using var timeout = new CancellationTokenSource(childTimeout);
            try
            {
                await child.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException($"模拟压测隔离子进程未在 {childTimeout.TotalMinutes:F1} 分钟内结束。");
            }

            string capturedStdout = await stdout;
            string capturedStderr = await stderr;
            if (child.ExitCode != 0 || !File.Exists(resultPath))
                throw new Xunit.Sdk.XunitException(
                    $"模拟压测隔离子进程失败：exit={child.ExitCode}{Environment.NewLine}{capturedStdout}{Environment.NewLine}{capturedStderr}");
            return File.ReadAllText(resultPath);
        }
        finally
        {
            if (child is { HasExited: false })
            {
                try { child.Kill(entireProcessTree: true); child.WaitForExit(10000); } catch { }
            }
            child?.Dispose();
            TryDeleteDirectory(root);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch { }
    }

    private static int ReadBoundedSetting(string name, int defaultValue, int minimum, int maximum)
    {
        string? raw = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(raw)) return defaultValue;
        if (!int.TryParse(raw, out int value) || value < minimum || value > maximum)
            throw new InvalidOperationException($"{name} 必须在 {minimum}..{maximum} 之间，当前值：{raw}");
        return value;
    }

    private static string AccountId(int index) => $"load{index:D4}";

    private sealed class ProtocolLoadScope : IDisposable
    {
        private readonly string _directory;
        private readonly IDisposable _secretScope;
        private readonly SettingsSnapshot _settings;
        private readonly bool _packetDirection;
        private readonly string? _perfEnabled;
        private readonly GatewayTrafficGovernance _gatewayGovernance;
        public int TlsPort { get; }
        public X509Certificate2 Certificate { get; }
        public Envir Environment { get; }

        public ProtocolLoadScope(int connections, int active)
        {
            _directory = Path.Combine(Path.GetTempPath(), "lyocrystal-simload-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            _secretScope = ProtectedSecretStore.UseTestRoot(Path.Combine(_directory, "Secrets"));
            _settings = SettingsSnapshot.Capture();
            _packetDirection = Packet.IsServer;
            _perfEnabled = System.Environment.GetEnvironmentVariable("LYOCRYSTAL_PERF00_ENABLED");

            int legacyPort = GetFreePort();
            TlsPort = GetFreePort();
            string certificatePath = Path.Combine(_directory, "load-server.pfx");
            Certificate = CreateCertificate();
            File.WriteAllBytes(certificatePath, Certificate.Export(X509ContentType.Pfx, "load-test-pfx"));
            ProtectedSecretStore.Write(ProtectedSecretStore.TlsCertificatePassword, "load-test-pfx");

            Settings.IPAddress = "127.0.0.1";
            Settings.Port = (ushort)legacyPort;
            Settings.TlsPort = (ushort)TlsPort;
            Settings.TlsEnabled = true;
            Settings.AllowLegacyV1 = false;
            Settings.TlsCertificatePath = certificatePath;
            Settings.DatabaseProvider = "Sqlite";
            Settings.SqlitePath = Path.Combine(_directory, "load-server.db");
            Settings.AutoApplySchemaOnStartup = true;
            Settings.AutoImportLegacyOnEmpty = false;
            Settings.MaxUser = (ushort)Math.Min(ushort.MaxValue, connections + 20);
            Settings.MaxIP = (ushort)Math.Min(ushort.MaxValue, connections + 20);
            // 单一回环源承载全部模拟连接；负值只在本测试域关闭“刚连接即短封禁”的同 IP 节流。
            Settings.IPBlockSeconds = -1;
            Settings.TimeOut = 60_000;
            Settings.CheckVersion = false;
            Settings.AllowLogin = true;
            Settings.LoginIpAttemptLimit = Math.Max(Settings.LoginIpAttemptLimit, active * 4);
            System.Environment.SetEnvironmentVariable("LYOCRYSTAL_PERF00_ENABLED", null);
            Packet.IsServer = true;
            Envir.IPBlocks.Clear();
            _gatewayGovernance = new GatewayTrafficGovernance(
                Path.Combine(_directory, "gateway-governance.json"), auditSink: _ => { });

            SeedAccounts(Settings.SqlitePath, active);
            // 账号事实仍使用生产单例；每个 MirConnection 则显式持有创建它的 Envir。
            Environment = Envir.Main;
            typeof(Envir).GetField("_persistence", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(Environment, null);
            Environment.LoadAccounts();
            if (Environment.AccountList.Count != active ||
                Environment.VerifyAccountPassword(Environment.AccountList[0], Password) == Server.Utils.PasswordVerificationResult.Invalid)
                throw new InvalidOperationException($"隔离 SQLite 账号种子校验失败：expected={active}, actual={Environment.AccountList.Count}");
            typeof(Envir).GetField("StatusPortEnabled", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(Environment, false);
        }

        public void Start() => Environment.Start(new EnvirStartOptions
        {
            EnforceProductionSecurity = false,
            LoadResources = false,
            BindNetwork = true,
            StartScripts = false,
            StartHttp = false,
            SaveOnStop = false,
            Multithreaded = false,
            GatewayGovernance = _gatewayGovernance,
        });

        public void Dispose()
        {
            Environment.Stop();
            PerformanceMetrics.Configure(enabled: false);
            Packet.IsServer = _packetDirection;
            _settings.Restore();
            typeof(Envir).GetField("_persistence", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(Environment, null);
            System.Environment.SetEnvironmentVariable("LYOCRYSTAL_PERF00_ENABLED", _perfEnabled);
            Certificate.Dispose();
            _secretScope.Dispose();
            TryDeleteDirectory(_directory);
        }

        private static void SeedAccounts(string databasePath, int count)
        {
            var source = new Envir();
            var passwordTemplate = new AccountInfo { Password = Password };
            for (int i = 0; i < count; i++)
            {
                var account = new AccountInfo
                {
                    Index = i + 1,
                    AccountID = AccountId(i),
                    UserName = $"模拟账号{i:D4}",
                    CreationDate = DateTime.UtcNow,
                    CreationIP = "127.0.0.1",
                };
                account.SetPasswordHashAndSalt(passwordTemplate.Password, Array.Empty<byte>());
                source.AccountList.Add(account);
            }

            var persistence = new SqlServerPersistence(
                DatabaseProviderKind.Sqlite,
                new SqlDatabaseOptions { SqlitePath = databasePath });
            persistence.SaveAccounts(source);
            ((IPendingSaveCoordinator)persistence).DrainPendingSaves();
        }

        private static int GetFreePort()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }

        private static X509Certificate2 CreateCertificate()
        {
            using RSA rsa = RSA.Create(2048);
            var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var san = new SubjectAlternativeNameBuilder();
            san.AddDnsName("localhost");
            request.CertificateExtensions.Add(san.Build());
            return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(2));
        }

        private static void TryDeleteDirectory(string path)
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    if (Directory.Exists(path)) Directory.Delete(path, true);
                    return;
                }
                catch (IOException) when (attempt < 4)
                {
                    Thread.Sleep(50);
                }
                catch (UnauthorizedAccessException) when (attempt < 4)
                {
                    Thread.Sleep(50);
                }
                catch (IOException)
                {
                    return;
                }
                catch (UnauthorizedAccessException)
                {
                    return;
                }
            }
        }
    }

    private sealed record SettingsSnapshot(
        string Address,
        ushort Port,
        ushort TlsPort,
        bool TlsEnabled,
        bool Legacy,
        string CertificatePath,
        string Provider,
        string SqlitePath,
        bool AutoApply,
        bool AutoImport,
        ushort MaxUser,
        ushort MaxIp,
        int IpBlockSeconds,
        ushort Timeout,
        bool CheckVersion,
        bool AllowLogin,
        int LoginIpAttemptLimit)
    {
        public static SettingsSnapshot Capture() => new(
            Settings.IPAddress,
            Settings.Port,
            Settings.TlsPort,
            Settings.TlsEnabled,
            Settings.AllowLegacyV1,
            Settings.TlsCertificatePath,
            Settings.DatabaseProvider,
            Settings.SqlitePath,
            Settings.AutoApplySchemaOnStartup,
            Settings.AutoImportLegacyOnEmpty,
            Settings.MaxUser,
            Settings.MaxIP,
            Settings.IPBlockSeconds,
            Settings.TimeOut,
            Settings.CheckVersion,
            Settings.AllowLogin,
            Settings.LoginIpAttemptLimit);

        public void Restore()
        {
            Settings.IPAddress = Address;
            Settings.Port = Port;
            Settings.TlsPort = TlsPort;
            Settings.TlsEnabled = TlsEnabled;
            Settings.AllowLegacyV1 = Legacy;
            Settings.TlsCertificatePath = CertificatePath;
            Settings.DatabaseProvider = Provider;
            Settings.SqlitePath = SqlitePath;
            Settings.AutoApplySchemaOnStartup = AutoApply;
            Settings.AutoImportLegacyOnEmpty = AutoImport;
            Settings.MaxUser = MaxUser;
            Settings.MaxIP = MaxIp;
            Settings.IPBlockSeconds = IpBlockSeconds;
            Settings.TimeOut = Timeout;
            Settings.CheckVersion = CheckVersion;
            Settings.AllowLogin = AllowLogin;
            Settings.LoginIpAttemptLimit = LoginIpAttemptLimit;
        }
    }

    private sealed class SimulatedLoadController : IAsyncDisposable
    {
        private readonly int _port;
        private readonly X509Certificate2 _certificate;
        private readonly int _connections;
        private readonly int _active;
        private readonly Func<int, string> _accountId;
        private readonly CancellationTokenSource _stop = new();
        private readonly SemaphoreSlim _connectGate = new(32);
        private readonly ConcurrentDictionary<int, SimulatedClient> _clients = new();
        private readonly ConcurrentDictionary<int, byte> _expectedDrops = new();
        private readonly ConcurrentBag<double> _latencies = new();
        private readonly int[] _activeHeartbeatCounts;
        private readonly long[] _activeLastHeartbeatTimestamps;
        private string? _firstError;
        private Task[] _slots = Array.Empty<Task>();
        private int _currentConnections;
        private int _currentActiveConnections;
        private int _peakConnections;
        private int _successfulLogins;
        private int _keepAliveReplies;
        private int _connectionRetries;
        private int _protocolFailures;
        private int _replenishments;

        public int CurrentConnections => Volatile.Read(ref _currentConnections);
        public int CurrentActiveConnections => Volatile.Read(ref _currentActiveConnections);
        public int SuccessfulLogins => Volatile.Read(ref _successfulLogins);
        public string? FirstError => Volatile.Read(ref _firstError);

        public SimulatedLoadController(
            int port,
            X509Certificate2 certificate,
            int connections,
            int active,
            Func<int, string> accountId)
        {
            _port = port;
            _certificate = certificate;
            _connections = connections;
            _active = active;
            _accountId = accountId;
            _activeHeartbeatCounts = new int[active];
            _activeLastHeartbeatTimestamps = new long[active];
        }

        public int[] CaptureActiveHeartbeatCounts()
        {
            var snapshot = new int[_active];
            for (int slot = 0; slot < _active; slot++)
                snapshot[slot] = Volatile.Read(ref _activeHeartbeatCounts[slot]);
            return snapshot;
        }

        public void AssertActiveHeartbeatProgress(int[] baseline, int minimumNewReplies, TimeSpan maximumSilence)
        {
            if (baseline == null || baseline.Length != _active)
                throw new ArgumentException("活跃心跳基线数量与目标不一致。", nameof(baseline));

            long now = Stopwatch.GetTimestamp();
            var unhealthy = new List<string>();
            for (int slot = 0; slot < _active; slot++)
            {
                int current = Volatile.Read(ref _activeHeartbeatCounts[slot]);
                long last = Volatile.Read(ref _activeLastHeartbeatTimestamps[slot]);
                double silenceMilliseconds = last == 0
                    ? double.PositiveInfinity
                    : Stopwatch.GetElapsedTime(last, now).TotalMilliseconds;
                if (current - baseline[slot] < minimumNewReplies || silenceMilliseconds > maximumSilence.TotalMilliseconds)
                    unhealthy.Add($"{slot}:新增{current - baseline[slot]},静默{silenceMilliseconds:F0}ms");
            }

            if (unhealthy.Count > 0)
                throw new InvalidOperationException(
                    $"登录会话心跳未持续达标（每槽至少新增 {minimumNewReplies} 次，末次不早于 {maximumSilence.TotalSeconds:F0}s）：" +
                    string.Join(";", unhealthy.Take(20)));
        }

        public async Task StartAsync(TimeSpan timeout)
        {
            _slots = Enumerable.Range(0, _connections)
                .Select(slot => Task.Run(() => RunSlotAsync(slot, _stop.Token)))
                .ToArray();

            using var wait = new CancellationTokenSource(timeout);
            try
            {
                while (CurrentConnections < _connections)
                {
                    wait.Token.ThrowIfCancellationRequested();
                    await Task.Delay(50, wait.Token);
                }
            }
            catch (OperationCanceledException) when (wait.IsCancellationRequested)
            {
                throw new InvalidOperationException(
                    $"模拟连接未在 {timeout.TotalSeconds:F0}s 内达到目标：current={CurrentConnections}, " +
                    $"failures={Volatile.Read(ref _protocolFailures)}, first={Volatile.Read(ref _firstError)}");
            }
        }

        public void Drop(int slot)
        {
            if (_clients.TryGetValue(slot, out SimulatedClient? client))
            {
                _expectedDrops[slot] = 0;
                client.Dispose();
            }
        }

        public async Task WaitForReplenishmentAsync(TimeSpan timeout)
        {
            using var wait = new CancellationTokenSource(timeout);
            while (Volatile.Read(ref _replenishments) < 1 || CurrentConnections < _connections)
            {
                wait.Token.ThrowIfCancellationRequested();
                await Task.Delay(50, wait.Token);
            }
        }

        public async Task<SimulatedLoadResult> StopAsync(PerformanceSnapshot snapshot, int networkQueueHighWater)
        {
            _stop.Cancel();
            await AwaitSlotsAfterCancellationAsync();
            double[] ordered = _latencies.OrderBy(value => value).ToArray();
            double p95 = ordered.Length == 0 ? 0 : ordered[(int)Math.Ceiling(ordered.Length * 0.95) - 1];
            PerformanceMetricSnapshot? update = snapshot.Metrics.SingleOrDefault(metric => metric.Name == nameof(PerformanceMetricKind.Update));
            PerformanceMetricSnapshot? gcPause = snapshot.Metrics.SingleOrDefault(metric => metric.Name == nameof(PerformanceMetricKind.GcPause));
            return new SimulatedLoadResult(
                _connections,
                _active,
                _peakConnections,
                _successfulLogins,
                _keepAliveReplies,
                _connectionRetries,
                _protocolFailures,
                _replenishments,
                p95,
                update?.P95Milliseconds,
                gcPause?.P95Milliseconds,
                networkQueueHighWater);
        }

        public async ValueTask DisposeAsync()
        {
            _stop.Cancel();
            await AwaitSlotsAfterCancellationAsync();
            _stop.Dispose();
            _connectGate.Dispose();
        }

        private async Task AwaitSlotsAfterCancellationAsync()
        {
            if (_slots.Length == 0) return;
            try
            {
                await Task.WhenAll(_slots);
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
            }
        }

        private async Task RunSlotAsync(int slot, CancellationToken cancellationToken)
        {
            bool firstConnection = true;
            while (!cancellationToken.IsCancellationRequested)
            {
                SimulatedClient? client = null;
                bool admitted = false;
                bool activeAdmitted = false;
                bool protocolReady = false;
                bool connectGateHeld = false;
                try
                {
                    await _connectGate.WaitAsync(cancellationToken);
                    connectGateHeld = true;
                    client = await SimulatedClient.ConnectAsync(_port, _certificate, cancellationToken);

                    _clients[slot] = client;
                    await client.EnterLoginStageAsync(cancellationToken);
                    protocolReady = true;
                    if (slot < _active)
                    {
                        await client.LoginAsync(_accountId(slot), Password, cancellationToken);
                        Interlocked.Increment(ref _successfulLogins);
                    }
                    _connectGate.Release();
                    connectGateHeld = false;

                    int current = Interlocked.Increment(ref _currentConnections);
                    admitted = true;
                    if (slot < _active)
                    {
                        Interlocked.Increment(ref _currentActiveConnections);
                        activeAdmitted = true;
                    }
                    UpdatePeak(current);
                    if (!firstConnection) Interlocked.Increment(ref _replenishments);
                    firstConnection = false;

                    TimeSpan heartbeatInterval = slot < _active
                        ? TimeSpan.FromMilliseconds(200)
                        : TimeSpan.FromSeconds(1);
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        double elapsed = await client.KeepAliveAsync(cancellationToken);
                        _latencies.Add(elapsed);
                        Interlocked.Increment(ref _keepAliveReplies);
                        if (slot < _active)
                        {
                            Interlocked.Increment(ref _activeHeartbeatCounts[slot]);
                            Volatile.Write(ref _activeLastHeartbeatTimestamps[slot], Stopwatch.GetTimestamp());
                        }
                        await Task.Delay(heartbeatInterval, cancellationToken);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    if (!_expectedDrops.TryRemove(slot, out _))
                    {
                        Interlocked.CompareExchange(
                            ref _firstError,
                            $"slot={slot}, {ex.GetType().Name}:{ex.Message}",
                            null);
                        if (protocolReady)
                            Interlocked.Increment(ref _protocolFailures);
                        else
                            Interlocked.Increment(ref _connectionRetries);
                    }
                    try
                    {
                        await Task.Delay(250, cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }
                }
                finally
                {
                    if (connectGateHeld) _connectGate.Release();
                    _clients.TryRemove(slot, out _);
                    client?.Dispose();
                    if (admitted) Interlocked.Decrement(ref _currentConnections);
                    if (activeAdmitted) Interlocked.Decrement(ref _currentActiveConnections);
                }
            }
        }

        private void UpdatePeak(int current)
        {
            int observed;
            while (current > (observed = Volatile.Read(ref _peakConnections)) &&
                   Interlocked.CompareExchange(ref _peakConnections, current, observed) != observed)
            {
            }
        }
    }

    private sealed class SimulatedClient : IDisposable
    {
        private readonly TcpClient _client;
        private readonly SslStream _stream;
        private byte[] _pending = Array.Empty<byte>();

        private SimulatedClient(TcpClient client, SslStream stream)
        {
            _client = client;
            _stream = stream;
        }

        public static async Task<SimulatedClient> ConnectAsync(
            int port,
            X509Certificate2 certificate,
            CancellationToken cancellationToken)
        {
            var client = new TcpClient { NoDelay = true };
            SslStream? ssl = null;
            try
            {
                await client.ConnectAsync(IPAddress.Loopback, port, cancellationToken);
                ssl = new SslStream(client.GetStream(), leaveInnerStreamOpen: false);
                var options = TlsClientPolicy.CreateOptions("localhost");
                options.CertificateChainPolicy = new X509ChainPolicy
                {
                    TrustMode = X509ChainTrustMode.CustomRootTrust,
                    RevocationMode = X509RevocationMode.NoCheck,
                };
                options.CertificateChainPolicy.CustomTrustStore.Add(certificate);
                await ssl.AuthenticateAsClientAsync(options, cancellationToken);
                return new SimulatedClient(client, ssl);
            }
            catch
            {
                ssl?.Dispose();
                client.Dispose();
                throw;
            }
        }

        public async Task EnterLoginStageAsync(CancellationToken cancellationToken)
        {
            await ReadUntilAsync((short)ServerPacketIds.Connected, cancellationToken);
            await SendAsync(new ClientPackets.ClientVersion { VersionHash = Array.Empty<byte>() }, cancellationToken);
            await ReadUntilAsync((short)ServerPacketIds.ClientVersion, cancellationToken);
        }

        public async Task LoginAsync(string accountId, string password, CancellationToken cancellationToken)
        {
            await SendAsync(new ClientPackets.Login { AccountID = accountId, Password = password }, cancellationToken);
            await ReadUntilAsync((short)ServerPacketIds.LoginSuccess, cancellationToken);
        }

        public async Task<double> KeepAliveAsync(CancellationToken cancellationToken)
        {
            long started = Stopwatch.GetTimestamp();
            await SendAsync(new ClientPackets.KeepAlive { Time = started }, cancellationToken);
            await ReadUntilAsync((short)ServerPacketIds.KeepAlive, cancellationToken);
            return Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        }

        public void Dispose()
        {
            _stream.Dispose();
            _client.Dispose();
        }

        private async Task SendAsync(Packet packet, CancellationToken cancellationToken)
        {
            byte[] data = packet.GetPacketBytes().ToArray();
            await _stream.WriteAsync(data, cancellationToken);
            await _stream.FlushAsync(cancellationToken);
        }

        private async Task ReadUntilAsync(short expectedId, CancellationToken cancellationToken)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            // 300 路 TLS 建连洪峰期间主循环仍需处理所有已建立会话，首包/心跳采用有界 30 秒等待。
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            var observedIds = new List<short>();
            try
            {
                while (true)
                {
                    (short id, byte[] payload) = await ReadFrameAsync(timeout.Token);
                    observedIds.Add(id);
                    if (id == expectedId) return;
                    if (id == (short)ServerPacketIds.LoginBanned)
                    {
                        using var stream = new MemoryStream(payload, writable: false);
                        using var reader = new BinaryReader(stream);
                        string reason = reader.ReadString();
                        DateTime expiry = DateTime.FromBinary(reader.ReadInt64());
                        throw new IOException($"登录被拒绝：{reason}，到期时间 {expiry:O}。");
                    }
                    if (id == (short)ServerPacketIds.Login)
                    {
                        byte result = payload.Length == 0 ? byte.MaxValue : payload[0];
                        throw new IOException($"登录失败，服务端结果码 {result}。");
                    }
                    if (id == (short)ServerPacketIds.Disconnect)
                        throw new IOException("服务端在等待协议响应时断开连接。");
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException(
                    $"等待服务端包 {expectedId} 超时，已见 [{string.Join(',', observedIds)}]，缓冲区 {_pending.Length} 字节。");
            }
        }

        private async Task<(short Id, byte[] Payload)> ReadFrameAsync(CancellationToken cancellationToken)
        {
            while (_pending.Length < 4)
                await ReadMoreAsync(cancellationToken);

            int length = BitConverter.ToUInt16(_pending, 0);
            if (length < 4 || length > 64 * 1024)
                throw new InvalidDataException($"收到非法数据包长度：{length}");
            while (_pending.Length < length)
                await ReadMoreAsync(cancellationToken);

            short id = BitConverter.ToInt16(_pending, 2);
            byte[] payload = _pending[4..length];
            _pending = _pending[length..];
            return (id, payload);
        }

        private async Task ReadMoreAsync(CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[8 * 1024];
            int read = await _stream.ReadAsync(buffer, cancellationToken);
            if (read == 0) throw new EndOfStreamException("服务端连接已关闭。");
            int previous = _pending.Length;
            Array.Resize(ref _pending, previous + read);
            Buffer.BlockCopy(buffer, 0, _pending, previous, read);
        }
    }

    private sealed record SimulatedLoadResult(
        int TargetConnections,
        int TargetActive,
        int PeakConnections,
        int SuccessfulLogins,
        int KeepAliveReplies,
        int ConnectionRetries,
        int ProtocolFailures,
        int Replenishments,
        double KeepAliveP95Milliseconds,
        double? TickP95Milliseconds,
        double? GcPauseP95Milliseconds,
        int NetworkQueueHighWater)
    {
        public string ToEvidenceLine() =>
            $"SIMULATED_LOAD_RESULT target={TargetConnections} active={TargetActive} peak={PeakConnections} " +
            $"logins={SuccessfulLogins} keepAliveReplies={KeepAliveReplies} replenishments={Replenishments} " +
            $"connectionRetries={ConnectionRetries} protocolFailures={ProtocolFailures} " +
            $"keepAliveP95Ms={KeepAliveP95Milliseconds:F2} " +
            $"tickP95Ms={TickP95Milliseconds?.ToString("F2") ?? "NA"} " +
            $"gcPauseP95Ms={GcPauseP95Milliseconds?.ToString("F2") ?? "NA"} queueHighWater={NetworkQueueHighWater}";
    }
}
