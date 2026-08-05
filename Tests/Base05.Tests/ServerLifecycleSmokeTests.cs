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
}
