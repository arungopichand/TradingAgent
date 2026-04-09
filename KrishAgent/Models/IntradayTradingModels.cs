namespace KrishAgent.Models
{
    public class IntradayTradingBoard
    {
        public DateTime GeneratedAtUtc { get; set; }
        public string Timeframe { get; set; } = string.Empty;
        public string MarketStatus { get; set; } = string.Empty;
        public string BeginnerNote { get; set; } = string.Empty;
        public List<IntradayTradeIdea> Picks { get; set; } = [];
    }

    public class IntradayTradeIdea
    {
        public string Symbol { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;
        public string Direction { get; set; } = string.Empty;
        public string Setup { get; set; } = string.Empty;
        public decimal CurrentPrice { get; set; }
        public decimal EntryPrice { get; set; }
        public decimal StopLoss { get; set; }
        public decimal TargetPrice1 { get; set; }
        public decimal TargetPrice2 { get; set; }
        public decimal RiskRewardRatio { get; set; }
        public decimal Rsi { get; set; }
        public decimal MomentumPercent { get; set; }
        public decimal DayChangePercent { get; set; }
        public long Volume { get; set; }
        public int Confidence { get; set; }
        public string WhyItWasPicked { get; set; } = string.Empty;
        public string WhatToDo { get; set; } = string.Empty;
        public string WhenToSell { get; set; } = string.Empty;
        public string BeginnerTip { get; set; } = string.Empty;
        public DateTime LastUpdatedUtc { get; set; }
    }
}
