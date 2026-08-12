namespace Server.Scripting.Ai
{
    public sealed class ScriptGenerationService
    {
        private readonly IAiClient _aiClient;
        private readonly ScriptDocumentationContextBuilder _documentationBuilder;
        private readonly ScriptPromptTemplateBuilder _promptTemplateBuilder;
        private readonly AiScriptOptions _options;

        public ScriptGenerationService(IAiClient aiClient, ScriptDocumentationContextBuilder documentationBuilder, ScriptPromptTemplateBuilder promptTemplateBuilder, AiScriptOptions options)
        {
            _aiClient = aiClient ?? throw new ArgumentNullException(nameof(aiClient));
            _documentationBuilder = documentationBuilder ?? throw new ArgumentNullException(nameof(documentationBuilder));
            _promptTemplateBuilder = promptTemplateBuilder ?? throw new ArgumentNullException(nameof(promptTemplateBuilder));
            _options = options ?? new AiScriptOptions();
        }

        public ScriptGenerationContext BuildContext(ScriptGenerationRequest request)
        {
            request ??= new ScriptGenerationRequest();
            return new ScriptGenerationContext
            {
                Request = request,
                Options = _options,
                Documentation = _documentationBuilder.BuildMvpContext(_options, request),
                PromptTemplateVersion = "v1",
            };
        }

        public string BuildPrompt(ScriptGenerationRequest request)
        {
            var context = BuildContext(request);
            return _promptTemplateBuilder.BuildPrompt(request, context);
        }

        public async Task<ScriptDraft> GenerateDraftAsync(ScriptGenerationRequest request, CancellationToken cancellationToken = default)
        {
            var context = BuildContext(request);
            var prompt = _promptTemplateBuilder.BuildPrompt(request, context);
            return await _aiClient.GenerateScriptAsync(prompt, context, cancellationToken);
        }

        public static ScriptGenerationService CreateDefault()
        {
            var options = AiScriptOptions.FromSettings();
            return new ScriptGenerationService(
                AiClientFactory.Create(options),
                new ScriptDocumentationContextBuilder(),
                new ScriptPromptTemplateBuilder(),
                options);
        }
    }
}
