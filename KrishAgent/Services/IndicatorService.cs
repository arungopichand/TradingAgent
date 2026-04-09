using Skender.Stock.Indicators;
using System.Globalization;
using System.Text.Json;

namespace KrishAgent.Services
{
    public class IndicatorService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public List<Quote> ConvertAlpacaJsonToQuotes(string json)
        {
            try
            {
                var response = JsonSerializer.Deserialize<Models.AlpacaBarsResponse>(json, JsonOptions);
                if (response?.Bars == null || response.Bars.Count == 0)
                {
                    var symbol = response?.Symbol ?? "unknown symbol";
                    throw new InvalidOperationException($"No bars returned from Alpaca API for {symbol}");
                }
                
                return response.Bars
                    .Where(b => !string.IsNullOrWhiteSpace(b.Timestamp))
                    .Select(b => new Quote
                    {
                        Date = DateTimeOffset.Parse(b.Timestamp, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).UtcDateTime,
                        Open = b.Open,
                        High = b.High,
                        Low = b.Low,
                        Close = b.Close,
                        Volume = b.Volume
                    })
                    .OrderBy(q => q.Date)
                    .ToList();
            }
            catch (JsonException ex)
            {
                throw new Exception($"Failed to parse Alpaca response: {ex.Message}");
            }
            catch (FormatException ex)
            {
                throw new Exception($"Failed to parse Alpaca timestamps: {ex.Message}");
            }
        }

        public decimal CalculateRsi(List<Quote> quotes)
        {
            if (quotes == null || quotes.Count < 3)
            {
                // Not enough data for RSI calculation
                return 0;
            }

            try
            {
                var rsiResults = quotes.GetRsi(2).ToList();
                if (!rsiResults.Any())
                {
                    return 0;
                }

                var lastRsi = rsiResults.LastOrDefault(result => result.Rsi.HasValue)?.Rsi;
                if (lastRsi.HasValue)
                {
                    return (decimal)lastRsi.Value;
                }

                return 0;
            }
            catch
            {
                return 0;
            }
        }

        public List<MacdResult> CalculateMacd(List<Quote> quotes)
        {
            if (quotes == null || quotes.Count < 26)
            {
                return new List<MacdResult>();
            }

            try
            {
                return quotes.GetMacd().ToList();
            }
            catch
            {
                return new List<MacdResult>();
            }
        }

        public List<BollingerBandsResult> CalculateBollingerBands(List<Quote> quotes)
        {
            if (quotes == null || quotes.Count < 20)
            {
                return new List<BollingerBandsResult>();
            }

            try
            {
                return quotes.GetBollingerBands().ToList();
            }
            catch
            {
                return new List<BollingerBandsResult>();
            }
        }

        public (List<SmaResult>, List<SmaResult>) CalculateMovingAverages(List<Quote> quotes)
        {
            var ma20 = new List<SmaResult>();
            var ma50 = new List<SmaResult>();

            if (quotes == null)
            {
                return (ma20, ma50);
            }

            try
            {
                if (quotes.Count >= 20)
                {
                    ma20 = quotes.GetSma(20).ToList();
                }

                if (quotes.Count >= 50)
                {
                    ma50 = quotes.GetSma(50).ToList();
                }
            }
            catch
            {
                // Return empty lists on error
            }

            return (ma20, ma50);
        }
    }
}
