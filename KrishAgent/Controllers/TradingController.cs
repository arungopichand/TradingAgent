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
                            Confidence = Math.Clamp(aiItem.Confidence, 0, 100),
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
    }
}
