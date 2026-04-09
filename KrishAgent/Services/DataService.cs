using KrishAgent.Configuration;
using KrishAgent.Data;
using KrishAgent.Models;
using Microsoft.EntityFrameworkCore;

namespace KrishAgent.Services
{
    public class DataService
    {
        private readonly TradingContext _context;
        private readonly ILogger<DataService> _logger;

        public DataService(TradingContext context, ILogger<DataService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task SaveStockPricesAsync(List<StockPrice> prices)
        {
            try
            {
                foreach (var price in prices)
                {
                    // Check if price already exists
                    var existing = await _context.StockPrices
                        .FirstOrDefaultAsync(sp => sp.Symbol == price.Symbol && sp.Date == price.Date);

                    if (existing == null)
                    {
                        _context.StockPrices.Add(price);
                    }
                    else
                    {
                        // Update existing record
                        existing.Open = price.Open;
                        existing.High = price.High;
                        existing.Low = price.Low;
                        existing.Close = price.Close;
                        existing.Volume = price.Volume;
                        existing.RSI = price.RSI;
                        existing.MACD_Line = price.MACD_Line;
                        existing.MACD_Signal = price.MACD_Signal;
                        existing.MACD_Histogram = price.MACD_Histogram;
                        existing.Bollinger_Upper = price.Bollinger_Upper;
                        existing.Bollinger_Middle = price.Bollinger_Middle;
                        existing.Bollinger_Lower = price.Bollinger_Lower;
                        existing.MA20 = price.MA20;
                        existing.MA50 = price.MA50;
                        existing.CreatedAt = DateTime.UtcNow;
                    }
                }

                await _context.SaveChangesAsync();
                _logger.LogInformation($"Saved {prices.Count} stock prices to database");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving stock prices to database");
                throw;
            }
        }

        public async Task SaveAnalysisHistoryAsync(AnalysisHistory analysis)
        {
            try
            {
                _context.AnalysisHistory.Add(analysis);
                await _context.SaveChangesAsync();
                _logger.LogInformation($"Saved analysis for {analysis.Symbol}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving analysis history to database");
                throw;
            }
        }

        public async Task EnsureWatchlistsSeededAsync(TradingOptions tradingOptions)
        {
            try
            {
                if (await _context.WatchlistEntries.AnyAsync())
                {
                    return;
                }

                var seededEntries = BuildSeedEntries(tradingOptions);
                _context.WatchlistEntries.AddRange(seededEntries);
                await _context.SaveChangesAsync();
                _logger.LogInformation("Seeded {Count} watchlist entries from Trading options", seededEntries.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error seeding watchlists");
                throw;
            }
        }

        public async Task<List<WatchlistEntry>> GetWatchlistEntriesAsync(string? listType = null)
        {
            try
            {
                var query = _context.WatchlistEntries.Where(entry => entry.IsActive);
                var normalizedListType = NormalizeListType(listType);

                if (!string.IsNullOrWhiteSpace(normalizedListType))
                {
                    query = query.Where(entry => entry.ListType == normalizedListType);
                }

                return await query
                    .OrderBy(entry => entry.ListType)
                    .ThenBy(entry => entry.SortOrder)
                    .ThenBy(entry => entry.Symbol)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving watchlist entries");
                return [];
            }
        }

        public async Task<List<string>> GetWatchlistSymbolsAsync(string listType, IEnumerable<string>? fallbackSymbols = null)
        {
            try
            {
                var normalizedListType = NormalizeListType(listType);
                if (string.IsNullOrWhiteSpace(normalizedListType))
                {
                    return NormalizeSymbols(fallbackSymbols);
                }

                var symbols = await _context.WatchlistEntries
                    .Where(entry => entry.IsActive && entry.ListType == normalizedListType)
                    .OrderBy(entry => entry.SortOrder)
                    .ThenBy(entry => entry.Symbol)
                    .Select(entry => entry.Symbol)
                    .ToListAsync();

                return symbols.Count > 0
                    ? symbols
                    : NormalizeSymbols(fallbackSymbols);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving watchlist symbols for {ListType}", listType);
                return NormalizeSymbols(fallbackSymbols);
            }
        }

        public async Task<WatchlistEntry?> GetWatchlistEntryByIdAsync(int id)
        {
            try
            {
                return await _context.WatchlistEntries.FindAsync(id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving watchlist entry {Id}", id);
                return null;
            }
        }

        public async Task<WatchlistEntry> CreateWatchlistEntryAsync(string listType, string symbol)
        {
            try
            {
                var normalizedListType = NormalizeListType(listType);
                var normalizedSymbol = NormalizeSymbol(symbol);

                if (string.IsNullOrWhiteSpace(normalizedListType) || string.IsNullOrWhiteSpace(normalizedSymbol))
                {
                    throw new InvalidOperationException("List type and symbol are required.");
                }

                var existing = await _context.WatchlistEntries
                    .FirstOrDefaultAsync(entry => entry.ListType == normalizedListType && entry.Symbol == normalizedSymbol);

                if (existing != null)
                {
                    existing.IsActive = true;
                    existing.UpdatedAt = DateTime.UtcNow;
                    await _context.SaveChangesAsync();
                    return existing;
                }

                var nextSortOrder = await _context.WatchlistEntries
                    .Where(entry => entry.ListType == normalizedListType)
                    .Select(entry => (int?)entry.SortOrder)
                    .MaxAsync() ?? -1;

                var entry = new WatchlistEntry
                {
                    ListType = normalizedListType,
                    Symbol = normalizedSymbol,
                    SortOrder = nextSortOrder + 1,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                _context.WatchlistEntries.Add(entry);
                await _context.SaveChangesAsync();
                return entry;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating watchlist entry for {ListType} / {Symbol}", listType, symbol);
                throw;
            }
        }

        public async Task DeleteWatchlistEntryAsync(WatchlistEntry entry)
        {
            try
            {
                _context.WatchlistEntries.Remove(entry);
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting watchlist entry {Id}", entry.Id);
                throw;
            }
        }

        public async Task<List<StockPrice>> GetStockPricesAsync(string symbol, DateTime? startDate = null, DateTime? endDate = null, int limit = 100)
        {
            try
            {
                var query = _context.StockPrices.Where(sp => sp.Symbol == symbol);

                if (startDate.HasValue)
                    query = query.Where(sp => sp.Date >= startDate.Value);

                if (endDate.HasValue)
                    query = query.Where(sp => sp.Date <= endDate.Value);

                return await query
                    .OrderByDescending(sp => sp.Date)
                    .Take(limit)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error retrieving stock prices for {symbol}");
                return new List<StockPrice>();
            }
        }

        public async Task<List<AnalysisHistory>> GetAnalysisHistoryAsync(string symbol, DateTime? startDate = null, int limit = 50)
        {
            try
            {
                var query = _context.AnalysisHistory.Where(ah => ah.Symbol == symbol);

                if (startDate.HasValue)
                    query = query.Where(ah => ah.Date >= startDate.Value);

                return await query
                    .OrderByDescending(ah => ah.CreatedAt)
                    .Take(limit)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error retrieving analysis history for {symbol}");
                return new List<AnalysisHistory>();
            }
        }

        public async Task<List<Alert>> GetActiveAlertsAsync()
        {
            try
            {
                return await _context.Alerts
                    .Where(a => a.IsActive && !a.IsTriggered)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving active alerts");
                return new List<Alert>();
            }
        }

        public async Task UpdateAlertAsync(Alert alert)
        {
            try
            {
                _context.Alerts.Update(alert);
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating alert {alert.Id}");
                throw;
            }
        }

        public async Task<List<Alert>> GetAllAlertsAsync()
        {
            try
            {
                return await _context.Alerts
                    .OrderByDescending(a => a.CreatedAt)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving alerts");
                return new List<Alert>();
            }
        }

        public async Task<Alert?> GetAlertByIdAsync(int id)
        {
            try
            {
                return await _context.Alerts.FindAsync(id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error retrieving alert {id}");
                return null;
            }
        }

        public async Task CreateAlertAsync(Alert alert)
        {
            try
            {
                _context.Alerts.Add(alert);
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating alert");
                throw;
            }
        }

        public async Task DeleteAlertAsync(Alert alert)
        {
            try
            {
                _context.Alerts.Remove(alert);
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error deleting alert {alert.Id}");
                throw;
            }
        }

        public async Task<List<PortfolioPosition>> GetPortfolioPositionsAsync(int limit = 100)
        {
            try
            {
                return await _context.PortfolioPositions
                    .OrderByDescending(p => p.UpdatedAt)
                    .Take(limit)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving portfolio positions");
                return new List<PortfolioPosition>();
            }
        }

        public async Task<PortfolioPosition?> GetPortfolioPositionByIdAsync(int id)
        {
            try
            {
                return await _context.PortfolioPositions.FindAsync(id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error retrieving portfolio position {id}");
                return null;
            }
        }

        public async Task CreatePortfolioPositionAsync(PortfolioPosition position)
        {
            try
            {
                _context.PortfolioPositions.Add(position);
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating portfolio position");
                throw;
            }
        }

        public async Task UpdatePortfolioPositionAsync(PortfolioPosition position)
        {
            try
            {
                _context.PortfolioPositions.Update(position);
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating portfolio position {position.Id}");
                throw;
            }
        }

        public async Task DeletePortfolioPositionAsync(PortfolioPosition position)
        {
            try
            {
                _context.PortfolioPositions.Remove(position);
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error deleting portfolio position {position.Id}");
                throw;
            }
        }

        public async Task<List<Trade>> GetTradesAsync(int limit = 100)
        {
            try
            {
                return await _context.Trades
                    .OrderByDescending(t => t.EntryDate)
                    .Take(limit)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving trades");
                return new List<Trade>();
            }
        }

        public async Task<Trade?> GetTradeByIdAsync(int id)
        {
            try
            {
                return await _context.Trades.FindAsync(id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error retrieving trade {id}");
                return null;
            }
        }

        public async Task CreateTradeAsync(Trade trade)
        {
            try
            {
                _context.Trades.Add(trade);
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating trade");
                throw;
            }
        }

        public async Task UpdateTradeAsync(Trade trade)
        {
            try
            {
                _context.Trades.Update(trade);
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating trade {trade.Id}");
                throw;
            }
        }

        public async Task DeleteTradeAsync(Trade trade)
        {
            try
            {
                _context.Trades.Remove(trade);
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error deleting trade {trade.Id}");
                throw;
            }
        }

        private static List<WatchlistEntry> BuildSeedEntries(TradingOptions tradingOptions)
        {
            var seededEntries = new List<WatchlistEntry>();
            AddSeedEntries(seededEntries, WatchlistTypes.Analysis, tradingOptions.AnalysisSymbols);
            AddSeedEntries(seededEntries, WatchlistTypes.Background, tradingOptions.BackgroundSymbols);
            AddSeedEntries(seededEntries, WatchlistTypes.DayTrading, tradingOptions.DayTradingSymbols);
            AddSeedEntries(seededEntries, WatchlistTypes.PennyStock, tradingOptions.PennyStockSymbols);
            return seededEntries;
        }

        private static void AddSeedEntries(List<WatchlistEntry> entries, string listType, IEnumerable<string>? symbols)
        {
            var orderedSymbols = NormalizeSymbols(symbols);
            for (var i = 0; i < orderedSymbols.Count; i++)
            {
                entries.Add(new WatchlistEntry
                {
                    ListType = listType,
                    Symbol = orderedSymbols[i],
                    SortOrder = i,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
            }
        }

        private static List<string> NormalizeSymbols(IEnumerable<string>? symbols)
        {
            return (symbols ?? [])
                .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
                .Select(NormalizeSymbol)
                .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string NormalizeSymbol(string? symbol)
        {
            return symbol?.Trim().ToUpperInvariant() ?? string.Empty;
        }

        private static string NormalizeListType(string? listType)
        {
            return listType?.Trim().ToLowerInvariant() switch
            {
                WatchlistTypes.Analysis => WatchlistTypes.Analysis,
                WatchlistTypes.Background => WatchlistTypes.Background,
                WatchlistTypes.DayTrading => WatchlistTypes.DayTrading,
                WatchlistTypes.PennyStock => WatchlistTypes.PennyStock,
                "daytrading" => WatchlistTypes.DayTrading,
                "day-trading" => WatchlistTypes.DayTrading,
                "penny" => WatchlistTypes.PennyStock,
                "penny-stocks" => WatchlistTypes.PennyStock,
                "penny_stocks" => WatchlistTypes.PennyStock,
                _ => string.Empty
            };
        }
    }
}
