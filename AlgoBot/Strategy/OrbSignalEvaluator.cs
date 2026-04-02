using System.Collections.Concurrent;
using AlgoBot.Configuration;
using AlgoBot.Helpers;
using AlgoBot.Interfaces;
using AlgoBot.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AlgoBot.Strategy;

public sealed class OrbSignalEvaluator : IOrbSignalEvaluator
{
    private readonly IMarketDataProvider _marketDataProvider;
    private readonly IFibonacciRetracementService _fibonacciRetracementService;
    private readonly IEnumerable<IEntryFilter> _entryFilters;
    private readonly IOptionsMonitor<BotSettings> _botSettings;
    private readonly ILogger<OrbSignalEvaluator> _logger;

    private readonly ConcurrentDictionary<string, SymbolSpecification> _symbolSpecificationCache =
        new(StringComparer.OrdinalIgnoreCase);

    public OrbSignalEvaluator(
        IMarketDataProvider marketDataProvider,
        IFibonacciRetracementService fibonacciRetracementService,
        IEnumerable<IEntryFilter> entryFilters,
        IOptionsMonitor<BotSettings> botSettings,
        ILogger<OrbSignalEvaluator> logger)
    {
        _marketDataProvider = marketDataProvider;
        _fibonacciRetracementService = fibonacciRetracementService;
        _entryFilters = entryFilters;
        _botSettings = botSettings;
        _logger = logger;
    }

    public async Task<OrbSignalEvaluationResult> EvaluateAsync(
        TradingSessionSettings session,
        InstrumentState instrumentState,
        CancellationToken cancellationToken = default)
    {
        var settings = _botSettings.CurrentValue;
        var strategy = settings.Strategy;

        var signal = new TradeSignal
        {
            SessionName = session.Name,
            Instrument = instrumentState.Instrument,
            Direction = TradeDirection.None,
            ShouldTrade = false
        };

        if (!instrumentState.RangeBuilt || instrumentState.OrbRange is null)
        {
            signal.FailedConditions.Add("ORB");
            signal.Reason = "ORB range has not been built yet.";
            return new OrbSignalEvaluationResult { Signal = signal };
        }

        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(settings.TimeZoneId);
        var nowUtc = DateTime.UtcNow;
        var nowLocal = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, timeZone);

        var tradingDay = TradingTimeHelper.GetSessionTradingDay(session, nowLocal);
        var window = TradingTimeHelper.GetSessionWindow(session, tradingDay, timeZone);

        var breakoutTimeframe = strategy.BreakoutTimeframe;
        var entryTimeframe = string.IsNullOrWhiteSpace(strategy.EntryTimeframe)
            ? breakoutTimeframe
            : strategy.EntryTimeframe;

        var breakoutTimeframeSpan = TradingTimeHelper.GetTimeframeSpan(breakoutTimeframe);
        var entryTimeframeSpan = TradingTimeHelper.GetTimeframeSpan(entryTimeframe);
        var useSeparateEntryTimeframe = !string.Equals(
            breakoutTimeframe,
            entryTimeframe,
            StringComparison.OrdinalIgnoreCase);

        var elapsedSinceOrb = nowUtc - window.OrbEndUtc;
        if (elapsedSinceOrb <= TimeSpan.Zero)
        {
            signal.FailedConditions.Add("Breakout");
            signal.Reason = "Breakout evaluation is waiting for ORB window to close.";
            return new OrbSignalEvaluationResult { Signal = signal };
        }

        var breakoutCandlesToRequest = Math.Min(
            1000,
            Math.Max(
                30,
                (int)Math.Ceiling(elapsedSinceOrb.TotalMilliseconds / breakoutTimeframeSpan.TotalMilliseconds) + 10));

        var breakoutCandles = await _marketDataProvider.GetCandlesAsync(
            instrumentState.Instrument,
            breakoutTimeframe,
            breakoutCandlesToRequest,
            nowUtc,
            cancellationToken);

        var postOrbBreakoutCandles = breakoutCandles
            .Where(c => c.Time >= window.OrbEndUtc && c.Time <= nowUtc)
            .OrderBy(c => c.Time)
            .ToList();

