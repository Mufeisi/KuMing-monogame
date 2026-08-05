using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;
using Server.MirEnvir;

namespace Server.MirNetwork
{
    internal static class PacketTraceLogger
    {
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        private static readonly ConcurrentQueue<string> Queue = new ConcurrentQueue<string>();
        private static int _queueCount;
        private static long _nextFlushAtMs;
        private static int _flushInProgress;

        private const int MaxQueuedLines = 50000;
        private const int MaxLinesPerFlush = 2000;
        private const int FlushIntervalMs = 250;

        public static void Trace(string direction, Packet packet, MirConnection connection, int byteLength = 0)
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

            string ip = connection?.IPAddress ?? "?";
            int sessionId = connection?.SessionID ?? 0;
            string stage = connection?.Stage.ToString() ?? "?";
            string accountId = connection?.Account?.AccountID ?? string.Empty;
            string playerName = connection?.Player?.Name ?? string.Empty;

            string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {direction} Session={sessionId} IP={ip} Stage={stage} Acc={accountId} Char={playerName} Id={packet.Index} Type={typeName} Bytes={byteLength}";
            EnqueueLine(line);
        }

        private static void EnqueueLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;

            int count = Interlocked.Increment(ref _queueCount);
            if (count > MaxQueuedLines)
            {
                Interlocked.Decrement(ref _queueCount);
                return;
            }

            Queue.Enqueue(line);
        }

        public static void FlushIfDue(bool force = false)
        {
            if (!Settings.TracePackets)
                return;

            long nowMs = Envir.Main.Time;
            if (!force && nowMs < Volatile.Read(ref _nextFlushAtMs))
                return;

            Volatile.Write(ref _nextFlushAtMs, nowMs + FlushIntervalMs);

            if (Volatile.Read(ref _queueCount) <= 0)
                return;

            if (Interlocked.Exchange(ref _flushInProgress, 1) != 0)
                return;

            try
            {
                string logPath = Settings.TracePacketsLogPath;
                if (string.IsNullOrWhiteSpace(logPath))
                    logPath = @".\Logs\PacketTrace.log";

                string directory = Path.GetDirectoryName(logPath);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);

                TryRotateLogIfNeeded(logPath);

                using var writer = new StreamWriter(logPath, append: true, Utf8NoBom);
                int written = 0;

                while (written < MaxLinesPerFlush && Queue.TryDequeue(out string line))
                {
                    Interlocked.Decrement(ref _queueCount);
                    writer.WriteLine(line);
                    written++;
                }
            }
            catch
            {
            }
            finally
            {
                Interlocked.Exchange(ref _flushInProgress, 0);
            }
        }

        private static void TryRotateLogIfNeeded(string logPath)
        {
            try
            {
                if (!File.Exists(logPath))
                    return;

                int rotateMb = Math.Max(1, Settings.TracePacketsRotateMB);
                long rotateBytes = rotateMb * 1024L * 1024L;

                var info = new FileInfo(logPath);
                if (info.Length < rotateBytes)
                    return;

                string directory = Path.GetDirectoryName(logPath) ?? ".";
                string rotated = Path.Combine(directory, $"PacketTrace.{DateTime.Now:yyyyMMdd-HHmmss}.log");

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
