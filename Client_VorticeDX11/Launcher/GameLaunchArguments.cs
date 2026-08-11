using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace Launcher.Remote
{
    public sealed class GameLaunchOptions
    {
        public string ServerAddress { get; }
        public int ServerPort { get; }
        public bool MicroEnabled { get; }
        public string MicroAddress { get; }
        public int MicroPort { get; }
        public string MicroBackupAddress { get; }
        public int MicroBackupPort { get; }

        public GameLaunchOptions(string serverAddress, int serverPort, bool microEnabled, string microAddress, int microPort, string microBackupAddress = "", int microBackupPort = 0)
        {
            ServerAddress = serverAddress;
            ServerPort = serverPort;
            MicroEnabled = microEnabled;
            MicroAddress = microAddress;
            MicroPort = microPort;
            MicroBackupAddress = microBackupAddress;
            MicroBackupPort = microBackupPort;
        }
    }

    public static class GameLaunchArguments
    {
        private const string ChildMode = "--game-instance";

        public static string[] Create(ServerEntry server)
        {
            if (server == null) throw new ArgumentNullException(nameof(server));
            return new[]
            {
                ChildMode,
                "--server-address", server.ServerAddress,
                "--server-port", server.ServerPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "--micro-enabled", server.MicroEnabled ? "true" : "false",
                "--micro-address", server.MicroAddress,
                "--micro-port", server.MicroPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
            };
        }

        public static bool TryParse(IReadOnlyList<string> arguments, out GameLaunchOptions options)
        {
            options = null;
            if (arguments == null) return false;
            string[] launchArguments = arguments
                .Where(argument => !string.Equals(argument, "-tc", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (launchArguments.Length is not (11 or 15) ||
                !string.Equals(launchArguments[0], ChildMode, StringComparison.OrdinalIgnoreCase)) return false;

            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int index = 1; index < launchArguments.Length; index += 2)
            {
                if (!launchArguments[index].StartsWith("--", StringComparison.Ordinal) || !values.TryAdd(launchArguments[index], launchArguments[index + 1] ?? string.Empty))
                    return false;
            }

            if (!values.TryGetValue("--server-address", out string address) || string.IsNullOrWhiteSpace(address) ||
                !values.TryGetValue("--server-port", out string portText) || !int.TryParse(portText, out int port) || port is < 1 or > 65535 ||
                !values.TryGetValue("--micro-enabled", out string microText) || !bool.TryParse(microText, out bool microEnabled) ||
                !values.TryGetValue("--micro-address", out string microAddress) ||
                !values.TryGetValue("--micro-port", out string microPortText) || !int.TryParse(microPortText, out int microPort) || microPort is < 0 or > 65535)
                return false;

            string microBackupAddress = string.Empty;
            int microBackupPort = 0;
            if (launchArguments.Length == 15 &&
                (!values.TryGetValue("--micro-backup-address", out microBackupAddress) ||
                 !values.TryGetValue("--micro-backup-port", out string backupPortText) ||
                 !int.TryParse(backupPortText, out microBackupPort) || microBackupPort is < 0 or > 65535))
                return false;

            address = address.Trim();
            microAddress = microAddress.Trim();
            microBackupAddress = microBackupAddress.Trim();
            if (!IsValidHost(address) || (microEnabled && !IsValidHost(microAddress)) ||
                (microBackupAddress.Length > 0 && !IsValidHost(microBackupAddress))) return false;
            if ((microEnabled && (microAddress.Length == 0 || microPort == 0)) || (!microEnabled && (microAddress.Length != 0 || microPort != 0)))
                return false;
            if ((microBackupAddress.Length == 0) != (microBackupPort == 0) || (!microEnabled && microBackupAddress.Length > 0)) return false;

            options = new GameLaunchOptions(address, port, microEnabled, microAddress, microPort, microBackupAddress, microBackupPort);
            return true;
        }

        private static bool IsValidHost(string value)
        {
            if (value.Length == 0 || value.Contains('/') || value.Contains('\\') || value.Contains("://", StringComparison.Ordinal))
                return false;
            UriHostNameType hostType = Uri.CheckHostName(value);
            return hostType is not (UriHostNameType.Unknown or UriHostNameType.Basic) || IPAddress.TryParse(value, out _);
        }
    }

    public sealed class GameInstanceLimit
    {
        private readonly int _maximum;
        private int _activeCount;

        public int ActiveCount => System.Threading.Volatile.Read(ref _activeCount);

        public GameInstanceLimit(int maximum)
        {
            if (maximum is < 1 or > 10) throw new ArgumentOutOfRangeException(nameof(maximum));
            _maximum = maximum;
        }

        public bool TryAcquire()
        {
            while (true)
            {
                int current = ActiveCount;
                if (current >= _maximum) return false;
                if (System.Threading.Interlocked.CompareExchange(ref _activeCount, current + 1, current) == current) return true;
            }
        }

        public void Release()
        {
            while (true)
            {
                int current = ActiveCount;
                if (current == 0) return;
                if (System.Threading.Interlocked.CompareExchange(ref _activeCount, current - 1, current) == current) return;
            }
        }
    }
}