        if (postOrbBreakoutCandles.Count == 0)
        {
            signal.FailedConditions.Add("Breakout");
            signal.Reason = "No breakout-timeframe candles available after ORB.";
            return new OrbSignalEvaluationResult { Signal = signal };
        }

        var spec = await GetSymbolSpecificationAsync(instrumentState.Instrument, cancellationToken);
        var breakout = FindBreakout(signal, postOrbBreakoutCandles, instrumentState.OrbRange, strategy, spec);

        if (!breakout.Found)
        {
            signal.Direction = TradeDirection.None;
            signal.ShouldTrade = false;
            signal.Reason = breakout.Reason;
            signal.FailedConditions.Add("Breakout");
            return new OrbSignalEvaluationResult { Signal = signal };
        }

        signal.Direction = breakout.Direction;
        signal.PassedConditions.Add("Breakout");

        var result = new OrbSignalEvaluationResult
        {
            Signal = signal,
            BreakoutDetected = true,
            BreakoutDirection = breakout.Direction,
            BreakoutTimeUtc = breakout.Candle?.Time,
            BreakoutClosePrice = breakout.Candle?.Close
        };

        var requiresRetest = strategy.EntryMode is EntryMode.BreakoutRetest or EntryMode.BreakoutRetestFibonacci;
        var requiresFib = strategy.EntryMode is EntryMode.BreakoutFibonacci or EntryMode.BreakoutRetestFibonacci;

        List<Candle> candlesAfterBreakout;

        if (useSeparateEntryTimeframe)
        {
            var elapsedSinceBreakout = nowUtc - breakout.Candle!.Time;
            var entryCandlesToRequest = Math.Min(
                1000,
                Math.Max(
                    30,
                    (int)Math.Ceiling(elapsedSinceBreakout.TotalMilliseconds / entryTimeframeSpan.TotalMilliseconds) + 10));

            var entryCandles = await _marketDataProvider.GetCandlesAsync(
                instrumentState.Instrument,
                entryTimeframe,
                entryCandlesToRequest,
                nowUtc,
                cancellationToken);

            candlesAfterBreakout = entryCandles
                .Where(c => c.Time > breakout.Candle.Time && c.Time <= nowUtc)
                .OrderBy(c => c.Time)
                .ToList();
        }
        else
        {
            candlesAfterBreakout = postOrbBreakoutCandles
                .Where(c => breakout.Candle is not null && c.Time > breakout.Candle.Time)
                .OrderBy(c => c.Time)
                .ToList();
        }

        if (useSeparateEntryTimeframe &&
    candlesAfterBreakout.Count == 0 &&
    strategy.EntryMode != EntryMode.BreakoutOnly)
        {
            signal.FailedConditions.Add("EntryTimeframe");
            signal.Reason = $"Waiting for {entryTimeframe} candles after breakout.";
            return new OrbSignalEvaluationResult
            {
                Signal = signal,
                BreakoutDetected = true,
                BreakoutDirection = breakout.Direction,
                BreakoutTimeUtc = breakout.Candle?.Time,
                BreakoutClosePrice = breakout.Candle?.Close
            };
        }

        var retestConfirmed = false;
        DateTime? retestTimeUtc = null;
        decimal? retestReferencePrice = null;

        if (requiresRetest)
        {
            var retestResult = TryConfirmRetest(
                breakout.Direction,
                instrumentState.OrbRange,
                candlesAfterBreakout,
                spec,
                strategy.Retest);

            retestConfirmed = retestResult.Confirmed;
            retestTimeUtc = retestResult.TouchTimeUtc;
            retestReferencePrice = retestResult.ReferencePrice;

            if (retestConfirmed)
            {
                signal.PassedConditions.Add("Retest");
            }
            else
            {
                signal.FailedConditions.Add("Retest");
                signal.Reason = retestResult.Reason;
            }
        }

        var fibConfirmed = false;
        string? fibLevelName = null;
        decimal? fibReferencePrice = null;
        DateTime? fibTouchTimeUtc = null;

