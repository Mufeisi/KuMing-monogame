using Server.MirDatabase;

namespace Server.Persistence
{
    public enum PersistenceModuleState
    {
        Created = 0,
        Loading = 1,
        Ready = 2,
        Faulted = 3,
    }

    public enum CheckpointKind
    {
        CharacterRuntime = 1,
        WorldDefinition = 2,
    }

    public enum CommitReason
    {
        AutoSave = 1,
        Shutdown = 2,
        EditorSave = 3,
        Operator = 4,
    }

    public abstract class PersistenceResult
    {
        public bool Committed { get; init; }
        public long Generation { get; init; }
        public bool Retryable { get; init; }
        public string ErrorCode { get; init; } = string.Empty;
        public string Diagnostics { get; init; } = string.Empty;

        public static T Failure<T>(string errorCode, Exception exception, bool retryable = false)
            where T : PersistenceResult, new()
        {
            return new T
            {
                Committed = false,
                Retryable = retryable,
                ErrorCode = errorCode ?? "persistence_error",
                Diagnostics = exception?.ToString() ?? string.Empty,
            };
        }
    }

    public sealed class StartupLoadResult : PersistenceResult
    {
        public bool Loaded => Committed;
    }

    public sealed class CommitResult : PersistenceResult { }
    public sealed class IdentityResult : PersistenceResult { }

    public sealed class CharacterResult : PersistenceResult
    {
        public CharacterInfo Character { get; init; }
    }

    public abstract record IdentityCommand;
    public sealed record PersistIdentitySnapshotCommand : IdentityCommand;

    public abstract record CharacterCommand;
    public sealed record BackupCharacterCommand(CharacterInfo Character) : CharacterCommand;
    public sealed record LoadCharacterBackupCommand(string Name) : CharacterCommand;
    public sealed record ArchiveCharacterCommand(CharacterInfo Character) : CharacterCommand;
    public sealed record RestoreCharacterCommand(string Name, AccountInfo Account) : CharacterCommand;
    public sealed record LoadGuildRuntimeCommand : CharacterCommand;
    public sealed record LoadConquestRuntimeCommand : CharacterCommand;
    public sealed record LoadNpcGoodsRuntimeCommand : CharacterCommand;

    public interface IGamePersistence
    {
        DatabaseProviderKind Provider { get; }
        PersistenceModuleState State { get; }

        StartupLoadResult LoadStartup();
        CommitResult Commit(CheckpointKind checkpoint, CommitReason reason);
        IdentityResult ExecuteIdentity(IdentityCommand command);
        CharacterResult ExecuteCharacter(CharacterCommand command);
    }
}
