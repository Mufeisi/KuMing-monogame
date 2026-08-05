namespace Server.Scripting.Ai
{
    public sealed class ScriptGenerationRequest
    {
        public string NaturalLanguageDescription { get; init; } = string.Empty;
        public string TargetKey { get; init; } = string.Empty;
        public ScriptGenerationKind Kind { get; init; }
        public string AdditionalRequirements { get; init; } = string.Empty;
        public string ExistingScriptContent { get; init; } = string.Empty;
        public IReadOnlyList<ScriptDiagnostic> Diagnostics { get; init; } = Array.Empty<ScriptDiagnostic>();
    }
}
