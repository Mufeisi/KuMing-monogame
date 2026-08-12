using System.Security.Cryptography;
using System.Text;

namespace Server.Security;

internal static class ProtectedSecretStore
{
    internal const string TlsCertificatePassword = "tls-certificate-password";
    internal const string AdministratorToken = "administrator-token";
    internal const string OperatorToken = "operator-token";
    internal const string GameMasterPassword = "game-master-password";
    internal const string MySqlConnectionString = "mysql-connection-string";
    internal const string MicroCode = "micro-code";
    internal const string AiApiKey = "ai-api-key";
    private const int MaxSecretCharacters = 16384;
    private static readonly object Sync = new();
    private static string _rootOverride;

    private static string RootPath => _rootOverride ?? Path.Combine(Settings.ConfigPath, "ProtectedSecrets");

    internal static void Write(string name, string secret)
    {
        ValidateName(name);
        if (string.IsNullOrEmpty(secret) || secret.Length > MaxSecretCharacters)
            throw new ArgumentException("受保护秘密必须为 1 到 16384 个字符", nameof(secret));
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("服务端受保护秘密存储要求 Windows DPAPI");

        byte[] plain = Encoding.UTF8.GetBytes(secret);
        byte[] protectedBytes = null;
        try
        {
            protectedBytes = ProtectedData.Protect(plain, Entropy(name), DataProtectionScope.CurrentUser);
            lock (Sync)
            {
                Directory.CreateDirectory(RootPath);
                string path = GetPath(name);
                string temporaryPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
                try
                {
                    File.WriteAllBytes(temporaryPath, protectedBytes);
                    File.Move(temporaryPath, path, true);
                }
                finally
                {
                    if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
                }
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
            if (protectedBytes != null) CryptographicOperations.ZeroMemory(protectedBytes);
        }
    }

    internal static string Read(string name)
    {
        ValidateName(name);
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("服务端受保护秘密存储要求 Windows DPAPI");
        byte[] protectedBytes;
        lock (Sync)
        {
            string path = GetPath(name);
            if (!File.Exists(path)) return null;
            protectedBytes = File.ReadAllBytes(path);
        }

        byte[] plain = null;
        try
        {
            plain = ProtectedData.Unprotect(protectedBytes, Entropy(name), DataProtectionScope.CurrentUser);
            return new UTF8Encoding(false, true).GetString(plain);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(protectedBytes);
            if (plain != null) CryptographicOperations.ZeroMemory(plain);
        }
    }

    internal static bool ImportAndClearEnvironment(string name, string environmentVariable)
    {
        string value = Environment.GetEnvironmentVariable(environmentVariable);
        if (string.IsNullOrEmpty(value)) return false;
        try
        {
            Write(name, value);
            return true;
        }
        finally
        {
            Environment.SetEnvironmentVariable(environmentVariable, null);
        }
    }

    internal static void Delete(string name)
    {
        ValidateName(name);
        lock (Sync)
        {
            string path = GetPath(name);
            if (File.Exists(path)) File.Delete(path);
        }
    }

    internal static IDisposable UseTestRoot(string root)
    {
        if (string.IsNullOrWhiteSpace(root)) throw new ArgumentException("测试秘密目录不能为空", nameof(root));
        lock (Sync)
        {
            if (_rootOverride != null) throw new InvalidOperationException("测试秘密目录已被占用");
            _rootOverride = Path.GetFullPath(root);
            return new TestRootScope();
        }
    }

    private static string GetPath(string name) => Path.Combine(RootPath, name + ".dpapi");

    private static byte[] Entropy(string name) =>
        SHA256.HashData(Encoding.UTF8.GetBytes("LyoCrystal.ProtectedSecret.v1:" + name));

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 64 ||
            name.Any(character => !(char.IsAsciiLetterOrDigit(character) || character == '-')))
            throw new ArgumentException("受保护秘密名称无效", nameof(name));
    }

    private sealed class TestRootScope : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            lock (Sync) _rootOverride = null;
            _disposed = true;
        }
    }
}
