using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;

namespace Launcher.Remote
{
    public sealed class RemoteLaunchManifest
    {
        private static readonly HashSet<string> RootProperties = new(StringComparer.Ordinal)
        {
            "version", "maxInstances", "patchUrl", "servers",
        };

        private static readonly HashSet<string> ServerProperties = new(StringComparer.Ordinal)
        {
            "name", "serverAddress", "serverPort", "microEnabled", "microAddress", "microPort",
        };

        public int Version { get; }
        public int MaxInstances { get; }
        public string PatchUrl { get; }
        public IReadOnlyList<ServerEntry> Servers { get; }

        private RemoteLaunchManifest(int version, int maxInstances, string patchUrl, IReadOnlyList<ServerEntry> servers)
        {
            Version = version;
            MaxInstances = maxInstances;
            PatchUrl = patchUrl;
            Servers = servers;
        }

        public static RemoteLaunchManifest CreateLocalFallback(string name, string serverAddress, int serverPort, string patchUrl, string microBaseUrl)
        {
            string normalizedName = string.IsNullOrWhiteSpace(name) ? "默认区服" : name.Trim();
            ValidateHost((serverAddress ?? string.Empty).Trim(), "本地 serverAddress");
            if (serverPort is < 1 or > 65535) throw new InvalidLaunchManifestException("本地 serverPort 必须在 1 到 65535 之间");

            bool microEnabled = Uri.TryCreate((microBaseUrl ?? string.Empty).Trim(), UriKind.Absolute, out Uri microUri)
                                && microUri.Scheme == Uri.UriSchemeHttp && microUri.Port is >= 1 and <= 65535;
            string microAddress = microEnabled ? microUri.Host : string.Empty;
            int microPort = microEnabled ? microUri.Port : 0;
            return new RemoteLaunchManifest(1, 1, NormalizePatchUrl((patchUrl ?? string.Empty).Trim()),
                new[] { new ServerEntry(normalizedName, serverAddress.Trim(), serverPort, microEnabled, microAddress, microPort) });
        }

        internal static RemoteLaunchManifest CreateTrustedLocal(int maxInstances, string patchUrl, IReadOnlyList<ServerEntry> servers)
        {
            if (maxInstances is < 1 or > 10 || servers == null || servers.Count is < 1 or > 100) throw new InvalidLaunchManifestException("本地区服列表参数无效");
            return new RemoteLaunchManifest(1, maxInstances, NormalizePatchUrl((patchUrl ?? string.Empty).Trim()), servers.ToArray());
        }

        public static RemoteLaunchManifest ParseAndValidate(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new InvalidLaunchManifestException("远程启动清单为空");

            try
            {
                using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 16,
                });

                JsonElement root = document.RootElement;
                RequireObject(root, "$", RootProperties);

                int version = ReadRequiredInt32(root, "version", "$", 1, 1);
                int maxInstances = ReadRequiredInt32(root, "maxInstances", "$", 1, 10);
                string patchUrl = NormalizePatchUrl(ReadRequiredString(root, "patchUrl", "$", allowEmpty: true));
                JsonElement serversElement = ReadRequiredProperty(root, "servers", "$", JsonValueKind.Array);
                int serverCount = serversElement.GetArrayLength();
                if (serverCount is < 1 or > 100)
                    throw new InvalidLaunchManifestException("$.servers 数量必须在 1 到 100 之间");

                var servers = new List<ServerEntry>(serverCount);
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                int index = 0;
                foreach (JsonElement element in serversElement.EnumerateArray())
                {
                    string path = $"$.servers[{index}]";
                    RequireObject(element, path, ServerProperties);
                    string name = ReadRequiredString(element, "name", path, allowEmpty: false).Trim();
                    if (name.Length is < 1 or > 64)
                        throw new InvalidLaunchManifestException($"{path}.name 长度必须在 1 到 64 之间");
                    if (!names.Add(name))
                        throw new InvalidLaunchManifestException($"{path}.name 与其他区服名称重复");

                    string serverAddress = ReadRequiredString(element, "serverAddress", path, allowEmpty: false).Trim();
                    ValidateHost(serverAddress, $"{path}.serverAddress");
                    int serverPort = ReadRequiredInt32(element, "serverPort", path, 1, 65535);
                    bool microEnabled = ReadRequiredBoolean(element, "microEnabled", path);
                    string microAddress = ReadRequiredString(element, "microAddress", path, allowEmpty: true).Trim();
                    int microPort = ReadRequiredInt32(element, "microPort", path, 0, 65535);

                    if (microEnabled)
                    {
                        ValidateHost(microAddress, $"{path}.microAddress");
                        if (microPort < 1)
                            throw new InvalidLaunchManifestException($"{path}.microPort 必须在 1 到 65535 之间");
                    }
                    else if (microAddress.Length != 0 || microPort != 0)
                    {
                        throw new InvalidLaunchManifestException($"{path} 关闭微端时地址必须为空且端口必须为 0");
                    }

                    servers.Add(new ServerEntry(name, serverAddress, serverPort, microEnabled, microAddress, microPort));
                    index++;
                }

