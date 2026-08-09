using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MonoShare.MirControls;
using MonoShare.MirScenes;
using C = ClientPackets;
using S = ServerPackets;
using Shared.Diagnostics;
using Shared.Security;
using Shared.Transport;


namespace MonoShare.MirNetwork
{
    static class Network
    {
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        private const int PacketTraceMaxQueuedLines = 6000;
        private const int PacketTraceMaxLinesPerFlush = 250;
        private const long PacketTraceFlushIntervalMs = 500;
        private const long PacketTraceRotateBytes = 3 * 1024 * 1024;
        private const long HandshakeIdleReconnectMs = 15000;
        private const long LoginAutoReconnectIntervalMs = 1500;
        private const int DefaultBackgroundKeepAliveTickMs = 1000;

        private static TcpClient _client;
        private static Stream _stream;
        private static bool _usingTls;
        private static int _activePort;
        private static int _connectionGeneration;
        public static int ConnectAttempt = 0;
        public static bool Connected;
        public static long TimeOutTime, TimeConnected;
        private static bool _paused;
        private static StreamWriteGate _sendGate = new StreamWriteGate();
        private static Timer _backgroundKeepAliveTimer;
        private static int _backgroundKeepAliveStarted;

        private static ConcurrentQueue<Packet> _receiveList;
        private static ConcurrentQueue<Packet> _sendList;
        private static readonly ConcurrentQueue<Packet> _preSendList = new ConcurrentQueue<Packet>();

        internal static int PendingSendCount => (_sendList?.Count ?? 0) + _preSendList.Count;
#if REAL_ANDROID
        internal static bool TlsTransportActive => _client?.Connected == true && _stream is SslStream && _usingTls;
        internal static string LastTlsProbeFailure { get; private set; } = string.Empty;
        internal static void RecordTlsProbeTimeout()
        {
            if (string.IsNullOrEmpty(LastTlsProbeFailure))
                LastTlsProbeFailure = TlsClientPolicy.ClassifyFailure(
                    new OperationCanceledException("12秒内未完成TLS握手"));
        }
#endif
        private static PerformanceQueueTracker _receiveQueueMetrics = new PerformanceQueueTracker();
        private static PerformanceQueueTracker _sendQueueMetrics = new PerformanceQueueTracker();
        private static PerformanceQueueTracker _networkQueueMetrics = new PerformanceQueueTracker();
        private static readonly PerformanceQueueTracker PreSendQueueMetrics = new PerformanceQueueTracker();
        private static readonly ConcurrentQueue<string> _packetTraceQueue = new ConcurrentQueue<string>();
        private static int _packetTraceQueueCount;
        private static long _nextPacketTraceFlushTime;
        private static long _connectedTick;
        private static long _lastReceiveTick;
        private static long _lastSendTick;
        private static long _nextLoginAutoConnectTime;

        private static readonly object _connectionGate = new object();
        static byte[] _rawData = new byte[0];
        private static long GetRuntimeTimeMs()
        {
            try
            {
                return CMain.Timer.ElapsedMilliseconds;
            }
            catch
            {
                return Environment.TickCount64;
            }
        }

        private static int GetBackgroundKeepAliveTickMs()
        {
            try
            {
                return Math.Clamp(Settings.BackgroundNetworkTickMs, 250, 5000);
            }
            catch
            {
                return DefaultBackgroundKeepAliveTickMs;
            }
        }

        private static int GetBackgroundKeepAliveIdleSendMs()
        {
            int tickMs = GetBackgroundKeepAliveTickMs();

            try
            {
                int timeoutMs = Math.Max(2000, Settings.TimeOut);
                int targetMs = Math.Max(tickMs, timeoutMs / 3);
                int maxMs = Math.Max(tickMs, timeoutMs - 1500);
                return Math.Clamp(targetMs, tickMs, maxMs);
            }
            catch
            {
                return Math.Max(tickMs, 1500);
            }
        }

