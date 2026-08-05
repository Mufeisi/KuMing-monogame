namespace Server.Scripting.Ai
{
    public sealed class ScriptDraft
    {
        public bool Success { get; init; }
        public string ProviderName { get; init; } = string.Empty;
        public string ModelName { get; init; } = string.Empty;
        public string Prompt { get; init; } = string.Empty;
        public string GeneratedCode { get; init; } = string.Empty;
        public string SuggestedRelativePath { get; init; } = string.Empty;
        public string ErrorMessage { get; init; } = string.Empty;
        public string RawResponse { get; init; } = string.Empty;
        public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    }
}
