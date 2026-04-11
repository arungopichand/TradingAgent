namespace KrishAgent.Services
{
    public sealed class SymbolUniverseService
    {
        private readonly DataService _dataService;
        private readonly ILogger<SymbolUniverseService> _logger;

        public SymbolUniverseService(DataService dataService, ILogger<SymbolUniverseService> logger)
        {
            _dataService = dataService;
            _logger = logger;
        }

        public async Task<IReadOnlyList<string>> GetSymbolsAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var entries = await _dataService.GetWatchlistEntriesAsync();
                var symbols = entries
                    .Where(entry => !string.IsNullOrWhiteSpace(entry.Symbol))
                    .Select(entry => entry.Symbol.Trim().ToUpperInvariant())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                return symbols;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load the symbol universe");
                return [];
            }
        }
    }
}