        public static void Connect()
        {
#if REAL_ANDROID
            LastTlsProbeFailure = string.Empty;
#endif
            if (!Settings.UseTlsV2 && !TlsClientPolicy.IsLoopbackHost(Settings.IPAddress))
            {
                if (Settings.LogErrors) CMain.SaveError("已拒绝非回环V1明文连接");
                return;
            }

            if (Settings.UseTlsV2 && (Settings.TlsPort < 1 || Settings.TlsPort > 65535))
            {
                if (Settings.LogErrors) CMain.SaveError("TLS端口配置无效");
                return;
            }

            ConnectAttempt++;

            bool useTls = Settings.UseTlsV2;
            int activePort = useTls ? Settings.TlsPort : Settings.Port;
            TcpClient client = new TcpClient { NoDelay = true };
            int generation;
            lock (_connectionGate)
            {
                if (_client != null)
                {
                    client.Close();
                    return;
                }

                (_client, _sendGate, _usingTls, _activePort) =
                    (client, new StreamWriteGate(), useTls, activePort);
                generation = Interlocked.Increment(ref _connectionGeneration);
            }

            try
            {
                var state = (Client: client, Generation: generation, UseTls: useTls,
                    Port: activePort, Host: Settings.IPAddress, ServerName: Settings.TlsServerName);
                EnqueuePacketTraceLine($"[{CMain.Now:yyyy-MM-dd HH:mm:ss.fff}] CONNECT Attempt={ConnectAttempt} Host={Settings.IPAddress}:{activePort}");
                EnsureBackgroundKeepAliveTimerStarted();
                client.BeginConnect(Settings.IPAddress, activePort, Connection, state);
            }
            catch (Exception ex) { FailIfCurrent(client, generation, "CONNECT BeginConnectFailed", ex.ToString()); }
        }

        private static async void Connection(IAsyncResult result)
        {
            var state = ((TcpClient Client, int Generation, bool UseTls, int Port, string Host, string ServerName))result.AsyncState;
            Stream stream = null;
            SslStream ssl = null;
            bool adopted = false;
            try
            {
                state.Client.EndConnect(result);
                bool current = state.Generation == Volatile.Read(ref _connectionGeneration) && ReferenceEquals(_client, state.Client);
                if (!current) return;

                if (!state.Client.Connected)
                {
                    FailIfCurrent(state.Client, state.Generation, "CONNECT Failed (NotConnected)");
                    return;
                }

                stream = state.Client.GetStream();
                if (state.UseTls)
                {
                    ssl = new SslStream(stream, leaveInnerStreamOpen: false);
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    await ssl.AuthenticateAsClientAsync(TlsClientPolicy.CreateOptions(state.ServerName), timeout.Token);
                    stream = ssl;
                }
                lock (_connectionGate)
                {
                    if (state.Generation != Volatile.Read(ref _connectionGeneration) || !ReferenceEquals(_client, state.Client)) return;
                    _stream = stream;
                    adopted = true;

                    _receiveList = new ConcurrentQueue<Packet>();
                    _sendList = new ConcurrentQueue<Packet>();
                    _receiveQueueMetrics = new PerformanceQueueTracker();
                    _sendQueueMetrics = new PerformanceQueueTracker();
                    _rawData = new byte[0];

                    long runtimeTime = GetRuntimeTimeMs();
                    TimeOutTime = runtimeTime + Settings.TimeOut;
                    TimeConnected = runtimeTime;

                    long nowTick = Environment.TickCount64;
                    _connectedTick = nowTick;
                    _lastReceiveTick = nowTick;
                    _lastSendTick = nowTick;
                }

                EnqueuePacketTraceLine($"[{CMain.Now:yyyy-MM-dd HH:mm:ss.fff}] CONNECTED Host={state.Host}:{state.Port}");

                BeginReceive(state.Client, state.Generation, stream);
            }
            catch (SocketException ex)
            {
                bool failedCurrent = FailIfCurrent(state.Client, state.Generation, "CONNECT SocketException");
#if REAL_ANDROID
                if (state.UseTls && failedCurrent) LastTlsProbeFailure = TlsClientPolicy.ClassifyFailure(ex);
#endif
            }
            catch (Exception ex)
            {
                string failure = state.UseTls ? TlsClientPolicy.FormatFailure(ex, state.Host, state.Port) : ex.ToString();
                bool failedCurrent = FailIfCurrent(state.Client, state.Generation, "CONNECT Failed", failure);
#if REAL_ANDROID
                if (state.UseTls && failedCurrent) LastTlsProbeFailure = TlsClientPolicy.ClassifyFailure(ex);
#endif
            }
            finally
            {
                if (!adopted)
                {
                    try { ssl?.Dispose(); } catch { }
                    try { if (stream != null && !ReferenceEquals(stream, ssl)) stream.Dispose(); } catch { }
                    try { state.Client.Close(); } catch { }
                }
            }
        }

