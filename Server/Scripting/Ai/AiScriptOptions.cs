namespace Server.Scripting.Ai
{
    public sealed class AiScriptOptions
    {
        public bool Enabled { get; init; }
        public string Provider { get; init; } = string.Empty;
        public string ApiBaseUrl { get; init; } = string.Empty;
        public string Model { get; init; } = string.Empty;
        public string ApiKey { get; init; } = string.Empty;
        public string ApiKeyEnvironmentVariable { get; init; } = string.Empty;
        public int TimeoutSeconds { get; init; }
        public int MaxRetries { get; init; }
        public int RequestsPerMinute { get; init; }
        public int DocumentationMaxCharacters { get; init; }

        public string ResolveApiKey()
        {
            return string.IsNullOrWhiteSpace(ApiKey) ? string.Empty : ApiKey.Trim();
        }

        public static AiScriptOptions FromSettings()
        {
            return new AiScriptOptions
            {
                Enabled = Settings.AiScriptsEnabled,
                Provider = Settings.AiScriptsProvider,
                ApiBaseUrl = Settings.AiScriptsApiBaseUrl,
                Model = Settings.AiScriptsModel,
                ApiKey = Settings.AiScriptsApiKey,
                ApiKeyEnvironmentVariable = string.Empty,
                TimeoutSeconds = Math.Max(1, Settings.AiScriptsTimeoutSeconds),
                MaxRetries = Math.Max(0, Settings.AiScriptsMaxRetries),
                RequestsPerMinute = Math.Max(1, Settings.AiScriptsRequestsPerMinute),
                DocumentationMaxCharacters = Math.Max(1024, Settings.AiScriptsDocumentationMaxChars),
            };
        }
    }
}
