using KrishAgent.Configuration;
using KrishAgent.Models;
using Microsoft.Extensions.Options;
using Skender.Stock.Indicators;

namespace KrishAgent.Services
{
    public class PennyStockTradingService
    {
        private static readonly TimeZoneInfo MarketTimeZone = ResolveMarketTimeZone();

        private readonly MarketService _marketService;
        private readonly IndicatorService _indicatorService;
        private readonly TradingOptions _tradingOptions;
        private readonly ILogger<PennyStockTradingService> _logger;

        public PennyStockTradingService(
            MarketService marketService,
            IndicatorService indicatorService,
            IOptions<TradingOptions> tradingOptions,
            ILogger<PennyStockTradingService> logger)
        {
            _marketService = marketService;
            _indicatorService = indicatorService;
            _tradingOptions = tradingOptions.Value;
            _logger = logger;
        }

        public async Task<IntradayTradingBoard> GetBoardAsync(CancellationToken cancellationToken = default)
        {
            var symbols = NormalizeSymbols(_tradingOptions.PennyStockSymbols);
            var pickCount = Math.Clamp(_tradingOptions.PennyStockPickCount, 1, 10);
            var maxPrice = _tradingOptions.PennyStockMaxPrice <= 0 ? 10m : _tradingOptions.PennyStockMaxPrice;

            var tasks = symbols.Select(symbol => BuildIdeaSafeAsync(symbol, maxPrice, cancellationToken));
            var ideas = (await Task.WhenAll(tasks))
                .Where(idea => idea != null)
                .Cast<IntradayTradeIdea>()
                .OrderByDescending(GetRankingScore)
                .ToList();

            if (ideas.Count == 0)
            {
                throw new InvalidOperationException($"No penny-stock setups were found below ${maxPrice:F2} from the live market feed.");
            }

            var picks = ideas.Take(pickCount).ToList();
            var newestBar = picks.Max(pick => pick.LastUpdatedUtc);
            var marketStatus = DateTime.UtcNow - newestBar > TimeSpan.FromMinutes(20)
                ? "Latest bars are older than 20 minutes, so the market may be closed or the feed may be delayed."
                : "Live market feed is active. These setups are volatile and can move very quickly.";

            return new IntradayTradingBoard
            {
                GeneratedAtUtc = DateTime.UtcNow,
                Timeframe = "5-minute penny-stock momentum setups with a 1-minute price check",
                MarketStatus = marketStatus,
                BeginnerNote =
                    $"This scanner only looks for low-priced stocks under ${maxPrice:F2}. Penny stocks are highly speculative, so use small position sizes and risk only money you can afford to lose.",
                Picks = picks
            };
        }

