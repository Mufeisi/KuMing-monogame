using Server.Operations;
using Server.Security;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Server;
using Server.Library.Utils;
using Xunit;
using Xunit.Abstractions;

namespace Base05.Tests;

[Collection("SEC04环境")]
public sealed class GatewayTrafficGovernanceTests
{
    private readonly ITestOutputHelper _output;

    public GatewayTrafficGovernanceTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void 默认观察模式统计超频但不误杀正常或异常动作()
    {
        using var fixture = new Fixture();
        DateTimeOffset now = DateTimeOffset.UnixEpoch;
        var audits = new List<string>();
        var governance = new GatewayTrafficGovernance(fixture.PolicyPath, () => now, audits.Add);

        for (int index = 0; index < 120; index++)
            Assert.True(governance.EvaluatePacket(7, "198.51.100.7", (short)ClientPacketIds.Walk).Allow);

        GatewayGovernanceDecision violation = governance.EvaluatePacket(
            7, "198.51.100.7", (short)ClientPacketIds.Walk);
        GatewayGovernanceSnapshot snapshot = governance.CaptureSnapshot();
        GatewayTrafficCategorySnapshot movement = Assert.Single(
            snapshot.Categories, value => value.Category == GatewayTrafficCategory.Movement);
        GatewayGovernanceEvidence evidence = Assert.Single(snapshot.RecentEvidence);

        Assert.True(violation.Allow);
        Assert.False(violation.Disconnect);
        Assert.Equal(GatewayGovernanceMode.Observe, snapshot.Policy.Mode);
        Assert.Equal(121, movement.Observed);
        Assert.Equal(1, movement.Violations);
        Assert.Equal(0, movement.Enforced);
        Assert.False(evidence.Enforced);
        Assert.Equal(7, evidence.SessionId);
        Assert.NotEqual("198.51.100.7", evidence.ClientReference);
        Assert.DoesNotContain(audits, line => line.Contains("198.51.100.7", StringComparison.Ordinal));
    }

    [Fact]
    public void 执行模式按动作分类丢弃限制断开并保留结构化证据()
    {
        using var fixture = new Fixture();
        DateTimeOffset now = DateTimeOffset.UnixEpoch;
        var governance = new GatewayTrafficGovernance(fixture.PolicyPath, () => now, _ => { });
        GatewayGovernancePolicy baseline = governance.CaptureSnapshot().Policy;
        GatewayTrafficRule[] rules = baseline.Rules.Select(rule => rule.Category switch
        {
            GatewayTrafficCategory.Movement => With(rule, 2, GatewayResponseLevel.DropAction),
            GatewayTrafficCategory.Chat => With(rule, 1, GatewayResponseLevel.TemporaryRestriction, 5),
            _ => rule,
        }).ToArray();
        governance.SetPolicy(new GatewayGovernanceChangeRequest
        {
            ExpectedRevision = baseline.Revision,
            Mode = GatewayGovernanceMode.Enforce,
            MaximumPacketBytes = 2048,
            Rules = rules,
            Reason = "测试执行模式分级处置",
        }, "test-admin");

        Assert.True(governance.EvaluatePacket(11, "203.0.113.11", (short)ClientPacketIds.Walk).Allow);
        Assert.True(governance.EvaluatePacket(11, "203.0.113.11", (short)ClientPacketIds.Run).Allow);
        GatewayGovernanceDecision movement = governance.EvaluatePacket(11, "203.0.113.11", (short)ClientPacketIds.Turn);
        Assert.False(movement.Allow);
        Assert.False(movement.Disconnect);
        Assert.Equal(GatewayResponseLevel.DropAction, movement.Response);

        Assert.True(governance.EvaluatePacket(11, "203.0.113.11", (short)ClientPacketIds.Chat).Allow);
        GatewayGovernanceDecision chat = governance.EvaluatePacket(11, "203.0.113.11", (short)ClientPacketIds.Chat);
        Assert.False(chat.Allow);
        Assert.Equal(GatewayResponseLevel.TemporaryRestriction, chat.Response);
        now = now.AddSeconds(1);
        Assert.False(governance.EvaluatePacket(11, "203.0.113.11", (short)ClientPacketIds.Chat).Allow);
        now = now.AddSeconds(5);
        Assert.True(governance.EvaluatePacket(11, "203.0.113.11", (short)ClientPacketIds.Chat).Allow);

        GatewayGovernanceDecision oversized = governance.EvaluatePacketSize(11, "203.0.113.11", 2049);
        Assert.False(oversized.Allow);
        Assert.True(oversized.Disconnect);
        Assert.All(governance.CaptureSnapshot().RecentEvidence, value => Assert.True(value.Enforced));

        GatewayGovernancePolicy current = governance.CaptureSnapshot().Policy;
        GatewayTrafficRule[] warningRules = current.Rules.Select(rule => rule.Category == GatewayTrafficCategory.Login
            ? With(rule, 1, GatewayResponseLevel.Warning)
            : rule).ToArray();
        governance.SetPolicy(new GatewayGovernanceChangeRequest
        {
            ExpectedRevision = current.Revision,
            Mode = GatewayGovernanceMode.Enforce,
            MaximumPacketBytes = current.MaximumPacketBytes,
            Rules = warningRules,
            Reason = "测试警告处置仍允许动作",
        }, "test-admin");
        Assert.True(governance.EvaluatePacket(12, "203.0.113.12", (short)ClientPacketIds.Login).Allow);
        GatewayGovernanceDecision warning = governance.EvaluatePacket(12, "203.0.113.12", (short)ClientPacketIds.Login);
        Assert.True(warning.Allow);
        Assert.Equal(GatewayResponseLevel.Warning, warning.Response);
    }

