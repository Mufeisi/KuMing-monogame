using Server.Security;
using Server.MirDatabase;
using Server.MirEnvir;
using Xunit;

namespace Base05.Tests;

[Collection("登录安全环境")]
public sealed class LoginProtectionTests
{
    private static readonly LoginProtectionOptions Options = new(
        AccountAttemptLimit: 10,
        IpAttemptLimit: 20,
        AttemptWindow: TimeSpan.FromMinutes(1),
        AccountFailureLimit: 3,
        IpFailureLimit: 4,
        FailureWindow: TimeSpan.FromMinutes(5),
        BaseBackoff: TimeSpan.FromSeconds(1),
        MaxBackoff: TimeSpan.FromSeconds(8),
        AccountBlockDuration: TimeSpan.FromMinutes(2),
        IpBlockDuration: TimeSpan.FromMinutes(3));

    [Fact]
    public void 失败后按指数退避并在上限处封禁账号()
    {
        var protection = new LoginProtection(Options);
        var now = new DateTime(2026, 8, 9, 1, 2, 3, DateTimeKind.Utc);

        var first = protection.RecordFailure("PlayerOne", "10.0.0.1", now);
        Assert.False(first.AccountBlocked);
        Assert.Equal(now.AddSeconds(1), first.RetryAfterUtc);
        Assert.False(protection.TryBegin("playerone", "10.0.0.1", now.AddMilliseconds(999)).Allowed);
        Assert.True(protection.TryBegin("playerone", "10.0.0.1", now.AddSeconds(1)).Allowed);

        var second = protection.RecordFailure("playerone", "10.0.0.1", now.AddSeconds(1));
        Assert.Equal(now.AddSeconds(3), second.RetryAfterUtc);

        var third = protection.RecordFailure("PLAYERONE", "10.0.0.2", now.AddSeconds(3));
        Assert.True(third.AccountBlocked);
        Assert.False(third.IpBlocked);
        Assert.Equal(now.AddMinutes(2).AddSeconds(3), third.RetryAfterUtc);

        var otherIp = protection.TryBegin("playerone", "10.0.0.99", now.AddMinutes(1));
        Assert.False(otherIp.Allowed);
        Assert.True(otherIp.AccountBlocked);
    }

    [Fact]
    public void 同一IP跨账号失败达到阈值后封禁IP()
    {
        var protection = new LoginProtection(Options);
        var now = new DateTime(2026, 8, 9, 1, 2, 3, DateTimeKind.Utc);

        protection.RecordFailure("a00001", "10.0.0.8", now);
        protection.RecordFailure("b00001", "10.0.0.8", now.AddSeconds(1));
        protection.RecordFailure("c00001", "10.0.0.8", now.AddSeconds(3));
        var fourth = protection.RecordFailure("d00001", "10.0.0.8", now.AddSeconds(7));

        Assert.True(fourth.IpBlocked);
        Assert.False(fourth.AccountBlocked);
        var decision = protection.TryBegin("clean01", "10.0.0.8", now.AddMinutes(1));
        Assert.False(decision.Allowed);
        Assert.True(decision.IpBlocked);
    }

    [Fact]
    public void 成功登录清除账号退避但保留IP窗口计数()
    {
        var protection = new LoginProtection(Options);
        var now = new DateTime(2026, 8, 9, 1, 2, 3, DateTimeKind.Utc);

        protection.RecordFailure("reset01", "10.0.0.5", now);
        protection.RecordSuccess("reset01", "10.0.0.5", now.AddSeconds(1));

        Assert.True(protection.TryBegin("reset01", "10.0.0.5", now.AddSeconds(1)).Allowed);
        var next = protection.RecordFailure("reset01", "10.0.0.5", now.AddSeconds(1));
        Assert.Equal(now.AddSeconds(2), next.RetryAfterUtc);

        protection.RecordFailure("other01", "10.0.0.5", now.AddSeconds(2));
        var fourthIpFailure = protection.RecordFailure("other02", "10.0.0.5", now.AddSeconds(4));
        Assert.True(fourthIpFailure.IpBlocked);
    }

