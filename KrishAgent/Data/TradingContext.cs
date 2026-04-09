using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace KrishAgent.Data
{
    public class TradingContext : DbContext
    {
        public TradingContext(DbContextOptions<TradingContext> options) : base(options) { }

        public DbSet<StockPrice> StockPrices { get; set; }
        public DbSet<AnalysisHistory> AnalysisHistory { get; set; }
        public DbSet<Alert> Alerts { get; set; }
        public DbSet<PortfolioPosition> PortfolioPositions { get; set; }
        public DbSet<Trade> Trades { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // StockPrice indexes
            modelBuilder.Entity<StockPrice>()
                .HasIndex(sp => new { sp.Symbol, sp.Date })
                .IsUnique();

            // AnalysisHistory indexes
            modelBuilder.Entity<AnalysisHistory>()
                .HasIndex(ah => new { ah.Symbol, ah.Date });

            // Alert indexes
            modelBuilder.Entity<Alert>()
                .HasIndex(a => new { a.Symbol, a.IsActive });
        }
    }

    public class StockPrice
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(10)]
        public string Symbol { get; set; } = string.Empty;

        [Required]
        public DateTime Date { get; set; }

        [Required]
        public decimal Open { get; set; }

        [Required]
        public decimal High { get; set; }

        [Required]
        public decimal Low { get; set; }

        [Required]
        public decimal Close { get; set; }

        [Required]
        public long Volume { get; set; }

        // Technical Indicators
        public decimal? RSI { get; set; }
        public decimal? MACD_Line { get; set; }
        public decimal? MACD_Signal { get; set; }
        public decimal? MACD_Histogram { get; set; }
        public decimal? Bollinger_Upper { get; set; }
        public decimal? Bollinger_Middle { get; set; }
        public decimal? Bollinger_Lower { get; set; }
        public decimal? MA20 { get; set; }
        public decimal? MA50 { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class AnalysisHistory
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(10)]
        public string Symbol { get; set; } = string.Empty;

        [Required]
        public DateTime Date { get; set; }

        [Required]
        public decimal Price { get; set; }

        public decimal? RSI { get; set; }
        public decimal? MACD_Line { get; set; }
        public decimal? Bollinger_Upper { get; set; }
        public decimal? MA20 { get; set; }

        [Required]
        [MaxLength(20)]
        public string Trend { get; set; } = string.Empty;

        [Required]
        [MaxLength(20)]
        public string Action { get; set; } = string.Empty;

        [Required]
        public int Confidence { get; set; }

        [Required]
        public string Reason { get; set; } = string.Empty;

        [MaxLength(50)]
        public string AIModel { get; set; } = "gpt-3.5-turbo";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class Alert
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(10)]
        public string Symbol { get; set; } = string.Empty;

        [Required]
        [MaxLength(50)]
        public string AlertType { get; set; } = string.Empty; // "price_above", "rsi_above", "macd_crossover"

        [Required]
        public decimal Threshold { get; set; }

        public string Condition { get; set; } = string.Empty; // Additional condition details

        [Required]
        public bool IsActive { get; set; } = true;

        public bool IsTriggered { get; set; } = false;

        public DateTime? TriggeredAt { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? ExpiresAt { get; set; }
    }

    public class PortfolioPosition
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(10)]
        public string Symbol { get; set; } = string.Empty;

        [Required]
        public decimal Quantity { get; set; }

        [Required]
        public decimal EntryPrice { get; set; }

        [Required]
        public DateTime EntryDate { get; set; }

        public decimal? StopLoss { get; set; }

        public decimal? TakeProfit { get; set; }

        public string Notes { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class Trade
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(10)]
        public string Symbol { get; set; } = string.Empty;

        [Required]
        [MaxLength(10)]
        public string Side { get; set; } = string.Empty; // "buy" or "sell"

        [Required]
        public decimal Quantity { get; set; }

        [Required]
        public decimal EntryPrice { get; set; }

        [Required]
        public DateTime EntryDate { get; set; }

        public decimal? ExitPrice { get; set; }

        public DateTime? ExitDate { get; set; }

        public decimal? Pnl { get; set; }

        public decimal? PnlPercent { get; set; }

        [MaxLength(100)]
        public string ExitReason { get; set; } = string.Empty;

        public string Notes { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}