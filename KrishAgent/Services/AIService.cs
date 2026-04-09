using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using KrishAgent.Configuration;
using KrishAgent.Models;
using Microsoft.Extensions.Options;

namespace KrishAgent.Services
{
    public class AIService
    {
        private static readonly HashSet<string> PlaceholderValues = new(StringComparer.OrdinalIgnoreCase)
        {
            "YOUR_OPENAI_API_KEY"
        };

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

        private readonly HttpClient _httpClient;
        private readonly NodeBridgeService _nodeBridgeService;
        private readonly IOptionsMonitor<OpenAiOptions> _options;
        private readonly ILogger<AIService> _logger;

        public string ConfiguredModel =>
            ResolveConfiguredValue(
                _options.CurrentValue.Model,
                "OpenAI__Model",
                "OPENAI_MODEL",
                OpenAiOptions.DefaultModel);

        private string ApiKey => ResolveConfiguredValue(
            _options.CurrentValue.ApiKey,
            "OpenAI__ApiKey",
            "OPENAI_API_KEY");

        public AIService(HttpClient httpClient, NodeBridgeService nodeBridgeService, IOptionsMonitor<OpenAiOptions> options, ILogger<AIService> logger)
        {
            _httpClient = httpClient;
            _nodeBridgeService = nodeBridgeService;
            _options = options;
            _logger = logger;
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
        }

        private static string ResolveConfiguredValue(string? configuredValue, params string[] envKeys)
        {
            return ResolveConfiguredValue(configuredValue, envKeys, string.Empty);
        }

        private static string ResolveConfiguredValue(string? configuredValue, string primaryEnvKey, string secondaryEnvKey, string fallback)
        {
            return ResolveConfiguredValue(configuredValue, new[] { primaryEnvKey, secondaryEnvKey }, fallback);
        }

        private static string ResolveConfiguredValue(string? configuredValue, IEnumerable<string> envKeys, string fallback)
        {
            var normalizedConfiguredValue = configuredValue?.Trim();
            if (!string.IsNullOrWhiteSpace(normalizedConfiguredValue))
            {
                return normalizedConfiguredValue;
            }

            foreach (var envKey in envKeys)
            {
                var envValue = Environment.GetEnvironmentVariable(envKey)?.Trim();
                if (!string.IsNullOrWhiteSpace(envValue))
                {
                    return envValue;
                }
            }

            return fallback;
        }

