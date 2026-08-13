using Server.Authoring;
using Server.Diagnostics;
using Server.MirDatabase;
using Server.MirEnvir;
using Server.MirForms.VisualMapInfo.Class;
using Server.Scripting;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Xunit;
using Xunit.Abstractions;

namespace Server.ContentAuthoringIntegration;

[CollectionDefinition(CollectionName, DisableParallelization = true)]
public sealed class RealContentSmokeCollection
{
    public const string CollectionName = "真实内容外部测试服";
}

[Collection(RealContentSmokeCollection.CollectionName)]
public sealed class RealContentSmokeTests
{
    private const string EnabledVariable = "LYOCRYSTAL_CONTENT06_REAL_SMOKE";
    private readonly ITestOutputHelper _output;

    public RealContentSmokeTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void 受控备份可建立隔离副本且源数据库保持不变()
    {
        string root = Path.Combine(Path.GetTempPath(), "LyoCrystal-CONTENT06-" + Guid.NewGuid().ToString("N"));
        string data = Path.Combine(root, "Data");
        string backup = Path.Combine(root, "Backups", "case");
        Directory.CreateDirectory(data);
        Directory.CreateDirectory(backup);
        Directory.CreateDirectory(Path.Combine(root, "Configs"));
        File.WriteAllText(Path.Combine(root, "Configs", "Setup.ini"), "[Database]\nProvider=Sqlite\nSqlitePath=.\\Data\\server.db\n");
        var previous = new Dictionary<string, string?>();
        string? previousBackupRoot = Environment.GetEnvironmentVariable("LYOCRYSTAL_CONTENT06_BACKUP_ROOT");
        try
        {
            Environment.SetEnvironmentVariable("LYOCRYSTAL_CONTENT06_BACKUP_ROOT", backup);
            foreach ((string fileName, byte[] bytes) in new[]
                     {
                         ("server.db", new byte[] { 1, 2, 3 }),
                         ("server.db-wal", new byte[] { 4, 5 }),
                         ("server.db-shm", new byte[] { 6 }),
                     })
            {
                File.WriteAllBytes(Path.Combine(data, fileName), bytes);
                File.WriteAllBytes(Path.Combine(backup, fileName), bytes);
                string variable = BackupSet.HashVariable(fileName);
                previous[variable] = Environment.GetEnvironmentVariable(variable);
                Environment.SetEnvironmentVariable(variable, Convert.ToHexString(SHA256.HashData(bytes)));
            }

            BackupSet set = BackupSet.Verify(root);
            using IsolatedServerWorkspace workspace = IsolatedServerWorkspace.Create(root, set);
            File.WriteAllBytes(Path.Combine(workspace.Root, "Data", "server.db"), [9, 9, 9]);
            set.VerifyCurrentSource();
            Assert.Equal([1, 2, 3], File.ReadAllBytes(Path.Combine(data, "server.db")));
        }
        finally
        {
            foreach ((string name, string? value) in previous)
                Environment.SetEnvironmentVariable(name, value);
            Environment.SetEnvironmentVariable("LYOCRYSTAL_CONTENT06_BACKUP_ROOT", previousBackupRoot);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("..\\outside.map")]
    [InlineData("C:\\outside.map")]
    public void 隔离副本拒绝越界内容路径(string unsafePath)
    {
        string root = Path.Combine(Path.GetTempPath(), "LyoCrystal-CONTENT06-path-" + Guid.NewGuid().ToString("N"));
        string source = Path.Combine(root, "source");
        string isolated = Path.Combine(root, "isolated");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(isolated);
        try
        {
            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                IsolatedServerWorkspace.CopyRelativeForTest(source, isolated, unsafePath));
            Assert.Contains("路径", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void 真实地图刷怪和Npc可完成差异校验保存重载及恢复验证()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable(EnabledVariable), "1", StringComparison.Ordinal))
        {
            Assert.Equal("apply", NormalizeMode("apply"));
            Assert.Equal("verify-restored", NormalizeMode("verify-restored"));
            return;
        }

        string sourceRoot = RequireDirectory("LYOCRYSTAL_CONTENT06_REAL_ROOT");
        string mode = NormalizeMode(RequireValue("LYOCRYSTAL_CONTENT06_MODE"));
        BackupSet? backup = mode == "apply" ? BackupSet.Verify(sourceRoot) : null;
        using IsolatedServerWorkspace? workspace = mode == "apply" ? IsolatedServerWorkspace.Create(sourceRoot, backup!) : null;
        string root = workspace?.Root ?? sourceRoot;
        string expectedDatabase = Path.GetFullPath(Path.Combine(root, "Data", "server.db"));
        using var currentDirectory = new CurrentDirectoryScope(root);
        string setup = File.ReadAllText(Path.Combine(root, "Configs", "Setup.ini"));
        Assert.Contains("Provider=Sqlite", setup, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SqlitePath=.\\Data\\server.db", setup, StringComparison.OrdinalIgnoreCase);
        using var settings = new SqliteSettingsScope(expectedDatabase);

        int mapIndex = RequirePositiveInt("LYOCRYSTAL_CONTENT06_MAP_INDEX");
        int respawnIndex = RequirePositiveInt("LYOCRYSTAL_CONTENT06_RESPAWN_INDEX");
        int npcIndex = RequirePositiveInt("LYOCRYSTAL_CONTENT06_NPC_INDEX");
        ushort originalDelay = checked((ushort)RequirePositiveInt("LYOCRYSTAL_CONTENT06_ORIGINAL_DELAY"));
        string originalNpcName = RequireValue("LYOCRYSTAL_CONTENT06_ORIGINAL_NPC_NAME");

        var envir = new Envir();
        Assert.True(envir.LoadDB());
        (MapInfo map, RespawnInfo respawn, NPCInfo npc) = SelectFacts(envir, mapIndex, respawnIndex, npcIndex);
        workspace?.CopyContentFiles(map.FileName, npc.FileName);

        if (mode == "verify-restored")
        {
            Assert.Equal(originalDelay, respawn.Delay);
            Assert.Equal(originalNpcName, npc.Name);
            _output.WriteLine($"CONTENT06_REAL_RESULT status=restored map={map.Index} respawn={respawn.RespawnIndex} npc={npc.Index} delay={respawn.Delay} name={npc.Name}");
            return;
        }

        Assert.Equal(originalDelay, respawn.Delay);
        Assert.Equal(originalNpcName, npc.Name);
        ushort changedDelay = checked((ushort)(originalDelay + 1));
        string changedNpcName = originalNpcName + "_内容冒烟";

        (int width, int height) = ReadMapBounds(map.FileName);
        var mapSession = new MapContentEditingSession(
            map,
            envir.MonsterInfoList.Select(value => value.Index),
            width,
            height);
        MapContentDraft originalMapDraft = MapContentEditingSession.Capture(map);
        MapRespawnDraft[] changedRespawns = originalMapDraft.Respawns.ToArray();
        int respawnSlot = Array.FindIndex(changedRespawns, value => value.RespawnIndex == respawnIndex);
        Assert.True(respawnSlot >= 0);
        changedRespawns[respawnSlot] = changedRespawns[respawnSlot] with { Delay = changedDelay };
        var changedMapDraft = new MapContentDraft(changedRespawns, originalMapDraft.MineZones);
        MapContentReview mapReview = mapSession.Review(changedMapDraft);
        Assert.False(mapReview.HasErrors);
        Assert.Contains(mapReview.Differences, value => value.Source == $"刷怪[{respawnSlot}]" && value.Summary.Contains($"刷新={changedDelay}", StringComparison.Ordinal));

        var npcSession = new NpcContentEditingSession(envir.NPCInfoList, envir.NPCIndex);
        NPCInfo npcDraft = Assert.Single(npcSession.Drafts, value => value.Index == npcIndex);
        npcDraft.Name = changedNpcName;
        NpcContentDiff npcDifference = Assert.Single(npcSession.BuildDiff());
        Assert.Equal(npcIndex, npcDifference.EntityIndex);
        Assert.Contains(nameof(NPCInfo.Name), npcDifference.Summary, StringComparison.Ordinal);

        ProjectPreflightReport preflight = ProjectSemanticPreflight.ValidateMapContent(new ProjectPreflightRequest
        {
            MapDirectory = global::Server.Settings.MapPath,
            NpcDirectory = global::Server.Settings.NPCPath,
            CSharpNpcDirectory = global::Server.Settings.CSharpScriptsPath,
            Maps = envir.MapInfoList,
            Monsters = envir.MonsterInfoList,
            Items = envir.ItemInfoList,
            Npcs = npcSession.Drafts,
            MapBounds = [new ProjectMapBounds(map.Index, width, height)],
            Scripts = new ScriptRegistry(),
        }, map.Index);
        Assert.DoesNotContain(preflight.Diagnostics, value =>
            value.Severity == ProjectPreflightSeverity.Error &&
            (value.Source.StartsWith($"MapInfo[{map.Index}]", StringComparison.Ordinal) ||
             value.Source.StartsWith($"NPCInfo[{npc.Index}:", StringComparison.Ordinal)));

        _output.WriteLine(
            $"CONTENT06_REAL_TARGET sourceRoot={sourceRoot} isolatedRoot={root} database={expectedDatabase} map={mapIndex} respawn={respawnIndex} npc={npcIndex} backup={backup!.Root}");
        // 与真实作者界面一致：地图和 NPC 分别显式保存；所有写入仅发生在临时测试服副本。
        MapContentCommitResult mapCommit = mapSession.TryCommit(changedMapDraft, envir.SaveDB);
        Assert.True(mapCommit.Completed, mapCommit.Error);
        NpcContentCommitResult npcCommit = npcSession.TryCommit(envir.SaveDB);
        Assert.True(npcCommit.Success, npcCommit.Error);

        var reloaded = new Envir();
        Assert.True(reloaded.LoadDB());
        (MapInfo reloadedMap, RespawnInfo reloadedRespawn, NPCInfo reloadedNpc) = SelectFacts(reloaded, mapIndex, respawnIndex, npcIndex);
        Assert.Equal(changedDelay, reloadedRespawn.Delay);
        Assert.Equal(changedNpcName, reloadedNpc.Name);
        backup.VerifyCurrentSource();
        _output.WriteLine(
            $"CONTENT06_REAL_RESULT status=applied-isolated map={reloadedMap.Index}:{reloadedMap.FileName} " +
            $"respawn={reloadedRespawn.RespawnIndex} monster={reloadedRespawn.MonsterIndex} delay={originalDelay}->{changedDelay} " +
            $"npc={reloadedNpc.Index} name={originalNpcName}->{changedNpcName} mapDiffs={mapReview.Differences.Count} npcDiffs=1 diagnostics={preflight.Diagnostics.Count}");
    }

    private static (MapInfo Map, RespawnInfo Respawn, NPCInfo Npc) SelectFacts(
        Envir envir,
        int mapIndex,
        int respawnIndex,
        int npcIndex)
    {
        MapInfo map = Assert.Single(envir.MapInfoList, value => value.Index == mapIndex);
        RespawnInfo respawn = Assert.Single(map.Respawns, value => value.RespawnIndex == respawnIndex);
        NPCInfo npc = Assert.Single(envir.NPCInfoList, value => value.Index == npcIndex);
        Assert.Equal(map.Index, npc.MapIndex);
        return (map, respawn, npc);
    }

    private static (int Width, int Height) ReadMapBounds(string fileName)
    {
        var reader = new ReadMap { mapFile = fileName };
        reader.Load();
        try
        {
            Assert.True(reader.Width > 0 && reader.Height > 0, $"无法读取真实地图边界：{fileName}");
            return (reader.Width, reader.Height);
        }
        finally
        {
            reader.clippingZone?.Dispose();
        }
    }

    private static string NormalizeMode(string value) => value switch
    {
        "apply" => value,
        "verify-restored" => value,
        _ => throw new InvalidOperationException("LYOCRYSTAL_CONTENT06_MODE 仅允许 apply 或 verify-restored。"),
    };

    private static int RequirePositiveInt(string name) =>
        int.TryParse(RequireValue(name), out int value) && value > 0
            ? value
            : throw new InvalidOperationException($"{name} 必须是显式设置的正整数。");

    private static string RequireDirectory(string name)
    {
        string value = Path.GetFullPath(RequireValue(name));
        return Directory.Exists(value) ? value : throw new DirectoryNotFoundException($"{name} 指向的目录不存在：{value}");
    }

    private static string RequireValue(string name) =>
        string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name))
            ? throw new InvalidOperationException($"缺少环境变量 {name}。")
            : Environment.GetEnvironmentVariable(name)!;

    private sealed class CurrentDirectoryScope : IDisposable
    {
        private readonly string _previous = Environment.CurrentDirectory;

        public CurrentDirectoryScope(string path) => Directory.SetCurrentDirectory(path);

        public void Dispose() => Directory.SetCurrentDirectory(_previous);
    }

    private sealed class SqliteSettingsScope : IDisposable
    {
        private readonly string _provider = global::Server.Settings.DatabaseProvider;
        private readonly string _sqlitePath = global::Server.Settings.SqlitePath;
        private readonly bool _autoApplySchema = global::Server.Settings.AutoApplySchemaOnStartup;
        private readonly bool _autoImportLegacy = global::Server.Settings.AutoImportLegacyOnEmpty;
        private readonly bool _legacyBlobRead = global::Server.Settings.WorldLegacyBlobReadFallbackEnabled;
        private readonly bool _legacyBlobWrite = global::Server.Settings.WorldLegacyBlobWriteEnabled;

        public SqliteSettingsScope(string databasePath)
        {
            global::Server.Settings.DatabaseProvider = "Sqlite";
            global::Server.Settings.SqlitePath = databasePath;
            global::Server.Settings.AutoApplySchemaOnStartup = true;
            global::Server.Settings.AutoImportLegacyOnEmpty = false;
            global::Server.Settings.WorldLegacyBlobReadFallbackEnabled = false;
            global::Server.Settings.WorldLegacyBlobWriteEnabled = false;
        }

        public void Dispose()
        {
            global::Server.Settings.DatabaseProvider = _provider;
            global::Server.Settings.SqlitePath = _sqlitePath;
            global::Server.Settings.AutoApplySchemaOnStartup = _autoApplySchema;
            global::Server.Settings.AutoImportLegacyOnEmpty = _autoImportLegacy;
            global::Server.Settings.WorldLegacyBlobReadFallbackEnabled = _legacyBlobRead;
            global::Server.Settings.WorldLegacyBlobWriteEnabled = _legacyBlobWrite;
        }
    }

    private sealed class BackupSet
    {
        private static readonly string[] FileNames = ["server.db", "server.db-wal", "server.db-shm"];
        private readonly string _dataDirectory;
        private readonly IReadOnlyDictionary<string, string> _hashes;

        private BackupSet(string root, string dataDirectory, IReadOnlyDictionary<string, string> hashes)
        {
            Root = root;
            _dataDirectory = dataDirectory;
            _hashes = hashes;
        }

        public string Root { get; }

        public static BackupSet Verify(string serverRoot)
        {
            string backupsDirectory = Path.GetFullPath(Path.Combine(serverRoot, "Backups"));
            string backupRoot = Path.GetFullPath(RequireDirectory("LYOCRYSTAL_CONTENT06_BACKUP_ROOT"));
            string boundary = backupsDirectory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!backupRoot.StartsWith(boundary, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("LYOCRYSTAL_CONTENT06_BACKUP_ROOT 必须位于测试服 Backups 目录内。");

            string dataDirectory = Path.GetFullPath(Path.Combine(serverRoot, "Data"));
            var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string fileName in FileNames)
            {
                string expected = RequireHash(HashVariable(fileName));
                string backupPath = Path.Combine(backupRoot, fileName);
                string currentPath = Path.Combine(dataDirectory, fileName);
                Assert.True(File.Exists(backupPath), $"备份文件不存在：{backupPath}");
                Assert.True(File.Exists(currentPath), $"当前数据库文件不存在：{currentPath}");
                Assert.Equal(expected, ComputeHash(backupPath));
                Assert.Equal(expected, ComputeHash(currentPath));
                hashes[fileName] = expected;
            }
            return new BackupSet(backupRoot, dataDirectory, hashes);
        }

        public void CopyTo(string destinationDataDirectory)
        {
            Directory.CreateDirectory(destinationDataDirectory);
            foreach (string fileName in FileNames)
            {
                string source = Path.Combine(Root, fileName);
                string target = Path.Combine(destinationDataDirectory, fileName);
                File.Copy(source, target, overwrite: true);
                Assert.Equal(_hashes[fileName], ComputeHash(target));
            }
        }

        public void VerifyCurrentSource()
        {
            foreach (string fileName in FileNames)
                Assert.Equal(_hashes[fileName], ComputeHash(Path.Combine(_dataDirectory, fileName)));
        }

        private static string RequireHash(string name)
        {
            string value = RequireValue(name).Trim().ToUpperInvariant();
            return value.Length == 64 && value.All(Uri.IsHexDigit)
                ? value
                : throw new InvalidOperationException($"{name} 必须是 64 位 SHA-256。 ");
        }

        private static string ComputeHash(string path)
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            return Convert.ToHexString(SHA256.HashData(stream));
        }

        public static string HashVariable(string fileName) =>
            "LYOCRYSTAL_CONTENT06_" + fileName.Replace('.', '_').Replace('-', '_').ToUpperInvariant() + "_SHA256";
    }

    private sealed class IsolatedServerWorkspace : IDisposable
    {
        private readonly string _sourceRoot;

        private IsolatedServerWorkspace(string sourceRoot, string root)
        {
            _sourceRoot = sourceRoot;
            Root = root;
        }

        public string Root { get; }

        public static IsolatedServerWorkspace Create(string sourceRoot, BackupSet backup)
        {
            string root = Path.Combine(Path.GetTempPath(), "LyoCrystal-CONTENT06-real-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "Configs"));
            File.Copy(Path.Combine(sourceRoot, "Configs", "Setup.ini"), Path.Combine(root, "Configs", "Setup.ini"));
            backup.CopyTo(Path.Combine(root, "Data"));
            return new IsolatedServerWorkspace(sourceRoot, root);
        }

        public void CopyContentFiles(string mapFileName, string npcFileName)
        {
            CopyRelative(Path.Combine("Maps", mapFileName + ".map"));
            string txt = Path.Combine("Envir", "NPCs", npcFileName + ".txt");
            string csharp = Path.Combine("Envir", "CSharpScripts", "NPCs", npcFileName + ".cs");
            if (File.Exists(Path.Combine(_sourceRoot, txt))) CopyRelative(txt);
            else CopyRelative(csharp);
        }

        private void CopyRelative(string relativePath)
        {
            if (Path.IsPathRooted(relativePath))
                throw new InvalidOperationException($"真实内容路径必须是相对路径：{relativePath}");
            string source = ResolveWithin(_sourceRoot, relativePath);
            Assert.True(File.Exists(source), $"真实内容文件不存在：{source}");
            string target = ResolveWithin(Root, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target, overwrite: true);
        }

        private static string ResolveWithin(string root, string relativePath)
        {
            string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string resolved = Path.GetFullPath(Path.Combine(fullRoot, relativePath));
            if (!resolved.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"真实内容路径越界：{relativePath}");
            return resolved;
        }

        internal static void CopyRelativeForTest(string sourceRoot, string targetRoot, string relativePath) =>
            new IsolatedServerWorkspace(sourceRoot, targetRoot).CopyRelative(relativePath);

        public void Dispose()
        {
            Exception? lastError = null;
            for (int attempt = 0; attempt < 20 && Directory.Exists(Root); attempt++)
            {
                try
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    SqliteConnection.ClearAllPools();
                    Directory.Delete(Root, recursive: true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    lastError = ex;
                    Thread.Sleep(250);
                }
            }
            if (Directory.Exists(Root))
                throw new IOException($"CONTENT-06 临时测试服清理失败，请手工删除：{Root}", lastError);
        }
    }
}
