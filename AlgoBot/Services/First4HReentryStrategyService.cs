using System.Collections.Concurrent;
using AlgoBot.Configuration;
using AlgoBot.Helpers;
using AlgoBot.Interfaces;
using AlgoBot.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AlgoBot.Services;

public sealed class First4HReentryStrategyService : IFirst4HReentryStrategyService
{
    private readonly IMarketDataProvider _marketDataProvider;
    private readonly IOptionsMonitor<BotSettings> _botSettings;
    private readonly ILogger<First4HReentryStrategyService> _logger;

    private readonly ConcurrentDictionary<string, LiveProfileInstrumentState> _state =
        new(StringComparer.OrdinalIgnoreCase);

    public First4HReentryStrategyService(
        IMarketDataProvider marketDataProvider,
        IOptionsMonitor<BotSettings> botSettings,
        ILogger<First4HReentryStrategyService> logger)
    {
        _marketDataProvider = marketDataProvider;
        _botSettings = botSettings;
        _logger = logger;
    }

    public async Task<First4HSignalEvaluationResult> EvaluateAsync(
        First4HProfileSettings profile,
        string instrument,
        CancellationToken cancellationToken = default)
    {
        var cfg = _botSettings.CurrentValue.First4HReentry;

        if (!cfg.Enabled || !profile.Enabled)
        {
            return new First4HSignalEvaluationResult
            {
                SignalReady = false,
                Reason = "First4H strategy or profile is disabled."
            };
        }

        if (!profile.Instruments.Contains(instrument, StringComparer.OrdinalIgnoreCase))
        {
            return new First4HSignalEvaluationResult
            {
                SignalReady = false,
                Reason = "Instrument is not assigned to this profile."
            };
        }

        var anchorTz = TimeZoneInfo.FindSystemTimeZoneById(profile.AnchorTimeZoneId);
        var nowAnchor = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, anchorTz);
        var tradingDay = DateOnly.FromDateTime(nowAnchor.Date);

        var stateKey = $"{profile.Name}::{instrument}";
        var state = _state.GetOrAdd(stateKey, _ => new LiveProfileInstrumentState
        {
            TradingDay = tradingDay
        });

        if (state.TradingDay != tradingDay)
            state.ResetForNewDay(tradingDay);

        if (!state.RangeBuilt)
        {
            var range = await TryBuildDailyRangeAsync(
                profile,
                instrument,
                tradingDay,
                cfg,
                anchorTz,
                cancellationToken);

            if (range is null)
            {
                return new First4HSignalEvaluationResult
                {
                    SignalReady = false,
                    Reason = "First 4H daily candle is not complete yet."
                };
            }

            state.RangeBuilt = true;
            state.Range = range;
        }

        var spec = await _marketDataProvider.GetSymbolSpecificationAsync(instrument, cancellationToken);
        if (spec is null)
        {
            return new First4HSignalEvaluationResult
            {
                SignalReady = false,
                Reason = "Symbol specification could not be loaded."
            };
        }

        var signalCandles = await LoadSignalCandlesForDayAsync(
            instrument,
            state.Range!,
            cfg.SignalTimeframe,
            cancellationToken);

        if (signalCandles.Count == 0)
        {
            return new First4HSignalEvaluationResult
            {
                SignalReady = false,
                Reason = "No signal candles available after 4H range."
            };
        }

        var candidate = FindNextSignalCandidate(
            signalCandles,
            state.Range!,
            state.LastSignaledReentryTimeUtc);

        if (candidate is null)
        {
            return new First4HSignalEvaluationResult
            {
                SignalReady = false,
                Reason = "No completed breakout-and-reentry setup yet."
            };
        }

        var stop = ResolveStopLoss(
            candidate.Direction,
            candidate.EntryPrice,
            candidate.BreakoutExtreme,
            state.Range!,
            spec,
            cfg,
            candidate.ContextCandles);

        if (!stop.HasValue)
        {
            return new First4HSignalEvaluationResult
            {
                SignalReady = false,
                Reason = "Setup found but stop loss could not be resolved."
            };
        }

        if (candidate.Direction == TradeDirection.Buy && stop.Value >= candidate.EntryPrice)
        {
            return new First4HSignalEvaluationResult
            {
                SignalReady = false,
                Reason = "Invalid long setup stop loss."
            };
        }

        if (candidate.Direction == TradeDirection.Sell && stop.Value <= candidate.EntryPrice)
        {
            return new First4HSignalEvaluationResult
            {
                SignalReady = false,
                Reason = "Invalid short setup stop loss."
            };
        }

