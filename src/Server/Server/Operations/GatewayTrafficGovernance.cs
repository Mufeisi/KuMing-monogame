using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Server.Operations;

[JsonConverter(typeof(JsonStringEnumConverter))]
internal enum GatewayTrafficCategory
{
    Login,
    Movement,
    Attack,
    Spell,
    Pickup,
    Chat,
    OversizedPacket,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
internal enum GatewayGovernanceMode
{
    Disabled,
    Observe,
    Enforce,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
internal enum GatewayResponseLevel
{
    Warning,
    DropAction,
    TemporaryRestriction,
    Disconnect,
    ManualBanReview,
}

internal sealed class GatewayTrafficRule
{
    [JsonRequired]
    public GatewayTrafficCategory Category { get; init; }
    [JsonRequired]
    public int Limit { get; init; }
    [JsonRequired]
    public int WindowMilliseconds { get; init; }
    [JsonRequired]
    public GatewayResponseLevel Response { get; init; }
    [JsonRequired]
    public int RestrictionSeconds { get; init; }
}

internal sealed class GatewayGovernancePolicy
{
    [JsonRequired]
    public int FormatVersion { get; init; } = 1;
    [JsonRequired]
    public long Revision { get; init; }
    [JsonRequired]
    public DateTimeOffset UpdatedAtUtc { get; init; }
    [JsonRequired]
    public string UpdatedBy { get; init; } = "startup";
    [JsonRequired]
    public string Reason { get; init; } = "initial observation baseline";
    [JsonRequired]
    public GatewayGovernanceMode Mode { get; init; } = GatewayGovernanceMode.Observe;
    [JsonRequired]
    public int MaximumPacketBytes { get; init; } = 32 * 1024;
    [JsonRequired]
    public IReadOnlyList<GatewayTrafficRule> Rules { get; init; } = Array.Empty<GatewayTrafficRule>();
}

internal sealed class GatewayGovernanceChangeRequest
{
    public long? ExpectedRevision { get; init; }
    public GatewayGovernanceMode? Mode { get; init; }
    public int? MaximumPacketBytes { get; init; }
    public IReadOnlyList<GatewayTrafficRule> Rules { get; init; }
    public string Reason { get; init; } = string.Empty;
}

internal sealed class GatewayGovernanceEvidence
{
    public DateTimeOffset ObservedAtUtc { get; init; }
    public int SessionId { get; init; }
    public string ClientReference { get; init; } = string.Empty;
    public GatewayTrafficCategory Category { get; init; }
    public int Threshold { get; init; }
    public int Observed { get; init; }
    public int WindowMilliseconds { get; init; }
    public GatewayResponseLevel Response { get; init; }
    public bool Enforced { get; init; }
}

internal sealed class GatewayTrafficCategorySnapshot
{
    public GatewayTrafficCategory Category { get; init; }
    public long Observed { get; init; }
    public long Violations { get; init; }
    public long Enforced { get; init; }
}

internal sealed class GatewayGovernanceSnapshot
{
    public DateTimeOffset GeneratedAtUtc { get; init; }
    public GatewayGovernancePolicy Policy { get; init; }
    public int TrackedSessions { get; init; }
    public IReadOnlyList<GatewayTrafficCategorySnapshot> Categories { get; init; } = Array.Empty<GatewayTrafficCategorySnapshot>();
    public IReadOnlyList<GatewayGovernanceEvidence> RecentEvidence { get; init; } = Array.Empty<GatewayGovernanceEvidence>();
}

internal readonly record struct GatewayGovernanceDecision(
    bool Allow,
    bool Disconnect,
    bool ManualBanReview,
    GatewayResponseLevel? Response)
{
    internal static GatewayGovernanceDecision Allowed => new(true, false, false, null);
}

/// <summary>
/// 游戏网关行为治理深模块：分类、计数、判定和审计集中在这里；调用方负责在主线程执行返回的处置。
/// </summary>
internal sealed class GatewayTrafficGovernance
{
    private const int CurrentFormatVersion = 1;
    private const int MaximumEvidence = 256;
    private const int MaximumReasonCharacters = 256;
    private readonly object _gate = new();
    private readonly string _policyPath;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Action<string> _auditSink;
    private readonly Dictionary<int, SessionState> _sessions = new();
    private readonly CategoryCounter[] _counters = Enum.GetValues<GatewayTrafficCategory>().Select(_ => new CategoryCounter()).ToArray();
    private readonly Queue<GatewayGovernanceEvidence> _evidence = new();
    private GatewayGovernancePolicy _policy;