        private static void EnsureBackgroundKeepAliveTimerStarted()
        {
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                return;

            if (Interlocked.Exchange(ref _backgroundKeepAliveStarted, 1) == 1)
                return;

            try
            {
                int tickMs = GetBackgroundKeepAliveTickMs();
                _backgroundKeepAliveTimer = new Timer(BackgroundKeepAliveTick, null, tickMs, tickMs);
            }
            catch
            {
                _backgroundKeepAliveTimer = null;
            }
        }

        private static void BackgroundKeepAliveTick(object state)
        {
            if (_paused)
                return;

            TcpClient client = _client;
            if (client == null || !client.Connected)
                return;

            // 未完成握手/登录前不主动插入 KeepAlive，避免干扰协议。
            if (!Connected)
                return;

            long nowTick = Environment.TickCount64;
            long lastSend = Interlocked.Read(ref _lastSendTick);
            if (lastSend <= 0)
                lastSend = nowTick;

            long idleSendMs = nowTick - lastSend;
            if (idleSendMs < GetBackgroundKeepAliveIdleSendMs())
                return;

            try
            {
                long runtimeTime = GetRuntimeTimeMs();
                Packet keepAlive = new C.KeepAlive
                {
                    Time = runtimeTime,
                };
                IEnumerable<byte> packetBytesEnumerable = keepAlive.GetPacketBytes();
                byte[] packetBytes = packetBytesEnumerable as byte[] ?? packetBytesEnumerable.ToArray();
                if (packetBytes.Length == 0)
                    return;

                if (!TrySendRawBytes(packetBytes))
                    return;

                Interlocked.Exchange(ref _lastSendTick, nowTick);
                Interlocked.Exchange(ref TimeOutTime, runtimeTime + Settings.TimeOut);
                TracePacket("SEND", keepAlive, packetBytes.Length);
            }
            catch
            {
            }
        }

        private static bool IsCurrent(int generation, TcpClient client, Stream stream) =>
            generation == Volatile.Read(ref _connectionGeneration) && ReferenceEquals(_client, client) && ReferenceEquals(_stream, stream);

        private static bool DetachCurrent(TcpClient expectedClient, int expectedGeneration,
            out (TcpClient Client, Stream Stream, StreamWriteGate Gate, int QueueDepth) detached)
        {
            lock (_connectionGate)
            {
                if (expectedClient != null &&
                    (expectedGeneration != Volatile.Read(ref _connectionGeneration) || !ReferenceEquals(_client, expectedClient)))
                {
                    detached = default;
                    return false;
                }

                bool hadState = _client != null || _stream != null || _receiveList != null || _sendList != null || !_preSendList.IsEmpty;
                int queueDepth = Math.Max(0, _receiveQueueMetrics.Depth) + Math.Max(0, _sendQueueMetrics.Depth);
                detached = (_client, _stream, _sendGate, queueDepth);
                Interlocked.Increment(ref _connectionGeneration);
                _client = null;
                _stream = null;
                _sendGate = new StreamWriteGate();
                _networkQueueMetrics.Dequeue(queueDepth);
                _sendList = null;
                _receiveList = null;
                _rawData = new byte[0];
                TimeConnected = 0;
                Connected = false;
                _usingTls = false;
                _activePort = 0;
                _connectedTick = 0;
                _lastReceiveTick = 0;
                _lastSendTick = 0;
                return hadState;
            }
        }

        private static void CloseDetached((TcpClient Client, Stream Stream, StreamWriteGate Gate, int QueueDepth) detached,
            string trace, string error = null)
        {
            try { detached.Gate?.Dispose(); } catch { }
            try { detached.Stream?.Dispose(); } catch { }
            try { detached.Client?.Close(); } catch { }
            if (!string.IsNullOrWhiteSpace(error) && Settings.LogErrors) CMain.SaveError(error);
            EnqueuePacketTraceLine($"[{CMain.Now:yyyy-MM-dd HH:mm:ss.fff}] {trace}");
            FlushPacketTraceIfDue(force: true);
        }

        private static bool FailIfCurrent(TcpClient expectedClient, int generation, string trace,
            string error = null)
        {
            if (!DetachCurrent(expectedClient, generation, out var detached)) return false;
            CloseDetached(detached, trace, error);
            return true;
        }

