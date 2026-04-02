using AlgoBot.Configuration;
using AlgoBot.Helpers;
using AlgoBot.Interfaces;
using AlgoBot.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AlgoBot.Services;

public sealed class MetaApiHistoricalDataProvider : IHistoricalDataProvider
{
    private readonly IMarketDataProvider _marketDataProvider;
    private readonly IOptionsMonitor<BotSettings> _botSettings;
    private readonly ILogger<MetaApiHistoricalDataProvider> _logger;

    public MetaApiHistoricalDataProvider(
        IMarketDataProvider marketDataProvider,
        IOptionsMonitor<BotSettings> botSettings,
        ILogger<MetaApiHistoricalDataProvider> logger)
    {
        _marketDataProvider = marketDataProvider;
        _botSettings = botSettings;
        _logger = logger;
    }

    public async Task<IReadOnlyList<Candle>> GetCandlesRangeAsync(
        string instrument,
        string timeframe,
        DateTime startUtc,
        DateTime endUtc,
        CancellationToken cancellationToken = default)
    {
        if (endUtc <= startUtc)
            return Array.Empty<Candle>();

        var settings = _botSettings.CurrentValue.Backtest;
        var timeframeSpan = TradingTimeHelper.GetTimeframeSpan(timeframe);
        var batchSize = Math.Clamp(settings.CandleBatchSize, 1, 1000);

        // MetaApi historical candles are requested ending at startTime and loaded backward,
        // so we page backward from the end and then filter the final requested range. :contentReference[oaicite:2]{index=2}
        var all = new List<Candle>();
        var currentEnd = endUtc;

        while (currentEnd > startUtc)
        {
            var batch = await _marketDataProvider.GetCandlesAsync(
                instrument,
                timeframe,
                batchSize,
                currentEnd,
                cancellationToken);

            if (batch.Count == 0)
                break;

            all.AddRange(batch);

            var earliest = batch.Min(c => c.Time);
            if (earliest >= currentEnd)
                break;

            currentEnd = earliest.AddTicks(-timeframeSpan.Ticks);

            if (batch.Count < batchSize)
                break;

            await Task.Delay(100);
        }

        var filtered = all
            .Where(c => c.Time >= startUtc && c.Time <= endUtc)
            .GroupBy(c => c.Time)
            .Select(g => g.First())
            .OrderBy(c => c.Time)
            .ToList();

        _logger.LogInformation(
            "Loaded historical candles for backtest. Instrument={Instrument}, Timeframe={Timeframe}, Count={Count}, Start={StartUtc:O}, End={EndUtc:O}",
            instrument,
            timeframe,
            filtered.Count,
            startUtc,
            endUtc);

        return filtered;
    }
}