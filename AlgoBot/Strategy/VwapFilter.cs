using AlgoBot.Configuration;
using AlgoBot.Helpers;
using AlgoBot.Interfaces;
using AlgoBot.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AlgoBot.Strategy;

public sealed class VwapFilter : IEntryFilter
{
    private readonly IMarketDataProvider _marketDataProvider;
    private readonly IOptionsMonitor<BotSettings> _botSettings;
    private readonly ILogger<VwapFilter> _logger;

    public VwapFilter(
        IMarketDataProvider marketDataProvider,
        IOptionsMonitor<BotSettings> botSettings,
        ILogger<VwapFilter> logger)
    {
        _marketDataProvider = marketDataProvider;
        _botSettings = botSettings;
        _logger = logger;
    }

    public string Name => "VWAP";

    public async Task<FilterEvaluationResult> EvaluateAsync(
        EntryFilterContext context,
        CancellationToken cancellationToken = default)
    {
        var settings = _botSettings.CurrentValue.Strategy.Vwap;

        if (!settings.Enabled)
            return FilterEvaluationResult.Success(Name, "VWAP filter disabled.");

        var tf = settings.Timeframe.Trim().ToUpperInvariant();

        var candles = await IndicatorCandleHelper.LoadEnoughCandlesAsync(
            _marketDataProvider,
            context.Instrument,
            tf,
            200,
            context.EvaluationTimeUtc,
            cancellationToken);

        var lastClosedTimeUtc = IndicatorCandleHelper.GetLastClosedCandleTimeUtc(
            context.EvaluationTimeUtc,
            tf);

        var startUtc = settings.AnchorToSessionStart
            ? context.SessionStartUtc
            : candles.FirstOrDefault()?.Time ?? context.SessionStartUtc;

        var usable = candles
            .Where(c => c.Time >= startUtc && c.Time <= lastClosedTimeUtc)
            .OrderBy(c => c.Time)
            .ToList();

        if (usable.Count == 0)
            return FilterEvaluationResult.Fail(Name, "No candles available to build VWAP.");

        decimal cumulativePv = 0m;
        decimal cumulativeVol = 0m;

        foreach (var candle in usable)
        {
            var typicalPrice = (candle.High + candle.Low + candle.Close) / 3m;
            var vol = settings.UseTickVolume ? candle.TickVolume : candle.Volume;

            if (vol <= 0)
                continue;

            cumulativePv += typicalPrice * vol;
            cumulativeVol += vol;
        }

        if (cumulativeVol <= 0)
            return FilterEvaluationResult.Fail(Name, "VWAP could not be calculated because cumulative volume is zero.");

        var vwap = cumulativePv / cumulativeVol;
        var spec = await _marketDataProvider.GetSymbolSpecificationAsync(context.Instrument, cancellationToken);

        if (spec is null)
            return FilterEvaluationResult.Fail(Name, "Symbol specification is missing for VWAP tolerance calculation.");

        var tolerance = PriceHelper.PipsToPriceDistance(spec, settings.TolerancePips);
        var price = context.BreakoutClosePrice ?? usable[^1].Close;

        var pass = context.BreakoutDirection switch
        {
            TradeDirection.Buy => !settings.RequirePriceOnCorrectSide || price >= vwap - tolerance,
            TradeDirection.Sell => !settings.RequirePriceOnCorrectSide || price <= vwap + tolerance,
            _ => false
        };

        var reason =
            $"Price={price}, VWAP={vwap}, Tolerance={tolerance}, UseTickVolume={settings.UseTickVolume}, TF={tf}.";

        _logger.LogDebug("VWAP filter evaluated for {Instrument}: {Reason}", context.Instrument, reason);

        return pass
            ? FilterEvaluationResult.Success(Name, reason)
            : FilterEvaluationResult.Fail(Name, reason);
    }
}