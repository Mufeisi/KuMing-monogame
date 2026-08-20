using System.Net;
using System.Net.Http.Headers;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Server.Scripting.Ai
{
    public sealed class OpenAiChatCompletionsAiClient : IAiClient
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false,
        };

        private readonly AiScriptOptions _options;
        private readonly bool _allowDefaultOpenAiBaseUrl;
        private readonly HttpClient _httpClient;
        private readonly string _baseAddressError;
        private readonly SemaphoreSlim _rateLimitLock = new(1, 1);
        private DateTime _nextAllowedRequestUtc = DateTime.MinValue;

        public OpenAiChatCompletionsAiClient(AiScriptOptions options, bool allowDefaultOpenAiBaseUrl)
        {
            _options = options ?? new AiScriptOptions();
            _allowDefaultOpenAiBaseUrl = allowDefaultOpenAiBaseUrl;
            _httpClient = CreateHttpClient(_options, _allowDefaultOpenAiBaseUrl, out _baseAddressError);
        }

        public async Task<ScriptDraft> GenerateScriptAsync(string prompt, ScriptGenerationContext context, CancellationToken cancellationToken = default)
        {
            prompt ??= string.Empty;
            context ??= new ScriptGenerationContext();

            var providerName = string.IsNullOrWhiteSpace(_options.Provider) ? "OpenAI" : _options.Provider.Trim();
            var modelName = (_options.Model ?? string.Empty).Trim();
            var warnings = new List<string>();
            var targetKey = context.Request.TargetKey;

            if (!_options.Enabled)
            {
                return new ScriptDraft
                {
                    Success = false,
                    ProviderName = providerName,
                    ModelName = modelName,
                    Prompt = prompt,
                    GeneratedCode = string.Empty,
                    SuggestedRelativePath = BuildSuggestedRelativePath(targetKey),
                    ErrorMessage = "AiScriptsEnabled=False，当前未启用 AI Provider。",
                    Warnings = warnings,
                };
            }

            if (string.IsNullOrWhiteSpace(modelName))
            {
                return new ScriptDraft
                {
                    Success = false,
                    ProviderName = providerName,
                    ModelName = string.Empty,
                    Prompt = prompt,
                    GeneratedCode = string.Empty,
                    SuggestedRelativePath = BuildSuggestedRelativePath(targetKey),
                    ErrorMessage = "未配置 AiScriptsModel，请在 Setup.ini 的 [AiScripts] 段设置 AiScriptsModel。",
                    Warnings = warnings,
                };
            }

            var apiKey = _options.ResolveApiKey();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return new ScriptDraft
                {
                    Success = false,
                    ProviderName = providerName,
                    ModelName = modelName,
                    Prompt = prompt,
                    GeneratedCode = string.Empty,
                    SuggestedRelativePath = BuildSuggestedRelativePath(targetKey),
                    ErrorMessage = $"未配置 AiScriptsApiKey，且环境变量 {(_options.ApiKeyEnvironmentVariable ?? string.Empty).Trim()} 不存在或为空。",
                    Warnings = warnings,
                };
            }

            if (!string.IsNullOrWhiteSpace(_baseAddressError))
            {
                return new ScriptDraft
                {
                    Success = false,
                    ProviderName = providerName,
                    ModelName = modelName,
                    Prompt = prompt,
                    GeneratedCode = string.Empty,
                    SuggestedRelativePath = BuildSuggestedRelativePath(targetKey),
                    ErrorMessage = _baseAddressError,
                    Warnings = warnings,
                };
            }

            await WaitForRateLimitAsync(cancellationToken);

            var request = new ChatCompletionsRequest
            {
                Model = modelName,
                Temperature = 0.1,
                MaxTokens = 4096,
                Messages = new[]
                {
                    new ChatMessage
                    {
                        Role = "system",
                        Content = "你是服务器 C# 脚本生成器。只输出一个可编译的 .cs 文件源码，不要解释，不要 markdown，不要多文件。",
                    },
                    new ChatMessage
                    {
                        Role = "user",
                        Content = prompt,
                    },
                },
            };

            var requestJson = JsonSerializer.Serialize(request, JsonOptions);

            var response = await SendWithRetriesAsync(
                async () =>
                {
                    using var requestMessage = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
                    {
                        Content = new StringContent(requestJson, Encoding.UTF8, "application/json"),
                    };
                    requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                    return await _httpClient.SendAsync(requestMessage, cancellationToken);
                },
                maxRetries: Math.Max(0, _options.MaxRetries),
                cancellationToken);

            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var error = TryExtractErrorMessage(responseBody);
                var message = string.IsNullOrWhiteSpace(error)
                    ? $"AI 请求失败：HTTP {(int)response.StatusCode} {response.ReasonPhrase}"
                    : $"AI 请求失败：HTTP {(int)response.StatusCode} {response.ReasonPhrase} - {error}";

                return new ScriptDraft
                {
                    Success = false,
                    ProviderName = providerName,
                    ModelName = modelName,
                    Prompt = prompt,
                    GeneratedCode = string.Empty,
                    SuggestedRelativePath = BuildSuggestedRelativePath(targetKey),
                    ErrorMessage = message,
                    RawResponse = TrimRaw(responseBody),
                    Warnings = warnings,
                };
            }

            if (!TryExtractAssistantContent(responseBody, out var output, out var extractError))
            {
                return new ScriptDraft
                {
                    Success = false,
                    ProviderName = providerName,
                    ModelName = modelName,
                    Prompt = prompt,
                    GeneratedCode = string.Empty,
                    SuggestedRelativePath = BuildSuggestedRelativePath(targetKey),
                    ErrorMessage = "AI 返回解析失败：" + extractError,
                    RawResponse = TrimRaw(responseBody),
                    Warnings = warnings,
                };
            }

            var generatedCode = ExtractCode(output, out var extractWarning);
            if (!string.IsNullOrWhiteSpace(extractWarning))
                warnings.Add(extractWarning);

            if (string.IsNullOrWhiteSpace(generatedCode))
            {
                return new ScriptDraft
                {
                    Success = false,
                    ProviderName = providerName,
                    ModelName = modelName,
                    Prompt = prompt,
                    GeneratedCode = string.Empty,
                    SuggestedRelativePath = BuildSuggestedRelativePath(targetKey),
                    ErrorMessage = "AI 未返回有效代码内容。",
                    RawResponse = TrimRaw(responseBody),
                    Warnings = warnings,
                };
            }

            return new ScriptDraft
            {
                Success = true,
                ProviderName = providerName,
                ModelName = modelName,
                Prompt = prompt,
                GeneratedCode = generatedCode.TrimEnd(),
                SuggestedRelativePath = BuildSuggestedRelativePath(targetKey),
                RawResponse = TrimRaw(responseBody),
                Warnings = warnings,
            };
        }

        private static HttpClient CreateHttpClient(AiScriptOptions options, bool allowDefaultOpenAiBaseUrl, out string error)
        {
            options ??= new AiScriptOptions();
            error = string.Empty;

            var baseUrl = (options.ApiBaseUrl ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                if (!allowDefaultOpenAiBaseUrl)
                    baseUrl = string.Empty;
                else
                    baseUrl = "https://api.openai.com/v1";
            }

            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                error = "未配置 AiScriptsApiBaseUrl，当前 Provider 需要显式指定 API 地址。";
                return new HttpClient { Timeout = TimeSpan.FromSeconds(Math.Max(1, options.TimeoutSeconds)) };
            }

            var normalized = baseUrl.TrimEnd('/') + "/";
            if (!Uri.TryCreate(normalized, UriKind.Absolute, out var baseUri))
            {
                error = "AiScriptsApiBaseUrl 无效：" + baseUrl;
                return new HttpClient { Timeout = TimeSpan.FromSeconds(Math.Max(1, options.TimeoutSeconds)) };
            }

            var client = new HttpClient { BaseAddress = baseUri, Timeout = TimeSpan.FromSeconds(Math.Max(1, options.TimeoutSeconds)) };

            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Lom_mir2_net8ZH-AiClient/1.0");
            return client;
        }

        private async Task WaitForRateLimitAsync(CancellationToken cancellationToken)
        {
            var rpm = Math.Max(1, _options.RequestsPerMinute);
            var interval = TimeSpan.FromSeconds(60d / rpm);
            TimeSpan delay;

            await _rateLimitLock.WaitAsync(cancellationToken);
            try
            {
                var now = DateTime.UtcNow;
                var scheduled = now >= _nextAllowedRequestUtc ? now : _nextAllowedRequestUtc;
                delay = scheduled - now;
                _nextAllowedRequestUtc = scheduled + interval;
            }
            finally
            {
                _rateLimitLock.Release();
            }

            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, cancellationToken);
        }

        private static async Task<HttpResponseMessage> SendWithRetriesAsync(Func<Task<HttpResponseMessage>> send, int maxRetries, CancellationToken cancellationToken)
        {
            var attempt = 0;
            while (true)
            {
                try
                {
                    var response = await send();
                    if (!IsTransientStatusCode(response.StatusCode) || attempt >= maxRetries)
                        return response;

                    var delay = GetRetryDelay(response, attempt);
                    response.Dispose();
                    await Task.Delay(delay, cancellationToken);
                }
                catch (HttpRequestException) when (attempt < maxRetries)
                {
                    await Task.Delay(Backoff(attempt), cancellationToken);
                }
                catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested && attempt < maxRetries)
                {
                    await Task.Delay(Backoff(attempt), cancellationToken);
                }

                attempt++;
            }
        }

        private static bool IsTransientStatusCode(HttpStatusCode statusCode)
        {
            return statusCode == HttpStatusCode.TooManyRequests ||
                   statusCode == HttpStatusCode.BadGateway ||
                   statusCode == HttpStatusCode.ServiceUnavailable ||
                   statusCode == HttpStatusCode.GatewayTimeout ||
                   (int)statusCode >= 500;
        }

        private static TimeSpan GetRetryDelay(HttpResponseMessage response, int attempt)
        {
            if (response.Headers.TryGetValues("Retry-After", out var values))
            {
                var first = values.FirstOrDefault();
                if (int.TryParse(first, out var seconds) && seconds > 0 && seconds <= 120)
                    return TimeSpan.FromSeconds(seconds);
            }

            return Backoff(attempt);
        }

        private static TimeSpan Backoff(int attempt)
        {
            var seconds = Math.Min(30, 1 + Math.Pow(2, Math.Min(6, attempt)));
            var jitter = Random.Shared.NextDouble() * 0.3;
            return TimeSpan.FromSeconds(seconds + jitter);
        }

        private static bool TryExtractAssistantContent(string responseBody, out string content, out string error)
        {
            content = string.Empty;
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(responseBody))
            {
                error = "返回内容为空。";
                return false;
            }

            try
            {
                using var doc = JsonDocument.Parse(responseBody);
                var root = doc.RootElement;

                if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0)
                {
                    var first = choices[0];
                    if (first.TryGetProperty("message", out var message) &&
                        message.TryGetProperty("content", out var contentElement) &&
                        contentElement.ValueKind == JsonValueKind.String)
                    {
                        content = contentElement.GetString() ?? string.Empty;
                        return true;
                    }

                    if (first.TryGetProperty("text", out var textElement) && textElement.ValueKind == JsonValueKind.String)
                    {
                        content = textElement.GetString() ?? string.Empty;
                        return true;
                    }
                }

                error = "未找到 choices[0].message.content。";
                return false;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static string TryExtractErrorMessage(string responseBody)
        {
            if (string.IsNullOrWhiteSpace(responseBody))
                return string.Empty;

            try
            {
                using var doc = JsonDocument.Parse(responseBody);
                if (doc.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
                {
                    if (error.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String)
                        return message.GetString() ?? string.Empty;
                }
            }
            catch
            {
                // ignore
            }

            return string.Empty;
        }

        private static string ExtractCode(string output, out string warning)
        {
            warning = string.Empty;
            if (string.IsNullOrWhiteSpace(output))
                return string.Empty;

            var text = output.Trim();
            var fenceStart = text.IndexOf("```", StringComparison.Ordinal);
            if (fenceStart < 0)
                return text;

            var fenceEnd = text.IndexOf("```", fenceStart + 3, StringComparison.Ordinal);
            if (fenceEnd < 0)
                return text;

            var codeStart = text.IndexOf('\n', fenceStart + 3);
            if (codeStart < 0 || codeStart >= fenceEnd)
                return text;

            codeStart += 1;
            warning = "AI 返回包含 Markdown 代码块，已自动提取代码部分。";
            return text.Substring(codeStart, fenceEnd - codeStart).TrimEnd();
        }

        private static string BuildSuggestedRelativePath(string targetKey)
        {
            if (string.IsNullOrWhiteSpace(targetKey))
                return "Generated/NewScript.cs";

            var normalized = targetKey.Trim().Replace('\\', '/').TrimStart('/');
            return normalized + ".cs";
        }

        private static string TrimRaw(string text, int max = 20000)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= max)
                return text ?? string.Empty;

            return text[..max] + "\n... (truncated)";
        }

        private sealed class ChatCompletionsRequest
        {
            [JsonPropertyName("model")]
            public string Model { get; init; } = string.Empty;

            [JsonPropertyName("messages")]
            public IReadOnlyList<ChatMessage> Messages { get; init; } = Array.Empty<ChatMessage>();

            [JsonPropertyName("temperature")]
            public double? Temperature { get; init; }

            [JsonPropertyName("max_tokens")]
            public int? MaxTokens { get; init; }

            [JsonPropertyName("stream")]
            public bool Stream { get; init; }
        }

        private sealed class ChatMessage
        {
            [JsonPropertyName("role")]
            public string Role { get; init; } = string.Empty;

            [JsonPropertyName("content")]
            public string Content { get; init; } = string.Empty;
        }
    }
}
