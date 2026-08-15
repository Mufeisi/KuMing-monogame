using System.Reflection;
using Microsoft.Data.Sqlite;
using Server.Diagnostics;
using Server.MirEnvir;
using Server.Persistence;
using Server.Persistence.Sql;
using Server.Scripting;
using Xunit;
using Xunit.Abstractions;

namespace Base05.Tests;

public sealed class Leg02ExternalProjectPreflightTests
{
    private readonly ITestOutputHelper _output;

    public Leg02ExternalProjectPreflightTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Real_project_snapshot_produces_located_read_only_report()
    {
        string? root = Environment.GetEnvironmentVariable("LYOCRYSTAL_LEG02_PROJECT_ROOT");
        if (string.IsNullOrWhiteSpace(root))
        {
            _output.WriteLine("LEG02_REAL_PROJECT status=not-requested");
            return;
        }

        root = Path.GetFullPath(root);
        string databasePath = Path.Combine(root, "Data", "server.db");
        string scriptsPath = Path.Combine(root, "Envir");
        Assert.True(File.Exists(databasePath), $"真实项目数据库不存在：{databasePath}");

        var envir = new Envir();
        string snapshotDirectory = Path.Combine(Path.GetTempPath(), "lyocrystal-leg02-real-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(snapshotDirectory);
        string snapshotPath = Path.Combine(snapshotDirectory, "server.db");
        try
        {
            CreateReadOnlySnapshot(databasePath, snapshotPath);
            using SqlSession session = SqlSession.Open(DatabaseProviderKind.Sqlite, new SqlDatabaseOptions { SqlitePath = snapshotPath });
            SqlWorldRelationsSnapshot? world = SqlWorldRelationsLoader.LoadAll(session);
            Assert.NotNull(world);
            SqlWorldRelationsLoader.RestoreToEnvir(envir, world!);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(snapshotDirectory, recursive: true);
        }

        ScriptRegistry scripts = LoadScripts(scriptsPath);
        ProjectPreflightReport report = ProjectSemanticPreflight.Validate(new ProjectPreflightRequest
        {
            MapDirectory = Path.Combine(root, "Maps"),
            NpcDirectory = Path.Combine(root, "Envir", "NPCs"),
            CSharpNpcDirectory = Path.Combine(root, "Envir", "CSharpScripts", "NPCs"),
            Maps = envir.MapInfoList,
            Monsters = envir.MonsterInfoList,
            Items = envir.ItemInfoList,
            Npcs = envir.NPCInfoList,
            Scripts = scripts,
            ItemExists = name => envir.GetItemInfo(name) is not null,
            StartupResources = Array.Empty<ProjectResourceReference>(),
        });

        ProjectPreflightDiagnostic[] blocking = report.Diagnostics.Where(value => value.Severity == ProjectPreflightSeverity.Error).ToArray();
        foreach (IGrouping<string, ProjectPreflightDiagnostic> group in report.Diagnostics.GroupBy(value => value.Code).OrderBy(value => value.Key, StringComparer.Ordinal))
        {
            _output.WriteLine($"{group.Key} count={group.Count()}");
            foreach (ProjectPreflightDiagnostic diagnostic in group.Take(3))
                _output.WriteLine($"  {diagnostic.Severity} {diagnostic.Source} {diagnostic.Message}");
        }
        _output.WriteLine($"LEG02_REAL_PROJECT status=completed publishable={(blocking.Length == 0 ? "yes" : "no")} maps={envir.MapInfoList.Count} monsters={envir.MonsterInfoList.Count} items={envir.ItemInfoList.Count} npcs={envir.NPCInfoList.Count} drops={scripts.Drops.Count} shops={scripts.NpcShops.Count} recipes={scripts.Recipes.Count} errors={blocking.Length} diagnostics={report.Diagnostics.Count}");
        Assert.NotEmpty(envir.MapInfoList);
        Assert.NotEmpty(envir.MonsterInfoList);
        Assert.NotEmpty(envir.ItemInfoList);
        Assert.All(report.Diagnostics, diagnostic =>
        {
            Assert.StartsWith("LEG02-", diagnostic.Code, StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(diagnostic.Source));
        });
    }

    private static void CreateReadOnlySnapshot(string sourcePath, string destinationPath)
    {
        var sourceBuilder = new SqliteConnectionStringBuilder
        {
            DataSource = sourcePath,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Private,
        };
        var destinationBuilder = new SqliteConnectionStringBuilder
        {
            DataSource = destinationPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
        };
        using var source = new SqliteConnection(sourceBuilder.ToString());
        using var destination = new SqliteConnection(destinationBuilder.ToString());
        source.Open();
        destination.Open();
        source.BackupDatabase(destination);
    }

    private static ScriptRegistry LoadScripts(string root)
    {
        var compiler = new ScriptCompiler();
        ScriptCompileResult result = compiler.CompileFromDirectory(root, "LomScripts_Leg02_" + Guid.NewGuid().ToString("N"), debugBuild: false);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        if (!result.HasScripts) return new ScriptRegistry();

        var context = new ScriptLoadContext();
        try
        {
            using var assemblyStream = new MemoryStream(result.AssemblyBytes);
            using var symbolsStream = new MemoryStream(result.PdbBytes);
            Assembly assembly = context.LoadFromStream(assemblyStream, symbolsStream);
            var registry = new ScriptRegistry();
            ScriptManager.RegisterModules(assembly, registry);
            registry.SealVariableDeclarations();
            return registry;
        }
        finally
        {
            context.Unload();
        }
    }
}
