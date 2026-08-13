using System.Diagnostics;
using System.Drawing;
using System.Net;
using System.Net.Sockets;
using Xunit;
using Xunit.Abstractions;

namespace Base05.Tests;

[Collection("TLS环境")]
public sealed class Leg01ExternalProtocolProbeTests
{
    private const string EnabledVariable = "LYOCRYSTAL_LEG01_EXTERNAL_PROBE";
    private readonly ITestOutputHelper _output;

    public Leg01ExternalProtocolProbeTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task 当前Shared协议可完成注册登录建角进图移动和退出()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable(EnabledVariable), "1", StringComparison.Ordinal))
        {
            Assert.Equal("login", Leg01ProbeFailure.Classify(Leg01ProbeStage.Login));
            Assert.Equal("enter-game", Leg01ProbeFailure.Classify(Leg01ProbeStage.EnterGame));
            Assert.Equal("shutdown", Leg01ProbeFailure.Classify(Leg01ProbeStage.Shutdown));
            return;
        }

        string host = Environment.GetEnvironmentVariable("LYOCRYSTAL_LEG01_HOST") ?? "127.0.0.1";
        int port = ReadPort(Environment.GetEnvironmentVariable("LYOCRYSTAL_LEG01_PORT"), 7000);
        string suffix = $"{DateTime.UtcNow:ddHHmmss}{Random.Shared.Next(10, 99)}";
        string account = "leg" + suffix;
        string character = "L" + suffix;
        string password = "P" + Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(7));
        var timeline = new List<string>();
        var total = Stopwatch.StartNew();

        bool previousDirection = Packet.IsServer;
        Packet.IsServer = false;
        try
        {
            await using var probe = await Leg01ProtocolProbe.ConnectAsync(host, port, TimeSpan.FromSeconds(10));
            await RunStage(Leg01ProbeStage.Process, () => probe.HandshakeAsync());
            await RunStage(Leg01ProbeStage.Login, () => probe.CreateAndLoginAsync(account, password));
            int characterIndex = await RunValueStage(Leg01ProbeStage.Character, () => probe.EnsureCharacterAsync(character));
            ServerPackets.UserInformation user = await RunValueStage(Leg01ProbeStage.EnterGame, () => probe.EnterGameAsync(characterIndex));
            Point moved = await RunValueStage(Leg01ProbeStage.EnterGame, () => probe.MoveOnceAsync(user.Location));
            await RunStage(Leg01ProbeStage.Shutdown, () => probe.ExitAsync());

            Assert.NotEqual(user.Location, moved);
            _output.WriteLine($"LEG01_PROTOCOL_RESULT status=passed stages={string.Join(',', timeline)} elapsedMs={total.ElapsedMilliseconds} map={probe.MapIndex} movement=confirmed");
        }
        finally
        {
            Packet.IsServer = previousDirection;
        }

        async Task RunStage(Leg01ProbeStage stage, Func<Task> action)
        {
            await RunValueStage<object?>(stage, async () => { await action(); return null; });
        }

        async Task<T> RunValueStage<T>(Leg01ProbeStage stage, Func<Task<T>> action)
        {
            var watch = Stopwatch.StartNew();
            try
            {
                T result = await action();
                timeline.Add($"{Leg01ProbeFailure.Classify(stage)}:{watch.ElapsedMilliseconds}");
                return result;
            }
            catch (Exception ex)
            {
                throw new Xunit.Sdk.XunitException($"LEG01_PROTOCOL_RESULT status=failed stage={Leg01ProbeFailure.Classify(stage)} elapsedMs={total.ElapsedMilliseconds} detail={Leg01ProbeFailure.Sanitize(ex.Message)}");
            }
        }
    }

    private static int ReadPort(string? value, int fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : int.TryParse(value, out int port) && port is > 0 and <= 65535
            ? port
            : throw new InvalidOperationException("LYOCRYSTAL_LEG01_PORT 必须是 1..65535 的端口。");
}

internal enum Leg01ProbeStage
{
    Launcher,
    Process,
    Login,
    Character,
    EnterGame,
    Resource,
    Shutdown,
}

internal static class Leg01ProbeFailure
{
    public static string Classify(Leg01ProbeStage stage) => stage switch
    {
        Leg01ProbeStage.Launcher => "launcher",
        Leg01ProbeStage.Process => "process",
        Leg01ProbeStage.Login => "login",
        Leg01ProbeStage.Character => "character",
        Leg01ProbeStage.EnterGame => "enter-game",
        Leg01ProbeStage.Resource => "resource",
        Leg01ProbeStage.Shutdown => "shutdown",
        _ => throw new ArgumentOutOfRangeException(nameof(stage)),
    };

    public static string Sanitize(string message)
    {
        string singleLine = message.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return singleLine.Length <= 240 ? singleLine : singleLine[..240];
    }
}

internal sealed class Leg01ProtocolProbe : IAsyncDisposable
{
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private byte[] _pending = Array.Empty<byte>();
    public int MapIndex { get; private set; } = -1;

    private Leg01ProtocolProbe(TcpClient client)
    {
        _client = client;
        _stream = client.GetStream();
    }

