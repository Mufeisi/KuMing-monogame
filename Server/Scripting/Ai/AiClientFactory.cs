namespace Server.Scripting.Ai
{
    public static class AiClientFactory
    {
        public static IAiClient Create(AiScriptOptions options)
        {
            options ??= new AiScriptOptions();

            if (!options.Enabled)
                return new DisabledAiClient(options);

            var provider = (options.Provider ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(provider) || string.Equals(provider, "Disabled", StringComparison.OrdinalIgnoreCase))
                return new DisabledAiClient(options);

            if (string.Equals(provider, "OpenAI", StringComparison.OrdinalIgnoreCase))
                return new OpenAiChatCompletionsAiClient(options, allowDefaultOpenAiBaseUrl: true);

            if (string.Equals(provider, "OpenAICompatible", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(provider, "Compatible", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(provider, "ChatCompletions", StringComparison.OrdinalIgnoreCase))
                return new OpenAiChatCompletionsAiClient(options, allowDefaultOpenAiBaseUrl: false);

            // 其他 Provider 默认按 OpenAI 兼容接口处理：需要用户显式配置 ApiBaseUrl。
            return new OpenAiChatCompletionsAiClient(options, allowDefaultOpenAiBaseUrl: false);
        }
    }
}
