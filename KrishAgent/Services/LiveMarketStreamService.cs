using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using KrishAgent.Configuration;
using KrishAgent.Models;
using Microsoft.Extensions.Options;
using Skender.Stock.Indicators;

namespace KrishAgent.Services
{
    public sealed class LiveMarketStreamService : BackgroundService
    {
        private const int MaxMinuteBarsPerSymbol = 600;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IOptionsMonitor<AlpacaOptions> _alpacaOptions;
        private readonly IOptionsMonitor<TradingOptions> _tradingOptions;
        private readonly NodeBridgeService _nodeBridgeService;
        private readonly ILogger<LiveMarketStreamService> _logger;
        private readonly ConcurrentDictionary<string, SymbolMarketState> _states = new(StringComparer.OrdinalIgnoreCase);
        private volatile bool _subscriptionsDirty;
        private bool _preferNodeBridge;

        public LiveMarketStreamService(
            IServiceScopeFactory scopeFactory,
            IOptionsMonitor<AlpacaOptions> alpacaOptions,
            IOptionsMonitor<TradingOptions> tradingOptions,
            NodeBridgeService nodeBridgeService,
            ILogger<LiveMarketStreamService> logger)
        {
            _scopeFactory = scopeFactory;
            _alpacaOptions = alpacaOptions;
            _tradingOptions = tradingOptions;
            _nodeBridgeService = nodeBridgeService;
            _logger = logger;

            foreach (var symbol in GetConfiguredTrackedSymbols())
            {
                _states.TryAdd(symbol, new SymbolMarketState(symbol));
            }
        }

        public void MarkSubscriptionsDirty()
        {
            _subscriptionsDirty = true;
        }

        public bool TryGetSnapshot(string symbol, out LiveMarketSnapshot? snapshot)
        {
            snapshot = null;

            if (!_states.TryGetValue(symbol.Trim().ToUpperInvariant(), out var state))
            {
                return false;
            }

            lock (state.Gate)
            {
                if (state.MinuteBars.Count == 0)
                {
                    return false;
                }

                var oneMinuteQuotes = state.MinuteBars.Values
                    .OrderBy(quote => quote.Date)
                    .Select(CloneQuote)
                    .ToList();

                snapshot = new LiveMarketSnapshot
                {
                    Symbol = state.Symbol,
                    OneMinuteQuotes = oneMinuteQuotes,
                    FiveMinuteQuotes = BuildFiveMinuteQuotes(oneMinuteQuotes),
                    CurrentPrice = state.LastTradePrice ?? oneMinuteQuotes.LastOrDefault()?.Close,
                    CurrentPriceTimestampUtc = state.LastTradeTimestampUtc ?? oneMinuteQuotes.LastOrDefault()?.Date
                };
            }

            return snapshot is not null;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                await SeedHistoricalBarsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Initial market stream seed failed. Continuing to live stream connection.");
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (_preferNodeBridge)
                    {
                        await RunLiveStreamViaNodeAsync(stoppingToken);
                    }
                    else
                    {
                        await RunLiveStreamAsync(stoppingToken);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    if (!_preferNodeBridge && NodeBridgeService.IsTlsStackFailure(ex))
                    {
                        _preferNodeBridge = true;
                        _logger.LogWarning(ex, "Native live stream failed because the Windows TLS stack is unavailable. Switching to the Node stream bridge.");
                        continue;
                    }

                    _logger.LogWarning(ex, "Live market stream disconnected. Retrying in 5 seconds.");
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
            }
        }

