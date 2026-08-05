using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using MonoShare.MirControls;
using MonoShare.MirScenes;
using C = ClientPackets;
using S = ServerPackets;


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
        public static int ConnectAttempt = 0;
        public static bool Connected;
        public static long TimeOutTime, TimeConnected;
        private static bool _paused;
        private static readonly object _sendGate = new object();
        private static Timer _backgroundKeepAliveTimer;
        private static int _backgroundKeepAliveStarted;

        private static ConcurrentQueue<Packet> _receiveList;
        private static ConcurrentQueue<Packet> _sendList;
        private static readonly ConcurrentQueue<Packet> _preSendList = new ConcurrentQueue<Packet>();
        private static readonly ConcurrentQueue<string> _packetTraceQueue = new ConcurrentQueue<string>();
        private static int _packetTraceQueueCount;
        private static long _nextPacketTraceFlushTime;
        private static long _connectedTick;
        private static long _lastReceiveTick;
        private static long _lastSendTick;
        private static long _nextLoginAutoConnectTime;

        static byte[] _rawData = new byte[0];
        static readonly byte[] _rawBytes = new byte[8 * 1024];

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
            if (_client != null)
                Disconnect();

            ConnectAttempt++;

            _client = new TcpClient {NoDelay = true};
            EnqueuePacketTraceLine($"[{CMain.Now:yyyy-MM-dd HH:mm:ss.fff}] CONNECT Attempt={ConnectAttempt} Host={Settings.IPAddress}:{Settings.Port}");
            EnsureBackgroundKeepAliveTimerStarted();
            _client.BeginConnect(Settings.IPAddress, Settings.Port, Connection, null);

        }

        private static void Connection(IAsyncResult result)
        {
            try
            {
                _client.EndConnect(result);

                if (!_client.Connected)
                {
                    EnqueuePacketTraceLine($"[{CMain.Now:yyyy-MM-dd HH:mm:ss.fff}] CONNECT Failed (NotConnected)");
                    Connect();
                    return;
                }

                _receiveList = new ConcurrentQueue<Packet>();
                _sendList = new ConcurrentQueue<Packet>();
                _rawData = new byte[0];

                long runtimeTime = GetRuntimeTimeMs();
                TimeOutTime = runtimeTime + Settings.TimeOut;
                TimeConnected = runtimeTime;

                long nowTick = Environment.TickCount64;
                _connectedTick = nowTick;
                _lastReceiveTick = nowTick;
                _lastSendTick = nowTick;

                EnqueuePacketTraceLine($"[{CMain.Now:yyyy-MM-dd HH:mm:ss.fff}] CONNECTED Host={Settings.IPAddress}:{Settings.Port}");

                BeginReceive();
            }
            catch (SocketException)
            {
                EnqueuePacketTraceLine($"[{CMain.Now:yyyy-MM-dd HH:mm:ss.fff}] CONNECT SocketException");
                Connect();
            }
            catch (Exception ex)
            {
                if (Settings.LogErrors) CMain.SaveError(ex.ToString());
                Disconnect();
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

        private static void BeginReceive()
        {
            if (_client == null || !_client.Connected) return;

            try
            {
                _client.Client.BeginReceive(_rawBytes, 0, _rawBytes.Length, SocketFlags.None, ReceiveData, _rawBytes);
            }
            catch
            {
                Disconnect();
            }
        }
        private static void ReceiveData(IAsyncResult result)
        {
            if (_client == null || !_client.Connected) return;

            int dataRead;

            try
            {
                dataRead = _client.Client.EndReceive(result);
            }
            catch
            {
                Disconnect();
                return;
            }

            if (dataRead == 0)
            {
                Disconnect();
                return;
            }

            _lastReceiveTick = Environment.TickCount64;

            byte[] rawBytes = result.AsyncState as byte[];

            byte[] temp = _rawData;
            _rawData = new byte[dataRead + temp.Length];
            Buffer.BlockCopy(temp, 0, _rawData, 0, temp.Length);
            Buffer.BlockCopy(rawBytes, 0, _rawData, temp.Length, dataRead);

            Packet p;
            List<byte> data = new List<byte>();

            while ((p = Packet.ReceivePacket(_rawData, out _rawData)) != null)
            {
                _receiveList.Enqueue(p);
                IEnumerable<byte> packetBytesEnumerable = p.GetPacketBytes();
                byte[] packetBytes = packetBytesEnumerable as byte[] ?? packetBytesEnumerable.ToArray();
                data.AddRange(packetBytes);
                TracePacket("RECV", p, packetBytes.Length);
            }

            CMain.BytesReceived += data.Count;

            BeginReceive();
        }

        private static void BeginSend(List<byte> data)
        {
            if (_client == null || !_client.Connected || data.Count == 0) return;

            long nowTick = Environment.TickCount64;
            Interlocked.Exchange(ref _lastSendTick, nowTick);

            try
            {
                byte[] bytes = data.ToArray();
                if (!TrySendRawBytes(bytes))
                    Disconnect();
            }
            catch
            {
                Disconnect();
            }
        }

        private static bool TrySendRawBytes(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
                return true;

            TcpClient client = _client;
            if (client == null || !client.Connected)
                return false;

            try
            {
                lock (_sendGate)
                {
                    client = _client;
                    if (client == null || !client.Connected)
                        return false;

                    Socket socket = client.Client;
                    int offset = 0;
                    int remaining = bytes.Length;
                    while (remaining > 0)
                    {
                        int sent = socket.Send(bytes, offset, remaining, SocketFlags.None);
                        if (sent <= 0)
                            return false;

                        offset += sent;
                        remaining -= sent;
                    }
                }
            }
            catch
            {
                return false;
            }

            return true;
        }


        public static void Disconnect()
        {
            if (_client == null) return;

            try
            {
                lock (_sendGate)
                {
                    try
                    {
                        _client.Close();
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }

            TimeConnected = 0;
            Connected = false;
            _sendList = null;
            _client = null;

            _receiveList = null;
            _connectedTick = 0;
            _lastReceiveTick = 0;
            _lastSendTick = 0;

            EnqueuePacketTraceLine($"[{CMain.Now:yyyy-MM-dd HH:mm:ss.fff}] DISCONNECT");
            FlushPacketTraceIfDue(force: true);
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
                        if (!_receiveList.TryDequeue(out Packet p) || p == null) continue;
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
                if (!_receiveList.TryDequeue(out Packet p) || p == null) continue;

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
                _sendList.Enqueue(new C.KeepAlive());

            if (_sendList != null && !_sendList.IsEmpty)
            {
                TimeOutTime = GetRuntimeTimeMs() + Settings.TimeOut;

                List<byte> data = new List<byte>();
                while (!_sendList.IsEmpty)
                {
                    if (!_sendList.TryDequeue(out Packet p)) continue;
                    IEnumerable<byte> packetBytesEnumerable = p.GetPacketBytes();
                    byte[] packetBytes = packetBytesEnumerable as byte[] ?? packetBytesEnumerable.ToArray();
                    data.AddRange(packetBytes);
                    TracePacket("SEND", p, packetBytes.Length);
                }

                CMain.BytesSent += data.Count;

                BeginSend(data);
            }

            if (_client == null || !_client.Connected)
                return;

            if (!Connected && TimeConnected > 0 && ShouldReconnectHandshake())
            {
                Disconnect();
                Connect();
            }
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
                return;
            }

            _preSendList.Enqueue(p);
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
                if (p == null)
                    continue;

                _sendList.Enqueue(p);
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
