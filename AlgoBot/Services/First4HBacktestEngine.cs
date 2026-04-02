using AlgoBot.Configuration;
using AlgoBot.Helpers;
using AlgoBot.Interfaces;
using AlgoBot.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace AlgoBot.Services;

public sealed class First4HBacktestEngine : IFirst4HBacktestEngine
{
    private readonly IHistoricalDataProvider _historicalDataProvider;
    private readonly IMarketDataProvider _marketDataProvider;
    private readonly IOptionsMonitor<BotSettings> _botSettings;
    private readonly ILogger<First4HBacktestEngine> _logger;
    private readonly First4HReentrySettings _cfg;

    public First4HBacktestEngine(
        IHistoricalDataProvider historicalDataProvider,
        IMarketDataProvider marketDataProvider,
        IOptionsMonitor<BotSettings> botSettings,
        ILogger<First4HBacktestEngine> logger,
        IOptions<First4HReentrySettings> cfg)
    {
        _historicalDataProvider = historicalDataProvider;
        _marketDataProvider = marketDataProvider;
        _botSettings = botSettings;
        _logger = logger;
        _cfg = cfg.Value;
    }

    public async Task<BacktestSummary> RunAsync(
        BacktestRequest request,
        CancellationToken cancellationToken = default)
    {
        var cfg = _botSettings.CurrentValue.First4HReentry;

        var profiles = cfg.Profiles
            .Where(p => p.Enabled)
            .Where(p => request.SessionsToRun.Count == 0 ||
                        request.SessionsToRun.Contains(p.Name, StringComparer.OrdinalIgnoreCase))
            .ToList();

        var summary = new BacktestSummary
        {
            StartUtc = request.StartUtc,
            EndUtc = request.EndUtc,
            StartingBalance = request.StartingBalance,
            EndingBalance = request.StartingBalance
        };

        var equity = request.StartingBalance;
        var peak = equity;

        foreach (var profile in profiles)
        {
            var instruments = request.InstrumentsOverride.Count > 0
                ? request.InstrumentsOverride
                : profile.Instruments;

            foreach (var instrument in instruments.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var trades = await BacktestProfileInstrumentAsync(
                    profile,
                    instrument,
                    request.StartUtc,
                    request.EndUtc,
                    equity,
                    cancellationToken);

                foreach (var trade in trades)
                {
                    summary.Trades.Add(trade);

                    equity += trade.ProfitLoss;
                    summary.EndingBalance = equity;

                    if (equity > peak)
                        peak = equity;

                    var drawdownAmount = peak - equity;
                    var drawdownPercent = peak == 0 ? 0 : (drawdownAmount / peak) * 100m;

                    if (drawdownAmount > summary.MaxDrawdownAmount)
                        summary.MaxDrawdownAmount = drawdownAmount;

                    if (drawdownPercent > summary.MaxDrawdownPercent)
                        summary.MaxDrawdownPercent = drawdownPercent;
                }
            }
        }

        BuildSummary(summary);

        _logger.LogInformation(
            "First4H backtest complete. Trades={Trades} NetProfit={NetProfit} WinRate={WinRate:F2}%",
            summary.TotalTrades,
            summary.NetProfit,
            summary.WinRatePercent);

        return summary;
    }

