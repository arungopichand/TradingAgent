using KrishAgent.Configuration;
using KrishAgent.Data;
using KrishAgent.Models;
using Microsoft.Extensions.Options;

namespace KrishAgent.Services
{
    public class TradingDataService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<TradingDataService> _logger;
        private readonly string[] _defaultBackgroundSymbols;
        private readonly TimeSpan _dataFetchInterval;
        private readonly TimeSpan _alertCheckInterval;
        private readonly bool _runInitialBackgroundFetch;

        public TradingDataService(
            IServiceScopeFactory scopeFactory,
            IOptions<TradingOptions> tradingOptions,
            ILogger<TradingDataService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;

            var options = tradingOptions.Value;
            _defaultBackgroundSymbols = NormalizeSymbols(options.BackgroundSymbols);
            _dataFetchInterval = TimeSpan.FromMinutes(Math.Max(1, options.DataFetchIntervalMinutes));
            _alertCheckInterval = TimeSpan.FromMinutes(Math.Max(1, options.AlertCheckIntervalMinutes));
            _runInitialBackgroundFetch = options.RunInitialBackgroundFetch;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation(
                "TradingDataService started. Default background watchlist has {SymbolCount} symbols. Market sync every {MarketSyncMinutes} minutes. Alert check every {AlertMinutes} minutes.",
                _defaultBackgroundSymbols.Length,
                _dataFetchInterval.TotalMinutes,
                _alertCheckInterval.TotalMinutes);

            var scheduledTasks = new[]
            {
                RunPeriodicTaskAsync("market data fetch", _dataFetchInterval, _runInitialBackgroundFetch, FetchAndStoreMarketDataAsync, stoppingToken),
                RunPeriodicTaskAsync("alert check", _alertCheckInterval, false, CheckAndTriggerAlertsAsync, stoppingToken)
            };

            try
            {
                await Task.WhenAll(scheduledTasks);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Expected during shutdown.
            }

            _logger.LogInformation("TradingDataService stopped");
        }

