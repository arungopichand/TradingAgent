using KrishAgent.Data;
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
            }
        }
    }
}