        private static void BeginReceive(TcpClient client, int generation, Stream stream)
        {
            if (!IsCurrent(generation, client, stream)) return;
            if (!client.Connected)
            {
                FailIfCurrent(client, generation, "RECV NotConnected");
                return;
            }

            try
            {
                var buffer = new byte[8 * 1024];
                stream.BeginRead(buffer, 0, buffer.Length, ReceiveData, (Client: client, Generation: generation, Stream: stream, Buffer: buffer));
            }
            catch
            {
                FailIfCurrent(client, generation, "RECV BeginReadFailed");
            }
        }
        private static void ReceiveData(IAsyncResult result)
        {
            var state = ((TcpClient Client, int Generation, Stream Stream, byte[] Buffer))result.AsyncState;
            if (!IsCurrent(state.Generation, state.Client, state.Stream)) return;
            if (!state.Client.Connected)
            {
                FailIfCurrent(state.Client, state.Generation, "RECV NotConnected");
                return;
            }

            int dataRead;

            try
            {
                dataRead = state.Stream.EndRead(result);
            }
            catch
            {
                FailIfCurrent(state.Client, state.Generation, "RECV Error");
                return;
            }

            if (!IsCurrent(state.Generation, state.Client, state.Stream)) return;
            if (dataRead == 0)
            {
                FailIfCurrent(state.Client, state.Generation, "RECV EOF");
                return;
            }

            lock (_connectionGate)
            {
            if (!IsCurrent(state.Generation, state.Client, state.Stream)) return;
            _lastReceiveTick = Environment.TickCount64;

            byte[] rawBytes = state.Buffer;

            byte[] temp = _rawData;
            _rawData = new byte[dataRead + temp.Length];
            Buffer.BlockCopy(temp, 0, _rawData, 0, temp.Length);
            Buffer.BlockCopy(rawBytes, 0, _rawData, temp.Length, dataRead);

            Packet p;
            List<byte> data = new List<byte>();

            while ((p = Packet.ReceivePacket(_rawData, out _rawData)) != null)
            {
                _receiveList.Enqueue(p);
                _receiveQueueMetrics.Enqueue();
                _networkQueueMetrics.Enqueue();
                IEnumerable<byte> packetBytesEnumerable = p.GetPacketBytes();
                byte[] packetBytes = packetBytesEnumerable as byte[] ?? packetBytesEnumerable.ToArray();
                data.AddRange(packetBytes);
                TracePacket("RECV", p, packetBytes.Length);
            }

            CMain.BytesReceived += data.Count;
            }

            BeginReceive(state.Client, state.Generation, state.Stream);
        }

        private static bool BeginSend(List<byte> data, bool gateHeld = false)
        {
            if (_client == null || !_client.Connected || data.Count == 0)
            {
                if (gateHeld) _sendGate.Complete();
                return false;
            }
            if (!gateHeld && !_sendGate.TryEnter()) return false;

            long nowTick = Environment.TickCount64;
            Interlocked.Exchange(ref _lastSendTick, nowTick);

            try
            {
                byte[] bytes = data.ToArray();
                Stream stream = _stream;
                if (stream == null)
                {
                    _sendGate.Complete();
                    return false;
                }
                stream.BeginWrite(bytes, 0, bytes.Length, SendData, (stream, _sendGate));
                return true;
            }
            catch
            {
                _sendGate.Complete();
                Disconnect();
                return false;
            }
        }

        private static bool TrySendRawBytes(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
                return true;

            StreamWriteGate gate = _sendGate;
            if (!gate.TryEnter())
                return false;

            Stream stream = _stream;
            if (stream == null || _client == null || !_client.Connected)
            {
                gate.Complete();
                return false;
            }
            try
            {
                stream.BeginWrite(bytes, 0, bytes.Length, SendData, (stream, gate));
                return true;
            }
            catch
            {
                gate.Complete();
                Disconnect();
                return false;
            }
        }

        private static void SendData(IAsyncResult result)
        {
            var state = ((Stream, StreamWriteGate))result.AsyncState;
            try
            {
                state.Item1.EndWrite(result);
            }
            catch
            {
                if (ReferenceEquals(_stream, state.Item1) && ReferenceEquals(_sendGate, state.Item2))
                    Disconnect();
            }
            finally
            {
                state.Item2.Complete();
            }

        }


        public static void Disconnect()
        {
            if (DetachCurrent(null, 0, out var detached))
                CloseDetached(detached, "DISCONNECT");
        }