        private async Task RunPeriodicTaskAsync(
            string taskName,
            TimeSpan interval,
            bool runImmediately,
            Func<CancellationToken, Task> work,
            CancellationToken stoppingToken)
        {
            if (runImmediately)
            {
                await ExecuteSafelyAsync(taskName, work, stoppingToken);
            }

            using var timer = new PeriodicTimer(interval);

            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await ExecuteSafelyAsync(taskName, work, stoppingToken);
            }
        }

        private async Task ExecuteSafelyAsync(
            string taskName,
            Func<CancellationToken, Task> work,
            CancellationToken stoppingToken)
        {
            try
            {
                await work(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Expected during shutdown.
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during scheduled {TaskName}", taskName);
            }
        }

        private async Task FetchAndStoreMarketDataAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Starting periodic market data fetch");

            using var scope = _scopeFactory.CreateScope();
            var marketService = scope.ServiceProvider.GetRequiredService<MarketService>();
            var indicatorService = scope.ServiceProvider.GetRequiredService<IndicatorService>();
            var dataService = scope.ServiceProvider.GetRequiredService<DataService>();
            var backgroundSymbols = NormalizeSymbols(await dataService.GetWatchlistSymbolsAsync(
                WatchlistTypes.Background,
                _defaultBackgroundSymbols));

            foreach (var symbol in backgroundSymbols)
            {
                stoppingToken.ThrowIfCancellationRequested();

                try
                {
                    var json = await marketService.GetMarketData(symbol, stoppingToken);
                    var quotes = indicatorService.ConvertAlpacaJsonToQuotes(json);

                    if (!quotes.Any())
                    {
                        continue;
                    }

                    var rsiSeries = indicatorService.CalculateRsiSeries(quotes);
                    var macd = indicatorService.CalculateMacd(quotes);
                    var bollingerBands = indicatorService.CalculateBollingerBands(quotes);
                    var movingAverages = indicatorService.CalculateMovingAverages(quotes);

                    var stockPrices = new List<StockPrice>();
                    for (int i = 0; i < quotes.Count; i++)
                    {
                        var quote = quotes[i];
                        stockPrices.Add(new StockPrice
                        {
                            Symbol = symbol,
                            Date = quote.Date,
                            Open = quote.Open,
                            High = quote.High,
                            Low = quote.Low,
                            Close = quote.Close,
                            Volume = (long)quote.Volume,
                            RSI = rsiSeries.Count > i ? (decimal?)rsiSeries[i].Rsi : null,
                            MACD_Line = macd.Count > i ? (decimal?)macd[i].Macd : null,
                            MACD_Signal = macd.Count > i ? (decimal?)macd[i].Signal : null,
                            MACD_Histogram = macd.Count > i ? (decimal?)macd[i].Histogram : null,
                            Bollinger_Upper = bollingerBands.Count > i ? (decimal?)bollingerBands[i].UpperBand : null,
                            Bollinger_Middle = bollingerBands.Count > i ? (decimal?)bollingerBands[i].Sma : null,
                            Bollinger_Lower = bollingerBands.Count > i ? (decimal?)bollingerBands[i].LowerBand : null,
                            MA20 = movingAverages.Item1.Count > i ? (decimal?)movingAverages.Item1[i].Sma : null,
                            MA50 = movingAverages.Item2.Count > i ? (decimal?)movingAverages.Item2[i].Sma : null
                        });
                    }

                    await dataService.SaveStockPricesAsync(stockPrices);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error fetching data for {Symbol}", symbol);
                }
            }

            _logger.LogInformation("Completed periodic market data fetch");
        }

        private async Task CheckAndTriggerAlertsAsync(CancellationToken stoppingToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var dataService = scope.ServiceProvider.GetRequiredService<DataService>();

            var activeAlerts = await dataService.GetActiveAlertsAsync();

            foreach (var alert in activeAlerts)
            {
                stoppingToken.ThrowIfCancellationRequested();

                try
                {
                    var latestPrices = await dataService.GetStockPricesAsync(alert.Symbol, limit: 1);
                    if (!latestPrices.Any())
                    {
                        continue;
                    }

                    var currentPrice = latestPrices.First().Close;
                    var shouldTrigger = alert.AlertType.ToLowerInvariant() switch
                    {
                        "price_above" => currentPrice > alert.Threshold,
                        "price_below" => currentPrice < alert.Threshold,
                        "rsi_above" => await ShouldTriggerRsiAlertAsync(dataService, alert.Symbol, alert.Threshold, isAbove: true),
                        "rsi_below" => await ShouldTriggerRsiAlertAsync(dataService, alert.Symbol, alert.Threshold, isAbove: false),
                        _ => false
                    };

                    if (!shouldTrigger)
                    {
                        continue;
                    }

                    alert.IsTriggered = true;
                    alert.TriggeredAt = DateTime.UtcNow;
                    await dataService.UpdateAlertAsync(alert);

                    _logger.LogInformation("Alert triggered: {Symbol} {AlertType} {Threshold}", alert.Symbol, alert.AlertType, alert.Threshold);
                    // TODO: Send notification (email, SMS, etc.)
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error checking alert {AlertId} for {Symbol}", alert.Id, alert.Symbol);
                }
            }
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
                : ["AAPL", "TSLA", "SPY", "MSFT", "GOOGL", "NVDA"];
        }

        private static async Task<bool> ShouldTriggerRsiAlertAsync(
            DataService dataService,
            string symbol,
            decimal threshold,
            bool isAbove)
        {
            var rsiPrices = await dataService.GetStockPricesAsync(symbol, limit: 30);
            var latest = rsiPrices.FirstOrDefault();
            if (latest?.RSI is not decimal lastRsi)
            {
                return false;
            }

            return isAbove ? lastRsi > threshold : lastRsi < threshold;
        }
    }
}