    public static async Task<Leg01ProtocolProbe> ConnectAsync(string host, int port, TimeSpan timeout)
    {
        var client = new TcpClient { NoDelay = true };
        using var cancellation = new CancellationTokenSource(timeout);
        try
        {
            await client.ConnectAsync(host, port, cancellation.Token);
            return new Leg01ProtocolProbe(client);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    public async Task HandshakeAsync()
    {
        await ReadUntilAsync<ServerPackets.Connected>(TimeSpan.FromSeconds(15));
        await SendAsync(new ClientPackets.ClientVersion { VersionHash = Array.Empty<byte>() });
        ServerPackets.ClientVersion version = await ReadUntilAsync<ServerPackets.ClientVersion>(TimeSpan.FromSeconds(15));
        if (version.Result != 1) throw new IOException($"客户端版本握手失败，结果码 {version.Result}。");
    }

    public async Task CreateAndLoginAsync(string account, string password)
    {
        await SendAsync(new ClientPackets.NewAccount
        {
            AccountID = account,
            Password = password,
            BirthDate = new DateTime(2000, 1, 1),
            UserName = "LEG01Probe",
            SecretQuestion = "ProbeQuestion",
            SecretAnswer = "ProbeAnswer",
            EMailAddress = account + "@example.invalid",
        });
        ServerPackets.NewAccount created = await ReadUntilAsync<ServerPackets.NewAccount>(TimeSpan.FromSeconds(15));
        if (created.Result != 8) throw new IOException($"创建临时测试账号失败，结果码 {created.Result}。");

        await SendAsync(new ClientPackets.Login { AccountID = account, Password = password });
        _ = await ReadUntilAsync<ServerPackets.LoginSuccess>(TimeSpan.FromSeconds(15));
    }

    public async Task<int> EnsureCharacterAsync(string character)
    {
        await SendAsync(new ClientPackets.NewCharacter { Name = character, Gender = MirGender.男性, Class = MirClass.战士 });
        ServerPackets.NewCharacterSuccess created = await ReadUntilAsync<ServerPackets.NewCharacterSuccess>(TimeSpan.FromSeconds(15));
        return created.CharInfo.Index;
    }

    public async Task<ServerPackets.UserInformation> EnterGameAsync(int characterIndex)
    {
        await SendAsync(new ClientPackets.StartGame { CharacterIndex = characterIndex });
        ServerPackets.StartGame started = await ReadUntilAsync<ServerPackets.StartGame>(TimeSpan.FromSeconds(20));
        if (started.Result != 4) throw new IOException($"开始游戏失败，结果码 {started.Result}。");
        ServerPackets.MapInformation map = await ReadUntilAsync<ServerPackets.MapInformation>(TimeSpan.FromSeconds(30));
        MapIndex = map.MapIndex;
        return await ReadUntilAsync<ServerPackets.UserInformation>(TimeSpan.FromSeconds(30));
    }

    public async Task<Point> MoveOnceAsync(Point original)
    {
        foreach (MirDirection direction in Enum.GetValues<MirDirection>().Distinct())
        {
            await SendAsync(new ClientPackets.Walk { Direction = direction });
            ServerPackets.UserLocation location = await ReadUntilAsync<ServerPackets.UserLocation>(TimeSpan.FromSeconds(5));
            if (location.Location != original) return location.Location;
            await Task.Delay(600);
        }
        throw new IOException($"服务端确认了移动请求，但位置始终未离开 ({original.X},{original.Y})。");
    }

    public async Task ExitAsync()
    {
        await SendAsync(new ClientPackets.LogOut());
        await Task.Delay(250);
        _client.Client.Shutdown(SocketShutdown.Send);
    }

    public async ValueTask DisposeAsync()
    {
        await _stream.DisposeAsync();
        _client.Dispose();
    }

    private async Task SendAsync(Packet packet)
    {
        byte[] data = packet.GetPacketBytes().ToArray();
        await _stream.WriteAsync(data);
        await _stream.FlushAsync();
    }

    private async Task<T> ReadUntilAsync<T>(TimeSpan timeout) where T : Packet
    {
        using var cancellation = new CancellationTokenSource(timeout);
        var observed = new List<short>();
        try
        {
            while (true)
            {
                byte[] frame = await ReadFrameAsync(cancellation.Token);
                short id = BitConverter.ToInt16(frame, 2);
                observed.Add(id);
                Packet? packet = Packet.ReceivePacket(frame, out byte[] extra);
                if (extra.Length != 0) throw new InvalidDataException("单帧解析后仍有额外字节。");
                if (packet is T expected) return expected;
                ThrowOnProtocolFailure(packet);
            }
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException($"等待 {typeof(T).Name} 超时，已见包 [{string.Join(',', observed)}]。");
        }
    }

    private static void ThrowOnProtocolFailure(Packet? packet)
    {
        if (packet is ServerPackets.Disconnect disconnect) throw new IOException($"服务端断开连接，原因码 {disconnect.Reason}。");
        if (packet is ServerPackets.Login login) throw new IOException($"登录失败，结果码 {login.Result}。");
        if (packet is ServerPackets.LoginBanned banned) throw new IOException($"登录被拒绝：{banned.Reason}。");
        if (packet is ServerPackets.NewCharacter character) throw new IOException($"创建角色失败，结果码 {character.Result}。");
        if (packet is ServerPackets.StartGame game && game.Result != 4) throw new IOException($"开始游戏失败，结果码 {game.Result}。");
    }

    private async Task<byte[]> ReadFrameAsync(CancellationToken cancellationToken)
    {
        while (_pending.Length < 4) await ReadMoreAsync(cancellationToken);
        int length = BitConverter.ToUInt16(_pending, 0);
        if (length < 4 || length > ushort.MaxValue) throw new InvalidDataException($"收到非法数据包长度：{length}。");
        while (_pending.Length < length) await ReadMoreAsync(cancellationToken);
        byte[] frame = _pending[..length];
        _pending = _pending[length..];
        return frame;
    }

    private async Task ReadMoreAsync(CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[16 * 1024];
        int read = await _stream.ReadAsync(buffer, cancellationToken);
        if (read == 0) throw new EndOfStreamException("服务端连接已关闭。");
        int previous = _pending.Length;
        Array.Resize(ref _pending, previous + read);
        Buffer.BlockCopy(buffer, 0, _pending, previous, read);
    }
}