    [Fact]
    public void 封禁到期后重新开始干净窗口()
    {
        var protection = new LoginProtection(Options);
        var now = new DateTime(2026, 8, 9, 1, 2, 3, DateTimeKind.Utc);

        protection.RecordFailure("expire1", "10.0.0.7", now);
        protection.RecordFailure("expire1", "10.0.0.7", now.AddSeconds(1));
        protection.RecordFailure("expire1", "10.0.0.7", now.AddSeconds(3));

        Assert.True(protection.TryBegin("expire1", "10.0.0.9", now.AddMinutes(2).AddSeconds(3)).Allowed);
        var firstAfterExpiry = protection.RecordFailure("expire1", "10.0.0.9", now.AddMinutes(2).AddSeconds(3));
        Assert.False(firstAfterExpiry.AccountBlocked);
        Assert.Equal(now.AddMinutes(2).AddSeconds(4), firstAfterExpiry.RetryAfterUtc);
    }

    [Fact]
    public void 失败窗口到期后退避级数归零()
    {
        var protection = new LoginProtection(Options);
        var now = new DateTime(2026, 8, 9, 1, 2, 3, DateTimeKind.Utc);

        protection.RecordFailure("window1", "10.0.0.6", now);
        protection.RecordFailure("window1", "10.0.0.6", now.AddSeconds(1));

        var afterWindow = now.AddMinutes(5).AddSeconds(1);
        Assert.True(protection.TryBegin("window1", "10.0.0.6", afterWindow).Allowed);
        var firstAgain = protection.RecordFailure("window1", "10.0.0.6", afterWindow);
        Assert.Equal(afterWindow.AddSeconds(1), firstAgain.RetryAfterUtc);
    }

    [Fact]
    public void 高频成功登录同样受账号窗口限流()
    {
        var options = Options with
        {
            AccountAttemptLimit = 3,
            IpAttemptLimit = 99,
            AttemptWindow = TimeSpan.FromMinutes(1),
        };
        var protection = new LoginProtection(options);
        var now = new DateTime(2026, 8, 10, 1, 2, 3, DateTimeKind.Utc);

        for (var i = 0; i < 3; i++)
        {
            var ip = $"10.0.1.{i + 1}";
            Assert.True(protection.TryBegin("success1", ip, now.AddSeconds(i)).Allowed);
            protection.RecordSuccess("success1", ip, now.AddSeconds(i));
        }

        var limited = protection.TryBegin("success1", "10.0.1.99", now.AddSeconds(3));
        Assert.False(limited.Allowed);
        Assert.True(limited.AccountRateLimited);
        Assert.False(limited.AccountBlocked);
        Assert.Equal(now.AddMinutes(1), limited.RetryAfterUtc);
    }

    [Fact]
    public async Task HTTP登录可跨来源地址触发账号封禁并请求保存()
    {
        using var settings = new LoginProtectionSettingsScope(accountLimit: 3, ipLimit: 99);
        var environment = StartMinimalEnvironment();
        try
        {
            var account = AddAccount(environment, "limit001", "secret12");

            Assert.Equal(6, await Task.Run(() => environment.HTTPLogin("limit001", "wrong111", "10.0.0.1", 5000)));
            Assert.Equal(6, await Task.Run(() => environment.HTTPLogin("limit001", "wrong111", "10.0.0.2", 5000)));
            Assert.Equal(5, await Task.Run(() => environment.HTTPLogin("limit001", "wrong111", "10.0.0.3", 5000)));

            Assert.True(account.Banned);
            Assert.Equal("登录失败次数过多", account.BanReason);
            Assert.True(account.ExpiryDate > environment.Now);
            Assert.Equal(5, await Task.Run(() => environment.HTTPLogin("limit001", "secret12", "10.0.0.4", 5000)));
        }
        finally
        {
            environment.Stop();
        }
    }

