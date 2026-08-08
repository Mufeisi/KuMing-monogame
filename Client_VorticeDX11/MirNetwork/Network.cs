using System.Collections.Concurrent;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using Client.MirControls;
using C = ClientPackets;
using Shared.Diagnostics;
using Shared.Security;
using Shared.Transport;


namespace Client.MirNetwork
{
    static class Network
    {
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        private const int PacketTraceMaxQueuedLines = 8000;
        private const int PacketTraceMaxLinesPerFlush = 300;
        private const long PacketTraceFlushIntervalMs = 500;
        private const long PacketTraceRotateBytes = 5 * 1024 * 1024;

        private static TcpClient _client;
        private static Stream _stream;
        private static StreamWriteGate _sendGate = new StreamWriteGate();
        private static bool _usingTls;
        private static int _activePort;
        public static int ConnectAttempt = 0;
        public static int MaxAttempts = 20;
        public static bool ErrorShown;
        public static bool Connected;
        public static long TimeOutTime, TimeConnected, RetryTime = CMain.Time + 5000;

        private static ConcurrentQueue<Packet> _receiveList;
        private static ConcurrentQueue<Packet> _sendList;
        private static PerformanceQueueTracker _receiveQueueMetrics = new PerformanceQueueTracker();
        private static PerformanceQueueTracker _sendQueueMetrics = new PerformanceQueueTracker();
        private static PerformanceQueueTracker _networkQueueMetrics = new PerformanceQueueTracker();
        private static readonly ConcurrentQueue<string> _packetTraceQueue = new ConcurrentQueue<string>();
        private static int _packetTraceQueueCount;
        private static long _nextPacketTraceFlushTime;

        static byte[] _rawData = new byte[0];
        static readonly byte[] _rawBytes = new byte[8 * 1024];

        public static void Connect()
        {
            if (_client != null)
                Disconnect();

            _sendGate = new StreamWriteGate();

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

            if (ConnectAttempt >= MaxAttempts)
            {
                if (ErrorShown)
                {
                    return;
                }

                ErrorShown = true;

                MirMessageBox errorBox = new("连接到服务器时出错", MirMessageBoxButtons.Cancel);
                errorBox.CancelButton.Click += (o, e) => Program.Form.Close();
                errorBox.Label.Text = $"已达最大连接尝试次数： {MaxAttempts}" +
                                      $"{Environment.NewLine}请稍后再试或检查您的连接设置";
                errorBox.Show();
                return;
            }

            ConnectAttempt++;

            try
            {
                _usingTls = Settings.UseTlsV2;
                _activePort = _usingTls ? Settings.TlsPort : Settings.Port;
                _client = new TcpClient { NoDelay = true };
                EnqueuePacketTraceLine($"[{CMain.Now:yyyy-MM-dd HH:mm:ss.fff}] CONNECT Attempt={ConnectAttempt} Host={Settings.IPAddress}:{_activePort}");
                _client?.BeginConnect(Settings.IPAddress, _activePort, Connection, null);
            }
            catch (ObjectDisposedException ex)
            {
                if (Settings.LogErrors) CMain.SaveError(ex.ToString());
                Disconnect();
            }
        }

        private static void Connection(IAsyncResult result)
        {
            try
            {
                _client?.EndConnect(result);

                if ((_client != null &&
                    !_client.Connected) ||
                    _client == null)
                {
                    Connect();
                    return;
                }

                _stream = _client.GetStream();
                if (_usingTls)
                {
                    var ssl = new SslStream(_stream, leaveInnerStreamOpen: false);
                    _stream = ssl;
                    ssl.AuthenticateAsClient(TlsClientPolicy.CreateOptions(Settings.TlsServerName));
                }

                _receiveList = new ConcurrentQueue<Packet>();
                _sendList = new ConcurrentQueue<Packet>();
                _receiveQueueMetrics = new PerformanceQueueTracker();
                _sendQueueMetrics = new PerformanceQueueTracker();
                _networkQueueMetrics = new PerformanceQueueTracker();
                _rawData = new byte[0];

                TimeOutTime = CMain.Time + Settings.TimeOut;
                TimeConnected = CMain.Time;

                EnqueuePacketTraceLine($"[{CMain.Now:yyyy-MM-dd HH:mm:ss.fff}] CONNECTED Host={Settings.IPAddress}:{_activePort}");
                BeginReceive();
            }
            catch (SocketException)
            {
                EnqueuePacketTraceLine($"[{CMain.Now:yyyy-MM-dd HH:mm:ss.fff}] CONNECT SocketException");
                Thread.Sleep(100);
                Connect();
            }
            catch (Exception ex)
            {
                if (Settings.LogErrors)
                    CMain.SaveError(_usingTls
                        ? TlsClientPolicy.FormatFailure(ex, Settings.IPAddress, _activePort)
                        : ex.ToString());
                Disconnect();
            }
        }