    [Fact]
    public void 人工封禁只进入复核并断开当前会话不写永久封禁()
    {
        using var fixture = new Fixture();
        var governance = new GatewayTrafficGovernance(fixture.PolicyPath, auditSink: _ => { });
        GatewayGovernancePolicy baseline = governance.CaptureSnapshot().Policy;
        GatewayTrafficRule[] rules = baseline.Rules.Select(rule => rule.Category == GatewayTrafficCategory.Attack
            ? With(rule, 1, GatewayResponseLevel.ManualBanReview)
            : rule).ToArray();
        governance.SetPolicy(new GatewayGovernanceChangeRequest
        {
            ExpectedRevision = baseline.Revision,
            Mode = GatewayGovernanceMode.Enforce,
            MaximumPacketBytes = baseline.MaximumPacketBytes,
            Rules = rules,
            Reason = "测试人工封禁复核边界",
        }, "test-admin");

        Assert.True(governance.EvaluatePacket(19, "192.0.2.19", (short)ClientPacketIds.Attack).Allow);
        GatewayGovernanceDecision decision = governance.EvaluatePacket(19, "192.0.2.19", (short)ClientPacketIds.Attack);

        Assert.False(decision.Allow);
        Assert.True(decision.Disconnect);
        Assert.True(decision.ManualBanReview);
        Assert.False(Server.MirEnvir.Envir.IPBlocks.ContainsKey("192.0.2.19"));
    }

    [Fact]
    public void 七类流量均有独立规则且关闭模式零计数零处置()
    {
        using var fixture = new Fixture();
        var governance = new GatewayTrafficGovernance(fixture.PolicyPath, auditSink: _ => { });
        GatewayGovernancePolicy baseline = governance.CaptureSnapshot().Policy;
        Assert.Equal(Enum.GetValues<GatewayTrafficCategory>(), baseline.Rules.Select(value => value.Category));

        governance.SetPolicy(new GatewayGovernanceChangeRequest
        {
            ExpectedRevision = baseline.Revision,
            Mode = GatewayGovernanceMode.Disabled,
            MaximumPacketBytes = baseline.MaximumPacketBytes,
            Rules = baseline.Rules,
            Reason = "测试关闭网关治理规则",
        }, "test-admin");

        Assert.True(governance.EvaluatePacket(1, "127.0.0.1", (short)ClientPacketIds.Login).Allow);
        Assert.True(governance.EvaluatePacketSize(1, "127.0.0.1", ushort.MaxValue).Allow);
        Assert.All(governance.CaptureSnapshot().Categories, value => Assert.Equal(0, value.Observed));
    }