    internal GatewayTrafficGovernance(
        string policyPath = null,
        Func<DateTimeOffset> clock = null,
        Action<string> auditSink = null)
    {
        _policyPath = Path.GetFullPath(policyPath ?? Path.Combine(Settings.ConfigPath, "Operations", "gateway-governance.json"));
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _auditSink = auditSink ?? (line => Logger.GetLogger(LogType.Server).Warn(line));
        _policy = LoadOrCreate();
    }

    internal GatewayGovernanceDecision EvaluatePacket(int sessionId, string clientAddress, short packetId)
    {
        GatewayTrafficCategory? category = Classify(packetId);
        return category.HasValue
            ? Evaluate(sessionId, clientAddress, category.Value, observedValue: null)
            : GatewayGovernanceDecision.Allowed;
    }

    internal GatewayGovernanceDecision EvaluatePacketSize(int sessionId, string clientAddress, int packetBytes)
    {
        if (Volatile.Read(ref _policy).Mode == GatewayGovernanceMode.Disabled)
            return GatewayGovernanceDecision.Allowed;
        return Evaluate(sessionId, clientAddress, GatewayTrafficCategory.OversizedPacket, packetBytes);
    }

    internal void RemoveSession(int sessionId)
    {
        lock (_gate)
            _sessions.Remove(sessionId);
    }

    internal GatewayGovernanceSnapshot CaptureSnapshot()
    {
        lock (_gate)
        {
            return new GatewayGovernanceSnapshot
            {
                GeneratedAtUtc = _clock(),
                Policy = Copy(Volatile.Read(ref _policy)),
                TrackedSessions = _sessions.Count,
                Categories = Enum.GetValues<GatewayTrafficCategory>().Select(category =>
                {
                    CategoryCounter counter = _counters[(int)category];
                    return new GatewayTrafficCategorySnapshot
                    {
                        Category = category,
                        Observed = counter.Observed,
                        Violations = counter.Violations,
                        Enforced = counter.Enforced,
                    };
                }).ToArray(),
                RecentEvidence = _evidence.ToArray(),
            };
        }
    }

    internal GatewayGovernancePolicy SetPolicy(GatewayGovernanceChangeRequest request, string principal)
    {
        ArgumentNullException.ThrowIfNull(request);
        string reason = NormalizeReason(request.Reason);
        string safePrincipal = SafeToken(principal, 64);
        GatewayGovernancePolicy changed;
        lock (_gate)
        {
            GatewayGovernancePolicy current = _policy;
            if (!request.ExpectedRevision.HasValue || request.ExpectedRevision.Value != current.Revision)
                throw new InvalidOperationException("网关治理配置代次已变化，请刷新后重试");
            if (!request.Mode.HasValue || !request.MaximumPacketBytes.HasValue || request.Rules == null)
                throw new ArgumentException("网关治理变更必须提供完整模式、包大小和规则");

            changed = new GatewayGovernancePolicy
            {
                FormatVersion = CurrentFormatVersion,
                Revision = current.Revision + 1,
                UpdatedAtUtc = _clock(),
                UpdatedBy = safePrincipal,
                Reason = reason,
                Mode = request.Mode.Value,
                MaximumPacketBytes = request.MaximumPacketBytes.Value,
                Rules = request.Rules.Select(Copy).ToArray(),
            };
            Validate(changed);
            Persist(changed);
            Volatile.Write(ref _policy, changed);
            _sessions.Clear();
        }

        try
        {
            _auditSink($"GATEWAY_POLICY revision={changed.Revision} mode={changed.Mode} principal={safePrincipal}");
        }
        catch
        {
            // 配置已原子提交，运行日志失败不能伪报配置失败。
        }
        return Copy(changed);
    }

