namespace Server.Scripting.Ai
{
    public interface IAiClient
    {
        Task<ScriptDraft> GenerateScriptAsync(string prompt, ScriptGenerationContext context, CancellationToken cancellationToken = default);
    }
}