    [Theory]
    [InlineData(ClientPacketIds.Login, (int)GatewayTrafficCategory.Login)]
    [InlineData(ClientPacketIds.Walk, (int)GatewayTrafficCategory.Movement)]
    [InlineData(ClientPacketIds.Attack, (int)GatewayTrafficCategory.Attack)]
    [InlineData(ClientPacketIds.Magic, (int)GatewayTrafficCategory.Spell)]
    [InlineData(ClientPacketIds.PickUp, (int)GatewayTrafficCategory.Pickup)]
    [InlineData(ClientPacketIds.Chat, (int)GatewayTrafficCategory.Chat)]
    public void 协议包映射到预期行为分类(ClientPacketIds packet, int expected)
    {
        Assert.Equal((GatewayTrafficCategory)expected, GatewayTrafficGovernance.Classify((short)packet));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"FormatVersion\":2}")]
    [InlineData("not-json")]
    public void 配置损坏缺字段或未知版本均失败关闭(string content)
    {
        using var fixture = new Fixture();
        File.WriteAllText(fixture.PolicyPath, content);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => new GatewayTrafficGovernance(fixture.PolicyPath, auditSink: _ => { }));

        Assert.Contains("拒绝启动网络监听", error.Message);
    }

    [Fact]
    public void 管理变更要求完整配置与匹配代次并原子持久化()
    {
        using var fixture = new Fixture();
        var governance = new GatewayTrafficGovernance(fixture.PolicyPath, auditSink: _ => { });
        GatewayGovernancePolicy baseline = governance.CaptureSnapshot().Policy;

        InvalidOperationException stale = Assert.Throws<InvalidOperationException>(() => governance.SetPolicy(
            new GatewayGovernanceChangeRequest
            {
                ExpectedRevision = baseline.Revision + 1,
                Mode = GatewayGovernanceMode.Enforce,
                MaximumPacketBytes = baseline.MaximumPacketBytes,
                Rules = baseline.Rules,
                Reason = "测试过期代次拒绝覆盖",
            }, "test-admin"));
        Assert.Contains("代次已变化", stale.Message);

        GatewayGovernancePolicy changed = governance.SetPolicy(new GatewayGovernanceChangeRequest
        {
            ExpectedRevision = baseline.Revision,
            Mode = GatewayGovernanceMode.Enforce,
            MaximumPacketBytes = baseline.MaximumPacketBytes,
            Rules = baseline.Rules,
            Reason = "测试原子保存完整配置",
        }, "test-admin");
        var reloaded = new GatewayTrafficGovernance(fixture.PolicyPath, auditSink: _ => { });

        Assert.Equal(1, changed.Revision);
        Assert.Equal(GatewayGovernanceMode.Enforce, reloaded.CaptureSnapshot().Policy.Mode);
        Assert.DoesNotContain(Directory.GetFiles(fixture.Root), value => value.Contains(".partial-", StringComparison.Ordinal));
    }

    [Fact]
    public void 配置持久化失败不发布进程内新策略()
    {
        using var fixture = new Fixture();
        var governance = new GatewayTrafficGovernance(fixture.PolicyPath, auditSink: _ => { });
        GatewayGovernancePolicy baseline = governance.CaptureSnapshot().Policy;
        File.Delete(fixture.PolicyPath);
        Directory.Delete(fixture.Root);
        File.WriteAllText(fixture.Root, "not-a-directory");

        Assert.ThrowsAny<IOException>(() => governance.SetPolicy(new GatewayGovernanceChangeRequest
        {
            ExpectedRevision = baseline.Revision,
            Mode = GatewayGovernanceMode.Enforce,
            MaximumPacketBytes = baseline.MaximumPacketBytes,
            Rules = baseline.Rules,
            Reason = "测试持久化失败不发布",
        }, "test-admin"));

        GatewayGovernancePolicy after = governance.CaptureSnapshot().Policy;
        Assert.Equal(baseline.Revision, after.Revision);
        Assert.Equal(GatewayGovernanceMode.Observe, after.Mode);
    }

    [Fact]
    public void 操作员仅可查询网关状态管理员才可修改()
    {
        const string administrator = "administrator-secret";
        const string operatorToken = "operator-secret";

        Assert.Equal(AdminAuthorizationStatus.Authorized, AdminSecurityPolicy.Authorize(
            "Bearer " + operatorToken, "/operations/gateway-governance", administrator, operatorToken).Status);
        Assert.Equal(AdminAuthorizationStatus.Forbidden, AdminSecurityPolicy.Authorize(
            "Bearer " + operatorToken, "/operations/gateway-governance/set", administrator, operatorToken).Status);
        Assert.Equal(AdminAuthorizationStatus.Authorized, AdminSecurityPolicy.Authorize(
            "Bearer " + administrator, "/operations/gateway-governance/set", administrator, operatorToken).Status);
    }

    [Fact]
    public async Task 真实管理端点允许操作员查询且仅管理员可提交完整策略()
    {
        using var fixture = new Fixture();
        string secretPath = Path.Combine(fixture.Root, "secrets");
        int port = GetFreePort();
        string originalAddress = Settings.HTTPIPAddress;
        string originalTrustedAddress = Settings.HTTPTrustedIPAddress;
        IDisposable secretScope = ProtectedSecretStore.UseTestRoot(secretPath);
        HttpServer server = null;
        try
        {
            Settings.HTTPIPAddress = $"http://127.0.0.1:{port}/";
            Settings.HTTPTrustedIPAddress = "127.0.0.1";
            ProtectedSecretStore.Write(ProtectedSecretStore.AdministratorToken, "administrator-secret-32-characters-minimum");
            ProtectedSecretStore.Write(ProtectedSecretStore.OperatorToken, "operator-secret-32-characters-minimum");
            var governance = new GatewayTrafficGovernance(fixture.PolicyPath, auditSink: _ => { });
            server = new HttpServer(gatewayGovernance: governance);
            server.Start();

            using var handler = new HttpClientHandler { UseProxy = false };
            using var client = new HttpClient(handler) { BaseAddress = new Uri(Settings.HTTPIPAddress) };
            using HttpResponseMessage query = await SendWhenReady(
                client, HttpMethod.Get, "/operations/gateway-governance", "operator-secret-32-characters-minimum");
            Assert.Equal(HttpStatusCode.OK, query.StatusCode);
            using JsonDocument snapshot = JsonDocument.Parse(await query.Content.ReadAsStringAsync());
            long revision = snapshot.RootElement.GetProperty("Policy").GetProperty("Revision").GetInt64();
            string body = JsonSerializer.Serialize(new
            {
                expectedRevision = revision,
                mode = "Disabled",
                maximumPacketBytes = 32768,
                rules = governance.CaptureSnapshot().Policy.Rules,
                reason = "管理端集成测试关闭规则",
            });

            using HttpResponseMessage forbidden = await SendWhenReady(
                client, HttpMethod.Post, "/operations/gateway-governance/set", "operator-secret-32-characters-minimum", body);
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
            using HttpResponseMessage changed = await SendWhenReady(
                client, HttpMethod.Post, "/operations/gateway-governance/set", "administrator-secret-32-characters-minimum", body);
            Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
            Assert.Equal(GatewayGovernanceMode.Disabled, governance.CaptureSnapshot().Policy.Mode);
        }
        finally
        {
            server?.Stop();
            secretScope.Dispose();
            Settings.HTTPIPAddress = originalAddress;
            Settings.HTTPTrustedIPAddress = originalTrustedAddress;
        }
    }

    [Fact]
    public void 观察分类的同环境中位绝对开销低于五微秒()
    {
        using var disabledFixture = new Fixture();
        using var observeFixture = new Fixture();
        var disabled = new GatewayTrafficGovernance(disabledFixture.PolicyPath, auditSink: _ => { });
        GatewayGovernancePolicy baseline = disabled.CaptureSnapshot().Policy;
        disabled.SetPolicy(new GatewayGovernanceChangeRequest
        {
            ExpectedRevision = baseline.Revision,
            Mode = GatewayGovernanceMode.Disabled,
            MaximumPacketBytes = baseline.MaximumPacketBytes,
            Rules = baseline.Rules,
            Reason = "性能基线关闭治理规则",
        }, "performance-test");
        var observe = new GatewayTrafficGovernance(observeFixture.PolicyPath, auditSink: _ => { });
        GatewayGovernancePolicy observeBaseline = observe.CaptureSnapshot().Policy;
        observe.SetPolicy(new GatewayGovernanceChangeRequest
        {
            ExpectedRevision = observeBaseline.Revision,
            Mode = GatewayGovernanceMode.Observe,
            MaximumPacketBytes = observeBaseline.MaximumPacketBytes,
            Rules = observeBaseline.Rules.Select(rule => rule.Category == GatewayTrafficCategory.Movement
                ? With(rule, 1_000_000, rule.Response, rule.RestrictionSeconds)
                : rule).ToArray(),
            Reason = "性能基线只测正常观察分类",
        }, "performance-test");

        for (int index = 0; index < 10_000; index++)
        {
            disabled.EvaluatePacket(index, "127.0.0.1", (short)ClientPacketIds.Walk);
            observe.EvaluatePacket(index, "127.0.0.1", (short)ClientPacketIds.Walk);
            observe.RemoveSession(index);
        }

        double disabledNanoseconds = MedianNanoseconds(disabled);
        double observeNanoseconds = MedianNanoseconds(observe);
        double addedMicroseconds = Math.Max(0, observeNanoseconds - disabledNanoseconds) / 1_000D;

        _output.WriteLine($"LEG03_PERFORMANCE disabledNs={disabledNanoseconds:F1} observeNs={observeNanoseconds:F1} addedUs={addedMicroseconds:F3} limitUs=5.000");

        Assert.True(addedMicroseconds < 5,
            $"观察分类每次绝对增量 {addedMicroseconds:F3}us，超过 5us；disabled={disabledNanoseconds:F1}ns observe={observeNanoseconds:F1}ns");
    }

    private static double MedianNanoseconds(GatewayTrafficGovernance governance)
    {
        const int iterations = 100_000;
        var samples = new double[5];
        for (int round = 0; round < samples.Length; round++)
        {
            var watch = Stopwatch.StartNew();
            for (int index = 0; index < iterations; index++)
                governance.EvaluatePacket(42, "127.0.0.1", (short)ClientPacketIds.Walk);
            watch.Stop();
            samples[round] = watch.ElapsedTicks * 1_000_000_000D / Stopwatch.Frequency / iterations;
        }
        Array.Sort(samples);
        return samples[samples.Length / 2];
    }

    private static async Task<HttpResponseMessage> SendWhenReady(
        HttpClient client,
        HttpMethod method,
        string path,
        string bearerToken,
        string? body = null)
    {
        Exception last = null;
        for (int attempt = 0; attempt < 30; attempt++)
        {
            try
            {
                var request = new HttpRequestMessage(method, path);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
                if (body != null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                return await client.SendAsync(request);
            }
            catch (HttpRequestException error)
            {
                last = error;
                await Task.Delay(50);
            }
        }
        throw new InvalidOperationException("网关治理管理端点未在期限内启动", last);
    }

    private static int GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static GatewayTrafficRule With(
        GatewayTrafficRule source,
        int limit,
        GatewayResponseLevel response,
        int restrictionSeconds = 0) => new()
    {
        Category = source.Category,
        Limit = limit,
        WindowMilliseconds = source.WindowMilliseconds,
        Response = response,
        RestrictionSeconds = restrictionSeconds,
    };

    private sealed class Fixture : IDisposable
    {
        internal Fixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "base05-leg03-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            PolicyPath = Path.Combine(Root, "gateway-governance.json");
        }

        internal string Root { get; }
        internal string PolicyPath { get; }

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); } catch { }
            try { File.Delete(Root); } catch { }
        }
    }
}
