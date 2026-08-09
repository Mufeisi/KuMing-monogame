namespace Server.Security;

internal sealed record LoginProtectionOptions(
    int AccountFailureLimit,
    int IpFailureLimit,
    TimeSpan FailureWindow,
    TimeSpan BaseBackoff,
    TimeSpan MaxBackoff,
    TimeSpan AccountBlockDuration,
    TimeSpan IpBlockDuration)
{
    public static LoginProtectionOptions FromSettings()
    {
        return new LoginProtectionOptions(
            Math.Max(1, Settings.LoginAccountFailureLimit),
            Math.Max(1, Settings.LoginIpFailureLimit),
            TimeSpan.FromSeconds(Math.Max(1, Settings.LoginFailureWindowSeconds)),
            TimeSpan.FromMilliseconds(Math.Max(0, Settings.LoginBaseBackoffMilliseconds)),
            TimeSpan.FromSeconds(Math.Max(0, Settings.LoginMaxBackoffSeconds)),
            TimeSpan.FromMinutes(Math.Max(1, Settings.LoginAccountBlockMinutes)),
            TimeSpan.FromMinutes(Math.Max(1, Settings.LoginIpBlockMinutes)));
    }
}

internal readonly record struct LoginProtectionDecision(
    bool Allowed,
    DateTime RetryAfterUtc,
    bool AccountBlocked,
    bool IpBlocked,
    DateTime AccountBlockedUntilUtc,
    DateTime IpBlockedUntilUtc);

internal sealed class LoginProtection
{
    private const int MaxTrackedAccounts = 20000;
    private const int MaxTrackedIps = 10000;

    private sealed class FailureState
    {
        public Queue<DateTime> Attempts { get; } = new();
        public int ConsecutiveFailures;
        public DateTime BackoffUntilUtc;
        public DateTime BlockedUntilUtc;
    }

    private readonly object _gate = new();
    private readonly LoginProtectionOptions _options;
    private readonly Dictionary<string, FailureState> _accounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FailureState> _ips = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<string> _accountOrder = new();
    private readonly Queue<string> _ipOrder = new();