        private async Task SeedHistoricalBarsAsync(CancellationToken cancellationToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var marketService = scope.ServiceProvider.GetRequiredService<MarketService>();
            var indicatorService = scope.ServiceProvider.GetRequiredService<IndicatorService>();
            var trackedSymbols = await GetTrackedSymbolsAsync(cancellationToken);

            EnsureStates(trackedSymbols);

            using var concurrencyGate = new SemaphoreSlim(6, 6);
            var seedTasks = trackedSymbols.Select(async symbol =>
            {
                await concurrencyGate.WaitAsync(cancellationToken);

                try
                {
                    var json = await marketService.GetMarketData(symbol, "1Min", 500, 2, cancellationToken);
                    var quotes = indicatorService.ConvertAlpacaJsonToQuotes(json)
                        .OrderBy(quote => quote.Date)
                        .TakeLast(MaxMinuteBarsPerSymbol)
                        .ToList();

                    if (quotes.Count == 0)
                    {
                        return;
                    }

                    var state = _states.GetOrAdd(symbol, normalizedSymbol => new SymbolMarketState(normalizedSymbol));
                    lock (state.Gate)
                    {
                        state.MinuteBars.Clear();
                        foreach (var quote in quotes)
                        {
                            state.MinuteBars[quote.Date] = CloneQuote(quote);
                        }

                        state.LastTradePrice = quotes.Last().Close;
                        state.LastTradeTimestampUtc = quotes.Last().Date;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to seed streaming cache for {Symbol}", symbol);
                }
                finally
                {
                    concurrencyGate.Release();
                }
            });

            await Task.WhenAll(seedTasks);
            _logger.LogInformation("Seeded live market cache for {SymbolCount} tracked symbols", _states.Count);
        }

        private async Task RunLiveStreamAsync(CancellationToken cancellationToken)
        {
            var streamUrl = GetStreamUrl();
            using var socket = new ClientWebSocket();
            if (ShouldBypassProxy())
            {
                socket.Options.Proxy = null;
            }
            await socket.ConnectAsync(new Uri(streamUrl), cancellationToken);
            _logger.LogInformation("Connected to Alpaca live stream at {StreamUrl}", streamUrl);

            await SendJsonAsync(socket, new
            {
                action = "auth",
                key = _alpacaOptions.CurrentValue.ApiKey?.Trim() ?? string.Empty,
                secret = _alpacaOptions.CurrentValue.SecretKey?.Trim() ?? string.Empty
            }, cancellationToken);

            var authenticated = false;

            while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                var payload = await ReceiveTextAsync(socket, cancellationToken);
                if (string.IsNullOrWhiteSpace(payload))
                {
                    if (authenticated && _subscriptionsDirty)
                    {
                        await SyncSubscriptionsAsync(socket, cancellationToken);
                    }

                    continue;
                }

                using var document = JsonDocument.Parse(payload);
                if (document.RootElement.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var message in document.RootElement.EnumerateArray())
                {
                    var messageType = message.TryGetProperty("T", out var typeValue)
                        ? typeValue.GetString()
                        : null;

                    if (string.Equals(messageType, "success", StringComparison.OrdinalIgnoreCase))
                    {
                        var successMessage = message.TryGetProperty("msg", out var msgValue)
                            ? msgValue.GetString()
                            : string.Empty;

                        if (string.Equals(successMessage, "authenticated", StringComparison.OrdinalIgnoreCase) && !authenticated)
                        {
                            authenticated = true;
                            await SyncSubscriptionsAsync(socket, cancellationToken);
                        }

                        continue;
                    }

                    if (string.Equals(messageType, "error", StringComparison.OrdinalIgnoreCase))
                    {
                        var errorMessage = message.TryGetProperty("msg", out var errorValue)
                            ? errorValue.GetString()
                            : "Unknown Alpaca stream error";
                        throw new InvalidOperationException($"Alpaca stream error: {errorMessage}");
                    }

                    if (string.Equals(messageType, "subscription", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    HandleMarketDataMessage(message);
                }

                if (authenticated && _subscriptionsDirty)
                {
                    await SyncSubscriptionsAsync(socket, cancellationToken);
                }
            }

            if (socket.State != WebSocketState.Closed && socket.State != WebSocketState.Aborted)
            {
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", cancellationToken);
            }
        }

        private async Task RunLiveStreamViaNodeAsync(CancellationToken cancellationToken)
        {
            var trackedSymbols = await GetTrackedSymbolsAsync(cancellationToken);
            EnsureStates(trackedSymbols);

            using var process = await _nodeBridgeService.StartAlpacaStreamAsync(new NodeAlpacaStreamRequest
            {
                Url = GetStreamUrl(),
                Key = _alpacaOptions.CurrentValue.ApiKey?.Trim() ?? string.Empty,
                Secret = _alpacaOptions.CurrentValue.SecretKey?.Trim() ?? string.Empty,
                Trades = trackedSymbols,
                Bars = trackedSymbols,
                UpdatedBars = trackedSymbols
            }, cancellationToken);

            _logger.LogInformation("Connected to Alpaca live stream via Node bridge at {StreamUrl}", GetStreamUrl());

            while (!cancellationToken.IsCancellationRequested)
            {
                if (_subscriptionsDirty)
                {
                    _subscriptionsDirty = false;
                    TryStopProcess(process);
                    return;
                }

                if (process.HasExited)
                {
                    var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
                    throw new InvalidOperationException(
                        $"Node stream bridge exited with code {process.ExitCode}. {stderr}".Trim());
                }

                var lineTask = process.StandardOutput.ReadLineAsync(cancellationToken).AsTask();
                var completedTask = await Task.WhenAny(lineTask, Task.Delay(500, cancellationToken));
                if (completedTask != lineTask)
                {
                    continue;
                }

                var payload = await lineTask;
                if (string.IsNullOrWhiteSpace(payload))
                {
                    continue;
                }

                ProcessStreamPayload(payload);
            }
        }

        private void ProcessStreamPayload(string payload)
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var message in document.RootElement.EnumerateArray())
            {
                var messageType = message.TryGetProperty("T", out var typeValue)
                    ? typeValue.GetString()
                    : null;

                if (string.Equals(messageType, "error", StringComparison.OrdinalIgnoreCase))
                {
                    var errorMessage = message.TryGetProperty("msg", out var errorValue)
                        ? errorValue.GetString()
                        : "Unknown Alpaca stream error";
                    throw new InvalidOperationException($"Alpaca stream error: {errorMessage}");
                }

                if (string.Equals(messageType, "subscription", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(messageType, "success", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                HandleMarketDataMessage(message);
            }
        }

        private void HandleMarketDataMessage(JsonElement message)
        {
            if (!message.TryGetProperty("S", out var symbolValue))
            {
                return;
            }

            var symbol = symbolValue.GetString();
            if (string.IsNullOrWhiteSpace(symbol))
            {
                return;
            }

            var state = _states.GetOrAdd(symbol.Trim().ToUpperInvariant(), normalizedSymbol => new SymbolMarketState(normalizedSymbol));
            var messageType = message.TryGetProperty("T", out var typeValue)
                ? typeValue.GetString()
                : string.Empty;

            switch (messageType)
            {
                case "t":
                    ApplyTrade(state, message);
                    break;
                case "b":
                case "u":
                    ApplyMinuteBar(state, message);
                    break;
            }
        }

        private void ApplyTrade(SymbolMarketState state, JsonElement message)
        {
            if (!message.TryGetProperty("p", out var priceValue) || !priceValue.TryGetDecimal(out var price))
            {
                return;
            }

            var timestamp = message.TryGetProperty("t", out var timestampValue)
                ? ParseTimestamp(timestampValue.GetString())
                : DateTime.UtcNow;

            lock (state.Gate)
            {
                state.LastTradePrice = price;
                state.LastTradeTimestampUtc = timestamp;
            }
        }

        private void ApplyMinuteBar(SymbolMarketState state, JsonElement message)
        {
            if (!TryBuildQuote(message, out var quote))
            {
                return;
            }

            lock (state.Gate)
            {
                state.MinuteBars[quote.Date] = quote;
                while (state.MinuteBars.Count > MaxMinuteBarsPerSymbol)
                {
                    state.MinuteBars.Remove(state.MinuteBars.Keys.First());
                }

                if (state.LastTradeTimestampUtc is null || quote.Date >= state.LastTradeTimestampUtc)
                {
                    state.LastTradePrice = quote.Close;
                    state.LastTradeTimestampUtc = quote.Date;
                }
            }
        }

        private static bool TryBuildQuote(JsonElement message, out Quote quote)
        {
            quote = new Quote();

            if (!message.TryGetProperty("o", out var openValue) ||
                !message.TryGetProperty("h", out var highValue) ||
                !message.TryGetProperty("l", out var lowValue) ||
                !message.TryGetProperty("c", out var closeValue) ||
                !message.TryGetProperty("v", out var volumeValue) ||
                !message.TryGetProperty("t", out var timestampValue))
            {
                return false;
            }

            var timestamp = ParseTimestamp(timestampValue.GetString());

            quote = new Quote
            {
                Date = timestamp,
                Open = openValue.GetDecimal(),
                High = highValue.GetDecimal(),
                Low = lowValue.GetDecimal(),
                Close = closeValue.GetDecimal(),
                Volume = volumeValue.GetDecimal()
            };

            return true;
        }

        private static Quote CloneQuote(Quote quote)
        {
            return new Quote
            {
                Date = quote.Date,
                Open = quote.Open,
                High = quote.High,
                Low = quote.Low,
                Close = quote.Close,
                Volume = quote.Volume
            };
        }

        private static List<Quote> BuildFiveMinuteQuotes(List<Quote> oneMinuteQuotes)
        {
            return oneMinuteQuotes
                .GroupBy(quote => AlignToFiveMinuteBoundary(quote.Date))
                .OrderBy(group => group.Key)
                .Select(group =>
                {
                    var orderedQuotes = group.OrderBy(quote => quote.Date).ToList();
                    return new Quote
                    {
                        Date = group.Key,
                        Open = orderedQuotes.First().Open,
                        High = orderedQuotes.Max(quote => quote.High),
                        Low = orderedQuotes.Min(quote => quote.Low),
                        Close = orderedQuotes.Last().Close,
                        Volume = orderedQuotes.Sum(quote => quote.Volume)
                    };
                })
                .ToList();
        }

        private static DateTime AlignToFiveMinuteBoundary(DateTime timestampUtc)
        {
            var minuteBucket = timestampUtc.Minute - (timestampUtc.Minute % 5);
            return new DateTime(
                timestampUtc.Year,
                timestampUtc.Month,
                timestampUtc.Day,
                timestampUtc.Hour,
                minuteBucket,
                0,
                DateTimeKind.Utc);
        }

        private async Task SendJsonAsync(ClientWebSocket socket, object payload, CancellationToken cancellationToken)
        {
            var json = JsonSerializer.Serialize(payload, JsonOptions);
            var bytes = Encoding.UTF8.GetBytes(json);
            await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
        }

        private static async Task<string> ReceiveTextAsync(ClientWebSocket socket, CancellationToken cancellationToken)
        {
            var buffer = new byte[8192];
            using var stream = new MemoryStream();

            while (true)
            {
                var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    throw new WebSocketException($"Alpaca stream closed: {result.CloseStatus} {result.CloseStatusDescription}");
                }

                stream.Write(buffer, 0, result.Count);
                if (result.EndOfMessage)
                {
                    break;
                }
            }

            return Encoding.UTF8.GetString(stream.ToArray());
        }

        private async Task SyncSubscriptionsAsync(ClientWebSocket socket, CancellationToken cancellationToken)
        {
            _subscriptionsDirty = false;

            var trackedSymbols = await GetTrackedSymbolsAsync(cancellationToken);
            EnsureStates(trackedSymbols);

            if (trackedSymbols.Length == 0)
            {
                _logger.LogInformation("No watchlist symbols are configured for live streaming yet.");
                return;
            }

            await SendJsonAsync(socket, new
            {
                action = "subscribe",
                trades = trackedSymbols,
                bars = trackedSymbols,
                updatedBars = trackedSymbols
            }, cancellationToken);

            _logger.LogInformation("Subscribed to Alpaca live channels for {SymbolCount} watchlist symbols", trackedSymbols.Length);
        }

        private async Task<string[]> GetTrackedSymbolsAsync(CancellationToken cancellationToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var dataService = scope.ServiceProvider.GetRequiredService<DataService>();

            var dayTradingSymbols = await dataService.GetWatchlistSymbolsAsync(
                WatchlistTypes.DayTrading,
                _tradingOptions.CurrentValue.DayTradingSymbols);
            var pennyStockSymbols = await dataService.GetWatchlistSymbolsAsync(
                WatchlistTypes.PennyStock,
                _tradingOptions.CurrentValue.PennyStockSymbols);

            cancellationToken.ThrowIfCancellationRequested();

            return NormalizeSymbols(dayTradingSymbols.Concat(pennyStockSymbols));
        }

        private string[] GetConfiguredTrackedSymbols()
        {
            return NormalizeSymbols(
                (_tradingOptions.CurrentValue.DayTradingSymbols ?? [])
                    .Concat(_tradingOptions.CurrentValue.PennyStockSymbols ?? []));
        }

        private void EnsureStates(IEnumerable<string> symbols)
        {
            foreach (var symbol in symbols)
            {
                _states.TryAdd(symbol, new SymbolMarketState(symbol));
            }
        }

        private static string[] NormalizeSymbols(IEnumerable<string>? symbols)
        {
            return (symbols ?? [])
                .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
                .Select(symbol => symbol.Trim().ToUpperInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private string GetStreamUrl()
        {
            var feed = (_alpacaOptions.CurrentValue.Feed ?? "iex").Trim().ToLowerInvariant();

            return feed switch
            {
                "boats" => "wss://stream.data.alpaca.markets/v1beta1/boats",
                "overnight" => "wss://stream.data.alpaca.markets/v1beta1/overnight",
                "sip" => "wss://stream.data.alpaca.markets/v2/sip",
                "delayed_sip" => "wss://stream.data.alpaca.markets/v2/delayed_sip",
                _ => "wss://stream.data.alpaca.markets/v2/iex"
            };
        }

        private static DateTime ParseTimestamp(string? timestamp)
        {
            return DateTimeOffset.Parse(
                    timestamp ?? DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind)
                .UtcDateTime;
        }

        private static bool ShouldBypassProxy()
        {
            var proxyCandidates = new[]
            {
                Environment.GetEnvironmentVariable("HTTPS_PROXY"),
                Environment.GetEnvironmentVariable("HTTP_PROXY"),
                Environment.GetEnvironmentVariable("ALL_PROXY")
            };

            return proxyCandidates.Any(IsBlackholeLoopbackProxy);
        }

        private static bool IsBlackholeLoopbackProxy(string? proxyValue)
        {
            if (string.IsNullOrWhiteSpace(proxyValue) || !Uri.TryCreate(proxyValue, UriKind.Absolute, out var proxyUri))
            {
                return false;
            }

            if (proxyUri.Port != 9)
            {
                return false;
            }

            if (string.Equals(proxyUri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return IPAddress.TryParse(proxyUri.Host, out var ipAddress) && IPAddress.IsLoopback(ipAddress);
        }

        private static void TryStopProcess(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // Ignore cleanup failures while reconnecting.
            }
        }

        private sealed class SymbolMarketState
        {
            public SymbolMarketState(string symbol)
            {
                Symbol = symbol;
            }

            public object Gate { get; } = new();

            public string Symbol { get; }

            public SortedDictionary<DateTime, Quote> MinuteBars { get; } = new();

            public decimal? LastTradePrice { get; set; }

            public DateTime? LastTradeTimestampUtc { get; set; }
        }
    }
}
