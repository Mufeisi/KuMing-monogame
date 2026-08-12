using Server.MirDatabase;
using Server.MirEnvir;
using Server.Utils;
using Xunit;

namespace Base05.Tests;

public sealed class Sec01LoginTransactionTests
{
    [Fact]
    public async Task HTTPLogin从工作线程投递完整账户事务并升级旧密码()
    {
        var environment = StartMinimalEnvironment();
        try
        {
            var account = new AccountInfo
            {
                AccountID = "httpuser",
                Banned = true,
                BanReason = "expired",
                ExpiryDate = DateTime.MinValue,
                WrongPasswordCount = 3,
            };
            var salt = Crypto.GenerateSalt();
            account.SetPasswordHashAndSalt(Crypto.HashPassword("secret12", salt), salt);
            environment.InvokeOnMainThread(() =>
            {
                environment.AccountList.Add(account);
                return 0;
            });

            var result = await Task.Run(() => environment.HTTPLogin("httpuser", "secret12"));

            Assert.Equal(7, result);
            Assert.False(account.Banned);
            Assert.Empty(account.BanReason);
            Assert.Equal(DateTime.MinValue, account.ExpiryDate);
            Assert.Equal(0, account.WrongPasswordCount);
            Assert.StartsWith("$argon2id$v=19$", account.Password, StringComparison.Ordinal);
        }
        finally
        {
            environment.Stop();
        }
    }

    [Fact]
    public void HTTPLogin事务异常时回滚账户与自动保存状态()
    {
        var environment = new Envir();
        var account = new AccountInfo
        {
            AccountID = "rollback",
            Banned = true,
            BanReason = "original",
            ExpiryDate = new DateTime(2030, 1, 2),
            WrongPasswordCount = 4,
        };
        var salt = Crypto.GenerateSalt();
        var hash = Crypto.HashPassword("secret12", salt);
        account.SetPasswordHashAndSalt(hash, salt);

        Assert.Throws<InvalidOperationException>(() => environment.ExecuteHttpLoginTransaction(account, () =>
        {
            account.Banned = false;
            account.BanReason = string.Empty;
            account.ExpiryDate = DateTime.MinValue;
            account.WrongPasswordCount = 0;
            account.Password = "changed12";
            environment.RequestAutoSave();
            throw new InvalidOperationException("模拟提交失败");
        }));

        Assert.True(account.Banned);
        Assert.Equal("original", account.BanReason);
        Assert.Equal(new DateTime(2030, 1, 2), account.ExpiryDate);
        Assert.Equal(4, account.WrongPasswordCount);
        Assert.Equal(hash, account.Password);
        Assert.Equal(salt, account.Salt);
        Assert.False(environment.HasPendingAutoSave);
    }

    [Fact]
    public async Task HTTPLogin主线程投递超时会取消排队事务且不延迟修改账户()
    {
        var environment = StartMinimalEnvironment();
        using var blockerEntered = new ManualResetEventSlim(false);
        using var releaseBlocker = new ManualResetEventSlim(false);
        Exception blockerException = null;
        try
        {
            var account = new AccountInfo
            {
                AccountID = "timeout1",
                Banned = false,
                WrongPasswordCount = 2,
            };
            var salt = Crypto.GenerateSalt();
            var hash = Crypto.HashPassword("secret12", salt);
            account.SetPasswordHashAndSalt(hash, salt);
            environment.InvokeOnMainThread(() =>
            {
                environment.AccountList.Add(account);
                return 0;
            });

            var blocker = new Thread(() =>
            {
                try
                {
                    environment.InvokeOnMainThread(() =>
                    {
                        blockerEntered.Set();
                        releaseBlocker.Wait();
                        return 0;
                    });
                }
                catch (Exception ex)
                {
                    blockerException = ex;
                }
            }) { IsBackground = true };
            blocker.Start();
            Assert.True(blockerEntered.Wait(TimeSpan.FromSeconds(10)));

            var result = await Task.Run(() => environment.HTTPLogin("timeout1", "secret12", 100));
            Assert.Equal(0, result);
            releaseBlocker.Set();
            Assert.True(blocker.Join(TimeSpan.FromSeconds(10)));
            Assert.Null(blockerException);

            Assert.Equal(hash, account.Password);
            Assert.Equal(salt, account.Salt);
            Assert.Equal(2, account.WrongPasswordCount);
            Assert.False(environment.HasPendingAutoSave);
        }
        finally
        {
            releaseBlocker.Set();
            environment.Stop();
        }
    }

    [Fact]
    public void HTTPLogin在主线程未启动时拒绝修改账户()
    {
        var environment = new Envir();
        var account = new AccountInfo
        {
            AccountID = "stopped1",
            WrongPasswordCount = 2,
        };
        var salt = Crypto.GenerateSalt();
        var hash = Crypto.HashPassword("secret12", salt);
        account.SetPasswordHashAndSalt(hash, salt);
        environment.AccountList.Add(account);

        Assert.Equal(0, environment.HTTPLogin("stopped1", "secret12"));
        Assert.Equal(hash, account.Password);
        Assert.Equal(salt, account.Salt);
        Assert.Equal(2, account.WrongPasswordCount);
    }

    [Fact]
    public void 账户事务投递遇到主线程停止时绝不回退到调用线程()
    {
        var environment = StartMinimalEnvironment();
        environment.Stop();
        var executedThreadId = 0;

        var result = environment.InvokeOnMainThread(
            () =>
            {
                executedThreadId = Environment.CurrentManagedThreadId;
                return 7;
            },
            100,
            allowInlineWithoutMainThread: false);

        Assert.Equal(0, result);
        Assert.Equal(0, executedThreadId);
    }

    private static Envir StartMinimalEnvironment()
    {
        var environment = new Envir();
        environment.Start(new EnvirStartOptions
        {
            EnforceProductionSecurity = false,
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
}
