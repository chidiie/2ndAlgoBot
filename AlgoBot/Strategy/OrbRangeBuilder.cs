using AlgoBot.Configuration;
using AlgoBot.Helpers;
using AlgoBot.Interfaces;
using AlgoBot.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AlgoBot.Strategy;

public sealed class OrbRangeBuilder : IOrbRangeBuilder
{
    private readonly IMarketDataProvider _marketDataProvider;
    private readonly IOptionsMonitor<BotSettings> _botSettings;
    private readonly ILogger<OrbRangeBuilder> _logger;

    public OrbRangeBuilder(
        IMarketDataProvider marketDataProvider,
        IOptionsMonitor<BotSettings> botSettings,
        ILogger<OrbRangeBuilder> logger)
    {
        _marketDataProvider = marketDataProvider;
        _botSettings = botSettings;
        _logger = logger;
    }

    public async Task<OrbBuildResult> TryBuildAsync(
        TradingSessionSettings session,
        InstrumentState instrumentState,
        CancellationToken cancellationToken = default)
    {
        if (instrumentState.RangeBuilt && instrumentState.OrbRange is not null)
        {
            return OrbBuildResult.Success(
                instrumentState.OrbRange,
                candlesUsed: 0,
                window: instrumentState.LastSessionWindow ?? new SessionWindow(),
                reason: "ORB range already built.");
        }

        var settings = _botSettings.CurrentValue;
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(settings.TimeZoneId);
        var nowLocal = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, timeZone);

        var tradingDay = TradingTimeHelper.GetSessionTradingDay(session, nowLocal);
        var window = TradingTimeHelper.GetSessionWindow(session, tradingDay, timeZone);

        if (nowLocal.DateTime < window.OrbEndLocal)
        {
            return OrbBuildResult.Pending(
                $"Waiting for ORB window to close at {window.OrbEndLocal:yyyy-MM-dd HH:mm:ss}.",
                window);
        }

        var rangeTimeframe = settings.Strategy.RangeTimeframe;
        var breakoutTimeframe = settings.Strategy.BreakoutTimeframe;
        var rangeTimeframeSpan = TradingTimeHelper.GetTimeframeSpan(rangeTimeframe);
        var breakoutTimeframeSpan = TradingTimeHelper.GetTimeframeSpan(breakoutTimeframe);
        var orbSpan = window.OrbEndUtc - window.SessionStartUtc;

        if (orbSpan <= TimeSpan.Zero)
        {
            return OrbBuildResult.Pending("ORB window duration is invalid.", window);
        }

        if (orbSpan.Ticks % rangeTimeframeSpan.Ticks != 0)
        {
            return OrbBuildResult.Pending(
                $"RangeTimeframe '{rangeTimeframeSpan}' does not divide session ORB duration ({session.OrbMinutes} minutes) evenly.",
                window);
        }

        var expectedBars = (int)(orbSpan.Ticks / rangeTimeframeSpan.Ticks);
        if (expectedBars <= 0)
        {
            return OrbBuildResult.Pending("Expected ORB candle count is zero.", window);
        }

        // MetaApi historical candles load backwards from startTime, so we request
        // from the ORB end boundary and then filter exactly to the ORB window.
        var requestLimit = Math.Min(1000, expectedBars + 5);

        IReadOnlyList<Candle> candles;
        try
        {
            candles = await _marketDataProvider.GetCandlesAsync(
                instrumentState.Instrument,
                breakoutTimeframe,
                requestLimit,
                window.OrbEndUtc,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to fetch ORB candles. Session={SessionName}, Instrument={Instrument}",
                session.Name,
                instrumentState.Instrument);

            return OrbBuildResult.Pending($"Failed to fetch ORB candles: {ex.Message}", window);
        }

        var rangeCandles = candles
            .Where(c => c.Time >= window.SessionStartUtc && c.Time < window.OrbEndUtc)
            .OrderBy(c => c.Time)
            .ToList();

        if (rangeCandles.Count < expectedBars)
        {
            return OrbBuildResult.Pending(
                $"Insufficient ORB candles. Expected={expectedBars}, Actual={rangeCandles.Count}.",
                window);
        }

        if (!HasContinuousCoverage(
                rangeCandles,
                window.SessionStartUtc,
                breakoutTimeframeSpan,
                expectedBars))
        {
            return OrbBuildResult.Pending(
                $"ORB candle coverage is incomplete or not aligned for timeframe {breakoutTimeframe}.",
                window);
        }

        var range = new OrbRange
        {
            StartTimeUtc = window.SessionStartUtc,
            EndTimeUtc = window.OrbEndUtc,
            High = rangeCandles.Max(c => c.High),
            Low = rangeCandles.Min(c => c.Low)
        };

        return OrbBuildResult.Success(
            range,
            rangeCandles.Count,
            window,
            $"ORB built successfully using {rangeCandles.Count} {breakoutTimeframe} - {candles.Count} candles.");
    }

    private static bool HasContinuousCoverage(
        IReadOnlyList<Candle> candles,
        DateTime expectedStartUtc,
        TimeSpan timeframeSpan,
        int expectedBars)
    {
        if (candles.Count < expectedBars)
            return false;

        for (var i = 0; i < expectedBars; i++)
        {
            var expectedTime = expectedStartUtc.AddTicks(timeframeSpan.Ticks * i);
            if (candles[i].Time != expectedTime)
            {
                return false;
            }
        }

        return true;
    }
}