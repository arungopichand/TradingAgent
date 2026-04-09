using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using KrishAgent.Configuration;
using Microsoft.Extensions.Options;

namespace KrishAgent.Services
{
    public class AIService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly HttpClient _httpClient;
        private readonly IOptionsMonitor<OpenAiOptions> _options;

        public string ConfiguredModel =>
            string.IsNullOrWhiteSpace(_options.CurrentValue.Model)
                ? OpenAiOptions.DefaultModel
                : _options.CurrentValue.Model.Trim();

        private string ApiKey => _options.CurrentValue.ApiKey?.Trim() ?? string.Empty;

        public AIService(HttpClient httpClient, IOptionsMonitor<OpenAiOptions> options)
        {
            _httpClient = httpClient;
            _options = options;
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
        }

        public async Task<string> Analyze(string input, CancellationToken cancellationToken = default)
        {
            EnsureConfigured();

            var prompt = $@"
Analyze the following list of stock quotes and provide trading recommendations for each item.

Input JSON:
{input}

Return a JSON array of objects with this structure:
[
  {{
    ""symbol"": ""<symbol>"",
    ""trend"": ""<trend>"",
    ""confidence"": <0-100>,
    ""reason"": ""brief explanation"",
    ""action"": ""buy_watch"" or ""sell_watch"" or ""hold""
  }}
]

The response must be valid JSON only, with no surrounding explanation text.
";

            var requestBody = new
            {
                model = ConfiguredModel,
                messages = new[]
                {
                    new
                    {
                        role = "system",
                        content = "You are a trading analysis assistant. Reply with valid JSON only."
                    },
                    new { role = "user", content = prompt }
                },
                temperature = 0,
                max_tokens = 400
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions")
            {
                Content = content
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);

            try
            {
                var response = await _httpClient.SendAsync(request, cancellationToken);
                
                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception($"OpenAI API error: {response.StatusCode} - {response.ReasonPhrase}");
                }
                
                var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
                var responseObj = JsonSerializer.Deserialize<OpenAiChatCompletionResponse>(responseJson, JsonOptions);
                var aiResponse = responseObj?.Choices?.FirstOrDefault()?.Message?.Content?.Trim();
                
                if (string.IsNullOrEmpty(aiResponse))
                {
                    throw new Exception("Empty response from OpenAI API");
                }
                
                return aiResponse;
            }
            catch (HttpRequestException ex)
            {
                throw new Exception($"Failed to call OpenAI API: Network error - {ex.Message}");
            }
            catch (TaskCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (TaskCanceledException ex)
            {
                throw new Exception($"Failed to call OpenAI API: Request timeout - {ex.Message}");
            }
            catch (JsonException ex)
            {
                throw new Exception($"Failed to parse OpenAI response: {ex.Message}");
            }
        }

        private void EnsureConfigured()
        {
            if (!string.IsNullOrWhiteSpace(ApiKey))
            {
                return;
            }

            throw new InvalidOperationException(
                "OpenAI API key is missing. Configure OpenAI:ApiKey via user secrets or environment variables.");
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