    internal static GatewayTrafficCategory? Classify(short packetId) => (ClientPacketIds)packetId switch
    {
        ClientPacketIds.ClientVersion or ClientPacketIds.NewAccount or ClientPacketIds.ChangePassword or
            ClientPacketIds.Login or ClientPacketIds.NewCharacter or ClientPacketIds.DeleteCharacter or
            ClientPacketIds.StartGame => GatewayTrafficCategory.Login,
        ClientPacketIds.Turn or ClientPacketIds.Walk or ClientPacketIds.Run => GatewayTrafficCategory.Movement,
        ClientPacketIds.Attack or ClientPacketIds.RangeAttack or ClientPacketIds.Harvest => GatewayTrafficCategory.Attack,
        ClientPacketIds.MagicKey or ClientPacketIds.Magic or ClientPacketIds.SpellToggle => GatewayTrafficCategory.Spell,
        ClientPacketIds.PickUp => GatewayTrafficCategory.Pickup,
        ClientPacketIds.Chat => GatewayTrafficCategory.Chat,
        _ => null,
    };

    private GatewayGovernanceDecision Evaluate(
        int sessionId,
        string clientAddress,
        GatewayTrafficCategory category,
        int? observedValue)
    {
        if (Volatile.Read(ref _policy).Mode == GatewayGovernanceMode.Disabled)
            return GatewayGovernanceDecision.Allowed;

        DateTimeOffset now = _clock();
        GatewayGovernanceEvidence evidence = null;
        GatewayGovernanceDecision decision;
        lock (_gate)
        {
            GatewayGovernancePolicy policy = _policy;
            if (policy.Mode == GatewayGovernanceMode.Disabled)
                return GatewayGovernanceDecision.Allowed;
            GatewayTrafficRule rule = policy.Rules.First(value => value.Category == category);
            CategoryCounter counter = _counters[(int)category];
            counter.Observed++;
            SessionState session = GetSession(sessionId);
            RuleWindow window = session.Windows[(int)category];
            int observed;
            bool violation;
            if (category == GatewayTrafficCategory.OversizedPacket)
            {
                observed = observedValue ?? 0;
                violation = observed > policy.MaximumPacketBytes;
            }
            else
            {
                if (now - window.StartedAtUtc >= TimeSpan.FromMilliseconds(rule.WindowMilliseconds))
                {
                    window.StartedAtUtc = now;
                    window.Count = 0;
                }
                observed = ++window.Count;
                violation = observed > rule.Limit || now < window.RestrictedUntilUtc;
            }

            if (!violation)
                return GatewayGovernanceDecision.Allowed;

            counter.Violations++;
            bool enforced = policy.Mode == GatewayGovernanceMode.Enforce;
            if (enforced) counter.Enforced++;
            if (enforced && rule.Response == GatewayResponseLevel.TemporaryRestriction)
                window.RestrictedUntilUtc = now.AddSeconds(rule.RestrictionSeconds);

            evidence = new GatewayGovernanceEvidence
            {
                ObservedAtUtc = now,
                SessionId = sessionId,
                ClientReference = HashReference(clientAddress),
                Category = category,
                Threshold = category == GatewayTrafficCategory.OversizedPacket ? policy.MaximumPacketBytes : rule.Limit,
                Observed = observed,
                WindowMilliseconds = rule.WindowMilliseconds,
                Response = rule.Response,
                Enforced = enforced,
            };
            _evidence.Enqueue(evidence);
            while (_evidence.Count > MaximumEvidence) _evidence.Dequeue();

            decision = !enforced || rule.Response == GatewayResponseLevel.Warning
                ? new GatewayGovernanceDecision(true, false, false, rule.Response)
                : rule.Response switch
                {
                    GatewayResponseLevel.Disconnect => new GatewayGovernanceDecision(false, true, false, rule.Response),
                    GatewayResponseLevel.ManualBanReview => new GatewayGovernanceDecision(false, true, true, rule.Response),
                    _ => new GatewayGovernanceDecision(false, false, false, rule.Response),
                };
        }

        try
        {
            _auditSink($"GATEWAY_GOVERNANCE time={evidence.ObservedAtUtc:O} session={evidence.SessionId} " +
                       $"client_ref={evidence.ClientReference} category={evidence.Category} threshold={evidence.Threshold} " +
                       $"observed={evidence.Observed} window_ms={evidence.WindowMilliseconds} response={evidence.Response} enforced={evidence.Enforced}");
        }
        catch
        {
            // 决策证据保留在内存快照中；日志故障不得改变玩家处置。
        }
        return decision;
    }

