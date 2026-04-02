using AlgoBot.Configuration;
using AlgoBot.Helpers;
using AlgoBot.Interfaces;
using AlgoBot.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AlgoBot.Strategy;

public sealed class VolumeBreakoutFilter : IEntryFilter
{
    private readonly IMarketDataProvider _marketDataProvider;
    private readonly IOptionsMonitor<BotSettings> _botSettings;
    private readonly ILogger<VolumeBreakoutFilter> _logger;

    public VolumeBreakoutFilter(
        IMarketDataProvider marketDataProvider,
        IOptionsMonitor<BotSettings> botSettings,
        ILogger<VolumeBreakoutFilter> logger)
    {
        _marketDataProvider = marketDataProvider;
        _botSettings = botSettings;
        _logger = logger;
    }

    public string Name => "Volume";

    public async Task<FilterEvaluationResult> EvaluateAsync(
        EntryFilterContext context,
        CancellationToken cancellationToken = default)
    {
        var settings = _botSettings.CurrentValue.Strategy.Volume;

        if (!settings.Enabled)
            return FilterEvaluationResult.Success(Name, "Volume filter disabled.");

        if (!context.BreakoutTimeUtc.HasValue)
            return FilterEvaluationResult.Fail(Name, "Breakout time is missing.");

        var tf = settings.Timeframe.Trim().ToUpperInvariant();
        var requiredBars = settings.LookbackCandles + 2;

        var candles = await IndicatorCandleHelper.LoadEnoughCandlesAsync(
            _marketDataProvider,
            context.Instrument,
            tf,
            requiredBars,
            context.EvaluationTimeUtc,
            cancellationToken);

        var usable = candles
            .Where(c => c.Time <= context.BreakoutTimeUtc.Value)
            .OrderBy(c => c.Time)
            .ToList();

        if (usable.Count < settings.LookbackCandles + 1)
        {
            return FilterEvaluationResult.Fail(
                Name,
                $"Not enough candles for volume filter. Needed {settings.LookbackCandles + 1}, got {usable.Count}.");
        }

        var breakoutCandle = usable[^1];
        var previous = usable.Skip(Math.Max(0, usable.Count - 1 - settings.LookbackCandles))
                             .Take(settings.LookbackCandles)
                             .ToList();

        if (previous.Count == 0)
            return FilterEvaluationResult.Fail(Name, "No previous candles available for volume comparison.");

        decimal breakoutVolume = GetVolume(breakoutCandle, settings.VolumeSource);
        decimal previousMax = previous.Max(c => GetVolume(c, settings.VolumeSource));
        decimal threshold = previousMax * settings.Multiplier;

        var pass = breakoutVolume > threshold;

        var reason =
            $"BreakoutVol={breakoutVolume}, PrevMax={previousMax}, Threshold={threshold}, Source={settings.VolumeSource}, Lookback={settings.LookbackCandles}, TF={tf}.";

        _logger.LogDebug("Volume filter evaluated for {Instrument}: {Reason}", context.Instrument, reason);

        return pass
            ? FilterEvaluationResult.Success(Name, reason)
            : FilterEvaluationResult.Fail(Name, reason);
    }

    private static decimal GetVolume(Candle candle, string volumeSource)
    {
        return string.Equals(volumeSource, "Volume", StringComparison.OrdinalIgnoreCase)
            ? candle.Volume
            : candle.TickVolume;
    }
}