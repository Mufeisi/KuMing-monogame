using System.Text.Json;

namespace Server.Persistence.Sql
{
    public sealed class SqliteDatabaseLayout
    {
        public const string ActivationFileName = "database-layout.json";

        public string RootDirectory { get; }
        public string IdentityPath { get; }
        public string CharacterPath { get; }
        public string WorldPath { get; }
        public string MigrationId { get; }

        private SqliteDatabaseLayout(string rootDirectory, string identityPath, string characterPath, string worldPath, string migrationId)
        {
            RootDirectory = rootDirectory;
            IdentityPath = identityPath;
            CharacterPath = characterPath;
            WorldPath = worldPath;
            MigrationId = migrationId ?? string.Empty;
        }

        public static SqliteDatabaseLayout Resolve(string rootDirectory)
        {
            var root = Path.GetFullPath(string.IsNullOrWhiteSpace(rootDirectory) ? ".\\Data" : rootDirectory);
            var activationPath = Path.Combine(root, ActivationFileName);
            if (!File.Exists(activationPath))
            {
                return new SqliteDatabaseLayout(
                    root,
                    Path.Combine(root, "identity.db"),
                    Path.Combine(root, "characters.db"),
                    Path.Combine(root, "world.db"),
                    "bootstrap");
            }

            var manifest = JsonSerializer.Deserialize<SqliteActivationManifest>(File.ReadAllText(activationPath));
            if (manifest == null || !manifest.Completed || string.IsNullOrWhiteSpace(manifest.GenerationDirectory))
                throw new InvalidOperationException($"SQLite 激活清单不完整，拒绝启动：{activationPath}");

            var generationRoot = Path.GetFullPath(Path.Combine(root, manifest.GenerationDirectory));
            if (!generationRoot.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("SQLite 激活清单的 generation 路径越界。");

            var identity = Path.Combine(generationRoot, "identity.db");
            var characters = Path.Combine(generationRoot, "characters.db");
            var world = Path.Combine(generationRoot, "world.db");
            if (!File.Exists(identity) || !File.Exists(characters) || !File.Exists(world))
                throw new InvalidOperationException("SQLite 激活清单引用的三库文件不完整，拒绝启动。");

            return new SqliteDatabaseLayout(root, identity, characters, world, manifest.MigrationId);
        }
    }

    public sealed class SqliteActivationManifest
    {
        public string MigrationId { get; set; } = string.Empty;
        public string SourceSha256 { get; set; } = string.Empty;
        public string GenerationDirectory { get; set; } = string.Empty;
        public bool Completed { get; set; }
        public long ActivatedUtcMs { get; set; }
    }
}