                return new RemoteLaunchManifest(version, maxInstances, patchUrl, servers);
            }
            catch (InvalidLaunchManifestException)
            {
                throw;
            }
            catch (JsonException ex)
            {
                throw new InvalidLaunchManifestException("远程启动清单不是有效 JSON", ex);
            }
        }

        private static void RequireObject(JsonElement element, string path, HashSet<string> allowedProperties)
        {
            if (element.ValueKind != JsonValueKind.Object)
                throw new InvalidLaunchManifestException($"{path} 必须是对象");

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (!seen.Add(property.Name))
                    throw new InvalidLaunchManifestException($"{path}.{property.Name} 重复定义");
                if (!allowedProperties.Contains(property.Name))
                    throw new InvalidLaunchManifestException($"{path}.{property.Name} 是未知字段");
            }

            foreach (string property in allowedProperties)
            {
                if (!seen.Contains(property))
                    throw new InvalidLaunchManifestException($"{path}.{property} 缺失");
            }
        }

        private static JsonElement ReadRequiredProperty(JsonElement element, string name, string path, JsonValueKind kind)
        {
            JsonElement value = element.GetProperty(name);
            if (value.ValueKind != kind)
                throw new InvalidLaunchManifestException($"{path}.{name} 类型错误");
            return value;
        }

        private static string ReadRequiredString(JsonElement element, string name, string path, bool allowEmpty)
        {
            string value = ReadRequiredProperty(element, name, path, JsonValueKind.String).GetString() ?? string.Empty;
            value = value.Trim();
            if (!allowEmpty && value.Length == 0)
                throw new InvalidLaunchManifestException($"{path}.{name} 不能为空");
            return value;
        }

        private static int ReadRequiredInt32(JsonElement element, string name, string path, int minimum, int maximum)
        {
            JsonElement value = ReadRequiredProperty(element, name, path, JsonValueKind.Number);
            if (!value.TryGetInt32(out int result) || result < minimum || result > maximum)
                throw new InvalidLaunchManifestException($"{path}.{name} 必须在 {minimum} 到 {maximum} 之间");
            return result;
        }

        private static bool ReadRequiredBoolean(JsonElement element, string name, string path)
        {
            JsonElement value = element.GetProperty(name);
            if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw new InvalidLaunchManifestException($"{path}.{name} 类型错误");
            return value.GetBoolean();
        }

        private static string NormalizePatchUrl(string value)
        {
            if (value.Length == 0) return string.Empty;
            if (!Uri.TryCreate(value, UriKind.Absolute, out Uri uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                || string.IsNullOrWhiteSpace(uri.Host)
                || !string.IsNullOrEmpty(uri.UserInfo)
                || !string.IsNullOrEmpty(uri.Query)
                || !string.IsNullOrEmpty(uri.Fragment))
            {
                throw new InvalidLaunchManifestException("$.patchUrl 必须是无凭据、查询和片段的 HTTP/HTTPS 绝对地址");
            }

            string normalized = uri.AbsoluteUri;
            return normalized.EndsWith("/", StringComparison.Ordinal) ? normalized : normalized + "/";
        }

        private static void ValidateHost(string value, string path)
        {
            if (value.Length == 0)
                throw new InvalidLaunchManifestException($"{path} 不能为空");
            if (value.Contains('/') || value.Contains('\\') || value.Contains("://", StringComparison.Ordinal))
                throw new InvalidLaunchManifestException($"{path} 只能填写 IP 或 DNS 主机名");

            UriHostNameType hostType = Uri.CheckHostName(value);
            if (hostType is UriHostNameType.Unknown or UriHostNameType.Basic)
            {
                if (!IPAddress.TryParse(value, out _))
                    throw new InvalidLaunchManifestException($"{path} 不是有效 IP 或 DNS 主机名");
            }
        }
    }

    public sealed class ServerEntry
    {
        public string Name { get; }
        public string ServerAddress { get; }
        public int ServerPort { get; }
        public bool MicroEnabled { get; }
        public string MicroAddress { get; }
        public int MicroPort { get; }
        public string MicroBackupAddress { get; }
        public int MicroBackupPort { get; }
        public string MicroUser { get; }

        public ServerEntry(string name, string serverAddress, int serverPort, bool microEnabled, string microAddress, int microPort, string microBackupAddress = "", int microBackupPort = 0, string microUser = "")
        {
            Name = name;
            ServerAddress = serverAddress;
            ServerPort = serverPort;
            MicroEnabled = microEnabled;
            MicroAddress = microAddress;
            MicroPort = microPort;
            MicroBackupAddress = microBackupAddress ?? string.Empty;
            MicroBackupPort = microBackupPort;
            MicroUser = microUser ?? string.Empty;
        }

        public string BuildMicroBaseUrl()
        {
            if (!MicroEnabled) return string.Empty;
            return new UriBuilder(Uri.UriSchemeHttp, MicroAddress, MicroPort, "api/").Uri.AbsoluteUri;
        }

        public override string ToString() => Name;
    }

    public sealed class InvalidLaunchManifestException : Exception
    {
        public InvalidLaunchManifestException(string message) : base(message) { }
        public InvalidLaunchManifestException(string message, Exception innerException) : base(message, innerException) { }
    }
}