        public async Task<AiAnalysisResponse> Analyze(IReadOnlyCollection<StockSnapshot> stocks, CancellationToken cancellationToken = default)
        {
            var normalizedStocks = stocks
                .Where(stock => !string.IsNullOrWhiteSpace(stock.Symbol))
                .GroupBy(stock => stock.Symbol.Trim().ToUpperInvariant(), StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                {
                    var stock = group.First();
                    stock.Symbol = stock.Symbol.Trim().ToUpperInvariant();
                    return stock;
                })
                .ToList();

            if (normalizedStocks.Count == 0)
            {
                throw new InvalidOperationException("At least one stock snapshot is required for analysis.");
            }

            if (!IsConfiguredValue(ApiKey))
            {
                const string warning = "OpenAI API key is missing, so rule-based fallback analysis was used.";
                _logger.LogWarning(warning);
                return BuildFallbackResponse(normalizedStocks, warning);
            }

            try
            {
                var aiResponse = await RequestAnalysisFromOpenAiAsync(normalizedStocks, cancellationToken);
                var aiItems = ParseAiAnalysis(aiResponse);
                return new AiAnalysisResponse
                {
                    Source = ConfiguredModel,
                    Items = MergeWithFallback(normalizedStocks, aiItems, "the AI response skipped this symbol")
                };
            }
            catch (HttpRequestException ex)
            {
                var warning = $"OpenAI request failed, so rule-based fallback analysis was used. {ex.Message}";
                _logger.LogWarning(ex, "OpenAI request failed. Falling back to rule-based analysis.");
                return BuildFallbackResponse(normalizedStocks, warning);
            }
            catch (TaskCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (TaskCanceledException ex)
            {
                var warning = $"OpenAI request timed out, so rule-based fallback analysis was used. {ex.Message}";
                _logger.LogWarning(ex, "OpenAI request timed out. Falling back to rule-based analysis.");
                return BuildFallbackResponse(normalizedStocks, warning);
            }
            catch (JsonException ex)
            {
                var warning = $"OpenAI returned malformed JSON, so rule-based fallback analysis was used. {ex.Message}";
                _logger.LogWarning(ex, "OpenAI returned malformed JSON. Falling back to rule-based analysis.");
                return BuildFallbackResponse(normalizedStocks, warning);
            }
            catch (InvalidOperationException ex)
            {
                var warning = $"AI analysis could not be completed cleanly, so rule-based fallback analysis was used. {ex.Message}";
                _logger.LogWarning(ex, "AI analysis could not be completed cleanly. Falling back to rule-based analysis.");
                return BuildFallbackResponse(normalizedStocks, warning);
            }
        }

        private async Task<string> RequestAnalysisFromOpenAiAsync(
            IReadOnlyCollection<StockSnapshot> stocks,
            CancellationToken cancellationToken)
        {
            var prompt = BuildPrompt(stocks);
            var maxOutputTokens = Math.Clamp((stocks.Count * 90) + 120, 300, 900);
            var requestBody = BuildChatCompletionRequestBody(prompt, maxOutputTokens);
            var json = JsonSerializer.Serialize(requestBody, JsonOptions);

            return await SendChatCompletionAsync(json, cancellationToken);
        }

        private object BuildChatCompletionRequestBody(string prompt, int maxOutputTokens)
        {
            var requestBody = new
            {
                model = ConfiguredModel,
                messages = new object[]
                {
                    new
                    {
                        role = "system",
                        content =
                            "You are a disciplined trading analysis assistant. Return only valid JSON. Never wrap the JSON in markdown code fences. Use exactly one result per input symbol."
                    },
                    new { role = "user", content = prompt }
                }
            };

            if (UsesMaxCompletionTokens(ConfiguredModel))
            {
                if (SupportsTemperatureOverride(ConfiguredModel))
                {
                    return new
                    {
                        requestBody.model,
                        requestBody.messages,
                        temperature = 0,
                        max_completion_tokens = maxOutputTokens
                    };
                }

                return new
                {
                    requestBody.model,
                    requestBody.messages,
                    max_completion_tokens = maxOutputTokens
                };
            }

            if (SupportsTemperatureOverride(ConfiguredModel))
            {
                return new
                {
                    requestBody.model,
                    requestBody.messages,
                    temperature = 0,
                    max_tokens = maxOutputTokens
                };
            }

            return new
            {
                requestBody.model,
                requestBody.messages,
                max_tokens = maxOutputTokens
            };
        }

        private async Task<string> SendChatCompletionAsync(string json, CancellationToken cancellationToken)
        {
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions")
            {
                Content = content
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);

            HttpResponseMessage response;
            string responseJson;

            try
            {
                response = await _httpClient.SendAsync(request, cancellationToken);
                responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            }
            catch (HttpRequestException ex) when (NodeBridgeService.IsTlsStackFailure(ex))
            {
                _logger.LogInformation("Using Node HTTP bridge for OpenAI because the native Windows TLS stack is unavailable.");

                var bridgeResponse = await _nodeBridgeService.SendHttpAsync(new NodeBridgeHttpRequest
                {
                    Url = "https://api.openai.com/v1/chat/completions",
                    Method = "POST",
                    Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["Authorization"] = $"Bearer {ApiKey}",
                        ["Content-Type"] = "application/json"
                    },
                    Body = json,
                    TimeoutMs = 30000
                }, cancellationToken);

                response = new HttpResponseMessage((System.Net.HttpStatusCode)bridgeResponse.StatusCode);
                responseJson = bridgeResponse.Body ?? string.Empty;
            }

            if (!response.IsSuccessStatusCode)
            {
                var errorDetail = TryExtractApiError(responseJson);
                throw new HttpRequestException(
                    $"OpenAI API error: {(int)response.StatusCode} {response.ReasonPhrase}. {errorDetail}".Trim());
            }

            var responseObj = JsonSerializer.Deserialize<OpenAiChatCompletionResponse>(responseJson, JsonOptions);
            var aiResponse = responseObj?.Choices?.FirstOrDefault()?.Message?.Content?.Trim();

            if (string.IsNullOrWhiteSpace(aiResponse))
            {
                throw new InvalidOperationException("OpenAI returned an empty analysis response.");
            }

            return aiResponse;
        }

