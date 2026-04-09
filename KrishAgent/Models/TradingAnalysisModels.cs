namespace KrishAgent.Models
{
    public class StockSnapshot
    {
        public string Symbol { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public decimal Rsi { get; set; }
        public string Trend { get; set; } = string.Empty;
    }

    public class AiAnalysisItem
    {
        public string Symbol { get; set; } = string.Empty;
        public string Trend { get; set; } = string.Empty;
        public decimal Confidence { get; set; }
        public string Reason { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;
    }

    public class AiAnalysisResponse
    {
        public string Source { get; set; } = string.Empty;
        public string Warning { get; set; } = string.Empty;
        public List<AiAnalysisItem> Items { get; set; } = [];
    }

    public class AnalysisResultItem
    {
        public string Symbol { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public decimal Rsi { get; set; }
        public string Trend { get; set; } = string.Empty;
        public int Confidence { get; set; }
        public string Reason { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;
    }
}
