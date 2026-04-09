namespace KrishAgent.Configuration
{
    public class AlpacaOptions
    {
        public const string SectionName = "Alpaca";

        public string ApiKey { get; set; } = string.Empty;
        public string SecretKey { get; set; } = string.Empty;
    }

    public class OpenAiOptions
    {
        public const string SectionName = "OpenAI";
        public const string DefaultModel = "gpt-3.5-turbo";

        public string ApiKey { get; set; } = string.Empty;
        public string Model { get; set; } = DefaultModel;
    }

    public class TradingOptions
    {
        public const string SectionName = "Trading";

        public string[] AnalysisSymbols { get; set; } = ["AAPL", "TSLA", "SPY"];
        public string[] BackgroundSymbols { get; set; } = ["AAPL", "TSLA", "SPY", "MSFT", "GOOGL", "NVDA"];
        public int DataFetchIntervalMinutes { get; set; } = 60;
        public int AlertCheckIntervalMinutes { get; set; } = 5;
        public bool RunInitialBackgroundFetch { get; set; } = true;
    }
}