        private static bool UsesMaxCompletionTokens(string model)
        {
            if (string.IsNullOrWhiteSpace(model))
            {
                return false;
            }

            return model.StartsWith("gpt-5", StringComparison.OrdinalIgnoreCase) ||
                   model.StartsWith("o1", StringComparison.OrdinalIgnoreCase) ||
                   model.StartsWith("o3", StringComparison.OrdinalIgnoreCase) ||
                   model.StartsWith("o4", StringComparison.OrdinalIgnoreCase);
        }

        private static bool SupportsTemperatureOverride(string model)
        {
            if (string.IsNullOrWhiteSpace(model))
            {
                return true;
            }

            return !model.StartsWith("gpt-5", StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildPrompt(IReadOnlyCollection<StockSnapshot> stocks)
        {
            var inputJson = JsonSerializer.Serialize(stocks, JsonOptions);

            return $@"
Analyze the following stock snapshots and provide one recommendation for every symbol.

Input JSON:
{inputJson}

Rules:
- Return exactly one result for every input symbol.
- Keep the same symbol spelling as the input.
- Use only these action values: buy_watch, sell_watch, hold.
- Use only these trend values: up, down.
- Confidence must be an integer from 0 to 100.
- Reason must be one short sentence grounded in the provided price, RSI, and trend.
- Do not include markdown, prose, or code fences.

Return JSON in this exact shape:
{{
  ""results"": [
    {{
      ""symbol"": ""AAPL"",
      ""trend"": ""up"",
      ""confidence"": 72,
      ""reason"": ""Short explanation here."",
      ""action"": ""buy_watch""
    }}
  ]
}}";
        }

        private static List<AiAnalysisItem> ParseAiAnalysis(string aiContent)
        {
            if (string.IsNullOrWhiteSpace(aiContent))
            {
                throw new InvalidOperationException("AI response was empty.");
            }

            var content = StripCodeFences(aiContent.Trim());
            if (TryParseAiItems(content, out var items))
            {
                return items;
            }

            var extractedPayload = ExtractJsonPayload(content);
            if (!string.Equals(extractedPayload, content, StringComparison.Ordinal) &&
                TryParseAiItems(extractedPayload, out items))
            {
                return items;
            }

            throw new JsonException("AI response did not contain a valid analysis payload.");
        }

        private static bool TryParseAiItems(string content, out List<AiAnalysisItem> items)
        {
            items = [];

            if (string.IsNullOrWhiteSpace(content))
            {
                return false;
            }

            try
            {
                using var document = JsonDocument.Parse(content);
                if (document.RootElement.ValueKind == JsonValueKind.Array)
                {
                    items = NormalizeAiItems(document.RootElement.Deserialize<List<AiAnalysisItem>>(JsonOptions));
                    return items.Count > 0;
                }

                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return false;
                }

                foreach (var propertyName in new[] { "results", "analysis", "items", "recommendations", "data" })
                {
                    if (!document.RootElement.TryGetProperty(propertyName, out var propertyValue) ||
                        propertyValue.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    items = NormalizeAiItems(propertyValue.Deserialize<List<AiAnalysisItem>>(JsonOptions));
                    if (items.Count > 0)
                    {
                        return true;
                    }
                }
            }
            catch (JsonException)
            {
                return false;
            }

            return false;
        }

        private static List<AiAnalysisItem> NormalizeAiItems(List<AiAnalysisItem>? items)
        {
            return (items ?? [])
                .Where(item => !string.IsNullOrWhiteSpace(item.Symbol))
                .GroupBy(item => item.Symbol.Trim().ToUpperInvariant(), StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                {
                    var item = group.First();
                    return new AiAnalysisItem
                    {
                        Symbol = group.Key,
                        Trend = NormalizeTrend(item.Trend),
                        Confidence = ClampConfidence(item.Confidence),
                        Reason = item.Reason?.Trim() ?? string.Empty,
                        Action = NormalizeAction(item.Action)
                    };
                })
                .ToList();
        }

        private static List<AiAnalysisItem> MergeWithFallback(
            IReadOnlyCollection<StockSnapshot> stocks,
            IEnumerable<AiAnalysisItem> aiItems,
            string missingReason)
        {
            var lookup = aiItems
                .Where(item => !string.IsNullOrWhiteSpace(item.Symbol))
                .ToDictionary(item => item.Symbol.Trim().ToUpperInvariant(), StringComparer.OrdinalIgnoreCase);

            var merged = new List<AiAnalysisItem>(stocks.Count);
            foreach (var stock in stocks)
            {
                var fallback = BuildFallbackItem(stock, missingReason);

                if (lookup.TryGetValue(stock.Symbol, out var aiItem))
                {
                    merged.Add(new AiAnalysisItem
                    {
                        Symbol = stock.Symbol,
                        Trend = string.IsNullOrWhiteSpace(aiItem.Trend) ? fallback.Trend : aiItem.Trend,
                        Confidence = aiItem.Confidence > 0 ? aiItem.Confidence : fallback.Confidence,
                        Reason = string.IsNullOrWhiteSpace(aiItem.Reason) ? fallback.Reason : aiItem.Reason.Trim(),
                        Action = string.IsNullOrWhiteSpace(aiItem.Action) ? fallback.Action : aiItem.Action
                    });
                    continue;
                }

                merged.Add(fallback);
            }

            return merged;
        }

        private static AiAnalysisResponse BuildFallbackResponse(
            IReadOnlyCollection<StockSnapshot> stocks,
            string warning)
        {
            return new AiAnalysisResponse
            {
                Source = "rule-based-fallback",
                Warning = warning,
                Items = stocks.Select(stock => BuildFallbackItem(stock, warning)).ToList()
            };
        }

        private static AiAnalysisItem BuildFallbackItem(StockSnapshot stock, string reasonContext)
        {
            var trend = NormalizeTrend(stock.Trend);
            var rsi = decimal.Round(stock.Rsi, 1, MidpointRounding.AwayFromZero);

            string action;
            decimal confidence;
            string reason;

            if (trend == "up" && rsi >= 48m && rsi <= 68m)
            {
                action = "buy_watch";
                confidence = ClampConfidence(60m + Math.Min(18m, Math.Abs(rsi - 50m)));
                reason = $"Rule-based fallback: trend is up and RSI is {rsi:F1}, so upside continuation is worth watching.";
            }
            else if (trend == "down" && rsi >= 32m && rsi <= 55m)
            {
                action = "sell_watch";
                confidence = ClampConfidence(58m + Math.Min(18m, Math.Abs(rsi - 50m)));
                reason = $"Rule-based fallback: trend is down and RSI is {rsi:F1}, so downside continuation is worth watching.";
            }
            else
            {
                action = "hold";
                confidence = ClampConfidence(52m + Math.Min(12m, Math.Abs(rsi - 50m) / 2m));
                reason = $"Rule-based fallback: RSI is {rsi:F1} with a mixed setup, so a neutral hold stance is safer.";
            }

            if (!string.IsNullOrWhiteSpace(reasonContext) &&
                !reason.Contains(reasonContext, StringComparison.OrdinalIgnoreCase))
            {
                reason = $"{reason} Trigger: {reasonContext.TrimEnd('.')}.";
            }

            return new AiAnalysisItem
            {
                Symbol = stock.Symbol,
                Trend = trend,
                Confidence = confidence,
                Reason = reason,
                Action = action
            };
        }

        private static string StripCodeFences(string content)
        {
            if (!content.StartsWith("```", StringComparison.Ordinal))
            {
                return content;
            }

            return content.Replace("```json", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("```", string.Empty, StringComparison.Ordinal)
                .Trim();
        }

        private static string ExtractJsonPayload(string content)
        {
            var firstBrace = content.IndexOf('{');
            var firstBracket = content.IndexOf('[');
            var firstIndex = ResolveFirstJsonIndex(firstBrace, firstBracket);

            if (firstIndex < 0)
            {
                return content;
            }

            var opening = content[firstIndex];
            var closing = opening == '{' ? '}' : ']';
            var depth = 0;
            var inString = false;
            var isEscaped = false;

            for (var i = firstIndex; i < content.Length; i++)
            {
                var current = content[i];
                if (inString)
                {
                    if (isEscaped)
                    {
                        isEscaped = false;
                    }
                    else if (current == '\\')
                    {
                        isEscaped = true;
                    }
                    else if (current == '"')
                    {
                        inString = false;
                    }

                    continue;
                }

                if (current == '"')
                {
                    inString = true;
                    continue;
                }

                if (current == opening)
                {
                    depth++;
                }
                else if (current == closing)
                {
                    depth--;
                    if (depth == 0)
                    {
                        return content[firstIndex..(i + 1)];
                    }
                }
            }

            return content;
        }

        private static int ResolveFirstJsonIndex(int firstBrace, int firstBracket)
        {
            if (firstBrace < 0)
            {
                return firstBracket;
            }

            if (firstBracket < 0)
            {
                return firstBrace;
            }

            return Math.Min(firstBrace, firstBracket);
        }

        private static string TryExtractApiError(string responseJson)
        {
            if (string.IsNullOrWhiteSpace(responseJson))
            {
                return string.Empty;
            }

            try
            {
                using var document = JsonDocument.Parse(responseJson);
                if (document.RootElement.ValueKind == JsonValueKind.Object &&
                    document.RootElement.TryGetProperty("error", out var errorElement))
                {
                    if (errorElement.ValueKind == JsonValueKind.String)
                    {
                        return errorElement.GetString() ?? string.Empty;
                    }

                    if (errorElement.ValueKind == JsonValueKind.Object &&
                        errorElement.TryGetProperty("message", out var messageElement))
                    {
                        return messageElement.GetString() ?? string.Empty;
                    }
                }
            }
            catch (JsonException)
            {
                return string.Empty;
            }

            return string.Empty;
        }

        private static string NormalizeAction(string? action)
        {
            return action?.Trim().ToLowerInvariant() switch
            {
                "buy_watch" => "buy_watch",
                "sell_watch" => "sell_watch",
                "hold" => "hold",
                _ => string.Empty
            };
        }

        private static string NormalizeTrend(string? trend)
        {
            return trend?.Trim().ToLowerInvariant() switch
            {
                "up" => "up",
                "down" => "down",
                _ => "down"
            };
        }

        private static decimal ClampConfidence(decimal confidence)
        {
            return decimal.Clamp(decimal.Round(confidence, 0, MidpointRounding.AwayFromZero), 0m, 100m);
        }

        private static bool IsConfiguredValue(string value)
        {
            return !string.IsNullOrWhiteSpace(value) && !PlaceholderValues.Contains(value.Trim());
        }

        private sealed class OpenAiChatCompletionResponse
        {
            public List<OpenAiChoice> Choices { get; set; } = [];
        }

        private sealed class OpenAiChoice
        {
            public OpenAiMessage Message { get; set; } = new();
        }

        private sealed class OpenAiMessage
        {
            public string Content { get; set; } = string.Empty;
        }
    }
}
