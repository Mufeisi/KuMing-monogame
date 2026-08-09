using System.Text;
using Server;
using Server.MirEnvir;
using Server.Security;
using Xunit;

namespace Base05.Tests;

[Collection("SEC05环境")]
public sealed class ProductionSecurityTests : IDisposable
{
    private readonly string _secretRoot = Path.Combine(
        Path.GetTempPath(), "LyoCrystalProductionSecrets-" + Guid.NewGuid().ToString("N"));
    private readonly IDisposable _secretScope;
    private readonly ProductionSettingsScope _settingsScope = new();

    public ProductionSecurityTests()
    {
        _secretScope = ProtectedSecretStore.UseTestRoot(_secretRoot);
        Settings.TestServer = false;
        Settings.GMPassword = "@123456";
        Settings.TlsEnabled = false;
        Settings.StartHTTPService = false;
        Settings.DatabaseProvider = "Sqlite";
        Settings.SqliteBackupEnabled = true;
        Settings.SqliteBackupDirectory = Path.Combine(_secretRoot, "backup-local");
        Settings.SqliteBackupOffsiteDirectory = @"\\backup-server\LyoCrystalTests\SQLite";
        Settings.SqliteBackupIntervalMinutes = 60;
        Settings.SqliteBackupRetentionCount = 48;
        Settings.MicroServerActive = false;
        Settings.AiScriptsEnabled = false;
    }

    public void Dispose()
    {
        _settingsScope.Dispose();
        _secretScope.Dispose();
        if (Directory.Exists(_secretRoot)) Directory.Delete(_secretRoot, true);
    }

    [Fact]
    public void DPAPI受保护存储往返且文件不含明文()
    {
        const string secret = "high-entropy-secret-value-1234567890";
        ProtectedSecretStore.Write(ProtectedSecretStore.GameMasterPassword, secret);

        Assert.Equal(secret, ProtectedSecretStore.Read(ProtectedSecretStore.GameMasterPassword));
        string file = Assert.Single(Directory.GetFiles(_secretRoot, "*.dpapi"));
        byte[] bytes = File.ReadAllBytes(file);
        Assert.DoesNotContain(secret, Encoding.UTF8.GetString(bytes));

        ProtectedSecretStore.Delete(ProtectedSecretStore.GameMasterPassword);
        Assert.Null(ProtectedSecretStore.Read(ProtectedSecretStore.GameMasterPassword));
    }

    [Fact]
    public void 历史INI秘密键可严格删除且保留非秘密配置()
    {
        Directory.CreateDirectory(_secretRoot);
        string path = Path.Combine(_secretRoot, "legacy-setup.ini");
        File.WriteAllText(path,
            "[General]\r\nGMPassword=plain-gm\r\nTestServer=false\r\n" +
            "[Micro]\r\nMicroCode=plain-micro\r\nMicroAuthor=user\r\n" +
            "[AiScripts]\r\nAiScriptsApiKey=plain-ai\r\n" +
            "[Database]\r\nMySqlConnectionString=plain-db\r\nProvider=Sqlite\r\n");
        var reader = new InIReader(path);

        Assert.Equal(1, reader.ClearKeys("General", "GMPassword"));
        Assert.Equal(1, reader.ClearKeys("Micro", "MicroCode"));
        Assert.Equal(1, reader.ClearKeys("AiScripts", "AiScriptsApiKey"));
        Assert.Equal(1, reader.ClearKeys("Database", "MySqlConnectionString"));

        string content = File.ReadAllText(path);
        Assert.DoesNotContain("plain-gm", content);
        Assert.DoesNotContain("plain-micro", content);
        Assert.DoesNotContain("plain-ai", content);
        Assert.DoesNotContain("plain-db", content);
        Assert.Contains("TestServer=false", content);
        Assert.Contains("Provider=Sqlite", content);
    }

