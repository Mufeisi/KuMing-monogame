namespace Server.Persistence
{
    public interface IIdentityStore
    {
        IdentityResult Execute(IdentityCommand command);
    }

    public interface ICharacterStore
    {
        CharacterResult Execute(CharacterCommand command);
        CommitResult Commit(CommitReason reason);
    }

    public interface IWorldStore
    {
        CommitResult Commit(CommitReason reason);
    }
}