        if (requiresFib)
        {
            var fibResult = _fibonacciRetracementService.Evaluate(
                breakout.Direction,
                instrumentState.OrbRange,
                candlesAfterBreakout,
                spec,
                strategy.Fibonacci);

            fibConfirmed = fibResult.Confirmed;
            fibLevelName = fibResult.TouchedLevelName;
            fibReferencePrice = fibResult.TouchedLevelPrice;
            fibTouchTimeUtc = fibResult.TouchTimeUtc;

            if (fibConfirmed)
            {
                signal.PassedConditions.Add("Fibonacci");
            }
            else
            {
                signal.FailedConditions.Add("Fibonacci");
                signal.Reason = fibResult.Reason;
            }
        }

        var baseReady = !requiresRetest || retestConfirmed;
        baseReady = baseReady && (!requiresFib || fibConfirmed);

        if (!baseReady)
        {
            signal.ShouldTrade = false;

            if (string.IsNullOrWhiteSpace(signal.Reason))
            {
                signal.Reason = "Breakout detected, waiting for retest/Fibonacci confirmations.";
            }

            return new OrbSignalEvaluationResult
            {
                Signal = signal,
                BreakoutDetected = true,
                BreakoutDirection = breakout.Direction,
                BreakoutTimeUtc = breakout.Candle?.Time,
                BreakoutClosePrice = breakout.Candle?.Close,
                RetestConfirmed = retestConfirmed,
                RetestTimeUtc = retestTimeUtc,
                RetestReferencePrice = retestReferencePrice,
                FibonacciConfirmed = fibConfirmed,
                FibonacciTouchedLevel = fibLevelName,
                FibonacciReferencePrice = fibReferencePrice,
                FibonacciTouchTimeUtc = fibTouchTimeUtc
            };
        }

        var filterContext = new EntryFilterContext
        {
            SessionName = session.Name,
            Instrument = instrumentState.Instrument,
            Range = instrumentState.OrbRange,
            BreakoutDirection = breakout.Direction,
            BreakoutTimeUtc = breakout.Candle?.Time,
            BreakoutClosePrice = breakout.Candle?.Close,
            EvaluationTimeUtc = nowUtc,
            SessionStartUtc = window.SessionStartUtc
        };

        var indicatorResults = await EvaluateEnabledIndicatorFiltersAsync(filterContext, cancellationToken);

        foreach (var filterResult in indicatorResults)
        {
            if (filterResult.Passed)
                signal.PassedConditions.Add(filterResult.FilterName);
            else
                signal.FailedConditions.Add(filterResult.FilterName);
        }

        var allIndicatorsPassed = indicatorResults.All(r => r.Passed);
        signal.ShouldTrade = allIndicatorsPassed;

        Candle? entryCandle;

        if (useSeparateEntryTimeframe)
        {
            entryCandle = candlesAfterBreakout.LastOrDefault();
        }
        else
        {
            entryCandle = candlesAfterBreakout.LastOrDefault() ?? breakout.Candle;
        }

        signal.EntryPrice = entryCandle?.Close ?? breakout.Candle?.Close;

        if (allIndicatorsPassed)
        {
            signal.Reason = BuildSuccessReason(
                requiresRetest,
                requiresFib,
                fibLevelName,
                indicatorResults.Where(x => x.Passed).Select(x => x.FilterName));
        }
        else
        {
            var failedReasons = indicatorResults
                .Where(r => !r.Passed)
                .Select(r => $"{r.FilterName}: {r.Reason}");

            signal.Reason = "Base setup confirmed, but indicator filters failed. " +
                            string.Join(" | ", failedReasons);
        }