    private async Task<List<BacktestTradeResult>> BacktestProfileInstrumentAsync(
        First4HProfileSettings profile,
        string instrument,
        DateTime startUtc,
        DateTime endUtc,
        decimal startingEquity,
        CancellationToken cancellationToken)
    {
        var cfg = _botSettings.CurrentValue.First4HReentry;
        var anchorTz = TimeZoneInfo.FindSystemTimeZoneById(profile.AnchorTimeZoneId);

        var rangeCandles = await _historicalDataProvider.GetCandlesRangeAsync(
            instrument,
            cfg.RangeTimeframe,
            startUtc.AddDays(-3),
            endUtc.AddDays(1),
            cancellationToken);

        var signalCandles = await _historicalDataProvider.GetCandlesRangeAsync(
            instrument,
            cfg.SignalTimeframe,
            startUtc.AddDays(-3),
            endUtc.AddDays(1),
            cancellationToken);

        if (rangeCandles.Count == 0 || signalCandles.Count == 0)
            return new List<BacktestTradeResult>();

        var spec = await _marketDataProvider.GetSymbolSpecificationAsync(instrument, cancellationToken);
        if (spec is null)
            return new List<BacktestTradeResult>();

        var results = new List<BacktestTradeResult>();
        var equity = startingEquity;

        var localStart = TimeZoneInfo.ConvertTimeFromUtc(startUtc, anchorTz).Date;
        var localEnd = TimeZoneInfo.ConvertTimeFromUtc(endUtc, anchorTz).Date;

        //for (var day = localStart; day <= localEnd; day = day.AddDays(1))
        for (var day = localEnd; day >= localStart; day = day.AddDays(-1))
        {
            var tradingDay = DateOnly.FromDateTime(day);

            var range = ResolveFirst4HRangeForDay(
                profile,
                tradingDay,
                cfg,
                anchorTz,
                rangeCandles);

            if (range is null)
                continue;

            var dayEndUtc = range.RangeStartUtc.AddDays(1);

            var daySignalCandles = signalCandles
                .Where(c => c.Time >= range.RangeEndUtc && c.Time < dayEndUtc)
                .OrderBy(c => c.Time)
                .ToList();

            if (daySignalCandles.Count == 0)
                continue;

            var dayTrades = SimulateDay(
                profile,
                instrument,
                tradingDay,
                range,
                daySignalCandles,
                spec,
                equity);

            foreach (var trade in dayTrades)
            {
                results.Add(trade);
                equity += trade.ProfitLoss;
            }
        }

        return results;
    }

    private List<BacktestTradeResult> SimulateDay(
        First4HProfileSettings profile,
        string instrument,
        DateOnly tradingDay,
        First4HDayRange range,
        IReadOnlyList<Candle> signalCandles,
        SymbolSpecification spec,
        decimal dayStartingEquity)
    {
        var cfg = _botSettings.CurrentValue.First4HReentry;
        var results = new List<BacktestTradeResult>();

        var tradesTaken = 0;
        var equity = dayStartingEquity;
        var index = 0;

        while (index < signalCandles.Count && tradesTaken < cfg.MaxTradesPerInstrumentPerDay)
        {
            var found = FindTradeFromIndex(
                signalCandles,
                profile,
                index,
                range,
                spec,
                cfg,
                instrument,
                tradingDay,
                equity,
                out var trade,
                out var nextIndex);

            if (!found)
                break;

            if (trade is not null)
            {
                results.Add(trade);
                equity += trade.ProfitLoss;
                tradesTaken++;
            }

            index = nextIndex;
        }

        return results;
    }

