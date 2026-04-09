using System.Net.Http;
using System.Globalization;
using KrishAgent.Configuration;
using Microsoft.Extensions.Options;

namespace KrishAgent.Services
{
    public class MarketService
    {
        private readonly HttpClient _httpClient;
        private readonly IOptionsMonitor<AlpacaOptions> _options;

        private string ApiKey => _options.CurrentValue.ApiKey?.Trim() ?? string.Empty;
        private string SecretKey => _options.CurrentValue.SecretKey?.Trim() ?? string.Empty;

        public MarketService(HttpClient httpClient, IOptionsMonitor<AlpacaOptions> options)
        {
            _httpClient = httpClient;
            _options = options;
            _httpClient.Timeout = TimeSpan.FromSeconds(15);
        }

        public async Task<string> GetMarketData(string symbol, CancellationToken cancellationToken = default)
        {
            try
            {
                EnsureConfigured();

                var normalizedSymbol = symbol.Trim().ToUpperInvariant();
                var end = DateTime.UtcNow;
                var start = end.AddDays(-90);
                var startText = Uri.EscapeDataString(start.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
                var endText = Uri.EscapeDataString(end.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));

                var url =
                    $"https://data.alpaca.markets/v2/stocks/{Uri.EscapeDataString(normalizedSymbol)}/bars" +
                    $"?timeframe=1Day&limit=30&start={startText}&end={endText}&feed=iex&adjustment=raw";
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("APCA-API-KEY-ID", ApiKey);
                request.Headers.Add("APCA-API-SECRET-KEY", SecretKey);
                
                var response = await _httpClient.SendAsync(request, cancellationToken);
                
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
            if (!string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(SecretKey))
            {
                return;
            }

            throw new InvalidOperationException(
                "Alpaca credentials are missing. Configure Alpaca:ApiKey and Alpaca:SecretKey via user secrets or environment variables.");
        }
    }
}
