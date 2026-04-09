using KrishAgent.Configuration;
using KrishAgent.Models;
using Microsoft.Extensions.Options;
using Skender.Stock.Indicators;

namespace KrishAgent.Services
{
    public class IntradayTradingService
    {
        private static readonly TimeZoneInfo MarketTimeZone = ResolveMarketTimeZone();

        private readonly DataService _dataService;
        private readonly MarketService _marketService;
        private readonly IndicatorService _indicatorService;
        private readonly LiveMarketStreamService _liveMarketStreamService;
        private readonly TradingOptions _tradingOptions;
        private readonly ILogger<IntradayTradingService> _logger;

        public IntradayTradingService(
            DataService dataService,
            MarketService marketService,
            IndicatorService indicatorService,
            LiveMarketStreamService liveMarketStreamService,
            IOptions<TradingOptions> tradingOptions,
            ILogger<IntradayTradingService> logger)
        {
            _dataService = dataService;
            _marketService = marketService;
            _indicatorService = indicatorService;
            _liveMarketStreamService = liveMarketStreamService;
            _tradingOptions = tradingOptions.Value;
            _logger = logger;
        }

        public async Task<IntradayTradingBoard> GetBoardAsync(CancellationToken cancellationToken = default)
        {
            var symbols = NormalizeSymbols(await _dataService.GetWatchlistSymbolsAsync(
                WatchlistTypes.DayTrading,
                _tradingOptions.DayTradingSymbols));
            var pickCount = Math.Clamp(_tradingOptions.DayTradingPickCount, 1, 10);

            var tasks = symbols.Select(symbol => BuildIdeaSafeAsync(symbol, cancellationToken));
            var ideas = (await Task.WhenAll(tasks))
                .Where(idea => idea != null)
                .Cast<IntradayTradeIdea>()
                .ToList();

            if (ideas.Count == 0)
            {
                return new IntradayTradingBoard
                {
                    GeneratedAtUtc = DateTime.UtcNow,
                    Timeframe = "5-minute setups with a 1-minute price check",
                    MarketStatus = BuildEmptyBoardStatus("intraday"),
                    BeginnerNote =
                        "Buy ideas are long trades. Sell ideas are short trades. If you are new, keep position sizes small, wait for the entry price, and skip any short setup you do not fully understand.",
                    Picks = []
                };
            }

            var picks = SelectTopPicks(ideas, pickCount);
            var newestUpdate = picks.Max(pick => pick.LastUpdatedUtc);
            var marketStatus = DateTime.UtcNow - newestUpdate > TimeSpan.FromMinutes(2)
                ? "Live updates are more than 2 minutes old, so the market may be closed or the stream may be reconnecting."
                : "Live market feed is active. Refresh often and wait for the entry price before acting.";

            return new IntradayTradingBoard
            {
                GeneratedAtUtc = DateTime.UtcNow,
                Timeframe = "5-minute setups with a 1-minute price check",
                MarketStatus = marketStatus,
                BeginnerNote =
                    "Buy ideas are long trades. Sell ideas are short trades. If you are new, keep position sizes small, wait for the entry price, and skip any short setup you do not fully understand.",
                Picks = picks
            };
        }