    [Fact]
    public void CI短暂导入后立即清除环境变量并应用秘密()
    {
        const string imported = "imported-game-master-password-123456";
        string original = Environment.GetEnvironmentVariable(ProductionSecurityPolicy.ImportGameMasterPassword);
        try
        {
            Environment.SetEnvironmentVariable(ProductionSecurityPolicy.ImportGameMasterPassword, imported);
            ProductionSecurityPolicy.ValidateAndApply();

            Assert.Null(Environment.GetEnvironmentVariable(ProductionSecurityPolicy.ImportGameMasterPassword));
            Assert.Equal(imported, ProtectedSecretStore.Read(ProtectedSecretStore.GameMasterPassword));
            Assert.Equal(imported, Settings.GMPassword);
        }
        finally
        {
            Environment.SetEnvironmentVariable(ProductionSecurityPolicy.ImportGameMasterPassword, original);
        }
    }

    [Fact]
    public void 正式启动策略拒绝默认口令公网明文管理与缺失秘密()
    {
        ProtectedSecretStore.Write(ProtectedSecretStore.GameMasterPassword, "@123456");
        Assert.Throws<InvalidOperationException>(() => ProductionSecurityPolicy.ValidateAndApply());

        ProtectedSecretStore.Write(ProtectedSecretStore.GameMasterPassword, "safe-game-master-password-123456");
        Settings.StartHTTPService = true;
        Settings.HTTPIPAddress = "http://0.0.0.0:7777/";
        Settings.HTTPTrustedIPAddress = "127.0.0.1";
        Assert.Throws<InvalidOperationException>(() => ProductionSecurityPolicy.ValidateAndApply());

        Settings.HTTPIPAddress = "http://127.0.0.1:7777/";
        Assert.Throws<InvalidOperationException>(() => ProductionSecurityPolicy.ValidateAndApply());

        ProtectedSecretStore.Write(ProtectedSecretStore.AdministratorToken, "short");
        Assert.Throws<InvalidOperationException>(() => ProductionSecurityPolicy.ValidateAndApply());

        ProtectedSecretStore.Write(ProtectedSecretStore.AdministratorToken, "administrator-token-at-least-32-characters");
        Settings.StartHTTPService = false;
        Settings.TlsEnabled = true;
        Assert.Throws<InvalidOperationException>(() => ProductionSecurityPolicy.ValidateAndApply());
    }

    [Fact]
    public void 完整受保护配置覆盖运行时明文占位并通过门禁()
    {
        ProtectedSecretStore.Write(ProtectedSecretStore.GameMasterPassword, "safe-game-master-password-123456");
        ProtectedSecretStore.Write(ProtectedSecretStore.TlsCertificatePassword, "tls-password-123456");
        ProtectedSecretStore.Write(ProtectedSecretStore.AdministratorToken, "administrator-token-at-least-32-characters");
        ProtectedSecretStore.Write(ProtectedSecretStore.OperatorToken, "operator-token-at-least-32-characters-long");
        ProtectedSecretStore.Write(ProtectedSecretStore.MySqlConnectionString, "Server=db;User ID=game;Password=secret;Database=mir");
        ProtectedSecretStore.Write(ProtectedSecretStore.MicroCode, "micro-code-1234567890");
        ProtectedSecretStore.Write(ProtectedSecretStore.AiApiKey, "ai-api-key-1234567890");

        Settings.GMPassword = "plain-placeholder";
        Settings.TlsEnabled = true;
        Settings.StartHTTPService = true;
        Settings.HTTPIPAddress = "http://127.0.0.1:7777/";
        Settings.HTTPTrustedIPAddress = "127.0.0.1";
        Settings.DatabaseProvider = "MySql";
        Settings.MySqlConnectionString = "plain-placeholder";
        Settings.MicroServerActive = true;
        Settings.MicroCode = "plain-placeholder";
        Settings.AiScriptsEnabled = true;
        Settings.AiScriptsApiKey = "plain-placeholder";

        ProductionSecurityPolicy.ValidateAndApply();

        Assert.Equal("safe-game-master-password-123456", Settings.GMPassword);
        Assert.Contains("Password=secret", Settings.MySqlConnectionString);
        Assert.Equal("micro-code-1234567890", Settings.MicroCode);
        Assert.Equal("ai-api-key-1234567890", Settings.AiScriptsApiKey);
    }

