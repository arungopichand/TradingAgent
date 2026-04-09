using Skender.Stock.Indicators;

namespace KrishAgent.Models
{
    public sealed class LiveMarketSnapshot
    {
        public string Symbol { get; init; } = string.Empty;
        public List<Quote> OneMinuteQuotes { get; init; } = [];
        public List<Quote> FiveMinuteQuotes { get; init; } = [];
        public decimal? CurrentPrice { get; init; }
        public DateTime? CurrentPriceTimestampUtc { get; init; }
    }
}
