using KrishAgent.Data;

namespace KrishAgent.Services
{
    public class TradingDataService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<TradingDataService> _logger;
        private readonly TimeSpan _dataFetchInterval = TimeSpan.FromHours(1); // Fetch data hourly
        private readonly TimeSpan _alertCheckInterval = TimeSpan.FromMinutes(5); // Check alerts every 5 minutes

        public TradingDataService(IServiceProvider serviceProvider, ILogger<TradingDataService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("TradingDataService started");

            // Initial data fetch
            await FetchAndStoreMarketDataAsync();

            // Set up periodic tasks
            var dataFetchTimer = new PeriodicTimer(_dataFetchInterval);
            var alertCheckTimer = new PeriodicTimer(_alertCheckInterval);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Wait for either timer
                    var dataTask = dataFetchTimer.WaitForNextTickAsync(stoppingToken).AsTask();
                    var alertTask = alertCheckTimer.WaitForNextTickAsync(stoppingToken).AsTask();

                    var completedTask = await Task.WhenAny(dataTask, alertTask);

                    if (completedTask == dataTask)
                    {
                        await FetchAndStoreMarketDataAsync();
                    }
                    else if (completedTask == alertTask)
                    {
                        await CheckAndTriggerAlertsAsync();
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in TradingDataService execution loop");
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); // Wait before retry
                }
            }

            _logger.LogInformation("TradingDataService stopped");
        }

        private async Task FetchAndStoreMarketDataAsync()
        {
            try
            {
                _logger.LogInformation("Starting periodic market data fetch");

                using var scope = _serviceProvider.CreateScope();
                var marketService = scope.ServiceProvider.GetRequiredService<MarketService>();
                var indicatorService = scope.ServiceProvider.GetRequiredService<IndicatorService>();
                var dataService = scope.ServiceProvider.GetRequiredService<DataService>();

                var symbols = new[] { "AAPL", "TSLA", "SPY", "MSFT", "GOOGL", "NVDA" }; // Expanded symbol list

                foreach (var symbol in symbols)
                {
                    try
                    {
                        // Fetch market data
                        var json = await marketService.GetMarketData(symbol);
                        var quotes = indicatorService.ConvertAlpacaJsonToQuotes(json);

                        if (quotes.Any())
                        {
                            // Calculate additional indicators
                            var rsi = indicatorService.CalculateRsi(quotes);
                            var macd = indicatorService.CalculateMacd(quotes);
                            var bollingerBands = indicatorService.CalculateBollingerBands(quotes);
                            var movingAverages = indicatorService.CalculateMovingAverages(quotes);

                            // Create stock price records
                            var stockPrices = new List<StockPrice>();
                            for (int i = 0; i < quotes.Count; i++)
                            {
                                var quote = quotes[i];
                                var stockPrice = new StockPrice
                                {
                                    Symbol = symbol,
                                    Date = quote.Date,
                                    Open = quote.Open,
                                    High = quote.High,
                                    Low = quote.Low,
                                    Close = quote.Close,
                                    Volume = (long)quote.Volume,
                                    RSI = i >= 1 ? indicatorService.CalculateRsi(quotes.Take(i + 1).ToList()) : null,
                                    MACD_Line = macd.Count > i ? (decimal?)macd[i].Macd : null,
                                    MACD_Signal = macd.Count > i ? (decimal?)macd[i].Signal : null,
                                    MACD_Histogram = macd.Count > i ? (decimal?)macd[i].Histogram : null,
                                    Bollinger_Upper = bollingerBands.Count > i ? (decimal?)bollingerBands[i].UpperBand : null,
                                    Bollinger_Middle = bollingerBands.Count > i ? (decimal?)bollingerBands[i].Sma : null,
                                    Bollinger_Lower = bollingerBands.Count > i ? (decimal?)bollingerBands[i].LowerBand : null,
                                    MA20 = movingAverages.Item1.Count > i ? (decimal?)movingAverages.Item1[i].Sma : null,
                                    MA50 = movingAverages.Item2.Count > i ? (decimal?)movingAverages.Item2[i].Sma : null
                                };
                                stockPrices.Add(stockPrice);
                            }

                            await dataService.SaveStockPricesAsync(stockPrices);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error fetching data for {symbol}");
                    }
                }

                _logger.LogInformation("Completed periodic market data fetch");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in FetchAndStoreMarketDataAsync");
            }
        }

        private async Task CheckAndTriggerAlertsAsync()
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var dataService = scope.ServiceProvider.GetRequiredService<DataService>();

                var activeAlerts = await dataService.GetActiveAlertsAsync();

                foreach (var alert in activeAlerts)
                {
                    try
                    {
                        // Get latest price for the symbol
                        var latestPrices = await dataService.GetStockPricesAsync(alert.Symbol, limit: 1);
                        if (latestPrices.Any())
                        {
                            var currentPrice = latestPrices.First().Close;
                            bool shouldTrigger = false;

                            switch (alert.AlertType.ToLower())
                            {
                                case "price_above":
                                    shouldTrigger = currentPrice > alert.Threshold;
                                    break;
                                case "price_below":
                                    shouldTrigger = currentPrice < alert.Threshold;
                                    break;
                                case "rsi_above":
                                {
                                    var rsiPrices = await dataService.GetStockPricesAsync(alert.Symbol, limit: 30);
                                    var latest = rsiPrices.FirstOrDefault();
                                    if (latest?.RSI is decimal lastRsi)
                                    {
                                        shouldTrigger = lastRsi > alert.Threshold;
                                    }
                                    break;
                                }
                                case "rsi_below":
                                {
                                    var rsiPricesBelow = await dataService.GetStockPricesAsync(alert.Symbol, limit: 30);
                                    var latest = rsiPricesBelow.FirstOrDefault();
                                    if (latest?.RSI is decimal lastRsi)
                                    {
                                        shouldTrigger = lastRsi < alert.Threshold;
                                    }
                                    break;
                                }
                            }

                            if (shouldTrigger)
                            {
                                alert.IsTriggered = true;
                                alert.TriggeredAt = DateTime.UtcNow;
                                await dataService.UpdateAlertAsync(alert);

                                _logger.LogInformation($"Alert triggered: {alert.Symbol} {alert.AlertType} {alert.Threshold}");
                                // TODO: Send notification (email, SMS, etc.)
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error checking alert {alert.Id} for {alert.Symbol}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in CheckAndTriggerAlertsAsync");
            }
        }
    }
}