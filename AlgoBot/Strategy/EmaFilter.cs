using AlgoBot.Configuration;
using AlgoBot.Helpers;
using AlgoBot.Interfaces;
using AlgoBot.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AlgoBot.Strategy;

public sealed class EmaFilter : IEntryFilter
{
    private readonly IMarketDataProvider _marketDataProvider;
    private readonly IIndicatorService _indicatorService;
    private readonly IOptionsMonitor<BotSettings> _botSettings;
    private readonly ILogger<EmaFilter> _logger;

    public EmaFilter(
        IMarketDataProvider marketDataProvider,
        IIndicatorService indicatorService,
        IOptionsMonitor<BotSettings> botSettings,
        ILogger<EmaFilter> logger)
    {
        _marketDataProvider = marketDataProvider;
        _indicatorService = indicatorService;
        _botSettings = botSettings;
        _logger = logger;
    }

    public string Name => "EMA";

    public async Task<FilterEvaluationResult> EvaluateAsync(
        EntryFilterContext context,
        CancellationToken cancellationToken = default)
    {
        var settings = _botSettings.CurrentValue.Strategy.Indicators.Ema;

        if (!settings.Enabled)
            return FilterEvaluationResult.Success(Name, "EMA filter disabled.");

        if (settings.FastPeriod >= settings.SlowPeriod)
        {
            return FilterEvaluationResult.Fail(
                Name,
                $"EMA config invalid. FastPeriod ({settings.FastPeriod}) must be less than SlowPeriod ({settings.SlowPeriod}).");
        }

        var requiredBars = Math.Max(settings.SlowPeriod + 20, settings.SlowPeriod * 5);

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

        if (closes.Count < settings.SlowPeriod)
        {
            return FilterEvaluationResult.Fail(
                Name,
                $"Not enough candles for EMA. Needed at least {settings.SlowPeriod}, got {closes.Count}. Timeframe={settings.Timeframe}.");
        }

        var fast = _indicatorService.CalculateEma(closes, settings.FastPeriod);
        var slow = _indicatorService.CalculateEma(closes, settings.SlowPeriod);

        if (!fast.HasValue || !slow.HasValue)
        {
            return FilterEvaluationResult.Fail(Name, "Failed to calculate EMA values.");
        }

        var pass = context.BreakoutDirection switch
        {
            TradeDirection.Buy => fast.Value > slow.Value,
            TradeDirection.Sell => fast.Value < slow.Value,
            _ => false
        };

        var reason =
            $"EMA fast={fast.Value:F5}, slow={slow.Value:F5}, bars={closes.Count}, timeframe={settings.Timeframe}, direction={context.BreakoutDirection}.";

        _logger.LogDebug("EMA filter evaluated for {Instrument}: {Reason}", context.Instrument, reason);

        return pass
            ? FilterEvaluationResult.Success(Name, reason)
            : FilterEvaluationResult.Fail(Name, reason);
    }
}