    [Fact]
    public void Envir在创建工作线程前拒绝正式服弱配置()
    {
        var environment = new Envir();
        var options = new EnvirStartOptions
        {
            EnforceProductionSecurity = true,
            LoadResources = false,
            BindNetwork = false,
            StartScripts = false,
            StartHttp = false,
            SaveOnStop = false,
            Multithreaded = false,
        };

        Assert.Throws<InvalidOperationException>(() => environment.Start(options));
        Assert.False(environment.Running);
    }

    [Fact]
    public void 正式服SQLite拒绝关闭备份或缺少异地目录()
    {
        ProtectedSecretStore.Write(ProtectedSecretStore.GameMasterPassword, "safe-game-master-password-123456");

        Settings.SqliteBackupEnabled = false;
        Assert.Throws<InvalidOperationException>(() => ProductionSecurityPolicy.ValidateAndApply());

        Settings.SqliteBackupEnabled = true;
        Settings.SqliteBackupOffsiteDirectory = string.Empty;
        Assert.Throws<InvalidOperationException>(() => ProductionSecurityPolicy.ValidateAndApply());

        Settings.SqliteBackupOffsiteDirectory = @"\\backup-server\LyoCrystalTests\SQLite";
        ProductionSecurityPolicy.ValidateAndApply();
    }

    private sealed class ProductionSettingsScope : IDisposable
    {
        private readonly bool _testServer = Settings.TestServer;
        private readonly string _gmPassword = Settings.GMPassword;
        private readonly bool _tlsEnabled = Settings.TlsEnabled;
        private readonly bool _startHttp = Settings.StartHTTPService;
        private readonly string _httpAddress = Settings.HTTPIPAddress;
        private readonly string _httpTrusted = Settings.HTTPTrustedIPAddress;
        private readonly string _provider = Settings.DatabaseProvider;
        private readonly string _mySql = Settings.MySqlConnectionString;
        private readonly bool _sqliteBackupEnabled = Settings.SqliteBackupEnabled;
        private readonly string _sqliteBackupDirectory = Settings.SqliteBackupDirectory;
        private readonly string _sqliteBackupOffsiteDirectory = Settings.SqliteBackupOffsiteDirectory;
        private readonly int _sqliteBackupIntervalMinutes = Settings.SqliteBackupIntervalMinutes;
        private readonly int _sqliteBackupRetentionCount = Settings.SqliteBackupRetentionCount;
        private readonly bool _microActive = Settings.MicroServerActive;
        private readonly string _microCode = Settings.MicroCode;
        private readonly bool _aiEnabled = Settings.AiScriptsEnabled;
        private readonly string _aiKey = Settings.AiScriptsApiKey;

        public void Dispose()
        {
            Settings.TestServer = _testServer;
            Settings.GMPassword = _gmPassword;
            Settings.TlsEnabled = _tlsEnabled;
            Settings.StartHTTPService = _startHttp;
            Settings.HTTPIPAddress = _httpAddress;
            Settings.HTTPTrustedIPAddress = _httpTrusted;
            Settings.DatabaseProvider = _provider;
            Settings.MySqlConnectionString = _mySql;
            Settings.SqliteBackupEnabled = _sqliteBackupEnabled;
            Settings.SqliteBackupDirectory = _sqliteBackupDirectory;
            Settings.SqliteBackupOffsiteDirectory = _sqliteBackupOffsiteDirectory;
            Settings.SqliteBackupIntervalMinutes = _sqliteBackupIntervalMinutes;
            Settings.SqliteBackupRetentionCount = _sqliteBackupRetentionCount;
            Settings.MicroServerActive = _microActive;
            Settings.MicroCode = _microCode;
            Settings.AiScriptsEnabled = _aiEnabled;
            Settings.AiScriptsApiKey = _aiKey;
        }
    }
}

[CollectionDefinition("SEC05环境", DisableParallelization = true)]
public sealed class ProductionSecurityCollection
{
}
