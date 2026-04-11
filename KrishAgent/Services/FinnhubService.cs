using System.Text.Json;
using KrishAgent.Models;

namespace KrishAgent.Services
{
    public sealed class FinnhubService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly HttpClient _httpClient;
        private readonly IConfiguration _config;
        private readonly ILogger<FinnhubService> _logger;

        public FinnhubService(HttpClient httpClient, IConfiguration config, ILogger<FinnhubService> logger)
        {
            _httpClient = httpClient;
            _config = config;
            _logger = logger;
        }

        public async Task<QuoteResponse?> GetQuoteAsync(string symbol, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                return null;
            }

            var apiKey = _config["Finnhub:ApiKey"]?.Trim();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return null;
            }

            try
            {
                var requestUri = $"quote?symbol={Uri.EscapeDataString(symbol.Trim().ToUpperInvariant())}&token={Uri.EscapeDataString(apiKey)}";
                using var response = await _httpClient.GetAsync(requestUri, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                var payload = await response.Content.ReadAsStringAsync(cancellationToken);
                if (string.IsNullOrWhiteSpace(payload))
                {
                    return null;
                }

                return JsonSerializer.Deserialize<QuoteResponse>(payload, JsonOptions);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Finnhub quote request failed for {Symbol}", symbol);
                return null;
            }
        }
    }
}
