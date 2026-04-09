namespace KrishAgent.Models
{
    public static class WatchlistTypes
    {
        public const string Analysis = "analysis";
        public const string Background = "background";
        public const string DayTrading = "day_trading";
        public const string PennyStock = "penny_stock";

        public static readonly string[] All =
        [
            Analysis,
            Background,
            DayTrading,
            PennyStock
        ];
    }

    public class WatchlistEntryRequest
    {
        public string ListType { get; set; } = string.Empty;

        public string Symbol { get; set; } = string.Empty;
    }
}