    private bool FindTradeFromIndex(
        IReadOnlyList<Candle> signalCandles,
        First4HProfileSettings profile,
        int startIndex,
        First4HDayRange range,
        SymbolSpecification spec,
        First4HReentrySettings cfg,
        string instrument,
        DateOnly tradingDay,
        decimal equity,
        out BacktestTradeResult? trade,
        out int nextIndex)
    {
        trade = null;
        nextIndex = signalCandles.Count;

        var breakoutActive = false;
        First4HBreakoutDirection breakoutDirection = First4HBreakoutDirection.None;
        decimal breakoutExtreme = 0m;

        for (var i = startIndex; i < signalCandles.Count; i++)
        {
            var candle = signalCandles[i];

            if (!breakoutActive)
            {
                if (candle.Close > range.High)
                {
                    breakoutActive = true;
                    breakoutDirection = First4HBreakoutDirection.AboveRange;
                    breakoutExtreme = candle.High;
                    continue;
                }

                if (candle.Close < range.Low)
                {
                    breakoutActive = true;
                    breakoutDirection = First4HBreakoutDirection.BelowRange;
                    breakoutExtreme = candle.Low;
                    continue;
                }

                continue;
            }

            if (breakoutDirection == First4HBreakoutDirection.AboveRange)
            {
                breakoutExtreme = Math.Max(breakoutExtreme, candle.High);

                if (candle.Close < range.Low)
                {
                    breakoutDirection = First4HBreakoutDirection.BelowRange;
                    breakoutExtreme = candle.Low;
                    continue;
                }

                var reentered = candle.Close < range.High && candle.Close >= range.Low;
                if (!reentered)
                    continue;

                var contextStart = Math.Max(0, i - cfg.KeyLevelLookbackBars);
                var contextCandles = signalCandles
                    .Skip(contextStart)
                    .Take(i - contextStart + 1)
                .ToList();

                trade = BuildTrade(
                    profile,
                    instrument,
                    tradingDay,
                    range,
                    TradeDirection.Sell,
                    breakoutExtreme,
                    candle,
                    contextCandles,
                    signalCandles.Where(c => c.Time > candle.Time).ToList(),
                    //signalCandles.Skip(i + 1).ToList(),
                    spec,
                    equity,
                    cfg);

                nextIndex = ResolveNextIndex(signalCandles, trade?.ExitTimeUtc, i);
                return trade is not null;
            }

            if (breakoutDirection == First4HBreakoutDirection.BelowRange)
            {
                breakoutExtreme = Math.Min(breakoutExtreme, candle.Low);

                if (candle.Close > range.High)
                {
                    breakoutDirection = First4HBreakoutDirection.AboveRange;
                    breakoutExtreme = candle.High;
                    continue;
                }

                var reentered = candle.Close > range.Low && candle.Close <= range.High;
                if (!reentered)
                    continue;

                var contextStart = Math.Max(0, i - cfg.KeyLevelLookbackBars);
                var contextCandles = signalCandles
                    .Skip(contextStart)
                    .Take(i - contextStart + 1)
                .ToList();

                trade = BuildTrade(
                    profile,
                    instrument,
                    tradingDay,
                    range,
                    TradeDirection.Buy,
                    breakoutExtreme,
                    candle,
                    contextCandles,
                    //signalCandles.Skip(i).ToList(),
                    signalCandles.Where(c => c.Time > candle.Time).ToList(),
                    spec,
                    equity,
                    cfg);

                nextIndex = ResolveNextIndex(signalCandles, trade?.ExitTimeUtc, i);
                return trade is not null;
            }
        }

        return false;
    }

    private static int ResolveNextIndex(
        IReadOnlyList<Candle> candles,
        DateTime? exitTimeUtc,
        int fallbackIndex)
    {
        if (!exitTimeUtc.HasValue)
            return fallbackIndex + 1;

        for (var i = 0; i < candles.Count; i++)
        {
            if (candles[i].Time > exitTimeUtc.Value)
                return i;
        }

        return candles.Count;
    }