    [Fact]
    public async Task HTTP登录可跨账号触发来源IP封禁()
    {
        using var settings = new LoginProtectionSettingsScope(accountLimit: 99, ipLimit: 3);
        var environment = StartMinimalEnvironment();
        const string sourceIp = "10.0.0.88";
        try
        {
            AddAccount(environment, "ipuser01", "secret12");
            AddAccount(environment, "ipuser02", "secret12");
            AddAccount(environment, "ipuser03", "secret12");
            AddAccount(environment, "ipclean1", "secret12");

            Assert.Equal(6, await Task.Run(() => environment.HTTPLogin("ipuser01", "wrong111", sourceIp, 5000)));
            Assert.Equal(6, await Task.Run(() => environment.HTTPLogin("ipuser02", "wrong111", sourceIp, 5000)));
            Assert.Equal(5, await Task.Run(() => environment.HTTPLogin("ipuser03", "wrong111", sourceIp, 5000)));

            Assert.True(Envir.IPBlocks.TryGetValue(sourceIp, out var blockedUntil));
            Assert.True(blockedUntil > environment.Now);
            Assert.Equal(5, await Task.Run(() => environment.HTTPLogin("ipclean1", "secret12", sourceIp, 5000)));
        }
        finally
        {
            Envir.IPBlocks.TryRemove(sourceIp, out _);
            environment.Stop();
        }
    }

    private static AccountInfo AddAccount(Envir environment, string accountId, string password)
    {
        var account = new AccountInfo { AccountID = accountId, Password = password };
        environment.InvokeOnMainThread(() =>
        {
            environment.AccountList.Add(account);
            return 0;
        });
        return account;
    }

    private static Envir StartMinimalEnvironment()
    {
        var environment = new Envir();
        environment.Start(new EnvirStartOptions
        {
            LoadResources = false,
            BindNetwork = false,
            StartScripts = false,
            StartHttp = false,
            SaveOnStop = false,
            Multithreaded = false,
        });
        Assert.True(SpinWait.SpinUntil(
            () => environment.StartState is EnvirStartState.Ready or EnvirStartState.Failed,
            TimeSpan.FromSeconds(2)));
        Assert.Equal(EnvirStartState.Ready, environment.StartState);
        return environment;
    }

    private sealed class LoginProtectionSettingsScope : IDisposable
    {
        private readonly int _accountLimit = Server.Settings.LoginAccountFailureLimit;
        private readonly int _ipLimit = Server.Settings.LoginIpFailureLimit;
        private readonly int _accountAttemptLimit = Server.Settings.LoginAccountAttemptLimit;
        private readonly int _ipAttemptLimit = Server.Settings.LoginIpAttemptLimit;
        private readonly int _attemptWindow = Server.Settings.LoginAttemptWindowSeconds;
        private readonly int _window = Server.Settings.LoginFailureWindowSeconds;
        private readonly int _baseBackoff = Server.Settings.LoginBaseBackoffMilliseconds;
        private readonly int _maxBackoff = Server.Settings.LoginMaxBackoffSeconds;
        private readonly int _accountBlock = Server.Settings.LoginAccountBlockMinutes;
        private readonly int _ipBlock = Server.Settings.LoginIpBlockMinutes;

        public LoginProtectionSettingsScope(int accountLimit, int ipLimit)
        {
            Server.Settings.LoginAccountFailureLimit = accountLimit;
            Server.Settings.LoginIpFailureLimit = ipLimit;
            Server.Settings.LoginAccountAttemptLimit = 99;
            Server.Settings.LoginIpAttemptLimit = 999;
            Server.Settings.LoginAttemptWindowSeconds = 60;
            Server.Settings.LoginFailureWindowSeconds = 300;
            Server.Settings.LoginBaseBackoffMilliseconds = 0;
            Server.Settings.LoginMaxBackoffSeconds = 0;
            Server.Settings.LoginAccountBlockMinutes = 2;
            Server.Settings.LoginIpBlockMinutes = 3;
        }

        public void Dispose()
        {
            Server.Settings.LoginAccountFailureLimit = _accountLimit;
            Server.Settings.LoginIpFailureLimit = _ipLimit;
            Server.Settings.LoginAccountAttemptLimit = _accountAttemptLimit;
            Server.Settings.LoginIpAttemptLimit = _ipAttemptLimit;
            Server.Settings.LoginAttemptWindowSeconds = _attemptWindow;
            Server.Settings.LoginFailureWindowSeconds = _window;
            Server.Settings.LoginBaseBackoffMilliseconds = _baseBackoff;
            Server.Settings.LoginMaxBackoffSeconds = _maxBackoff;
            Server.Settings.LoginAccountBlockMinutes = _accountBlock;
            Server.Settings.LoginIpBlockMinutes = _ipBlock;
        }
    }
}

[CollectionDefinition("登录安全环境", DisableParallelization = true)]
public sealed class LoginProtectionCollection;