        public static void Process()
        {
            FlushPacketTraceIfDue();

            if (_paused)
                return;

            if (_client == null || !_client.Connected)
            {
                if (Connected)
                {
                    while (_receiveList != null && !_receiveList.IsEmpty)
                    {
                        if (!_receiveList.TryDequeue(out Packet p)) continue;
                        _receiveQueueMetrics.Dequeue();
                        _networkQueueMetrics.Dequeue();
                        if (p == null) continue;
                        if (!(p is ServerPackets.Disconnect) && !(p is ServerPackets.ClientVersion)) continue;

                        MirScene.ActiveScene.ProcessPacket(p);
                        _receiveList = null;
                        return;
                    }

                    Disconnect();
                    MirScene.ReturnToLoginScene("与服务器连接已断开，请检查网络后重新登录。");
                    return;
                }

                if (_client == null && MirScene.ActiveScene is LoginScene)
                {
                    long now = CMain.Time;
                    if (_nextLoginAutoConnectTime == 0 || now >= _nextLoginAutoConnectTime)
                    {
                        _nextLoginAutoConnectTime = now + LoginAutoReconnectIntervalMs;
                        Connect();
                    }
                }

                return;
            }



            while (_receiveList != null && !_receiveList.IsEmpty)
            {
                if (!_receiveList.TryDequeue(out Packet p)) continue;
                _receiveQueueMetrics.Dequeue();
                _networkQueueMetrics.Dequeue();
                if (p == null) continue;

                // 移动端：在进入 GameScene 之前也缓存服务端聊天/系统消息（例如欢迎消息），
                // 这样进入地图后 BottomUI/DChatWindow 仍能回显到 HUD。
                if (Environment.OSVersion.Platform != PlatformID.Win32NT && p is S.Chat chat && MirScene.ActiveScene is not GameScene)
                {
                    try
                    {
                        string cleaned = chat.Message ?? string.Empty;
                        try
                        {
                            cleaned = RegexFunctions.CleanChatString(cleaned);
                        }
                        catch
                        {
                            cleaned = chat.Message ?? string.Empty;
                        }

                        MonoShare.FairyGuiHost.AppendMobileChatMessage(cleaned, chat.Type);
                    }
                    catch
                    {
                    }
                }

                MirScene.ActiveScene.ProcessPacket(p);
            }

            FlushPreSendPacketsIfConnected();

            if (CMain.Time > TimeOutTime && _sendList != null && _sendList.IsEmpty)
            {
                _sendList.Enqueue(new C.KeepAlive());
                _sendQueueMetrics.Enqueue();
                _networkQueueMetrics.Enqueue();
            }

            if (_sendList != null && !_sendList.IsEmpty)
            {
                if (!_sendGate.TryEnter())
                    return;

                TimeOutTime = GetRuntimeTimeMs() + Settings.TimeOut;

                List<byte> data = new List<byte>();
                while (!_sendList.IsEmpty)
                {
                    if (!_sendList.TryDequeue(out Packet p)) continue;
                    _sendQueueMetrics.Dequeue();
                    _networkQueueMetrics.Dequeue();
                    IEnumerable<byte> packetBytesEnumerable = p.GetPacketBytes();
                    byte[] packetBytes = packetBytesEnumerable as byte[] ?? packetBytesEnumerable.ToArray();
                    data.AddRange(packetBytes);
                    TracePacket("SEND", p, packetBytes.Length);
                }

                CMain.BytesSent += data.Count;

                BeginSend(data, gateHeld: true);
            }

            if (_client == null || !_client.Connected)
                return;

            if (!Connected && TimeConnected > 0 && ShouldReconnectHandshake())
            {
                Disconnect();
                Connect();
            }
        }

        public static void RecordPerformanceQueueMetrics()
        {
            if (!PerformanceMetrics.Enabled) return;

            var receive = _receiveQueueMetrics.Depth;
            var send = _sendQueueMetrics.Depth + PreSendQueueMetrics.Depth;
            PerformanceMetrics.SetGauge(PerformanceMetricKind.NetworkInQueue, receive);
            PerformanceMetrics.SetGauge(PerformanceMetricKind.NetworkOutQueue, send);
            PerformanceMetrics.SetGauge(PerformanceMetricKind.NetworkQueue, _networkQueueMetrics.Depth);
            var receiveHighWater = _receiveQueueMetrics.CaptureHighWater();
            var sendHighWater = Math.Max(
                _sendQueueMetrics.CaptureHighWater(),
                PreSendQueueMetrics.CaptureHighWater());
            PerformanceMetrics.SetGauge(PerformanceMetricKind.NetworkInQueueHighWater, receiveHighWater);
            PerformanceMetrics.SetGauge(PerformanceMetricKind.NetworkOutQueueHighWater, sendHighWater);
            PerformanceMetrics.SetGauge(PerformanceMetricKind.NetworkQueueHighWater, _networkQueueMetrics.CaptureHighWater());
        }

