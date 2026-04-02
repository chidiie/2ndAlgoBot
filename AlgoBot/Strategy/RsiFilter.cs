using AlgoBot.Configuration;
using AlgoBot.Helpers;
using AlgoBot.Interfaces;
using AlgoBot.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AlgoBot.Strategy;

public sealed class RsiFilter : IEntryFilter
{
    private readonly IMarketDataProvider _marketDataProvider;
    private readonly IIndicatorService _indicatorService;
    private readonly IOptionsMonitor<BotSettings> _botSettings;
    private readonly ILogger<RsiFilter> _logger;

    public RsiFilter(
        IMarketDataProvider marketDataProvider,
        IIndicatorService indicatorService,
        IOptionsMonitor<BotSettings> botSettings,
        ILogger<RsiFilter> logger)
    {
        _marketDataProvider = marketDataProvider;
        _indicatorService = indicatorService;
        _botSettings = botSettings;
        _logger = logger;
    }

    public string Name => "RSI";

    public async Task<FilterEvaluationResult> EvaluateAsync(
        EntryFilterContext context,
        CancellationToken cancellationToken = default)
    {
        var settings = _botSettings.CurrentValue.Strategy.Indicators.Rsi;

        if (!settings.Enabled)
            return FilterEvaluationResult.Success(Name, "RSI filter disabled.");

        var requiredBars = Math.Max(settings.Period + 20, settings.Period * 4);

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

        if (closes.Count <= settings.Period)
        {
            return FilterEvaluationResult.Fail(
                Name,
                $"Not enough candles for RSI. Needed more than {settings.Period}, got {closes.Count}. Timeframe={settings.Timeframe}.");
        }

        var rsi = _indicatorService.CalculateRsi(closes, settings.Period);

        if (!rsi.HasValue)
        {
            return FilterEvaluationResult.Fail(
                Name,
                "Failed to calculate RSI due to insufficient data.");
        }

        var pass = context.BreakoutDirection switch
        {
            TradeDirection.Buy => rsi.Value >= settings.BuyMin,
            TradeDirection.Sell => rsi.Value <= settings.SellMax,
            _ => false
        };

        var reason =
            $"RSI={rsi.Value:F2}, bars={closes.Count}, timeframe={settings.Timeframe}, direction={context.BreakoutDirection}, buyMin={settings.BuyMin}, sellMax={settings.SellMax}.";

        _logger.LogDebug("RSI filter evaluated for {Instrument}: {Reason}", context.Instrument, reason);

        return pass
            ? FilterEvaluationResult.Success(Name, reason)
            : FilterEvaluationResult.Fail(Name, reason);
    }
}