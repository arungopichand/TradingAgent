using Microsoft.AspNetCore.Mvc;
using KrishAgent.Services;
using KrishAgent.Models;
using System.Text.Json;

namespace KrishAgent.Controllers
{
    [ApiController]
    [Route("api")]
    public class TradingController : ControllerBase
    {
        private readonly MarketService _marketService;
        private readonly IndicatorService _indicatorService;
        private readonly AIService _aiService;
        private readonly DataService _dataService;

        public TradingController(MarketService marketService, IndicatorService indicatorService, AIService aiService, DataService dataService)
        {
            _marketService = marketService;
            _indicatorService = indicatorService;
            _aiService = aiService;
            _dataService = dataService;
        }

        [HttpGet("test")]
        public IActionResult Test()
        {
            return Ok("Krish Agent Running");
        }

        [HttpGet("trade/analyze")]
        public async Task<IActionResult> Analyze()
        {
            var symbols = new[] { "AAPL", "TSLA", "SPY" };
            return await PerformAnalysis(symbols);
        }

        [HttpPost("trade/analyze")]
        public async Task<IActionResult> AnalyzeCustom([FromBody] AnalysisRequest request)
        {
            if (request == null || request.Symbols == null || request.Symbols.Count == 0)
            {
                return BadRequest(new { error = "Symbols list cannot be empty" });
            }

            if (request.Symbols.Count > 20)
            {
                return BadRequest(new { error = "Maximum 20 symbols allowed per request" });
            }

            return await PerformAnalysis(request.Symbols.ToArray());
        }

        [HttpGet("stock/{symbol}/history")]
        public async Task<IActionResult> GetStockHistory(string symbol, [FromQuery] int limit = 50, [FromQuery] string? startDate = null, [FromQuery] string? endDate = null)
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                return BadRequest(new { error = "Symbol is required" });
            }

            DateTime? start = null;
            DateTime? end = null;
            DateTime parsedStart;
            DateTime parsedEnd;

            if (!string.IsNullOrWhiteSpace(startDate))
            {
                if (!DateTime.TryParse(startDate, out parsedStart))
                {
                    return BadRequest(new { error = "Invalid startDate format" });
                }

                start = parsedStart;
            }

            if (!string.IsNullOrWhiteSpace(endDate))
            {
                if (!DateTime.TryParse(endDate, out parsedEnd))
                {
                    return BadRequest(new { error = "Invalid endDate format" });
                }

                end = parsedEnd;
            }