        var riskDistance = Math.Abs(candidate.EntryPrice - stop.Value);
        if (riskDistance <= 0)
        {
            return new First4HSignalEvaluationResult
            {
                SignalReady = false,
                Reason = "Risk distance is zero."
            };
        }

        var takeProfit = candidate.Direction == TradeDirection.Buy
            ? candidate.EntryPrice + (riskDistance * cfg.RewardRiskRatio)
            : candidate.EntryPrice - (riskDistance * cfg.RewardRiskRatio);

        state.LastSignaledReentryTimeUtc = candidate.ReentryCandle.Time;

        var signal = new TradeSignal
        {
            SessionName = $"First4H:{profile.Name}",
            Instrument = instrument,
            Direction = candidate.Direction,
            ShouldTrade = true,
            EntryPrice = candidate.EntryPrice,
            StopLoss = stop.Value,
            TakeProfit = takeProfit,
            PassedConditions = new List<string>
            {
                "First4HRange",
                "BreakoutOutsideRange",
                "ReEntryInsideRange"
            },
            Reason = candidate.Direction == TradeDirection.Sell
                ? "Upside breakout failed and re-entered range. Short setup confirmed."
                : "Downside breakout failed and re-entered range. Long setup confirmed."
        };

        _logger.LogInformation(
            "First4H signal ready | Profile={Profile} Instrument={Instrument} Direction={Direction} Entry={Entry} Stop={Stop} TP={TP} ReentryTime={ReentryTime:O}",
            profile.Name,
            instrument,
            signal.Direction,
            signal.EntryPrice,
            signal.StopLoss,
            signal.TakeProfit,
            candidate.ReentryCandle.Time);