    private BacktestTradeResult? BuildTrade(
        First4HProfileSettings profile,
        string instrument,
        DateOnly tradingDay,
        First4HDayRange range,
        TradeDirection direction,
        decimal breakoutExtreme,
        Candle reentryCandle,
        IReadOnlyList<Candle> contextCandles,
        IReadOnlyList<Candle> futureCandles,
        SymbolSpecification spec,
        decimal equity,
        First4HReentrySettings cfg)
    {
        var entry = reentryCandle.Close;

        var stop = ResolveStopLoss(
            direction,
            entry,
            breakoutExtreme,
            range,
            spec,
            cfg,
            contextCandles);

        if (!stop.HasValue)
            return null;

        if (direction == TradeDirection.Buy && stop.Value >= entry)
            return null;

        if (direction == TradeDirection.Sell && stop.Value <= entry)
            return null;

        var riskDistance = Math.Abs(entry - stop.Value);
        if (riskDistance <= 0)
            return null;

        var riskAmount = equity * (_botSettings.CurrentValue.Risk.RiskPerTradePercent / 100m);
        var qty = SizeVolumeForBacktest(spec, instrument, riskDistance, riskAmount);
        if (qty <= 0)
            return null;

        var tp = direction == TradeDirection.Buy
            ? entry + (riskDistance * cfg.RewardRiskRatio)
            : entry - (riskDistance * cfg.RewardRiskRatio);

        var exit = SimulateExit(
            direction,
            futureCandles,
            stop.Value,
            tp,
            spec,
            _botSettings.CurrentValue.Backtest.ExitSpreadPips,
            cfg.ForceCloseAtDayEnd);

        var contractSize = ResolveContractSize(spec, instrument);

        var priceMove = direction == TradeDirection.Buy
            ? exit.ExitPrice - entry
            : entry - exit.ExitPrice;

        var profitLoss = priceMove * contractSize * qty;
        var totalRisk = riskDistance * contractSize * qty;
        var rMultiple = totalRisk <= 0 ? 0 : profitLoss / totalRisk;

        return new BacktestTradeResult
        {
            SessionName = $"First4H:{profile.Name}",
            Instrument = instrument,
            Direction = direction,
            SignalTimeUtc = reentryCandle.Time,
            EntryTimeUtc = reentryCandle.Time,
            ExitTimeUtc = exit.ExitTimeUtc,
            EntryPrice = entry,
            StopLoss = stop.Value,
            TakeProfit = tp,
            ExitPrice = exit.ExitPrice,
            Quantity = qty,
            RiskAmount = riskAmount,
            ProfitLoss = profitLoss,
            RMultiple = rMultiple,
            ExitReason = exit.ExitReason,
            Notes = $"TradingDay={tradingDay}; Profile={profile.Name}"
        };
    }

    private First4HDayRange? ResolveFirst4HRangeForDay(
        First4HProfileSettings profile,
        DateOnly tradingDay,
        First4HReentrySettings cfg,
        TimeZoneInfo anchorTz,
        IReadOnlyList<Candle> rangeCandles)
    {
        var dayStartLocal = tradingDay.ToDateTime(TimeOnly.FromTimeSpan(profile.DayStartTime));
        var rangeStartOffset = new DateTimeOffset(dayStartLocal, anchorTz.GetUtcOffset(dayStartLocal));
        var rangeEndOffset = rangeStartOffset.AddHours(4);

        var rangeStartUtc = rangeStartOffset.UtcDateTime;
        var rangeEndUtc = rangeEndOffset.UtcDateTime;

        int totalRangeHours = RangeTfHours() * cfg.RangeCount;

        var candle = rangeCandles
            .Where(c => c.Time == rangeStartUtc)
            .OrderBy(c => c.Time)
            .FirstOrDefault();

        var rangeCandlesPerDay = rangeCandles
            .Where(c => c.Time >= rangeStartUtc &&
                        c.Time < rangeStartUtc.AddHours(totalRangeHours))
            .OrderBy(c => c.Time)
            .ToList();

        //if (candle is null)
        //{
        //    candle = rangeCandles
        //        .Where(c => c.Time >= rangeStartUtc && c.Time < rangeEndUtc)
        //        .OrderBy(c => c.Time)
        //        .FirstOrDefault();
        //}

        //if (candle is null)
        //    return null;

        if (rangeCandlesPerDay is null)
            return null;

        decimal high = rangeCandlesPerDay.Max(c => (decimal)c.High);
        decimal low = rangeCandlesPerDay.Min(c => (decimal)c.Low);

        return new First4HDayRange
        {
            TradingDay = tradingDay,
            RangeStartUtc = rangeStartUtc,
            RangeEndUtc = rangeEndUtc,
            High = high,
            Low = low
        };

        //return new First4HDayRange
        //{
        //    TradingDay = tradingDay,
        //    RangeStartUtc = rangeStartUtc,
        //    RangeEndUtc = rangeEndUtc,
        //    High = candle.High,
        //    Low = candle.Low
        //};
    }

    private int RangeTfHours() => _cfg.RangeTimeframe.ToUpper() switch
    {
        "H1" => 1,
        "H4" => 4, // Changed from 3 to 4
        _ => 1  // Default to 1 hour if unknown
    };