        private static bool ShouldReconnectHandshake()
        {
            if (Connected)
                return false;

            if (_client == null || !_client.Connected)
                return false;

            long nowTick = Environment.TickCount64;
            long connectedTick = _connectedTick;
            if (connectedTick <= 0)
                return false;

            long elapsedMs = nowTick - connectedTick;

            long lastReceiveTick = _lastReceiveTick > 0 ? _lastReceiveTick : connectedTick;
            long idleReceiveMs = nowTick - lastReceiveTick;

            return elapsedMs >= HandshakeIdleReconnectMs && idleReceiveMs >= HandshakeIdleReconnectMs;
        }

        public static void SetPaused(bool paused)
        {
            _paused = paused;

            if (!paused && _client != null && _client.Connected)
                TimeOutTime = GetRuntimeTimeMs() + Settings.TimeOut;
        }
        
        public static void Enqueue(Packet p)
        {
            if (p == null)
                return;

            if (_sendList != null)
            {
                _sendList.Enqueue(p);
                _sendQueueMetrics.Enqueue();
                _networkQueueMetrics.Enqueue();
                return;
            }

            _preSendList.Enqueue(p);
            PreSendQueueMetrics.Enqueue();
            _networkQueueMetrics.Enqueue();
        }

        private static void FlushPreSendPacketsIfConnected()
        {
            if (!Connected)
                return;

            if (_sendList == null || _preSendList.IsEmpty)
                return;

            int drained = 0;
            while (drained < 256 && _preSendList.TryDequeue(out Packet p))
            {
                drained++;
                PreSendQueueMetrics.Dequeue();
                if (p == null)
                    continue;

                _sendList.Enqueue(p);
                _sendQueueMetrics.Enqueue();
            }
        }

        private static void TracePacket(string direction, Packet packet, int byteLength)
        {
            if (!Settings.TracePackets || packet == null)
                return;

            string typeName;
            try
            {
                typeName = packet.GetType().Name;
            }
            catch
            {
                typeName = "UnknownPacket";
            }

            EnqueuePacketTraceLine($"[{CMain.Now:yyyy-MM-dd HH:mm:ss.fff}] {direction} Id={packet.Index} Type={typeName} Bytes={byteLength}");
        }

        private static void EnqueuePacketTraceLine(string line)
        {
            if (!Settings.TracePackets || string.IsNullOrWhiteSpace(line))
                return;

            int count = Interlocked.Increment(ref _packetTraceQueueCount);
            if (count > PacketTraceMaxQueuedLines)
            {
                Interlocked.Decrement(ref _packetTraceQueueCount);
                return;
            }

            _packetTraceQueue.Enqueue(line);
        }

        private static void FlushPacketTraceIfDue(bool force = false)
        {
            if (!Settings.TracePackets)
                return;

            long now = CMain.Time;
            if (!force && now < _nextPacketTraceFlushTime)
                return;

            _nextPacketTraceFlushTime = now + PacketTraceFlushIntervalMs;

            if (Volatile.Read(ref _packetTraceQueueCount) <= 0)
                return;

            string logPath = Path.Combine(ClientResourceLayout.RuntimeRoot, "MobilePacketTrace.log");

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(logPath) ?? ClientResourceLayout.RuntimeRoot);
                TryRotatePacketTraceLog(logPath);

                using var writer = new StreamWriter(logPath, append: true, Utf8NoBom);
                int written = 0;

                while (written < PacketTraceMaxLinesPerFlush && _packetTraceQueue.TryDequeue(out string line))
                {
                    Interlocked.Decrement(ref _packetTraceQueueCount);
                    writer.WriteLine(line);
                    written++;
                }
            }
            catch (Exception ex)
            {
                if (Settings.LogErrors) CMain.SaveError(ex.ToString());
            }
        }

        private static void TryRotatePacketTraceLog(string logPath)
        {
            try
            {
                if (!File.Exists(logPath))
                    return;

                var info = new FileInfo(logPath);
                if (info.Length < PacketTraceRotateBytes)
                    return;

                string directory = Path.GetDirectoryName(logPath) ?? ClientResourceLayout.RuntimeRoot;
                string rotated = Path.Combine(directory, $"MobilePacketTrace.{DateTime.Now:yyyyMMdd-HHmmss}.log");

                if (File.Exists(rotated))
                    return;

                File.Move(logPath, rotated);
            }
            catch
            {
            }
        }
    }
}