        private async Task<IntradayTradeIdea?> BuildIdeaSafeAsync(string symbol, CancellationToken cancellationToken)
        {
            try
            {
                return await BuildIdeaAsync(symbol, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to build intraday setup for {Symbol}", symbol);
                return null;
            }
        }

        private async Task<IntradayTradeIdea> BuildIdeaAsync(string symbol, CancellationToken cancellationToken)
        {
            var snapshot = await GetSnapshotAsync(symbol, cancellationToken);
            var fiveMinuteQuotes = GetLatestSessionQuotes(snapshot.FiveMinuteQuotes);
            var oneMinuteQuotes = GetLatestSessionQuotes(snapshot.OneMinuteQuotes);
            var minimumBarCount = GetMinimumBarCount();

            if (fiveMinuteQuotes.Count < minimumBarCount)
            {
                throw new InvalidOperationException($"Not enough intraday data returned for {symbol}. Need at least {minimumBarCount} five-minute bars but only received {fiveMinuteQuotes.Count}.");
            }

            var currentQuote = oneMinuteQuotes.LastOrDefault() ?? fiveMinuteQuotes.Last();
            var currentPrice = snapshot.CurrentPrice ?? currentQuote.Close;
            var openPrice = fiveMinuteQuotes.First().Open;
            var momentumBars = Math.Min(3, fiveMinuteQuotes.Count - 1);
            var momentumBase = fiveMinuteQuotes[^ (momentumBars + 1)].Close;
            var momentumPercent = PercentageChange(currentPrice, momentumBase);
            var dayChangePercent = PercentageChange(currentPrice, openPrice);
            var rsi = GetLatestRsi(_indicatorService.CalculateRsiSeries(fiveMinuteQuotes, 6));
            var sma8 = AverageClose(fiveMinuteQuotes, 8);
            var sma20 = AverageClose(fiveMinuteQuotes, 20);

            var recentQuotes = TakeLast(fiveMinuteQuotes, Math.Min(12, fiveMinuteQuotes.Count));
            var recentHigh = recentQuotes.Max(quote => quote.High);
            var recentLow = recentQuotes.Min(quote => quote.Low);
            var averageRange = recentQuotes.Average(quote => quote.High - quote.Low);
            var latestVolume = Convert.ToInt64(currentQuote.Volume);

            var longScore = ScoreLongIdea(currentPrice, sma8, sma20, momentumPercent, dayChangePercent, rsi, recentHigh, averageRange);
            var shortScore = ScoreShortIdea(currentPrice, sma8, sma20, momentumPercent, dayChangePercent, rsi, recentLow, averageRange);

            var bias = ResolveBias(longScore, shortScore, dayChangePercent);
            var confidence = CalculateConfidence(longScore, shortScore, momentumPercent);
            var entryBuffer = ClampDecimal(averageRange * 0.15m, currentPrice * 0.0010m, currentPrice * 0.0060m);
            var stopDistance = ClampDecimal(averageRange * 1.25m, currentPrice * 0.0040m, currentPrice * 0.0200m);

            if (bias == "buy")
            {
                var entryPrice = RoundPrice(Math.Max(currentPrice, recentHigh) + entryBuffer);
                var stopLoss = RoundPrice(entryPrice - stopDistance);
                var targetPrice1 = RoundPrice(entryPrice + (stopDistance * 1.5m));
                var targetPrice2 = RoundPrice(entryPrice + (stopDistance * 2.2m));

                return new IntradayTradeIdea
                {
                    Symbol = symbol,
                    Action = "buy",
                    Direction = "Long",
                    Setup = longScore >= 5 ? "Momentum breakout" : "Trend continuation",
                    CurrentPrice = RoundPrice(currentPrice),
                    EntryPrice = entryPrice,
                    StopLoss = stopLoss,
                    TargetPrice1 = targetPrice1,
                    TargetPrice2 = targetPrice2,
                    RiskRewardRatio = CalculateRiskReward(entryPrice, stopLoss, targetPrice1),
                    Rsi = RoundMetric(rsi),
                    MomentumPercent = RoundMetric(momentumPercent),
                    DayChangePercent = RoundMetric(dayChangePercent),
                    Volume = latestVolume,
                    Confidence = confidence,
                    WhyItWasPicked =
                        $"{symbol} is holding above its short-term trend, showing {RoundMetric(momentumPercent)}% short-term momentum with RSI at {RoundMetric(rsi)} near the session high.",
                    WhatToDo =
                        $"Buy only if price trades above ${entryPrice:F2}. Start small, do not chase a big candle, and keep the stop fixed at ${stopLoss:F2}.",
                    WhenToSell =
                        $"Book some profit near ${targetPrice1:F2}. Exit the rest near ${targetPrice2:F2} or get out immediately if price falls below ${stopLoss:F2}.",
                    BeginnerTip =
                        "Wait for the breakout to happen first. If the stock never reaches entry, do nothing and move on.",
                    LastUpdatedUtc = snapshot.CurrentPriceTimestampUtc ?? currentQuote.Date
                };
            }

            var shortEntry = RoundPrice(Math.Min(currentPrice, recentLow) - entryBuffer);
            var shortStop = RoundPrice(shortEntry + stopDistance);
            var shortTarget1 = RoundPrice(shortEntry - (stopDistance * 1.5m));
            var shortTarget2 = RoundPrice(shortEntry - (stopDistance * 2.2m));

            return new IntradayTradeIdea
            {
                Symbol = symbol,
                Action = "sell",
                Direction = "Short",
                Setup = shortScore >= 5 ? "Breakdown short" : "Weakness continuation",
                CurrentPrice = RoundPrice(currentPrice),
                EntryPrice = shortEntry,
                StopLoss = shortStop,
                TargetPrice1 = shortTarget1,
                TargetPrice2 = shortTarget2,
                RiskRewardRatio = CalculateRiskReward(shortEntry, shortStop, shortTarget1),
                Rsi = RoundMetric(rsi),
                MomentumPercent = RoundMetric(momentumPercent),
                DayChangePercent = RoundMetric(dayChangePercent),
                Volume = latestVolume,
                Confidence = confidence,
                WhyItWasPicked =
                    $"{symbol} is trading below its short-term trend, showing {RoundMetric(momentumPercent)}% downside momentum with RSI at {RoundMetric(rsi)} and repeated weakness near the session low.",
                WhatToDo =
                    $"Sell short only if price trades below ${shortEntry:F2}. If you do not short stocks, skip this idea and wait for a buy setup instead.",
                WhenToSell =
                    $"Cover some shares near ${shortTarget1:F2}. Exit the rest near ${shortTarget2:F2} or cover immediately if price rises above ${shortStop:F2}.",
                BeginnerTip =
                    "Short selling is advanced. If your account does not support it or you are unsure, do not take this setup.",
                LastUpdatedUtc = snapshot.CurrentPriceTimestampUtc ?? currentQuote.Date
            };
        }

        private async Task<LiveMarketSnapshot> GetSnapshotAsync(string symbol, CancellationToken cancellationToken)
        {
            if (_liveMarketStreamService.TryGetSnapshot(symbol, out var liveSnapshot) && liveSnapshot is not null)
            {
                return liveSnapshot;
            }

            var fiveMinuteJson = await _marketService.GetMarketData(symbol, "5Min", 78, 2, cancellationToken);
            var oneMinuteJson = await _marketService.GetMarketData(symbol, "1Min", 120, 1, cancellationToken);

            var fiveMinuteQuotes = _indicatorService.ConvertAlpacaJsonToQuotes(fiveMinuteJson);
            var oneMinuteQuotes = _indicatorService.ConvertAlpacaJsonToQuotes(oneMinuteJson);
            var currentQuote = oneMinuteQuotes.LastOrDefault() ?? fiveMinuteQuotes.LastOrDefault();

            return new LiveMarketSnapshot
            {
                Symbol = symbol,
                FiveMinuteQuotes = fiveMinuteQuotes,
                OneMinuteQuotes = oneMinuteQuotes,
                CurrentPrice = currentQuote?.Close,
                CurrentPriceTimestampUtc = currentQuote?.Date
            };
        }

        private static List<IntradayTradeIdea> SelectTopPicks(List<IntradayTradeIdea> ideas, int pickCount)
        {
            var orderedBuys = ideas
                .Where(idea => idea.Action == "buy")
                .OrderByDescending(GetRankingScore)
                .ToList();

            var orderedSells = ideas
                .Where(idea => idea.Action == "sell")
                .OrderByDescending(GetRankingScore)
                .ToList();

            var picks = new List<IntradayTradeIdea>();
            picks.AddRange(orderedBuys.Take(Math.Min(3, pickCount)));
            picks.AddRange(orderedSells.Take(Math.Min(2, pickCount - picks.Count)));

            var selectedSymbols = picks.Select(pick => pick.Symbol).ToHashSet(StringComparer.OrdinalIgnoreCase);
            picks.AddRange(ideas
                .Where(idea => !selectedSymbols.Contains(idea.Symbol))
                .OrderByDescending(GetRankingScore)
                .Take(Math.Max(0, pickCount - picks.Count)));

            return picks
                .OrderByDescending(GetRankingScore)
                .Take(pickCount)
                .ToList();
        }

        private static decimal GetRankingScore(IntradayTradeIdea idea)
        {
            return idea.Confidence + Math.Abs(idea.MomentumPercent * 3m) + Math.Abs(idea.DayChangePercent);
        }

        private static List<Quote> GetLatestSessionQuotes(List<Quote> quotes)
        {
            var latestDate = quotes
                .Select(quote => TimeZoneInfo.ConvertTimeFromUtc(quote.Date, MarketTimeZone).Date)
                .DefaultIfEmpty(DateTime.UtcNow.Date)
                .Max();

            return quotes
                .Where(quote => TimeZoneInfo.ConvertTimeFromUtc(quote.Date, MarketTimeZone).Date == latestDate)
                .OrderBy(quote => quote.Date)
                .ToList();
        }

        private static string[] NormalizeSymbols(IEnumerable<string>? symbols)
        {
            var normalized = (symbols ?? [])
                .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
                .Select(symbol => symbol.Trim().ToUpperInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return normalized.Length > 0
                ? normalized
                : ["AAPL", "MSFT", "NVDA", "AMD", "TSLA", "META", "AMZN", "SPY", "QQQ", "PLTR"];
        }

        private static List<Quote> TakeLast(List<Quote> quotes, int count)
        {
            return quotes.Skip(Math.Max(0, quotes.Count - count)).ToList();
        }

        private static decimal AverageClose(List<Quote> quotes, int period)
        {
            var sample = TakeLast(quotes, Math.Min(period, quotes.Count));
            if (sample.Count == 0)
            {
                return 0;
            }

            return sample.Average(quote => quote.Close);
        }

        private static decimal GetLatestRsi(List<RsiResult> rsiResults)
        {
            var lastRsi = rsiResults.LastOrDefault(result => result.Rsi.HasValue)?.Rsi;
            return lastRsi.HasValue ? (decimal)lastRsi.Value : 0;
        }

        private static int ScoreLongIdea(
            decimal currentPrice,
            decimal sma8,
            decimal sma20,
            decimal momentumPercent,
            decimal dayChangePercent,
            decimal rsi,
            decimal recentHigh,
            decimal averageRange)
        {
            var score = 0;
            if (currentPrice > sma8) score++;
            if (sma8 >= sma20) score++;
            if (momentumPercent > 0.18m) score++;
            if (dayChangePercent > 0.35m) score++;
            if (rsi >= 52m && rsi <= 74m) score++;
            if (currentPrice >= recentHigh - (averageRange * 0.35m)) score++;
            return score;
        }

        private static int ScoreShortIdea(
            decimal currentPrice,
            decimal sma8,
            decimal sma20,
            decimal momentumPercent,
            decimal dayChangePercent,
            decimal rsi,
            decimal recentLow,
            decimal averageRange)
        {
            var score = 0;
            if (currentPrice < sma8) score++;
            if (sma8 <= sma20) score++;
            if (momentumPercent < -0.18m) score++;
            if (dayChangePercent < -0.35m) score++;
            if (rsi >= 24m && rsi <= 48m) score++;
            if (currentPrice <= recentLow + (averageRange * 0.35m)) score++;
            return score;
        }

        private static string ResolveBias(int longScore, int shortScore, decimal dayChangePercent)
        {
            if (longScore == shortScore)
            {
                return dayChangePercent >= 0 ? "buy" : "sell";
            }

            return longScore > shortScore ? "buy" : "sell";
        }

        private static int CalculateConfidence(int longScore, int shortScore, decimal momentumPercent)
        {
            var strongestScore = Math.Max(longScore, shortScore);
            var scoreGap = Math.Abs(longScore - shortScore);
            var confidence = 48 + (strongestScore * 7) + (scoreGap * 4) + Math.Min((int)Math.Round(Math.Abs(momentumPercent) * 3m), 10);
            return Math.Clamp(confidence, 50, 95);
        }

        private static decimal CalculateRiskReward(decimal entryPrice, decimal stopLoss, decimal targetPrice)
        {
            var risk = Math.Abs(entryPrice - stopLoss);
            if (risk == 0)
            {
                return 0;
            }

            var reward = Math.Abs(targetPrice - entryPrice);
            return RoundMetric(reward / risk);
        }

        private static decimal PercentageChange(decimal currentPrice, decimal referencePrice)
        {
            if (referencePrice == 0)
            {
                return 0;
            }

            return ((currentPrice - referencePrice) / referencePrice) * 100m;
        }

        private static decimal ClampDecimal(decimal value, decimal min, decimal max)
        {
            return decimal.Clamp(value, min, max);
        }

        private static decimal RoundPrice(decimal value)
        {
            return decimal.Round(value, 2, MidpointRounding.AwayFromZero);
        }

        private static decimal RoundMetric(decimal value)
        {
            return decimal.Round(value, 2, MidpointRounding.AwayFromZero);
        }

        private static int GetMinimumBarCount()
        {
            return IsEarlySession() ? 8 : 12;
        }

        private static string BuildEmptyBoardStatus(string scannerName)
        {
            return IsEarlySession()
                ? $"Live market data is coming in, but the {scannerName} scanner is still gathering enough early-session candles to rank clean setups."
                : $"Live market data is active, but no {scannerName} setups match the current scanner rules right now.";
        }

        private static bool IsEarlySession()
        {
            var easternNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, MarketTimeZone).TimeOfDay;
            return easternNow >= new TimeSpan(9, 30, 0) && easternNow < new TimeSpan(10, 30, 0);
        }

        private static TimeZoneInfo ResolveMarketTimeZone()
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
            }
            catch (TimeZoneNotFoundException)
            {
                return TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
            }
        }
    }
}