        private static void BeginReceive()
        {
            if (_stream == null || _client == null || !_client.Connected) return;

            try
            {
                _stream.BeginRead(_rawBytes, 0, _rawBytes.Length, ReceiveData, _rawBytes);
            }
            catch
            {
                Disconnect();
            }
        }
        private static void ReceiveData(IAsyncResult result)
        {
            if (_stream == null || _client == null || !_client.Connected) return;

            int dataRead;

            try
            {
                dataRead = _stream.EndRead(result);
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
                _receiveQueueMetrics.Enqueue();
                _networkQueueMetrics.Enqueue();
                IEnumerable<byte> packetBytesEnumerable = p.GetPacketBytes();
                byte[] packetBytes = packetBytesEnumerable as byte[] ?? packetBytesEnumerable.ToArray();
                data.AddRange(packetBytes);
                TracePacket("RECV", p, packetBytes.Length);
            }

            CMain.BytesReceived += data.Count;

            BeginReceive();
        }

        private static bool BeginSend(List<byte> data, bool gateHeld = false)
        {
            if (_stream == null || _client == null || !_client.Connected || data.Count == 0)
            {
                if (gateHeld) _sendGate.Complete();
                return false;
            }
            if (!gateHeld && !_sendGate.TryEnter()) return false;

            try
            {
                Stream stream = _stream;
                stream.BeginWrite(data.ToArray(), 0, data.Count, SendData, (stream, _sendGate));
                return true;
            }
            catch
            {
                _sendGate.Complete();
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
            if (_client == null && _receiveList == null && _sendList == null) return;

            _networkQueueMetrics.Dequeue(
                Math.Max(0, _receiveQueueMetrics.Depth) + Math.Max(0, _sendQueueMetrics.Depth));

            _sendGate.Dispose();
            _stream?.Dispose();
            _stream = null;
            _client?.Close();

            TimeConnected = 0;
            Connected = false;
            _sendList = null;
            _client = null;
            _usingTls = false;
            _activePort = 0;

            _receiveList = null;

            EnqueuePacketTraceLine($"[{CMain.Now:yyyy-MM-dd HH:mm:ss.fff}] DISCONNECT");
            FlushPacketTraceIfDue(force: true);
        }

        public static void Process()
        {
            FlushPacketTraceIfDue();

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

                    MirMessageBox.Show("与服务器的连接中断", true);
                    Disconnect();
                    return;
                }
                else if (CMain.Time >= RetryTime)
                {
                    RetryTime = CMain.Time + 5000;
                    Connect();
                }
                return;
            }

            if (!Connected && TimeConnected > 0 && CMain.Time > TimeConnected + 5000)
            {
                Disconnect();
                Connect();
                return;
            }



            while (_receiveList != null && !_receiveList.IsEmpty)
            {
                if (!_receiveList.TryDequeue(out Packet p)) continue;
                _receiveQueueMetrics.Dequeue();
                _networkQueueMetrics.Dequeue();
                if (p == null) continue;
                if (MirScene.ActiveScene == null)
                {
                    Client.Utils.ResolutionTrace.Log("Network.Process", $"ActiveScene=null, drop packet={p.GetType().Name}");
                    continue;
                }
                MirScene.ActiveScene.ProcessPacket(p);
            }


            if (CMain.Time > TimeOutTime && _sendList != null && _sendList.IsEmpty)
            {
                _sendList.Enqueue(new C.KeepAlive());
                _sendQueueMetrics.Enqueue();
                _networkQueueMetrics.Enqueue();
            }

            if (_sendList == null || _sendList.IsEmpty) return;

            if (!_sendGate.TryEnter()) return;

            TimeOutTime = CMain.Time + Settings.TimeOut;

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
        
        public static void Enqueue(Packet p)
        {
            if (_sendList != null && p != null)
            {
                _sendList.Enqueue(p);
                _sendQueueMetrics.Enqueue();
                _networkQueueMetrics.Enqueue();
            }
        }

        public static void RecordPerformanceQueueMetrics()
        {
            if (!PerformanceMetrics.Enabled) return;

            var receive = _receiveQueueMetrics.Depth;
            var send = _sendQueueMetrics.Depth;
            PerformanceMetrics.SetGauge(PerformanceMetricKind.NetworkInQueue, receive);
            PerformanceMetrics.SetGauge(PerformanceMetricKind.NetworkOutQueue, send);
            PerformanceMetrics.SetGauge(PerformanceMetricKind.NetworkQueue, receive + send);

            var receiveHighWater = _receiveQueueMetrics.CaptureHighWater();
            var sendHighWater = _sendQueueMetrics.CaptureHighWater();
            PerformanceMetrics.SetGauge(PerformanceMetricKind.NetworkInQueueHighWater, receiveHighWater);
            PerformanceMetrics.SetGauge(PerformanceMetricKind.NetworkOutQueueHighWater, sendHighWater);
            PerformanceMetrics.SetGauge(PerformanceMetricKind.NetworkQueueHighWater, _networkQueueMetrics.CaptureHighWater());
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

            string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ClientPacketTrace.log");

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(logPath) ?? AppDomain.CurrentDomain.BaseDirectory);
                TryRotatePacketTraceLog(logPath);

                using var writer = new StreamWriter(logPath, append: true, Utf8NoBom);
                int written = 0;

                while (written < PacketTraceMaxLinesPerFlush && _packetTraceQueue.TryDequeue(out string queued))
                {
                    Interlocked.Decrement(ref _packetTraceQueueCount);
                    writer.WriteLine(queued);
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

                string directory = Path.GetDirectoryName(logPath) ?? AppDomain.CurrentDomain.BaseDirectory;
                string rotated = Path.Combine(directory, $"ClientPacketTrace.{DateTime.Now:yyyyMMdd-HHmmss}.log");

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
