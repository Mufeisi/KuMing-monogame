using Server.Persistence.Sql;

if (args.Length == 1 && args[0].Equals("sha256", StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine("Usage: DbMigrator sha256 <source.db>");
    return 2;
}

if (args.Length == 2 && args[0].Equals("sha256", StringComparison.OrdinalIgnoreCase))
{
    Console.WriteLine(SqliteLayoutMigrator.ComputeFileSha256(args[1]));
    return 0;
}

if (args.Length != 6 || !args[0].Equals("world-only-reset-players", StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine("Usage: DbMigrator world-only-reset-players <source.db> <target-dir> <migration-id> <source-sha256> --authorize-reset");
    return 2;
}

if (!args[5].Equals("--authorize-reset", StringComparison.Ordinal))
{
    Console.Error.WriteLine("The final argument must be --authorize-reset.");
    return 2;
}

try
{
    var result = new SqliteLayoutMigrator().Migrate(new SqliteWorldOnlyMigrationRequest
    {
        SourcePath = args[1],
        TargetDirectory = args[2],
        MigrationId = args[3],
        AuthorizedSourceSha256 = args[4],
        WorldOnlyResetPlayers = true,
    });

    Console.WriteLine($"source_sha256={result.SourceSha256}");
    Console.WriteLine($"generation={result.GenerationDirectory}");
    foreach (var table in result.Tables)
        Console.WriteLine($"{table.Table}: rows={table.TargetRows}, sha256={table.TargetChecksum}");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception);
    return 1;
}