        return new OrbSignalEvaluationResult
        {
            Signal = signal,
            BreakoutDetected = true,
            BreakoutDirection = breakout.Direction,
            BreakoutTimeUtc = breakout.Candle?.Time,
            BreakoutClosePrice = breakout.Candle?.Close,
            RetestConfirmed = retestConfirmed,
            RetestTimeUtc = retestTimeUtc,
            RetestReferencePrice = retestReferencePrice,
            FibonacciConfirmed = fibConfirmed,
            FibonacciTouchedLevel = fibLevelName,
            FibonacciReferencePrice = fibReferencePrice,
            FibonacciTouchTimeUtc = fibTouchTimeUtc
        };
    }

    private async Task<IReadOnlyList<FilterEvaluationResult>> EvaluateEnabledIndicatorFiltersAsync(
        EntryFilterContext context,
        CancellationToken cancellationToken)
    {
        var results = new List<FilterEvaluationResult>();

        foreach (var filter in GetEnabledIndicatorFilters())
        {
            var result = await filter.EvaluateAsync(context, cancellationToken);
            results.Add(result);

            _logger.LogDebug(
                "Indicator filter evaluated | Filter={FilterName} Passed={Passed} Reason={Reason}",
                result.FilterName,
                result.Passed,
                result.Reason);
        }

        return results;
    }

    private IEnumerable<IEntryFilter> GetEnabledIndicatorFilters()
    {
        var indicators = _botSettings.CurrentValue.Strategy.Indicators;
        var strategy = _botSettings.CurrentValue.Strategy;

        foreach (var filter in _entryFilters)
        {
            if (filter.Name.Equals("EMA", StringComparison.OrdinalIgnoreCase) && indicators.Ema.Enabled)
                yield return filter;

            if (filter.Name.Equals("MACD", StringComparison.OrdinalIgnoreCase) && indicators.Macd.Enabled)
                yield return filter;

            if (filter.Name.Equals("RSI", StringComparison.OrdinalIgnoreCase) && indicators.Rsi.Enabled)
                yield return filter;

            if (filter.Name.Equals("Volume", StringComparison.OrdinalIgnoreCase) && strategy.Volume.Enabled)
                yield return filter;

            if (filter.Name.Equals("VWAP", StringComparison.OrdinalIgnoreCase) && strategy.Vwap.Enabled)
                yield return filter;
        }
    }

    private async Task<SymbolSpecification> GetSymbolSpecificationAsync(
        string instrument,
        CancellationToken cancellationToken)
    {
        if (_symbolSpecificationCache.TryGetValue(instrument, out var cached))
            return cached;

        var specification = await _marketDataProvider.GetSymbolSpecificationAsync(instrument, cancellationToken);
        if (specification is null)
            throw new InvalidOperationException($"Could not load symbol specification for {instrument}.");

        _symbolSpecificationCache[instrument] = specification;
        return specification;
    }

    private static BreakoutDetectionResult FindBreakout(
        TradeSignal signal,
        IReadOnlyList<Candle> postOrbCandles,
        OrbRange range,
        StrategySettings strategy,
        SymbolSpecification spec)
    {
        var buffer = PriceHelper.PipsToPriceDistance(spec, strategy.Breakout.CloseBufferPips);
        var maxLookahead = Math.Min(postOrbCandles.Count, strategy.Breakout.MaxBreakoutCandlesAfterOrb);

        for (var i = 0; i < maxLookahead; i++)
        {
            var candle = postOrbCandles[i];

            var bullishCloseBreakout = candle.Close > range.High + buffer;
            var bearishCloseBreakout = candle.Close < range.Low - buffer;

            if (!strategy.Breakout.RequireCandleCloseOutsideRange)
            {
                bullishCloseBreakout = candle.High > range.High + buffer;
                bearishCloseBreakout = candle.Low < range.Low - buffer;
            }

            if (bullishCloseBreakout)
            {
                signal.Direction = TradeDirection.Buy;
                return BreakoutDetectionResult.Success(TradeDirection.Buy, candle);
            }

            if (bearishCloseBreakout)
            {
                signal.Direction = TradeDirection.Sell;
                return BreakoutDetectionResult.Success(TradeDirection.Sell, candle);
            }
        }

        return BreakoutDetectionResult.Fail(
            $"No valid breakout found within {strategy.Breakout.MaxBreakoutCandlesAfterOrb} {strategy.BreakoutTimeframe} candles after ORB.");
    }

    private static RetestConfirmationResult TryConfirmRetest(
        TradeDirection direction,
        OrbRange range,
        IReadOnlyList<Candle> candlesAfterBreakout,
        SymbolSpecification specification,
        RetestSettings settings)
    {
        if (candlesAfterBreakout.Count == 0)
        {
            return RetestConfirmationResult.Fail("Waiting for post-breakout candles to evaluate retest.");
        }

        var tolerance = PriceHelper.PipsToPriceDistance(specification, settings.TolerancePips);
        var limit = Math.Min(candlesAfterBreakout.Count, settings.MaxCandlesAfterBreakout);

        for (var i = 0; i < limit; i++)
        {
            var candle = candlesAfterBreakout[i];

            if (direction == TradeDirection.Buy)
            {
                var touched = PriceHelper.CandleTouchesLevel(candle, range.High, tolerance);
                if (touched)
                {
                    if (!settings.RequireCloseInBreakoutDirection || candle.Close > range.High)
                    {
                        return RetestConfirmationResult.Success(candle.Time, candle.Low);
                    }
                }
            }
            else if (direction == TradeDirection.Sell)
            {
                var touched = PriceHelper.CandleTouchesLevel(candle, range.Low, tolerance);
                if (touched)
                {
                    if (!settings.RequireCloseInBreakoutDirection || candle.Close < range.Low)
                    {
                        return RetestConfirmationResult.Success(candle.Time, candle.High);
                    }
                }
            }
        }

        return RetestConfirmationResult.Fail("No valid retest confirmation found yet.");
    }

    private static string BuildSuccessReason(
        bool requiresRetest,
        bool requiresFib,
        string? fibLevelName,
        IEnumerable<string> passedIndicatorFilters)
    {
        var indicatorText = string.Join(", ", passedIndicatorFilters);

        if (!requiresRetest && !requiresFib)
            return string.IsNullOrWhiteSpace(indicatorText)
                ? "Breakout-only entry confirmed."
                : $"Breakout-only entry confirmed with indicators: {indicatorText}.";

        if (requiresRetest && !requiresFib)
            return string.IsNullOrWhiteSpace(indicatorText)
                ? "Breakout and retest confirmed."
                : $"Breakout and retest confirmed with indicators: {indicatorText}.";

        if (!requiresRetest && requiresFib)
        {
            var baseText = string.IsNullOrWhiteSpace(fibLevelName)
                ? "Breakout and Fibonacci confirmed."
                : $"Breakout and Fibonacci confirmed at level {fibLevelName}.";

            return string.IsNullOrWhiteSpace(indicatorText)
                ? baseText
                : $"{baseText} Indicators: {indicatorText}.";
        }

        var combinedText = string.IsNullOrWhiteSpace(fibLevelName)
            ? "Breakout, retest, and Fibonacci confirmed."
            : $"Breakout, retest, and Fibonacci confirmed at level {fibLevelName}.";

        return string.IsNullOrWhiteSpace(indicatorText)
            ? combinedText
            : $"{combinedText} Indicators: {indicatorText}.";
    }

    private sealed class BreakoutDetectionResult
    {
        public bool Found { get; init; }
        public TradeDirection Direction { get; init; } = TradeDirection.None;
        public Candle? Candle { get; init; }
        public string Reason { get; init; } = string.Empty;

        public static BreakoutDetectionResult Success(TradeDirection direction, Candle candle) =>
            new()
            {
                Found = true,
                Direction = direction,
                Candle = candle,
                Reason = "Breakout detected."
            };

        public static BreakoutDetectionResult Fail(string reason) =>
            new()
            {
                Found = false,
                Reason = reason
            };
    }

    private sealed class RetestConfirmationResult
    {
        public bool Confirmed { get; init; }
        public DateTime? TouchTimeUtc { get; init; }
        public decimal? ReferencePrice { get; init; }
        public string Reason { get; init; } = string.Empty;

        public static RetestConfirmationResult Success(DateTime touchTimeUtc, decimal referencePrice) =>
            new()
            {
                Confirmed = true,
                TouchTimeUtc = touchTimeUtc,
                ReferencePrice = referencePrice,
                Reason = "Retest confirmed."
            };

        public static RetestConfirmationResult Fail(string reason) =>
            new()
            {
                Confirmed = false,
                Reason = reason
            };
    }
}