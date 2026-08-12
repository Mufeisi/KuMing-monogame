namespace Server.Scripting.Ai
{
    public sealed class ScriptGenerationContext
    {
        public ScriptGenerationRequest Request { get; init; } = new ScriptGenerationRequest();
        public AiScriptOptions Options { get; init; } = new AiScriptOptions();
        public IReadOnlyList<DocumentationSnippet> Documentation { get; init; } = Array.Empty<DocumentationSnippet>();
        public string PromptTemplateVersion { get; init; } = "v1";
    }
}
