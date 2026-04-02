using AlgoBot.Interfaces;
using AlgoBot.Models;

namespace AlgoBot.Helpers;

public static class IndicatorCandleHelper
{
    public static DateTime GetLastClosedCandleTimeUtc(DateTime evaluationTimeUtc, string timeframe)
    {
        var span = TradingTimeHelper.GetTimeframeSpan(timeframe);

        var alignedTicks = evaluationTimeUtc.Ticks - (evaluationTimeUtc.Ticks % span.Ticks);
        var boundary = new DateTime(alignedTicks, DateTimeKind.Utc);

        // use the last fully closed candle, not the currently forming one
        return boundary.AddTicks(-1);
    }

    public static async Task<List<Candle>> LoadEnoughCandlesAsync(
        IMarketDataProvider marketDataProvider,
        string instrument,
        string timeframe,
        int minRequired,
        DateTime evaluationTimeUtc,
        CancellationToken cancellationToken = default)
    {
        var lastClosedTimeUtc = GetLastClosedCandleTimeUtc(evaluationTimeUtc, timeframe);

        var pageSize = Math.Clamp(Math.Max(minRequired, 100), 100, 1000);
        var merged = new SortedDictionary<DateTime, Candle>();

        var currentEnd = lastClosedTimeUtc;

        for (var attempt = 0; attempt < 10 && merged.Count < minRequired; attempt++)
        {
            var batch = await marketDataProvider.GetCandlesAsync(
                instrument,
                timeframe,
                pageSize,
                currentEnd,
                cancellationToken);

            if (batch.Count == 0)
                break;

            foreach (var candle in batch.OrderBy(c => c.Time))
            {
                merged[candle.Time] = candle;
            }

            var earliest = batch.Min(c => c.Time);
            if (earliest >= currentEnd)
                break;

            currentEnd = earliest.AddTicks(-1);
        }

        return merged.Values.OrderBy(c => c.Time).ToList();
    }
}