    private static decimal? ResolveStopLoss(
        TradeDirection direction,
        decimal entryPrice,
        decimal breakoutExtreme,
        First4HDayRange range,
        SymbolSpecification spec,
        First4HReentrySettings cfg,
        IReadOnlyList<Candle> contextCandles)
    {
        var buffer = PriceHelper.PipsToPriceDistance(spec, cfg.StopBufferPips);

        decimal defaultStop = direction == TradeDirection.Sell
            ? breakoutExtreme + buffer
            : breakoutExtreme - buffer;

        var stopDistance = Math.Abs(entryPrice - defaultStop);
        var tooLargeByRange = stopDistance > range.Size * cfg.MaxStopAsRangeMultiple;

        var tooLargeByPips = cfg.MaxStopDistancePips > 0 &&
                             PriceHelper.PriceDistanceToPips(spec, stopDistance) > cfg.MaxStopDistancePips;

        if (!tooLargeByRange && !tooLargeByPips)
            return defaultStop;

        if (!cfg.UseKeyLevelFallback)
            return null;

        if (direction == TradeDirection.Sell)
        {
            var pivotHigh = FindNearestPivotHigh(contextCandles, entryPrice, defaultStop, cfg.PivotStrength);
            return pivotHigh.HasValue ? pivotHigh.Value + buffer : null;
        }

        var pivotLow = FindNearestPivotLow(contextCandles, defaultStop, entryPrice, cfg.PivotStrength);
        return pivotLow.HasValue ? pivotLow.Value - buffer : null;
    }

    private static decimal? FindNearestPivotHigh(
        IReadOnlyList<Candle> candles,
        decimal minPrice,
        decimal maxPrice,
        int strength)
    {
        var pivots = new List<decimal>();

        for (var i = strength; i < candles.Count - strength; i++)
        {
            var candidate = candles[i].High;
            var isPivot = true;

            for (var j = 1; j <= strength; j++)
            {
                if (candles[i - j].High >= candidate || candles[i + j].High > candidate)
                {
                    isPivot = false;
                    break;
                }
            }

            if (isPivot && candidate > minPrice && candidate <= maxPrice)
                pivots.Add(candidate);
        }

        return pivots.Count == 0 ? null : pivots.OrderBy(x => x).First();
    }

    private static decimal? FindNearestPivotLow(
        IReadOnlyList<Candle> candles,
        decimal minPrice,
        decimal maxPrice,
        int strength)
    {
        var pivots = new List<decimal>();

        for (var i = strength; i < candles.Count - strength; i++)
        {
            var candidate = candles[i].Low;
            var isPivot = true;

            for (var j = 1; j <= strength; j++)
            {
                if (candles[i - j].Low <= candidate || candles[i + j].Low < candidate)
                {
                    isPivot = false;
                    break;
                }
            }

            if (isPivot && candidate >= minPrice && candidate < maxPrice)
                pivots.Add(candidate);
        }

        return pivots.Count == 0 ? null : pivots.OrderByDescending(x => x).First();
    }

    private static BacktestExit SimulateExit(
        TradeDirection direction,
        IReadOnlyList<Candle> futureBars,
        decimal stopLoss,
        decimal takeProfit,
        SymbolSpecification spec,
        decimal exitSpreadPips,
        bool forceCloseAtDayEnd)
    {
        var exitAdj = PriceHelper.PipsToPriceDistance(spec, exitSpreadPips);

        foreach (var bar in futureBars)
        {
            if (direction == TradeDirection.Buy)
            {
                var hitSl = bar.Low <= stopLoss;
                var hitTp = bar.High >= takeProfit;

                if (hitSl && hitTp)
                    return new BacktestExit(stopLoss - exitAdj, bar.Time, "SameBar_SL_First");

                if (hitSl)
                    return new BacktestExit(stopLoss - exitAdj, bar.Time, "StopLoss");

                if (hitTp)
                    return new BacktestExit(takeProfit - exitAdj, bar.Time, "TakeProfit");
            }
            else
            {
                var hitSl = bar.High >= stopLoss;
                var hitTp = bar.Low <= takeProfit;

                if (hitSl && hitTp)
                    return new BacktestExit(stopLoss + exitAdj, bar.Time, "SameBar_SL_First");

                if (hitSl)
                    return new BacktestExit(stopLoss + exitAdj, bar.Time, "StopLoss");

                if (hitTp)
                    return new BacktestExit(takeProfit + exitAdj, bar.Time, "TakeProfit");
            }
        }

        var last = futureBars[^1];
        return new BacktestExit(last.Close, last.Time, forceCloseAtDayEnd ? "DayEnd" : "DataEnd");
    }

