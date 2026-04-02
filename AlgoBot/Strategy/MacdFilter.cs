using AlgoBot.Configuration;
using AlgoBot.Helpers;
using AlgoBot.Interfaces;
using AlgoBot.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AlgoBot.Strategy;

public sealed class MacdFilter : IEntryFilter
{
    private readonly IMarketDataProvider _marketDataProvider;
    private readonly IIndicatorService _indicatorService;
    private readonly IOptionsMonitor<BotSettings> _botSettings;
    private readonly ILogger<MacdFilter> _logger;

    public MacdFilter(
        IMarketDataProvider marketDataProvider,
        IIndicatorService indicatorService,
        IOptionsMonitor<BotSettings> botSettings,
        ILogger<MacdFilter> logger)
    {
        _marketDataProvider = marketDataProvider;
        _indicatorService = indicatorService;
        _botSettings = botSettings;
        _logger = logger;
    }

    public string Name => "MACD";

    public async Task<FilterEvaluationResult> EvaluateAsync(
        EntryFilterContext context,
        CancellationToken cancellationToken = default)
    {
        var settings = _botSettings.CurrentValue.Strategy.Indicators.Macd;

        if (!settings.Enabled)
            return FilterEvaluationResult.Success(Name, "MACD filter disabled.");

        if (settings.FastPeriod >= settings.SlowPeriod)
        {
            return FilterEvaluationResult.Fail(
                Name,
                $"MACD config invalid. FastPeriod ({settings.FastPeriod}) must be less than SlowPeriod ({settings.SlowPeriod}).");
        }

        var requiredBars = Math.Max(settings.SlowPeriod + settings.SignalPeriod + 50, 200);

        var candles = await IndicatorCandleHelper.LoadEnoughCandlesAsync(
            _marketDataProvider,
            context.Instrument,
            settings.Timeframe,
            requiredBars,
            context.EvaluationTimeUtc,
            cancellationToken);

        var closes = candles
            .OrderBy(c => c.Time)
            .Select(c => c.Close)
            .ToList();

        if (closes.Count < settings.SlowPeriod + settings.SignalPeriod + 2)
        {
            return FilterEvaluationResult.Fail(
                Name,
                $"Not enough candles for MACD. Needed at least {settings.SlowPeriod + settings.SignalPeriod + 2}, got {closes.Count}. Timeframe={settings.Timeframe}.");
        }

        var macd = _indicatorService.CalculateMacd(
            closes,
            settings.FastPeriod,
            settings.SlowPeriod,
            settings.SignalPeriod);

        if (macd is null)
        {
            return FilterEvaluationResult.Fail(
                Name,
                "Failed to calculate MACD values.");
        }

        var pass = context.BreakoutDirection switch
        {
            TradeDirection.Buy => settings.RequireCrossover
                ? macd.PreviousMacd <= macd.PreviousSignal && macd.CurrentMacd > macd.CurrentSignal
                : macd.CurrentMacd > macd.CurrentSignal,

            TradeDirection.Sell => settings.RequireCrossover
                ? macd.PreviousMacd >= macd.PreviousSignal && macd.CurrentMacd < macd.CurrentSignal
                : macd.CurrentMacd < macd.CurrentSignal,

            _ => false
        };

        var reason =
            $"MACD={macd.CurrentMacd:F5}, Signal={macd.CurrentSignal:F5}, Histogram={macd.CurrentHistogram:F5}, bars={closes.Count}, timeframe={settings.Timeframe}, direction={context.BreakoutDirection}, requireCrossover={settings.RequireCrossover}.";

        _logger.LogDebug("MACD filter evaluated for {Instrument}: {Reason}", context.Instrument, reason);

        return pass
            ? FilterEvaluationResult.Success(Name, reason)
            : FilterEvaluationResult.Fail(Name, reason);
    }
}