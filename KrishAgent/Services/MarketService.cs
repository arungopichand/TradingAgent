using System.Net.Http;
using System.Globalization;
using KrishAgent.Configuration;
using Microsoft.Extensions.Options;

namespace KrishAgent.Services
{
    public class MarketService
    {
        private static readonly HashSet<string> PlaceholderValues = new(StringComparer.OrdinalIgnoreCase)
        {
            "YOUR_ALPACA_API_KEY",
            "YOUR_ALPACA_SECRET_KEY"
        };

        private readonly HttpClient _httpClient;
        private readonly NodeBridgeService _nodeBridgeService;
        private readonly IOptionsMonitor<AlpacaOptions> _options;

        private string ApiKey => _options.CurrentValue.ApiKey?.Trim() ?? string.Empty;
        private string SecretKey => _options.CurrentValue.SecretKey?.Trim() ?? string.Empty;
        private string Feed => NormalizeFeed(_options.CurrentValue.Feed);

        public MarketService(HttpClient httpClient, NodeBridgeService nodeBridgeService, IOptionsMonitor<AlpacaOptions> options)
        {
            _httpClient = httpClient;
            _nodeBridgeService = nodeBridgeService;
            _options = options;
            _httpClient.Timeout = TimeSpan.FromSeconds(15);
        }

        public Task<string> GetMarketData(string symbol, CancellationToken cancellationToken = default)
        {
            return GetMarketData(symbol, "1Day", 30, 90, cancellationToken);
        }

        public async Task<string> GetMarketData(
            string symbol,
            string timeframe,
            int limit,
            int lookbackDays,
            CancellationToken cancellationToken = default)
        {
            try
            {
                EnsureConfigured();

                var normalizedSymbol = symbol.Trim().ToUpperInvariant();
                var normalizedTimeframe = string.IsNullOrWhiteSpace(timeframe) ? "1Day" : timeframe.Trim();
                var boundedLimit = Math.Clamp(limit, 1, 1000);
                var boundedLookbackDays = Math.Clamp(lookbackDays, 1, 365);
                var end = DateTime.UtcNow;
                var start = end.AddDays(-boundedLookbackDays);
                var startText = Uri.EscapeDataString(start.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
                var endText = Uri.EscapeDataString(end.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));

                var url =
                    $"https://data.alpaca.markets/v2/stocks/{Uri.EscapeDataString(normalizedSymbol)}/bars" +
                    $"?timeframe={Uri.EscapeDataString(normalizedTimeframe)}&limit={boundedLimit}&start={startText}&end={endText}&feed={Uri.EscapeDataString(Feed)}&adjustment=raw";
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("APCA-API-KEY-ID", ApiKey);
                request.Headers.Add("APCA-API-SECRET-KEY", SecretKey);
                
                var response = await SendWithFallbackAsync(request, cancellationToken);
                
                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception($"Alpaca API error: {response.StatusCode} - {response.ReasonPhrase}");
                }
                
                return await response.Content.ReadAsStringAsync(cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                throw new Exception($"Failed to fetch data for {symbol}: Network error - {ex.Message}");
            }
            catch (TaskCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (TaskCanceledException ex)
            {
                throw new Exception($"Failed to fetch data for {symbol}: Request timeout - {ex.Message}");
            }
        }

        private void EnsureConfigured()
        {
            if (IsConfiguredValue(ApiKey) && IsConfiguredValue(SecretKey))
            {
                return;
            }

            throw new InvalidOperationException(
                "Alpaca credentials are missing. Configure Alpaca:ApiKey and Alpaca:SecretKey via user secrets or environment variables.");
        }

        private static bool IsConfiguredValue(string value)
        {
            return !string.IsNullOrWhiteSpace(value) && !PlaceholderValues.Contains(value.Trim());
        }

        private static string NormalizeFeed(string? feed)
        {
            return feed?.Trim().ToLowerInvariant() switch
            {
                "sip" => "sip",
                "delayed_sip" => "delayed_sip",
                "boats" => "boats",
                "overnight" => "overnight",
                _ => "iex"
            };
        }

        private async Task<HttpResponseMessage> SendWithFallbackAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            try
            {
                return await _httpClient.SendAsync(request, cancellationToken);
            }
            catch (HttpRequestException ex) when (NodeBridgeService.IsTlsStackFailure(ex))
            {
                var bridgeResponse = await _nodeBridgeService.SendHttpAsync(new NodeBridgeHttpRequest
                {
                    Url = request.RequestUri?.ToString() ?? string.Empty,
                    Method = request.Method.Method,
                    Headers = request.Headers.ToDictionary(
                        header => header.Key,
                        header => string.Join(", ", header.Value),
                        StringComparer.OrdinalIgnoreCase),
                    TimeoutMs = 15000
                }, cancellationToken);

                return new HttpResponseMessage((System.Net.HttpStatusCode)bridgeResponse.StatusCode)
                {
                    Content = new StringContent(bridgeResponse.Body ?? string.Empty)
                };
            }
        }
    }
}
