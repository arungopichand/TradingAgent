namespace KrishAgent.Models
{
    public class PortfolioPositionRequest
    {
        public string Symbol { get; set; } = string.Empty;
        public decimal Quantity { get; set; }
        public decimal EntryPrice { get; set; }
        public DateTime EntryDate { get; set; } = DateTime.UtcNow;
        public decimal? StopLoss { get; set; }
        public decimal? TakeProfit { get; set; }
        public string? Notes { get; set; }
    }
}