            var prices = await _dataService.GetStockPricesAsync(symbol.ToUpperInvariant(), start, end, limit);
            return Ok(prices);
        }

        [HttpGet("analysis/history/{symbol}")]
        public async Task<IActionResult> GetAnalysisHistory(string symbol, [FromQuery] int limit = 50, [FromQuery] string? startDate = null)
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                return BadRequest(new { error = "Symbol is required" });
            }

            DateTime? start = null;
            if (!string.IsNullOrWhiteSpace(startDate))
            {
                if (!DateTime.TryParse(startDate, out var parsedStart))
                {
                    return BadRequest(new { error = "Invalid startDate format" });
                }

                start = parsedStart;
            }

            var history = await _dataService.GetAnalysisHistoryAsync(symbol.ToUpperInvariant(), start, limit);
            return Ok(history);
        }

        [HttpGet("alerts")]
        public async Task<IActionResult> GetAlerts()
        {
            var alerts = await _dataService.GetAllAlertsAsync();
            return Ok(alerts);
        }

        [HttpGet("alerts/{id}")]
        public async Task<IActionResult> GetAlert(int id)
        {
            var alert = await _dataService.GetAlertByIdAsync(id);
            if (alert == null)
            {
                return NotFound(new { error = "Alert not found" });
            }

            return Ok(alert);
        }

        [HttpPost("alerts")]
        public async Task<IActionResult> CreateAlert([FromBody] AlertRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Symbol) || string.IsNullOrWhiteSpace(request.AlertType))
            {
                return BadRequest(new { error = "Symbol and AlertType are required" });
            }

            var alert = new KrishAgent.Data.Alert
            {
                Symbol = request.Symbol.Trim().ToUpperInvariant(),
                AlertType = request.AlertType.Trim().ToLowerInvariant(),
                Threshold = request.Threshold,
                Condition = request.Condition?.Trim() ?? string.Empty,
                IsActive = request.IsActive,
                ExpiresAt = request.ExpiresAt
            };

            await _dataService.CreateAlertAsync(alert);
            return CreatedAtAction(nameof(GetAlert), new { id = alert.Id }, alert);
        }

        [HttpPut("alerts/{id}")]
        public async Task<IActionResult> UpdateAlert(int id, [FromBody] AlertRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Symbol) || string.IsNullOrWhiteSpace(request.AlertType))
            {
                return BadRequest(new { error = "Symbol and AlertType are required" });
            }

            var existing = await _dataService.GetAlertByIdAsync(id);
            if (existing == null)
            {
                return NotFound(new { error = "Alert not found" });
            }

            existing.Symbol = request.Symbol.Trim().ToUpperInvariant();
            existing.AlertType = request.AlertType.Trim().ToLowerInvariant();
            existing.Threshold = request.Threshold;
            existing.Condition = request.Condition?.Trim() ?? string.Empty;
            existing.IsActive = request.IsActive;
            existing.ExpiresAt = request.ExpiresAt;

            await _dataService.UpdateAlertAsync(existing);
            return Ok(existing);
        }

        [HttpDelete("alerts/{id}")]
        public async Task<IActionResult> DeleteAlert(int id)
        {
            var existing = await _dataService.GetAlertByIdAsync(id);
            if (existing == null)
            {
                return NotFound(new { error = "Alert not found" });
            }

            await _dataService.DeleteAlertAsync(existing);
            return NoContent();
        }

        [HttpGet("portfolio")]
        public async Task<IActionResult> GetPortfolioPositions([FromQuery] int limit = 100)
        {
            var positions = await _dataService.GetPortfolioPositionsAsync(limit);
            return Ok(positions);
        }

        [HttpGet("portfolio/{id}")]
        public async Task<IActionResult> GetPortfolioPosition(int id)
        {
            var position = await _dataService.GetPortfolioPositionByIdAsync(id);
            if (position == null)
            {
                return NotFound(new { error = "Portfolio position not found" });
            }

            return Ok(position);
        }

        [HttpPost("portfolio")]
        public async Task<IActionResult> CreatePortfolioPosition([FromBody] PortfolioPositionRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Symbol) || request.Quantity <= 0 || request.EntryPrice <= 0)
            {
                return BadRequest(new { error = "Symbol, quantity, and entry price are required" });
            }

            var position = new KrishAgent.Data.PortfolioPosition
            {
                Symbol = request.Symbol.Trim().ToUpperInvariant(),
                Quantity = request.Quantity,
                EntryPrice = request.EntryPrice,
                EntryDate = request.EntryDate,
                StopLoss = request.StopLoss,
                TakeProfit = request.TakeProfit,
                Notes = request.Notes?.Trim() ?? string.Empty,
                UpdatedAt = DateTime.UtcNow
            };

            await _dataService.CreatePortfolioPositionAsync(position);
            return CreatedAtAction(nameof(GetPortfolioPosition), new { id = position.Id }, position);
        }

        [HttpPut("portfolio/{id}")]
        public async Task<IActionResult> UpdatePortfolioPosition(int id, [FromBody] PortfolioPositionRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Symbol) || request.Quantity <= 0 || request.EntryPrice <= 0)
            {
                return BadRequest(new { error = "Symbol, quantity, and entry price are required" });
            }

            var existing = await _dataService.GetPortfolioPositionByIdAsync(id);
            if (existing == null)
            {
                return NotFound(new { error = "Portfolio position not found" });
            }

            existing.Symbol = request.Symbol.Trim().ToUpperInvariant();
            existing.Quantity = request.Quantity;
            existing.EntryPrice = request.EntryPrice;
            existing.EntryDate = request.EntryDate;
            existing.StopLoss = request.StopLoss;
            existing.TakeProfit = request.TakeProfit;
            existing.Notes = request.Notes?.Trim() ?? string.Empty;
            existing.UpdatedAt = DateTime.UtcNow;

            await _dataService.UpdatePortfolioPositionAsync(existing);
            return Ok(existing);
        }

        [HttpDelete("portfolio/{id}")]
        public async Task<IActionResult> DeletePortfolioPosition(int id)
        {
            var existing = await _dataService.GetPortfolioPositionByIdAsync(id);
            if (existing == null)
            {
                return NotFound(new { error = "Portfolio position not found" });
            }

            await _dataService.DeletePortfolioPositionAsync(existing);
            return NoContent();
        }

        [HttpGet("trades")]
        public async Task<IActionResult> GetTrades([FromQuery] int limit = 100)
        {
            var trades = await _dataService.GetTradesAsync(limit);
            return Ok(trades);
        }

        [HttpGet("trades/{id}")]
        public async Task<IActionResult> GetTrade(int id)
        {
            var trade = await _dataService.GetTradeByIdAsync(id);
            if (trade == null)
            {
                return NotFound(new { error = "Trade not found" });
            }

            return Ok(trade);
        }

        [HttpPost("trades")]
        public async Task<IActionResult> CreateTrade([FromBody] TradeRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Symbol) || string.IsNullOrWhiteSpace(request.Side) || request.Quantity <= 0 || request.EntryPrice <= 0)
            {
                return BadRequest(new { error = "Symbol, side, quantity, and entry price are required" });
            }

            var trade = new KrishAgent.Data.Trade
            {
                Symbol = request.Symbol.Trim().ToUpperInvariant(),
                Side = request.Side.Trim().ToLowerInvariant(),
                Quantity = request.Quantity,
                EntryPrice = request.EntryPrice,
                EntryDate = request.EntryDate,
                ExitPrice = request.ExitPrice,
                ExitDate = request.ExitDate,
                Pnl = request.Pnl,
                PnlPercent = request.PnlPercent,
                ExitReason = request.ExitReason?.Trim() ?? string.Empty,
                Notes = request.Notes?.Trim() ?? string.Empty,
                CreatedAt = DateTime.UtcNow
            };

            await _dataService.CreateTradeAsync(trade);
            return CreatedAtAction(nameof(GetTrade), new { id = trade.Id }, trade);
        }

        [HttpPut("trades/{id}")]
        public async Task<IActionResult> UpdateTrade(int id, [FromBody] TradeRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Symbol) || string.IsNullOrWhiteSpace(request.Side) || request.Quantity <= 0 || request.EntryPrice <= 0)
            {
                return BadRequest(new { error = "Symbol, side, quantity, and entry price are required" });
            }

            var existing = await _dataService.GetTradeByIdAsync(id);
            if (existing == null)
            {
                return NotFound(new { error = "Trade not found" });
            }

            existing.Symbol = request.Symbol.Trim().ToUpperInvariant();
            existing.Side = request.Side.Trim().ToLowerInvariant();
            existing.Quantity = request.Quantity;
            existing.EntryPrice = request.EntryPrice;
            existing.EntryDate = request.EntryDate;
            existing.ExitPrice = request.ExitPrice;
            existing.ExitDate = request.ExitDate;
            existing.Pnl = request.Pnl;
            existing.PnlPercent = request.PnlPercent;
            existing.ExitReason = request.ExitReason?.Trim() ?? string.Empty;
            existing.Notes = request.Notes?.Trim() ?? string.Empty;

            await _dataService.UpdateTradeAsync(existing);
            return Ok(existing);
        }

        [HttpDelete("trades/{id}")]
        public async Task<IActionResult> DeleteTrade(int id)
        {
            var existing = await _dataService.GetTradeByIdAsync(id);
            if (existing == null)
            {
                return NotFound(new { error = "Trade not found" });
            }

            await _dataService.DeleteTradeAsync(existing);
            return NoContent();
        }

        private async Task<IActionResult> PerformAnalysis(string[] symbols)
        {
            var normalizedSymbols = symbols
                .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
                .Select(symbol => symbol.Trim().ToUpperInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (normalizedSymbols.Length == 0)
            {
                return BadRequest(new { error = "Symbols list cannot be empty" });
            }

            try
            {
                var stockPayload = new List<StockSnapshot>();

                foreach (var symbol in normalizedSymbols)
                {
                    try
                    {
                        var json = await _marketService.GetMarketData(symbol);
                        var quotes = _indicatorService.ConvertAlpacaJsonToQuotes(json);
                        var rsi = _indicatorService.CalculateRsi(quotes);

                        var trend = quotes.Count >= 2 && quotes[^1].Close > quotes[^2].Close ? "up" : "down";
                        var lastQuote = quotes.Last();
                        var currentPrice = lastQuote?.Close ?? 0;
                        
                        // Ensure price is not zero
                        if (currentPrice == 0 && quotes.Count > 0)
                        {
                            currentPrice = quotes.LastOrDefault()?.Close ?? 0;
                        }

                        stockPayload.Add(new StockSnapshot
                        {
                            Symbol = symbol,
                            Price = currentPrice,
                            Rsi = rsi,
                            Trend = trend
                        });
                    }
                    catch (Exception ex)
                    {
                        return StatusCode(500, new { error = $"Failed to process {symbol}", details = ex.Message });
                    }
                }

                if (stockPayload.Count == 0)
                {
                    return BadRequest(new { error = "No stock data collected" });
                }

                var inputJson = JsonSerializer.Serialize(stockPayload);
                var aiContent = await _aiService.Analyze(inputJson);

                try
                {
                    var aiArray = ParseAiAnalysis(aiContent);
                    var stockLookup = stockPayload.ToDictionary(stock => stock.Symbol, StringComparer.OrdinalIgnoreCase);
                    var resultList = new List<AnalysisResultItem>();

                    foreach (var aiItem in aiArray)
                    {
                        if (string.IsNullOrWhiteSpace(aiItem.Symbol) ||
                            !stockLookup.TryGetValue(aiItem.Symbol, out var stockData))
                        {
                            continue;
                        }

                        resultList.Add(new AnalysisResultItem
                        {
                            Symbol = stockData.Symbol,
                            Price = stockData.Price,
                            Rsi = stockData.Rsi,
                            Trend = NormalizeTrend(aiItem.Trend, stockData.Trend),
                            Confidence = NormalizeConfidence(aiItem.Confidence),
                            Reason = string.IsNullOrWhiteSpace(aiItem.Reason) ? "No reason provided." : aiItem.Reason.Trim(),
                            Action = NormalizeAction(aiItem.Action)
                        });
                    }

                    if (resultList.Count == 0)
                    {
                        return StatusCode(502, new { error = "AI response did not include any matching symbols" });
                    }

                    // Save analysis results to database
                    foreach (var result in resultList)
                    {
                        var analysisHistory = new KrishAgent.Data.AnalysisHistory
                        {
                            Symbol = result.Symbol,
                            Date = DateTime.UtcNow.Date,
                            Price = result.Price,
                            RSI = result.Rsi,
                            Trend = result.Trend,
                            Action = result.Action,
                            Confidence = result.Confidence,
                            Reason = result.Reason,
                            AIModel = "gpt-3.5-turbo"
                        };

                        await _dataService.SaveAnalysisHistoryAsync(analysisHistory);
                    }

                    return Ok(resultList);
                }
                catch (Exception parseEx)
                {
                    return StatusCode(500, new { error = "Failed to parse AI response", details = parseEx.Message });
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Analysis failed", details = ex.Message });
            }
        }

        private static List<AiAnalysisItem> ParseAiAnalysis(string aiContent)
        {
            if (string.IsNullOrWhiteSpace(aiContent))
            {
                throw new InvalidOperationException("AI response was empty");
            }

            var content = aiContent.Trim();
            if (content.StartsWith("```", StringComparison.Ordinal))
            {
                content = content.Replace("```json", "", StringComparison.OrdinalIgnoreCase)
                                 .Replace("```", "", StringComparison.Ordinal)
                                 .Trim();
            }

            var aiArray = JsonSerializer.Deserialize<List<AiAnalysisItem>>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (aiArray == null || aiArray.Count == 0)
            {
                throw new InvalidOperationException("AI response did not contain any analysis items");
            }

            return aiArray;
        }

        private static string NormalizeAction(string? action)
        {
            return action?.Trim().ToLowerInvariant() switch
            {
                "buy_watch" => "buy_watch",
                "sell_watch" => "sell_watch",
                "hold" => "hold",
                _ => "hold"
            };
        }

        private static string NormalizeTrend(string? aiTrend, string fallbackTrend)
        {
            return aiTrend?.Trim().ToLowerInvariant() switch
            {
                "up" => "up",
                "down" => "down",
                _ => fallbackTrend
            };
        }

        private static int NormalizeConfidence(decimal confidence)
        {
            var rounded = decimal.Round(confidence, 0, MidpointRounding.AwayFromZero);
            return decimal.ToInt32(decimal.Clamp(rounded, 0, 100));
        }
    }
}
