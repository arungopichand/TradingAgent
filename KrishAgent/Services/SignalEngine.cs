using System.Collections.Concurrent;
using KrishAgent.Models;

namespace KrishAgent.Services
{
    public sealed class SignalEngine : BackgroundService
    {
        private const int BatchSize = 5;
        private const int MaxResults = 20;
        private static readonly TimeSpan ScanInterval = TimeSpan.FromSeconds(15);

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<SignalEngine> _logger;
        private readonly ConcurrentDictionary<string, SignalScanResult> _latestSignals = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _rotationGate = new();
        private int _nextUniverseIndex;

        public SignalEngine(IServiceScopeFactory scopeFactory, ILogger<SignalEngine> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public Task<SignalBoard> GetBoardAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var results = _latestSignals.Values
                .OrderByDescending(signal => signal.ActivityScore)
                .ThenByDescending(signal => signal.LastScannedUtc)
                .Take(MaxResults)
                .ToList();

            return Task.FromResult(new SignalBoard
            {
                GeneratedAtUtc = DateTime.UtcNow,
                LastScanUtc = results.Count > 0 ? results.Max(result => result.LastScannedUtc) : null,
                CachedSignalCount = _latestSignals.Count,
                Results = results
            });
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("SignalEngine started. Scanning {BatchSize} symbols every {IntervalSeconds} seconds.", BatchSize, ScanInterval.TotalSeconds);

            while (!stoppingToken.IsCancellationRequested)
            {
                await RunScanCycleAsync(stoppingToken);

                try
                {
                    await Task.Delay(ScanInterval, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
            }
        }

        private async Task RunScanCycleAsync(CancellationToken stoppingToken)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var symbolUniverseService = scope.ServiceProvider.GetRequiredService<SymbolUniverseService>();
                var finnhubService = scope.ServiceProvider.GetRequiredService<FinnhubService>();

                var universe = (await symbolUniverseService.GetSymbolsAsync(stoppingToken)).ToList();
                if (universe.Count == 0)
                {
                    _logger.LogDebug("SignalEngine skipped a cycle because the symbol universe is empty.");
                    return;
                }

                var batch = GetNextBatch(universe, out var startIndex);
                if (batch.Count == 0)
                {
                    return;
                }

                _logger.LogInformation(
                    "SignalEngine scanning {BatchCount} symbols from universe size {UniverseCount} starting at index {StartIndex}.",
                    batch.Count,
                    universe.Count,
                    startIndex);

                foreach (var symbol in batch)
                {
                    stoppingToken.ThrowIfCancellationRequested();

                    try
                    {
                        var result = await BuildSignalAsync(symbol, finnhubService, stoppingToken);
                        if (result is null)
                        {
                            _latestSignals.TryRemove(symbol, out _);
                            continue;
                        }

                        _latestSignals[result.Symbol] = result;
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to process symbol {Symbol}", symbol);
                    }
                }

                _logger.LogInformation("SignalEngine cycle complete. Cached {SignalCount} active signals.", _latestSignals.Count);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SignalEngine scan cycle failed");
            }
        }

        private async Task<SignalScanResult?> BuildSignalAsync(
            string symbol,
            FinnhubService finnhubService,
            CancellationToken cancellationToken)
        {
            var quote = await finnhubService.GetQuoteAsync(symbol, cancellationToken);
            if (quote is null)
            {
                return null;
            }

            var currentPrice = quote.CurrentPrice;
            var previousClose = quote.PreviousClose;
            var high = quote.High;

            if (currentPrice is not > 0m || previousClose is not > 0m || high is not > 0m)
            {
                return null;
            }

            var current = currentPrice.Value;
            var prior = previousClose.Value;
            var sessionHigh = high.Value;
            var changePercent = ((current - prior) / prior) * 100m;

            var nhod = current >= sessionHigh;
            var momentum = changePercent > 3m;
            var bullish = changePercent > 1m;
            var bearish = changePercent < -1m;

            var signal = ResolvePrimarySignal(nhod, momentum, bullish, bearish);
            if (string.IsNullOrWhiteSpace(signal))
            {
                return null;
            }

            var activityScore = Math.Abs(changePercent);
            if (nhod)
            {
                activityScore += 3m;
            }

            if (momentum)
            {
                activityScore += 2m;
            }

            return new SignalScanResult
            {
                Symbol = symbol.Trim().ToUpperInvariant(),
                Signal = signal,
                CurrentPrice = RoundDecimal(current),
                PreviousClose = RoundDecimal(prior),
                High = RoundDecimal(sessionHigh),
                Low = RoundDecimal(quote.Low ?? 0m),
                ChangePercent = RoundDecimal(changePercent),
                ActivityScore = RoundDecimal(activityScore),
                IsNhod = nhod,
                IsMomentum = momentum,
                IsBullish = bullish,
                IsBearish = bearish,
                LastScannedUtc = DateTime.UtcNow
            };
        }

        private List<string> GetNextBatch(IReadOnlyList<string> universe, out int startIndex)
        {
            lock (_rotationGate)
            {
                if (universe.Count == 0)
                {
                    startIndex = 0;
                    return [];
                }

                if (_nextUniverseIndex >= universe.Count)
                {
                    _nextUniverseIndex = 0;
                }

                startIndex = _nextUniverseIndex;
                var batchSize = Math.Min(BatchSize, universe.Count);
                var batch = new List<string>(batchSize);

                for (var i = 0; i < batchSize; i++)
                {
                    var index = (startIndex + i) % universe.Count;
                    batch.Add(universe[index]);
                }

                _nextUniverseIndex = (startIndex + batchSize) % universe.Count;
                return batch;
            }
        }

        private static string ResolvePrimarySignal(bool nhod, bool momentum, bool bullish, bool bearish)
        {
            if (nhod)
            {
                return "🚀 NHOD";
            }

            if (momentum)
            {
                return "🔥 MOMENTUM";
            }

            if (bullish)
            {
                return "📈 BULLISH";
            }

            if (bearish)
            {
                return "📉 BEARISH";
            }

            return string.Empty;
        }

        private static decimal RoundDecimal(decimal value)
        {
            return Math.Round(value, 2, MidpointRounding.AwayFromZero);
        }
    }

    public sealed class SignalBoard
    {
        public DateTime GeneratedAtUtc { get; set; }

        public DateTime? LastScanUtc { get; set; }

        public int CachedSignalCount { get; set; }

        public List<SignalScanResult> Results { get; set; } = [];
    }

    public sealed class SignalScanResult
    {
        public string Symbol { get; set; } = string.Empty;

        public string Signal { get; set; } = string.Empty;

        public decimal CurrentPrice { get; set; }

        public decimal PreviousClose { get; set; }

        public decimal High { get; set; }

        public decimal Low { get; set; }

        public decimal ChangePercent { get; set; }

        public decimal ActivityScore { get; set; }

        public bool IsNhod { get; set; }

        public bool IsMomentum { get; set; }

        public bool IsBullish { get; set; }

        public bool IsBearish { get; set; }

        public DateTime LastScannedUtc { get; set; }
    }
}