    private SessionState GetSession(int sessionId)
    {
        if (_sessions.TryGetValue(sessionId, out SessionState value)) return value;
        value = new SessionState(Enum.GetValues<GatewayTrafficCategory>().Length, _clock());
        _sessions.Add(sessionId, value);
        return value;
    }

    private GatewayGovernancePolicy LoadOrCreate()
    {
        if (!File.Exists(_policyPath))
        {
            GatewayGovernancePolicy initial = DefaultPolicy(_clock());
            Persist(initial);
            return initial;
        }

        try
        {
            GatewayGovernancePolicy value = JsonSerializer.Deserialize<GatewayGovernancePolicy>(
                File.ReadAllBytes(_policyPath), JsonOptions());
            Validate(value);
            return value;
        }
        catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            throw new InvalidOperationException("网关治理配置无效，服务器拒绝启动网络监听", error);
        }
    }

    private void Persist(GatewayGovernancePolicy policy)
    {
        string directory = Path.GetDirectoryName(_policyPath) ?? throw new InvalidOperationException("网关治理配置目录无效");
        Directory.CreateDirectory(directory);
        string temporary = _policyPath + ".partial-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(policy, JsonOptions()));
            File.Move(temporary, _policyPath, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
        }
    }

    private static void Validate(GatewayGovernancePolicy policy)
    {
        if (policy == null || policy.FormatVersion != CurrentFormatVersion || policy.Revision < 0 ||
            policy.UpdatedAtUtc == default || string.IsNullOrWhiteSpace(policy.UpdatedBy) ||
            string.IsNullOrWhiteSpace(policy.Reason) || policy.Reason.Length > MaximumReasonCharacters ||
            !Enum.IsDefined(policy.Mode))
            throw new InvalidOperationException("网关治理配置元数据无效");
        if (policy.MaximumPacketBytes is < 1024 or > ushort.MaxValue)
            throw new InvalidOperationException("最大封包大小必须在 1024～65535 字节之间");
        if (policy.Rules == null || policy.Rules.Count != Enum.GetValues<GatewayTrafficCategory>().Length ||
            policy.Rules.Select(value => value.Category).Distinct().Count() != policy.Rules.Count)
            throw new InvalidOperationException("网关治理规则必须完整且分类唯一");
        foreach (GatewayTrafficRule rule in policy.Rules)
        {
            if (rule == null || !Enum.IsDefined(rule.Category) || !Enum.IsDefined(rule.Response) ||
                rule.Limit is < 1 or > 1_000_000 || rule.WindowMilliseconds is < 100 or > 3_600_000 ||
                rule.RestrictionSeconds is < 0 or > 86_400 ||
                rule.Response == GatewayResponseLevel.TemporaryRestriction && rule.RestrictionSeconds == 0)
                throw new InvalidOperationException("网关治理规则阈值或处置无效");
        }
    }