    private static decimal ResolveContractSize(SymbolSpecification spec, string instrument)
    {
        if (spec.ContractSize > 0)
            return spec.ContractSize;

        if (instrument.Equals("XAUUSD", StringComparison.OrdinalIgnoreCase))
            return 100m;

        if (instrument.EndsWith("USD", StringComparison.OrdinalIgnoreCase))
            return 100000m;

        return 1m;
    }

    private static decimal SizeVolumeForBacktest(
        SymbolSpecification spec,
        string instrument,
        decimal stopDistancePrice,
        decimal riskAmount)
    {
        if (stopDistancePrice <= 0 || riskAmount <= 0)
            return 0m;

        var contractSize = ResolveContractSize(spec, instrument);
        var minVolume = spec.MinVolume > 0 ? spec.MinVolume : 0.01m;
        var maxVolume = spec.MaxVolume > 0 ? spec.MaxVolume : decimal.MaxValue;
        var volumeStep = spec.VolumeStep > 0 ? spec.VolumeStep : minVolume;

        var lossPerLot = stopDistancePrice * contractSize;
        if (lossPerLot <= 0)
            return 0m;

        var rawVolume = riskAmount / lossPerLot;
        if (rawVolume <= 0)
            return 0m;

        var bounded = Math.Min(rawVolume, maxVolume);

        var steps = Math.Floor(bounded / volumeStep);
        var normalized = steps * volumeStep;

        normalized = RoundToStepPrecision(normalized, volumeStep);

        if (normalized < minVolume)
            return 0m;

        return normalized;
    }

    private static decimal RoundToStepPrecision(decimal value, decimal step)
    {
        var stepText = step.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var decimals = 0;

        var dotIndex = stepText.IndexOf('.');
        if (dotIndex >= 0)
            decimals = stepText.Length - dotIndex - 1;

        return Math.Round(value, decimals, MidpointRounding.ToZero);
    }

    private static void BuildSummary(BacktestSummary summary)
    {
        summary.TotalTrades = summary.Trades.Count;
        summary.Wins = summary.Trades.Count(t => t.ProfitLoss > 0);
        summary.Losses = summary.Trades.Count(t => t.ProfitLoss < 0);
        summary.Breakevens = summary.Trades.Count(t => t.ProfitLoss == 0);

        summary.WinRatePercent = summary.TotalTrades == 0
            ? 0
            : (decimal)summary.Wins / summary.TotalTrades * 100m;

        summary.GrossProfit = summary.Trades.Where(t => t.ProfitLoss > 0).Sum(t => t.ProfitLoss);
        summary.GrossLoss = summary.Trades.Where(t => t.ProfitLoss < 0).Sum(t => Math.Abs(t.ProfitLoss));
        summary.NetProfit = summary.EndingBalance - summary.StartingBalance;

        summary.ProfitFactor = summary.GrossLoss == 0
            ? 0
            : summary.GrossProfit / summary.GrossLoss;

        summary.AverageWin = summary.Wins == 0
            ? 0
            : summary.Trades.Where(t => t.ProfitLoss > 0).Average(t => t.ProfitLoss);

        summary.AverageLoss = summary.Losses == 0
            ? 0
            : summary.Trades.Where(t => t.ProfitLoss < 0).Average(t => Math.Abs(t.ProfitLoss));

        summary.ExpectancyPerTrade = summary.TotalTrades == 0
            ? 0
            : summary.NetProfit / summary.TotalTrades;
    }

    private sealed record BacktestExit(decimal ExitPrice, DateTime ExitTimeUtc, string ExitReason);
}