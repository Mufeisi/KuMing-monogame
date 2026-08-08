using Server;
using Server.MirEnvir;
using Xunit;

namespace Base05.Tests;

public sealed class ServerLifecycleSmokeTests
{
    [Fact]
    public void Minimal_server_start_stop_is_isolated_and_repeatable()
    {
        var envir = new Envir();
        var options = new EnvirStartOptions
        {
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
    public void 无游戏监听器启动失败后可重试且不进入Ready()
    {
        string oldAddress = Settings.IPAddress;
        bool oldTls = Settings.TlsEnabled;
        bool oldLegacy = Settings.AllowLegacyV1;
        var envir = new Envir();
        var failOptions = new EnvirStartOptions
        {
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
}