    private static GatewayGovernancePolicy DefaultPolicy(DateTimeOffset now) => new()
    {
        UpdatedAtUtc = now,
        Rules =
        [
            Rule(GatewayTrafficCategory.Login, 30, 10_000, GatewayResponseLevel.Warning),
            Rule(GatewayTrafficCategory.Movement, 120, 1_000, GatewayResponseLevel.DropAction),
            Rule(GatewayTrafficCategory.Attack, 40, 1_000, GatewayResponseLevel.DropAction),
            Rule(GatewayTrafficCategory.Spell, 40, 1_000, GatewayResponseLevel.DropAction),
            Rule(GatewayTrafficCategory.Pickup, 60, 1_000, GatewayResponseLevel.DropAction),
            Rule(GatewayTrafficCategory.Chat, 10, 5_000, GatewayResponseLevel.TemporaryRestriction, 10),
            Rule(GatewayTrafficCategory.OversizedPacket, 1, 1_000, GatewayResponseLevel.Disconnect),
        ],
    };

    private static GatewayTrafficRule Rule(
        GatewayTrafficCategory category,
        int limit,
        int windowMilliseconds,
        GatewayResponseLevel response,
        int restrictionSeconds = 0) => new()
    {
        Category = category,
        Limit = limit,
        WindowMilliseconds = windowMilliseconds,
        Response = response,
        RestrictionSeconds = restrictionSeconds,
    };

    private static GatewayGovernancePolicy Copy(GatewayGovernancePolicy source) => new()
    {
        FormatVersion = source.FormatVersion,
        Revision = source.Revision,
        UpdatedAtUtc = source.UpdatedAtUtc,
        UpdatedBy = source.UpdatedBy,
        Reason = source.Reason,
        Mode = source.Mode,
        MaximumPacketBytes = source.MaximumPacketBytes,
        Rules = source.Rules.Select(Copy).ToArray(),
    };

    private static GatewayTrafficRule Copy(GatewayTrafficRule source) => new()
    {
        Category = source.Category,
        Limit = source.Limit,
        WindowMilliseconds = source.WindowMilliseconds,
        Response = source.Response,
        RestrictionSeconds = source.RestrictionSeconds,
    };

    private static string NormalizeReason(string reason)
    {
        string value = (reason ?? string.Empty).Trim();
        if (value.Length is < 3 or > MaximumReasonCharacters)
            throw new ArgumentException("网关治理变更原因必须为 3～256 个字符", nameof(reason));
        return value;
    }

    private static string SafeToken(string value, int maximumLength)
    {
        string safe = string.IsNullOrWhiteSpace(value) ? "unknown" : value.Trim();
        var builder = new StringBuilder(Math.Min(safe.Length, maximumLength));
        foreach (char character in safe)
        {
            if (builder.Length >= maximumLength) break;
            builder.Append(char.IsControl(character) || char.IsWhiteSpace(character) ? '_' : character);
        }
        return builder.Length == 0 ? "unknown" : builder.ToString();
    }

    private static string HashReference(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "unknown";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..16];
    }

    private static JsonSerializerOptions JsonOptions() => new()
    {
        PropertyNameCaseInsensitive = false,
        WriteIndented = true,
    };

    private sealed class SessionState
    {
        internal SessionState(int categoryCount, DateTimeOffset now)
        {
            Windows = Enumerable.Range(0, categoryCount).Select(_ => new RuleWindow { StartedAtUtc = now }).ToArray();
        }

        internal RuleWindow[] Windows { get; }
    }

    private sealed class RuleWindow
    {
        internal DateTimeOffset StartedAtUtc;
        internal DateTimeOffset RestrictedUntilUtc;
        internal int Count;
    }

    private sealed class CategoryCounter
    {
        internal long Observed;
        internal long Violations;
        internal long Enforced;
    }
}
