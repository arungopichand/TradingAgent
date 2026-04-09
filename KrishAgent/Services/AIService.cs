using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace KrishAgent.Services
{
    public class AIService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly HttpClient _httpClient;
        private readonly string? _apiKey;
        private readonly string _model;

        public AIService(HttpClient httpClient, IConfiguration configuration)
        {
            _httpClient = httpClient;
            _apiKey = configuration["OpenAI:ApiKey"]?.Trim();
            _model = configuration["OpenAI:Model"]?.Trim() ?? "gpt-3.5-turbo";
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
        }

        public async Task<string> Analyze(string input)
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
                model = _model,
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
            request.Headers.Add("Authorization", $"Bearer {_apiKey}");

            try
            {
                var response = await _httpClient.SendAsync(request);
                
                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception($"OpenAI API error: {response.StatusCode} - {response.ReasonPhrase}");
                }
                
                var responseJson = await response.Content.ReadAsStringAsync();
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
            if (!string.IsNullOrWhiteSpace(_apiKey))
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