    public LoginProtection(LoginProtectionOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public LoginProtectionDecision TryBegin(string accountId, string ipAddress, DateTime utcNow)
    {
        lock (_gate)
        {
            _accounts.TryGetValue(NormalizeAccount(accountId), out var account);
            _ips.TryGetValue(NormalizeIp(ipAddress), out var ip);
            if (account != null) Prepare(account, utcNow);
            if (ip != null) Prepare(ip, utcNow);

            var accountBlocked = account != null && account.BlockedUntilUtc > utcNow;
            var ipBlocked = ip != null && ip.BlockedUntilUtc > utcNow;
            var retryAfter = Max(
                accountBlocked ? account.BlockedUntilUtc : account?.BackoffUntilUtc ?? DateTime.MinValue,
                ipBlocked ? ip.BlockedUntilUtc : ip?.BackoffUntilUtc ?? DateTime.MinValue);

            return new LoginProtectionDecision(
                retryAfter <= utcNow,
                retryAfter,
                accountBlocked,
                ipBlocked,
                account?.BlockedUntilUtc ?? DateTime.MinValue,
                ip?.BlockedUntilUtc ?? DateTime.MinValue);
        }
    }

    public LoginProtectionDecision RecordFailure(string accountId, string ipAddress, DateTime utcNow)
    {
        lock (_gate)
        {
            var account = GetOrCreate(
                _accounts,
                _accountOrder,
                MaxTrackedAccounts,
                NormalizeAccount(accountId));
            var ip = GetOrCreate(
                _ips,
                _ipOrder,
                MaxTrackedIps,
                NormalizeIp(ipAddress));
            Prepare(account, utcNow);
            Prepare(ip, utcNow);

            RegisterFailure(account, _options.AccountFailureLimit, _options.AccountBlockDuration, utcNow);
            RegisterFailure(ip, _options.IpFailureLimit, _options.IpBlockDuration, utcNow);

            var accountBlocked = account.BlockedUntilUtc > utcNow;
            var ipBlocked = ip.BlockedUntilUtc > utcNow;
            var retryAfter = Max(
                accountBlocked ? account.BlockedUntilUtc : account.BackoffUntilUtc,
                ipBlocked ? ip.BlockedUntilUtc : ip.BackoffUntilUtc);

            return new LoginProtectionDecision(
                false,
                retryAfter,
                accountBlocked,
                ipBlocked,
                account.BlockedUntilUtc,
                ip.BlockedUntilUtc);
        }
    }

    public void RecordSuccess(string accountId, string ipAddress, DateTime utcNow)
    {
        lock (_gate)
        {
            _accounts.Remove(NormalizeAccount(accountId));
            var ipKey = NormalizeIp(ipAddress);
            if (!_ips.TryGetValue(ipKey, out var ip)) return;

            Prepare(ip, utcNow);
            ip.ConsecutiveFailures = 0;
            ip.BackoffUntilUtc = DateTime.MinValue;
        }
    }

    private void RegisterFailure(FailureState state, int limit, TimeSpan blockDuration, DateTime utcNow)
    {
        state.Attempts.Enqueue(utcNow);
        state.ConsecutiveFailures++;
        state.BackoffUntilUtc = utcNow.Add(BackoffFor(state.ConsecutiveFailures));
        if (state.Attempts.Count < limit) return;

        state.BlockedUntilUtc = utcNow.Add(blockDuration);
        state.BackoffUntilUtc = state.BlockedUntilUtc;
        state.Attempts.Clear();
        state.ConsecutiveFailures = 0;
    }

    private TimeSpan BackoffFor(int consecutiveFailures)
    {
        if (_options.BaseBackoff <= TimeSpan.Zero || _options.MaxBackoff <= TimeSpan.Zero)
            return TimeSpan.Zero;

        var exponent = Math.Min(30, Math.Max(0, consecutiveFailures - 1));
        var milliseconds = _options.BaseBackoff.TotalMilliseconds * Math.Pow(2, exponent);
        return TimeSpan.FromMilliseconds(Math.Min(milliseconds, _options.MaxBackoff.TotalMilliseconds));
    }

    private void Prepare(FailureState state, DateTime utcNow)
    {
        while (state.Attempts.TryPeek(out var attempt) && utcNow - attempt >= _options.FailureWindow)
            state.Attempts.Dequeue();

        if (state.BlockedUntilUtc > utcNow) return;
        if (state.BlockedUntilUtc != DateTime.MinValue)
        {
            state.BlockedUntilUtc = DateTime.MinValue;
            state.BackoffUntilUtc = DateTime.MinValue;
            state.ConsecutiveFailures = 0;
            state.Attempts.Clear();
        }

        if (state.Attempts.Count == 0 && state.BackoffUntilUtc <= utcNow)
        {
            state.ConsecutiveFailures = 0;
            state.BackoffUntilUtc = DateTime.MinValue;
        }
    }

    private static FailureState GetOrCreate(
        Dictionary<string, FailureState> states,
        Queue<string> insertionOrder,
        int capacity,
        string key)
    {
        if (states.TryGetValue(key, out var state)) return state;

        while (states.Count >= capacity && insertionOrder.TryDequeue(out var oldestKey))
        {
            if (states.Remove(oldestKey)) break;
        }

        state = new FailureState();
        states.Add(key, state);
        insertionOrder.Enqueue(key);
        return state;
    }

    private static string NormalizeAccount(string accountId) =>
        string.IsNullOrWhiteSpace(accountId) ? "<empty-account>" : accountId.Trim();

    private static string NormalizeIp(string ipAddress) =>
        string.IsNullOrWhiteSpace(ipAddress) ? "<unknown-ip>" : ipAddress.Trim();

    private static DateTime Max(DateTime left, DateTime right) => left >= right ? left : right;
}
