namespace KrishAgent.Models
{
    public class TradeRequest
    {
        public string Symbol { get; set; } = string.Empty;
        public string Side { get; set; } = string.Empty;
        public decimal Quantity { get; set; }
        public decimal EntryPrice { get; set; }
        public DateTime EntryDate { get; set; } = DateTime.UtcNow;
        public decimal? ExitPrice { get; set; }
        public DateTime? ExitDate { get; set; }
        public decimal? Pnl { get; set; }
        public decimal? PnlPercent { get; set; }
        public string? ExitReason { get; set; }
        public string? Notes { get; set; }
    }
}
