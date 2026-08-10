using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Server.Operations;

[JsonConverter(typeof(JsonStringEnumConverter))]
internal enum KillSwitchFeature
{
    GameShop,
    ResourceUpdate,
    Activities,
    HighRiskOperations,
}

internal sealed class KillSwitchSnapshot
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
    public string Reason { get; init; } = "initial state";
    [JsonRequired]
    public bool GameShopEnabled { get; init; } = true;
    [JsonRequired]
    public bool ResourceUpdateEnabled { get; init; } = true;
    [JsonRequired]
    public bool ActivitiesEnabled { get; init; } = true;
    [JsonRequired]
    public bool HighRiskOperationsEnabled { get; init; } = true;
}

internal sealed class KillSwitchChangeRequest
{
    public string Feature { get; init; } = string.Empty;
    public bool? Enabled { get; init; }
    public string Reason { get; init; } = string.Empty;
}

/// <summary>
/// 发布前最小 Kill Switch：以不可变快照向游戏线程提供无锁读取，变更先原子持久化再发布。
/// </summary>
internal sealed class KillSwitchService
{
    private const int CurrentFormatVersion = 1;
    private const int MaxReasonCharacters = 256;
    private readonly object _gate = new();
    private readonly string _statePath;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Action<string> _auditSink;
    private KillSwitchSnapshot _snapshot;

    internal KillSwitchService(
        string statePath = null,
        Func<DateTimeOffset> clock = null,
        Action<string> auditSink = null)
    {
        _statePath = Path.GetFullPath(statePath ?? Path.Combine(Settings.ConfigPath, "Operations", "kill-switches.json"));
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _auditSink = auditSink ?? (line => Logger.GetLogger(LogType.Server).Warn(line));
        _snapshot = LoadOrCreate();
    }

    internal KillSwitchSnapshot GetSnapshot() => Copy(Volatile.Read(ref _snapshot));

    internal bool IsEnabled(KillSwitchFeature feature)
    {
        KillSwitchSnapshot snapshot = Volatile.Read(ref _snapshot);
        return feature switch
        {
            KillSwitchFeature.GameShop => snapshot.GameShopEnabled,
            KillSwitchFeature.ResourceUpdate => snapshot.ResourceUpdateEnabled,
            KillSwitchFeature.Activities => snapshot.ActivitiesEnabled,
            KillSwitchFeature.HighRiskOperations => snapshot.HighRiskOperationsEnabled,
            _ => false,
        };
    }

    internal KillSwitchSnapshot Set(KillSwitchFeature feature, bool enabled, string reason, string principal)
    {
        string safeReason = NormalizeReason(reason);
        string safePrincipal = SafeToken(principal, 48);
        lock (_gate)
        {
            KillSwitchSnapshot current = _snapshot;
            var candidate = new KillSwitchSnapshot
            {
                FormatVersion = CurrentFormatVersion,
                Revision = checked(current.Revision + 1),
                UpdatedAtUtc = _clock(),
                UpdatedBy = safePrincipal,
                Reason = safeReason,
                GameShopEnabled = feature == KillSwitchFeature.GameShop ? enabled : current.GameShopEnabled,
                ResourceUpdateEnabled = feature == KillSwitchFeature.ResourceUpdate ? enabled : current.ResourceUpdateEnabled,
                ActivitiesEnabled = feature == KillSwitchFeature.Activities ? enabled : current.ActivitiesEnabled,
                HighRiskOperationsEnabled = feature == KillSwitchFeature.HighRiskOperations ? enabled : current.HighRiskOperationsEnabled,
            };

            Persist(candidate);
            Volatile.Write(ref _snapshot, candidate);
            string reasonReference = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(safeReason)))[..16];
            _auditSink($"OPS_KILL_SWITCH feature={feature} enabled={enabled.ToString().ToLowerInvariant()} " +
                       $"revision={candidate.Revision} principal={safePrincipal} reason_ref={reasonReference}");
            return Copy(candidate);
        }
    }

    internal static bool TryParseFeature(string value, out KillSwitchFeature feature)
    {
        string normalized = (value ?? string.Empty).Trim().Replace("_", "-").ToLowerInvariant();
        feature = normalized switch
        {
            "game-shop" => KillSwitchFeature.GameShop,
            "resource-update" => KillSwitchFeature.ResourceUpdate,
            "activities" => KillSwitchFeature.Activities,
            "high-risk-operations" => KillSwitchFeature.HighRiskOperations,
            _ => default,
        };
        return normalized is "game-shop" or "resource-update" or "activities" or "high-risk-operations";
    }

    private KillSwitchSnapshot LoadOrCreate()
    {
        if (!File.Exists(_statePath))
        {
            var initial = new KillSwitchSnapshot { UpdatedAtUtc = _clock() };
            Persist(initial);
            return initial;
        }

        try
        {
            byte[] bytes = File.ReadAllBytes(_statePath);
            var snapshot = JsonSerializer.Deserialize<KillSwitchSnapshot>(bytes, JsonOptions());
            Validate(snapshot);
            return snapshot;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            throw new InvalidOperationException("Kill Switch 状态无法安全读取，服务器拒绝启动", error);
        }
    }

    private void Persist(KillSwitchSnapshot snapshot)
    {
        Validate(snapshot);
        string directory = Path.GetDirectoryName(_statePath)
                           ?? throw new InvalidOperationException("Kill Switch 状态目录无效");
        Directory.CreateDirectory(directory);
        string temporaryPath = _statePath + ".partial-" + Guid.NewGuid().ToString("N");
        try
        {
            byte[] payload = JsonSerializer.SerializeToUtf8Bytes(snapshot, JsonOptions());
            using (var stream = new FileStream(
                       temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                       bufferSize: 4096, options: FileOptions.WriteThrough))
            {
                stream.Write(payload);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, _statePath, overwrite: true);
        }
        catch
        {
            try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); } catch { }
            throw;
        }
    }

    private static void Validate(KillSwitchSnapshot snapshot)
    {
        if (snapshot == null || snapshot.FormatVersion != CurrentFormatVersion || snapshot.Revision < 0)
            throw new InvalidOperationException("Kill Switch 状态格式或代次无效");
        if (snapshot.UpdatedAtUtc == default || string.IsNullOrWhiteSpace(snapshot.UpdatedBy) ||
            string.IsNullOrWhiteSpace(snapshot.Reason) || snapshot.Reason.Length > MaxReasonCharacters)
            throw new InvalidOperationException("Kill Switch 状态审计字段无效");
    }

    private static string NormalizeReason(string reason)
    {
        string value = (reason ?? string.Empty).Trim();
        if (value.Length is < 3 or > MaxReasonCharacters)
            throw new ArgumentException("Kill Switch 变更原因必须为 3～256 个字符", nameof(reason));
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

    private static KillSwitchSnapshot Copy(KillSwitchSnapshot source) => new()
    {
        FormatVersion = source.FormatVersion,
        Revision = source.Revision,
        UpdatedAtUtc = source.UpdatedAtUtc,
        UpdatedBy = source.UpdatedBy,
        Reason = source.Reason,
        GameShopEnabled = source.GameShopEnabled,
        ResourceUpdateEnabled = source.ResourceUpdateEnabled,
        ActivitiesEnabled = source.ActivitiesEnabled,
        HighRiskOperationsEnabled = source.HighRiskOperationsEnabled,
    };

    private static JsonSerializerOptions JsonOptions() => new()
    {
        PropertyNameCaseInsensitive = false,
        WriteIndented = true,
    };
}