        private async Task<IntradayTradeIdea?> BuildIdeaSafeAsync(string symbol, decimal maxPrice, CancellationToken cancellationToken)
        {
            try
            {
                return await BuildIdeaAsync(symbol, maxPrice, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to build penny-stock setup for {Symbol}", symbol);
                return null;
            }
        }

        private async Task<IntradayTradeIdea?> BuildIdeaAsync(string symbol, decimal maxPrice, CancellationToken cancellationToken)
        {
            var fiveMinuteJson = await _marketService.GetMarketData(symbol, "5Min", 78, 2, cancellationToken);
            var oneMinuteJson = await _marketService.GetMarketData(symbol, "1Min", 20, 1, cancellationToken);

            var fiveMinuteQuotes = GetLatestSessionQuotes(_indicatorService.ConvertAlpacaJsonToQuotes(fiveMinuteJson));
            var oneMinuteQuotes = GetLatestSessionQuotes(_indicatorService.ConvertAlpacaJsonToQuotes(oneMinuteJson));

            if (fiveMinuteQuotes.Count < 12)
            {
                throw new InvalidOperationException($"Not enough intraday data returned for {symbol}");
            }

            var currentQuote = oneMinuteQuotes.LastOrDefault() ?? fiveMinuteQuotes.Last();
            var currentPrice = currentQuote.Close;
            if (currentPrice <= 0 || currentPrice > maxPrice)
            {
                return null;
            }

            var currentPriceRounded = RoundPrice(currentPrice);
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
            var latestBarVolume = Convert.ToDecimal(fiveMinuteQuotes.Last().Volume);
            var sessionVolume = fiveMinuteQuotes.Sum(quote => Convert.ToInt64(quote.Volume));
            var averageVolume = recentQuotes.Count == 0 ? 0m : recentQuotes.Average(quote => (decimal)quote.Volume);
            var volumeSpike = averageVolume == 0 ? 1m : latestBarVolume / averageVolume;

            var score = ScorePennyIdea(currentPrice, sma8, sma20, momentumPercent, dayChangePercent, rsi, recentHigh, averageRange, volumeSpike, sessionVolume);
            if (score < 5 || rsi < 45m || momentumPercent <= 0 || dayChangePercent <= 0)
            {
                return null;
            }

            var confidence = CalculateConfidence(score, momentumPercent, volumeSpike, dayChangePercent);
            var entryBuffer = ClampDecimal(averageRange * 0.12m, currentPrice * 0.003m, currentPrice * 0.03m);
            var stopDistance = ClampDecimal(averageRange * 1.5m, currentPrice * 0.02m, currentPrice * 0.08m);
            var entryPrice = RoundPrice(Math.Max(currentPrice, recentHigh) + entryBuffer);
            var stopLoss = RoundPrice(entryPrice - stopDistance);
            var targetPrice1 = RoundPrice(entryPrice + (stopDistance * 1.8m));
            var targetPrice2 = RoundPrice(entryPrice + (stopDistance * 2.8m));

            return new IntradayTradeIdea
            {
                Symbol = symbol,
                Action = "buy",
                Direction = "Long",
                Setup = score >= 6 ? "Penny breakout" : "Penny momentum follow-through",
                CurrentPrice = currentPriceRounded,
                EntryPrice = entryPrice,
                StopLoss = stopLoss,
                TargetPrice1 = targetPrice1,
                TargetPrice2 = targetPrice2,
                RiskRewardRatio = CalculateRiskReward(entryPrice, stopLoss, targetPrice1),
                Rsi = RoundMetric(rsi),
                MomentumPercent = RoundMetric(momentumPercent),
                DayChangePercent = RoundMetric(dayChangePercent),
                Volume = sessionVolume,
                Confidence = confidence,
                WhyItWasPicked =
                    $"{symbol} is still trading under ${maxPrice:F2}, but it is showing strong short-term momentum, a volume spike of {RoundMetric(volumeSpike)}x, and strength near the session high.",
                WhatToDo =
                    $"Buy only if price trades above ${entryPrice:F2}. Keep the size small because penny stocks can reverse fast, and place the stop loss at ${stopLoss:F2} the moment you enter.",
                WhenToSell =
                    $"Take partial profit near ${targetPrice1:F2}. Exit the rest near ${targetPrice2:F2}, or sell immediately if the stock loses ${stopLoss:F2}.",
                BeginnerTip =
                    "Never average down on a penny stock. If the stop loss gets hit, accept the small loss and wait for the next setup.",
                LastUpdatedUtc = currentQuote.Date
            };
        }

        private static int ScorePennyIdea(
            decimal currentPrice,
            decimal sma8,
            decimal sma20,
            decimal momentumPercent,
            decimal dayChangePercent,
            decimal rsi,
            decimal recentHigh,
            decimal averageRange,
            decimal volumeSpike,
            long sessionVolume)
        {
            var score = 0;
            if (currentPrice > sma8) score++;
            if (sma8 >= sma20) score++;
            if (momentumPercent > 0.45m) score++;
            if (dayChangePercent > 1.0m) score++;
            if (rsi >= 52m && rsi <= 82m) score++;
            if (currentPrice >= recentHigh - (averageRange * 0.40m)) score++;
            if (volumeSpike >= 1.15m) score++;
            if (sessionVolume >= 30000) score++;
            return score;
        }

        private static decimal GetRankingScore(IntradayTradeIdea idea)
        {
            return idea.Confidence + (idea.MomentumPercent * 4m) + (idea.DayChangePercent * 2m);
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
                : ["PLUG", "OPEN", "FUBO", "CLOV", "QBTS", "RGTI", "ACHR", "LCID", "BBAI", "KULR", "RUN", "JOBY", "WULF", "LUNR", "RKLB", "SOFI", "MVST", "SIRI", "GRAB", "TMC"];
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

        private static int CalculateConfidence(int score, decimal momentumPercent, decimal volumeSpike, decimal dayChangePercent)
        {
            var confidence = 45 + (score * 7) + Math.Min((int)Math.Round(Math.Abs(momentumPercent) * 4m), 15) +
                Math.Min((int)Math.Round(volumeSpike * 4m), 12) +
                Math.Min((int)Math.Round(Math.Abs(dayChangePercent)), 10);

            return Math.Clamp(confidence, 55, 96);
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