        return new First4HSignalEvaluationResult
        {
            SignalReady = true,
            Reason = signal.Reason,
            Signal = signal
        };
    }

    private async Task<First4HDayRange?> TryBuildDailyRangeAsync(
        First4HProfileSettings profile,
        string instrument,
        DateOnly tradingDay,
        First4HReentrySettings cfg,
        TimeZoneInfo anchorTz,
        CancellationToken cancellationToken)
    {
        var dayStartLocal = tradingDay.ToDateTime(TimeOnly.FromTimeSpan(profile.DayStartTime));
        var rangeStartOffset = new DateTimeOffset(dayStartLocal, anchorTz.GetUtcOffset(dayStartLocal));
        var rangeEndOffset = rangeStartOffset.AddHours(4);

        var rangeStartUtc = rangeStartOffset.UtcDateTime;
        var rangeEndUtc = rangeEndOffset.UtcDateTime;

        if (DateTime.UtcNow < rangeEndUtc)
            return null;

        var candles = await _marketDataProvider.GetCandlesAsync(
            instrument,
            cfg.RangeTimeframe,
            20,
            rangeEndUtc.AddMinutes(1),
            cancellationToken);

        var firstDaily4HCandle = candles
            .Where(c => c.Time == rangeStartUtc)
            .OrderBy(c => c.Time)
            .FirstOrDefault();

        if (firstDaily4HCandle is null)
        {
            firstDaily4HCandle = candles
                .Where(c => c.Time >= rangeStartUtc && c.Time < rangeEndUtc)
                .OrderBy(c => c.Time)
                .FirstOrDefault();
        }

        if (firstDaily4HCandle is null)
            return null;

        return new First4HDayRange
        {
            TradingDay = tradingDay,
            RangeStartUtc = rangeStartUtc,
            RangeEndUtc = rangeEndUtc,
            High = firstDaily4HCandle.High,
            Low = firstDaily4HCandle.Low
        };
    }

    private async Task<List<Candle>> LoadSignalCandlesForDayAsync(
        string instrument,
        First4HDayRange range,
        string signalTimeframe,
        CancellationToken cancellationToken)
    {
        var tfSpan = TradingTimeHelper.GetTimeframeSpan(signalTimeframe);
        var elapsed = DateTime.UtcNow - range.RangeEndUtc;

        var limit = Math.Min(
            2000,
            Math.Max(100, (int)Math.Ceiling(elapsed.TotalMilliseconds / tfSpan.TotalMilliseconds) + 50));

        var candles = await _marketDataProvider.GetCandlesAsync(
            instrument,
            signalTimeframe,
            limit,
            DateTime.UtcNow,
            cancellationToken);

        var dayEndUtc = range.RangeStartUtc.AddDays(1);

        return candles
            .Where(c => c.Time >= range.RangeEndUtc && c.Time < dayEndUtc)
            .OrderBy(c => c.Time)
            .ToList();
    }

    private SignalCandidate? FindNextSignalCandidate(
        IReadOnlyList<Candle> signalCandles,
        First4HDayRange range,
        DateTime? lastSignaledReentryTimeUtc)
    {
        var cfg = _botSettings.CurrentValue.First4HReentry;

        var breakoutActive = false;
        First4HBreakoutDirection breakoutDirection = First4HBreakoutDirection.None;
        decimal breakoutExtreme = 0m;
        var breakoutIndex = -1;

        for (var i = 0; i < signalCandles.Count; i++)
        {
            var candle = signalCandles[i];

            if (!breakoutActive)
            {
                if (candle.Close > range.High)
                {
                    breakoutActive = true;
                    breakoutDirection = First4HBreakoutDirection.AboveRange;
                    breakoutExtreme = candle.High;
                    breakoutIndex = i;
                    continue;
                }

                if (candle.Close < range.Low)
                {
                    breakoutActive = true;
                    breakoutDirection = First4HBreakoutDirection.BelowRange;
                    breakoutExtreme = candle.Low;
                    breakoutIndex = i;
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
                    breakoutIndex = i;
                    continue;
                }

                var reentered = candle.Close < range.High && candle.Close >= range.Low;
                if (!reentered)
                    continue;

                if (lastSignaledReentryTimeUtc.HasValue && candle.Time <= lastSignaledReentryTimeUtc.Value)
                {
                    breakoutActive = false;
                    breakoutDirection = First4HBreakoutDirection.None;
                    breakoutExtreme = 0m;
                    breakoutIndex = -1;
                    continue;
                }

                var contextStart = Math.Max(0, i - cfg.KeyLevelLookbackBars);
                var contextCandles = signalCandles
                    .Skip(contextStart)
                    .Take(i - contextStart + 1)
                    .ToList();

                return new SignalCandidate(
                    Direction: TradeDirection.Sell,
                    ReentryCandle: candle,
                    EntryPrice: candle.Close,
                    BreakoutExtreme: breakoutExtreme,
                    ContextCandles: contextCandles);
            }

            if (breakoutDirection == First4HBreakoutDirection.BelowRange)
            {
                breakoutExtreme = Math.Min(breakoutExtreme, candle.Low);

                if (candle.Close > range.High)
                {
                    breakoutDirection = First4HBreakoutDirection.AboveRange;
                    breakoutExtreme = candle.High;
                    breakoutIndex = i;
                    continue;
                }

                var reentered = candle.Close > range.Low && candle.Close <= range.High;
                if (!reentered)
                    continue;

                if (lastSignaledReentryTimeUtc.HasValue && candle.Time <= lastSignaledReentryTimeUtc.Value)
                {
                    breakoutActive = false;
                    breakoutDirection = First4HBreakoutDirection.None;
                    breakoutExtreme = 0m;
                    breakoutIndex = -1;
                    continue;
                }

                var contextStart = Math.Max(0, i - cfg.KeyLevelLookbackBars);
                var contextCandles = signalCandles
                    .Skip(contextStart)
                    .Take(i - contextStart + 1)
                    .ToList();

                return new SignalCandidate(
                    Direction: TradeDirection.Buy,
                    ReentryCandle: candle,
                    EntryPrice: candle.Close,
                    BreakoutExtreme: breakoutExtreme,
                    ContextCandles: contextCandles);
            }
        }

        return null;
    }

    private decimal? ResolveStopLoss(
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
            var pivotHigh = FindNearestPivotHigh(
                contextCandles,
                entryPrice,
                defaultStop,
                cfg.PivotStrength);

            return pivotHigh.HasValue
                ? pivotHigh.Value + buffer
                : null;
        }

        var pivotLow = FindNearestPivotLow(
            contextCandles,
            defaultStop,
            entryPrice,
            cfg.PivotStrength);

        return pivotLow.HasValue
            ? pivotLow.Value - buffer
            : null;
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

        return pivots.Count == 0
            ? null
            : pivots.OrderBy(x => x).First();
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

        return pivots.Count == 0
            ? null
            : pivots.OrderByDescending(x => x).First();
    }

    private sealed class LiveProfileInstrumentState
    {
        public DateOnly TradingDay { get; set; }
        public bool RangeBuilt { get; set; }
        public First4HDayRange? Range { get; set; }
        public DateTime? LastSignaledReentryTimeUtc { get; set; }

        public void ResetForNewDay(DateOnly tradingDay)
        {
            TradingDay = tradingDay;
            RangeBuilt = false;
            Range = null;
            LastSignaledReentryTimeUtc = null;
        }
    }

    private sealed record SignalCandidate(
        TradeDirection Direction,
        Candle ReentryCandle,
        decimal EntryPrice,
        decimal BreakoutExtreme,
        IReadOnlyList<Candle> ContextCandles);
}