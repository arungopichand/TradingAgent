using System.Net.Http;
using Microsoft.Extensions.Configuration;

namespace KrishAgent.Services
{
    public class MarketService
    {
        private readonly HttpClient _httpClient;
        private readonly string? _apiKey;
        private readonly string? _secretKey;

        public MarketService(HttpClient httpClient, IConfiguration configuration)
        {
            _httpClient = httpClient;
            _apiKey = configuration["Alpaca:ApiKey"]?.Trim();
            _secretKey = configuration["Alpaca:SecretKey"]?.Trim();
            _httpClient.Timeout = TimeSpan.FromSeconds(15);
        }

        public async Task<string> GetMarketData(string symbol)
        {
            try
            {
                EnsureConfigured();

                var url = $"https://data.alpaca.markets/v2/stocks/{Uri.EscapeDataString(symbol)}/bars?timeframe=1Day&limit=30";
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("APCA-API-KEY-ID", _apiKey);
                request.Headers.Add("APCA-API-SECRET-KEY", _secretKey);
                
                var response = await _httpClient.SendAsync(request);
                
                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception($"Alpaca API error: {response.StatusCode} - {response.ReasonPhrase}");
                }
                
                return await response.Content.ReadAsStringAsync();
            }
            catch (HttpRequestException ex)
            {
                throw new Exception($"Failed to fetch data for {symbol}: Network error - {ex.Message}");
            }
            catch (TaskCanceledException ex)
            {
                throw new Exception($"Failed to fetch data for {symbol}: Request timeout - {ex.Message}");
            }
        }

        private void EnsureConfigured()
        {
            if (!string.IsNullOrWhiteSpace(_apiKey) && !string.IsNullOrWhiteSpace(_secretKey))
            {
                return;
            }

            throw new InvalidOperationException(
                "Alpaca credentials are missing. Configure Alpaca:ApiKey and Alpaca:SecretKey via user secrets or environment variables.");
        }
    }
}
