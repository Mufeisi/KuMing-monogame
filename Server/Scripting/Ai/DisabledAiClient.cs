namespace Server.Scripting.Ai
{
    public sealed class DisabledAiClient : IAiClient
    {
        private readonly AiScriptOptions _options;
        private readonly ScriptDraftScaffolder _scaffolder = new ScriptDraftScaffolder();

        public DisabledAiClient(AiScriptOptions options)
        {
            _options = options ?? new AiScriptOptions();
        }

        public Task<ScriptDraft> GenerateScriptAsync(string prompt, ScriptGenerationContext context, CancellationToken cancellationToken = default)
        {
            var warnings = new List<string>();

            if (!_options.Enabled)
                warnings.Add("AiScriptsEnabled=False，当前未启用 AI 生成功能。");

            if (string.IsNullOrWhiteSpace(_options.Provider) || string.Equals(_options.Provider, "Disabled", StringComparison.OrdinalIgnoreCase))
                warnings.Add("当前未配置可用的 AI Provider。");

            var draft = _scaffolder.CreateDraft(
                context?.Request ?? new ScriptGenerationRequest(),
                context ?? new ScriptGenerationContext(),
                prompt,
                string.IsNullOrWhiteSpace(_options.Provider) ? "Disabled" : _options.Provider,
                _options.Model);

            return Task.FromResult(new ScriptDraft
            {
                Success = draft.Success,
                ProviderName = draft.ProviderName,
                ModelName = draft.ModelName,
                Prompt = draft.Prompt,
                GeneratedCode = draft.GeneratedCode,
                SuggestedRelativePath = draft.SuggestedRelativePath,
                ErrorMessage = "AI Provider 尚未配置完成，已返回离线模板草稿。",
                RawResponse = draft.RawResponse,
                Warnings = draft.Warnings.Concat(warnings).Distinct(StringComparer.Ordinal).ToArray(),
            });
        }
    }
}
