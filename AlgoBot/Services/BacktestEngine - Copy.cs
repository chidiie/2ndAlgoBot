//using AlgoBot.Configuration;
//using AlgoBot.Helpers;
//using AlgoBot.Interfaces;
//using AlgoBot.Models;
//using Microsoft.Extensions.Logging;
//using Microsoft.Extensions.Options;

//namespace AlgoBot.Services;

//public sealed class BacktestEngine : IBacktestEngine
//{
//    private readonly IHistoricalDataProvider _historicalDataProvider;
//    private readonly IMarketDataProvider _marketDataProvider;
//    private readonly IIndicatorService _indicatorService;
//    private readonly IOptionsMonitor<BotSettings> _botSettings;
//    private readonly ILogger<BacktestEngine> _logger;

//    public BacktestEngine(
//        IHistoricalDataProvider historicalDataProvider,
//        IMarketDataProvider marketDataProvider,
//        IIndicatorService indicatorService,
//        IOptionsMonitor<BotSettings> botSettings,
//        ILogger<BacktestEngine> logger)
//    {
//        _historicalDataProvider = historicalDataProvider;
//        _marketDataProvider = marketDataProvider;
//        _indicatorService = indicatorService;
//        _botSettings = botSettings;
//        _logger = logger;
//    }

//    private static string NormaliseSymbol(string instrument) =>
//        instrument + "m";

//    public async Task<BacktestSummary> RunAsync(
//        BacktestRequest request,
//        CancellationToken cancellationToken = default)
//    {
//        var bot = _botSettings.CurrentValue;
//        var sessions = bot.TradingSessions
//            .Where(x => x.Enabled)
//            .Where(x => request.SessionsToRun.Count == 0 ||
//                        request.SessionsToRun.Contains(x.Name, StringComparer.OrdinalIgnoreCase))
//            .ToList();

//        var summary = new BacktestSummary
//        {
//            StartUtc = request.StartUtc,
//            EndUtc = request.EndUtc,
//            StartingBalance = request.StartingBalance,
//            EndingBalance = request.StartingBalance
//        };

//        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(bot.TimeZoneId);
//        var equity = request.StartingBalance;
//        var peak = equity;

//        foreach (var session in sessions)
//        {
//            var instruments = request.InstrumentsOverride.Count > 0
//                ? request.InstrumentsOverride
//                : session.Instruments;

//            foreach (var instrument in instruments.Distinct(StringComparer.OrdinalIgnoreCase))
//            {
//                var instrumentM = NormaliseSymbol(instrument);
//                var trades = await BacktestInstrumentAsync(
//                    session,
//                    instrumentM,
//                    request.StartUtc,
//                    request.EndUtc,
//                    equity,
//                    timeZone,
//                    cancellationToken);

//                foreach (var trade in trades)
//                {
//                    summary.Trades.Add(trade);
//                    equity += trade.ProfitLoss;
//                    summary.EndingBalance = equity;

//                    if (equity > peak)
//                        peak = equity;

//                    var ddAmount = peak - equity;
//                    var ddPercent = peak <= 0 ? 0 : (ddAmount / peak) * 100m;

//                    if (ddAmount > summary.MaxDrawdownAmount)
//                        summary.MaxDrawdownAmount = ddAmount;

//                    if (ddPercent > summary.MaxDrawdownPercent)
//                        summary.MaxDrawdownPercent = ddPercent;
//                }
//            }
//        }

//        BuildSummary(summary);

//        _logger.LogInformation(
//            "Backtest complete. Trades={Trades}, NetProfit={NetProfit}, WinRate={WinRate:F2}%, MaxDD={MaxDd:F2}%",
//            summary.TotalTrades,
//            summary.NetProfit,
//            summary.WinRatePercent,
//            summary.MaxDrawdownPercent);

//        return summary;
//    }

//    private async Task<List<BacktestTradeResult>> BacktestInstrumentAsync(
//    TradingSessionSettings session,
//    string instrument,
//    DateTime startUtc,
//    DateTime endUtc,
//    decimal startingEquity,
//    TimeZoneInfo timeZone,
//    CancellationToken cancellationToken)
//    {
//        var bot = _botSettings.CurrentValue;
//        var strategy = bot.Strategy;

//        string setup = $"{strategy.RangeTimeframe}range_{strategy.BreakoutTimeframe}bk_{strategy.EntryTimeframe}entry";
            
//        string timeUtc = $"{startUtc.ToString("yyyy-MM-dd")} - {endUtc.ToString("yyyy-MM-dd")}"; 

//        string cacheFile = $"candle_cache_{instrument}_{setup}_{timeUtc}.json";
//        CandleCache? cache = null;

//        if (File.Exists(cacheFile))
//        {
//            try
//            {
//                string json = await File.ReadAllTextAsync(cacheFile);
//                cache = System.Text.Json.JsonSerializer.Deserialize<CandleCache>(json);
//                //if (cache != null)
//                    //Log.Information("✅ Using cached candles from {File} — results will be consistent", cacheFile);
//            }
//            catch { cache = null; }
//        }

//        var breakoutTf = strategy.BreakoutTimeframe;
//        var rangeTf = strategy.RangeTimeframe;

//        if (cache == null)
//        {
//            var breakoutCandles = await _historicalDataProvider.GetCandlesRangeAsync(
//                instrument,
//                breakoutTf,
//                startUtc.AddDays(-2),
//                endUtc.AddDays(1),
//                cancellationToken);

//            if (breakoutCandles.Count == 0)
//                return new List<BacktestTradeResult>();

//            var entryTimeframe = string.IsNullOrWhiteSpace(strategy.EntryTimeframe)
//        ? breakoutTf
//        : strategy.EntryTimeframe;

//            var entryCandles = string.Equals(entryTimeframe, breakoutTf, StringComparison.OrdinalIgnoreCase)
//                ? breakoutCandles.ToList()
//                : (await _historicalDataProvider.GetCandlesRangeAsync(
//                    instrument,
//                    entryTimeframe,
//                    startUtc.AddDays(-2),
//                    endUtc.AddDays(1),
//                    cancellationToken)).ToList();

//            // Preload indicator candles by their own timeframe
//            var indicatorCandlesByTimeframe = await LoadIndicatorCandlesAsync(
//                instrument,
//                startUtc,
//                endUtc,
//                cancellationToken);

//            cache = new CandleCache
//            {
//                IndicatorCandles = indicatorCandlesByTimeframe,
//                BreakoutCandles = breakoutCandles,
//                EntryCandles = entryCandles,
//                FetchedAt = DateTime.UtcNow
//            };

//            // Save to disk for future runs
//            try
//            {
//                string json = System.Text.Json.JsonSerializer.Serialize(cache,
//                    new System.Text.Json.JsonSerializerOptions { WriteIndented = false });
//                await File.WriteAllTextAsync(cacheFile, json);
//                //Log.Information("💾 Candles cached to {File} — future runs will use this file", cacheFile);
//                //Log.Information("    To refresh: dotnet run -- backtest-orb {Inst} {Count} --refresh",
//                //    instrument, m1Count);
//            }
//            catch (Exception ex)
//            {
//                //Log.Warning(ex, "Could not save candle cache — results may vary between runs");
//            }
//        }

        

//        var spec = await _marketDataProvider.GetSymbolSpecificationAsync(instrument, cancellationToken);
//        if (spec is null)
//            return new List<BacktestTradeResult>();

//        //// Preload indicator candles by their own timeframe
//        //var indicatorCandlesByTimeframe = await LoadIndicatorCandlesAsync(
//        //    instrument,
//        //    startUtc,
//        //    endUtc,
//        //    cancellationToken);

//        var results = new List<BacktestTradeResult>();
//        var equity = startingEquity;

//        var localStart = TimeZoneInfo.ConvertTimeFromUtc(startUtc, timeZone).Date;
//        var localEnd = TimeZoneInfo.ConvertTimeFromUtc(endUtc, timeZone).Date;

//        for (var day = localStart; day <= localEnd; day = day.AddDays(1))
//        {
//            var tradingDay = DateOnly.FromDateTime(day);
//            var window = TradingTimeHelper.GetSessionWindow(session, tradingDay, timeZone);

//            if (window.SessionEndUtc < startUtc || window.SessionStartUtc > endUtc)
//                continue;

//            var sessionCandles = cache.BreakoutCandles
//                .Where(c => c.Time >= window.SessionStartUtc && c.Time <= window.SessionEndUtc)
//                .OrderBy(c => c.Time)
//                .ToList();

//            if (sessionCandles.Count == 0)
//                continue;

//            var sessionEntryCandles = cache.EntryCandles
//                .Where(c => c.Time >= window.SessionStartUtc && c.Time <= window.SessionEndUtc)
//                .OrderBy(c => c.Time)
//                .ToList();

//            var rangeCandles = sessionCandles
//                .Where(c => c.Time >= window.SessionStartUtc && c.Time < window.OrbEndUtc)
//                .OrderBy(c => c.Time)
//                .ToList();

//            var expectedRangeBars = GetExpectedBarCount(rangeTf, window.SessionStartUtc, window.OrbEndUtc);
//            if (expectedRangeBars <= 0 || rangeCandles.Count < expectedRangeBars)
//                continue;

//            var range = new OrbRange
//            {
//                StartTimeUtc = window.SessionStartUtc,
//                EndTimeUtc = window.OrbEndUtc,
//                High = rangeCandles.Max(x => x.High),
//                Low = rangeCandles.Min(x => x.Low)
//            };

//            var postOrb = sessionCandles
//                .Where(c => c.Time >= window.OrbEndUtc)
//                .OrderBy(c => c.Time)
//                .ToList();

//            if (postOrb.Count == 0)
//                continue;

//            var trade = SimulateSingleTrade(
//                session.Name,
//                instrument,
//                tradingDay,
//                postOrb,
//                sessionEntryCandles,
//                range,
//                spec,
//                equity,
//                window,
//                cache.IndicatorCandles);

//            if (trade is null)
//                continue;

//            equity += trade.ProfitLoss;
//            results.Add(trade);
//        }

//        return results;
//    }

//    private BacktestTradeResult? SimulateSingleTrade(
//    string sessionName,
//    string instrument,
//    DateOnly tradingDay,
//    IReadOnlyList<Candle> postOrbCandles,
//    IReadOnlyList<Candle> sessionEntryCandles,
//    OrbRange range,
//    SymbolSpecification spec,
//    decimal equity,
//    SessionWindow window,
//    IReadOnlyDictionary<string, List<Candle>> indicatorCandlesByTimeframe)
//    {
//        var bot = _botSettings.CurrentValue;
//        var strategy = bot.Strategy;
//        var risk = bot.Risk;
//        var backtest = bot.Backtest;

//        var breakout = FindBreakout(postOrbCandles, range, strategy, spec);
//        if (breakout is null)
//            return null;

//        var entryTimeframe = string.IsNullOrWhiteSpace(strategy.EntryTimeframe)
//            ? strategy.BreakoutTimeframe
//            : strategy.EntryTimeframe;

//        var useSeparateEntryTimeframe = !string.Equals(
//            entryTimeframe,
//            strategy.BreakoutTimeframe,
//            StringComparison.OrdinalIgnoreCase);

//        var candlesAfterBreakout = useSeparateEntryTimeframe
//            ? sessionEntryCandles
//                .Where(c => c.Time > breakout.TimeUtc)
//                .OrderBy(c => c.Time)
//                .ToList()
//            : postOrbCandles
//                .Where(c => c.Time > breakout.TimeUtc)
//                .OrderBy(c => c.Time)
//                .ToList();

//        if (RequiresRetest(strategy.EntryMode) &&
//            !ConfirmRetest(breakout.Direction, range, candlesAfterBreakout, spec, strategy.Retest))
//        {
//            return null;
//        }

//        if (RequiresFib(strategy.EntryMode) &&
//            !ConfirmFib(breakout.Direction, range, candlesAfterBreakout, spec, strategy.Fibonacci))
//        {
//            return null;
//        }

//        if (!ConfirmIndicators(
//        breakout.Direction,
//        breakout.TimeUtc,
//        breakout.Candle.Close,
//        window.SessionStartUtc,
//        strategy,
//        spec,
//        indicatorCandlesByTimeframe))
//        {
//            return null;
//        }

//        var entryBar = backtest.UseNextBarOpenForEntry
//            ? candlesAfterBreakout.FirstOrDefault()
//            : breakout.Candle;

//        if (entryBar is null)
//            return null;

//        var rawEntry = backtest.UseNextBarOpenForEntry ? entryBar.Open : breakout.Candle.Close;
//        var entryPrice = ApplyEntryCosts(
//            rawEntry,
//            breakout.Direction,
//            spec,
//            backtest.EntrySpreadPips + backtest.SlippagePips);

//        var stopLoss = BuildStopLossForBacktest(breakout.Direction, range, spec, risk);
//        if (!stopLoss.HasValue)
//            return null;

//        var stopDistancePrice = Math.Abs(entryPrice - stopLoss.Value);
//        if (stopDistancePrice <= 0)
//            return null;

//        var riskAmount = equity * (risk.RiskPerTradePercent / 100m);
//        //var qty = SizeVolumeForBacktest(spec, stopDistancePrice, riskAmount);
//        var qty = SizeVolumeForBacktest(spec, instrument, stopDistancePrice, riskAmount);
//        if (qty < spec.MinVolume)
//            return null;

//        var takeProfit = breakout.Direction == TradeDirection.Buy
//            ? entryPrice + (stopDistancePrice * risk.RewardRiskRatio)
//            : entryPrice - (stopDistancePrice * risk.RewardRiskRatio);

//        var futureBars = postOrbCandles
//            .Where(c => c.Time >= entryBar.Time)
//            .OrderBy(c => c.Time)
//            .ToList();

//        if (futureBars.Count == 0)
//            return null;

//        var exit = SimulateExit(
//            breakout.Direction,
//            futureBars,
//            stopLoss.Value,
//            takeProfit,
//            spec,
//            backtest.ExitSpreadPips);

//        var contractSize = ResolveContractSize(spec, instrument);

//        var priceMove = breakout.Direction == TradeDirection.Buy
//            ? exit.ExitPrice - entryPrice
//            : entryPrice - exit.ExitPrice;

//        var stopMove = breakout.Direction == TradeDirection.Buy
//            ? entryPrice - stopLoss.Value
//            : stopLoss.Value - entryPrice;

//        var profitLoss = priceMove * contractSize * qty;
//        var totalRisk = Math.Abs(stopMove * contractSize * qty);
//        var rMultiple = totalRisk <= 0 ? 0 : profitLoss / totalRisk;

//        return new BacktestTradeResult
//        {
//            SessionName = sessionName,
//            Instrument = instrument,
//            Direction = breakout.Direction,
//            SignalTimeUtc = breakout.TimeUtc,
//            EntryTimeUtc = entryBar.Time,
//            ExitTimeUtc = exit.ExitTimeUtc,
//            EntryPrice = entryPrice,
//            StopLoss = stopLoss.Value,
//            TakeProfit = takeProfit,
//            ExitPrice = exit.ExitPrice,
//            Quantity = qty,
//            RiskAmount = riskAmount,
//            ProfitLoss = profitLoss,
//            RMultiple = rMultiple,
//            ExitReason = exit.ExitReason,
//            Notes = $"TradingDay={tradingDay}; SessionStart={window.SessionStartUtc:O}"
//        };
//    }

//    private BacktestBreakoutCandidate? FindBreakout(
//        IReadOnlyList<Candle> candles,
//        OrbRange range,
//        StrategySettings strategy,
//        SymbolSpecification spec)
//    {
//        var buffer = PriceHelper.PipsToPriceDistance(spec, strategy.Breakout.CloseBufferPips);
//        var limit = Math.Min(strategy.Breakout.MaxBreakoutCandlesAfterOrb, candles.Count);

//        for (var i = 0; i < limit; i++)
//        {
//            var c = candles[i];

//            var buyBreak = strategy.Breakout.RequireCandleCloseOutsideRange
//                ? c.Close > range.High + buffer
//                : c.High > range.High + buffer;

//            var sellBreak = strategy.Breakout.RequireCandleCloseOutsideRange
//                ? c.Close < range.Low - buffer
//                : c.Low < range.Low - buffer;

//            if (buyBreak)
//                return new BacktestBreakoutCandidate(c, TradeDirection.Buy);

//            if (sellBreak)
//                return new BacktestBreakoutCandidate(c, TradeDirection.Sell);
//        }

//        return null;
//    }

//    private static bool RequiresRetest(EntryMode mode) =>
//        mode is EntryMode.BreakoutRetest or EntryMode.BreakoutRetestFibonacci;

//    private static bool RequiresFib(EntryMode mode) =>
//        mode is EntryMode.BreakoutFibonacci or EntryMode.BreakoutRetestFibonacci;

//    private static bool ConfirmRetest(
//        TradeDirection direction,
//        OrbRange range,
//        IReadOnlyList<Candle> candlesAfterBreakout,
//        SymbolSpecification spec,
//        RetestSettings settings)
//    {
//        var tolerance = PriceHelper.PipsToPriceDistance(spec, settings.TolerancePips);
//        var limit = Math.Min(settings.MaxCandlesAfterBreakout, candlesAfterBreakout.Count);

//        for (var i = 0; i < limit; i++)
//        {
//            var c = candlesAfterBreakout[i];

//            if (direction == TradeDirection.Buy)
//            {
//                if (PriceHelper.CandleTouchesLevel(c, range.High, tolerance) &&
//                    (!settings.RequireCloseInBreakoutDirection || c.Close > range.High))
//                    return true;
//            }
//            else
//            {
//                if (PriceHelper.CandleTouchesLevel(c, range.Low, tolerance) &&
//                    (!settings.RequireCloseInBreakoutDirection || c.Close < range.Low))
//                    return true;
//            }
//        }

//        return false;
//    }

//    private static bool ConfirmFib(
//        TradeDirection direction,
//        OrbRange range,
//        IReadOnlyList<Candle> candlesAfterBreakout,
//        SymbolSpecification spec,
//        FibonacciSettings settings)
//    {
//        var limit = Math.Min(settings.MaxCandlesAfterBreakout, candlesAfterBreakout.Count);
//        if (limit == 0)
//            return false;

//        var subset = candlesAfterBreakout.Take(limit).ToList();

//        List<decimal> levels;
//        if (direction == TradeDirection.Buy)
//        {
//            var impulseLow = range.Low;
//            var impulseHigh = subset.Max(x => x.High);
//            var diff = impulseHigh - impulseLow;

//            levels = GetFibLevels(
//                settings,
//                impulseHigh - diff * 0.382m,
//                impulseHigh - diff * 0.500m,
//                impulseHigh - diff * 0.618m,
//                impulseHigh - diff * 0.786m);
//        }
//        else
//        {
//            var impulseHigh = range.High;
//            var impulseLow = subset.Min(x => x.Low);
//            var diff = impulseHigh - impulseLow;

//            levels = GetFibLevels(
//                settings,
//                impulseLow + diff * 0.382m,
//                impulseLow + diff * 0.500m,
//                impulseLow + diff * 0.618m,
//                impulseLow + diff * 0.786m);
//        }

//        if (levels.Count == 0)
//            return false;

//        var tolerance = PriceHelper.PipsToPriceDistance(spec, settings.ZoneTolerancePips);
//        var zoneLower = levels.Min();
//        var zoneUpper = levels.Max();

//        return subset.Any(c => PriceHelper.CandleTouchesZone(c, zoneLower, zoneUpper, tolerance));
//    }

//    private bool ConfirmIndicators(
//    TradeDirection direction,
//    DateTime breakoutTimeUtc,
//    decimal breakoutClosePrice,
//    DateTime sessionStartUtc,
//    StrategySettings strategy,
//    SymbolSpecification spec,
//    IReadOnlyDictionary<string, List<Candle>> indicatorCandlesByTimeframe)
//    {
//        if (strategy.Indicators.Ema.Enabled)
//        {
//            var tf = strategy.Indicators.Ema.Timeframe.Trim().ToUpperInvariant();

//            if (!indicatorCandlesByTimeframe.TryGetValue(tf, out var candles))
//                return false;

//            var closes = GetClosedIndicatorCloses(
//                candles,
//                breakoutTimeUtc,
//                tf,
//                Math.Max(strategy.Indicators.Ema.SlowPeriod + 20, strategy.Indicators.Ema.SlowPeriod * 5));

//            if (closes.Count < strategy.Indicators.Ema.SlowPeriod)
//                return false;

//            var fast = _indicatorService.CalculateEma(closes, strategy.Indicators.Ema.FastPeriod);
//            var slow = _indicatorService.CalculateEma(closes, strategy.Indicators.Ema.SlowPeriod);

//            if (!fast.HasValue || !slow.HasValue)
//                return false;

//            if (direction == TradeDirection.Buy && fast.Value <= slow.Value)
//                return false;

//            if (direction == TradeDirection.Sell && fast.Value >= slow.Value)
//                return false;
//        }

//        if (strategy.Indicators.Macd.Enabled)
//        {
//            var tf = strategy.Indicators.Macd.Timeframe.Trim().ToUpperInvariant();

//            if (!indicatorCandlesByTimeframe.TryGetValue(tf, out var candles))
//                return false;

//            var closes = GetClosedIndicatorCloses(
//                candles,
//                breakoutTimeUtc,
//                tf,
//                Math.Max(strategy.Indicators.Macd.SlowPeriod + strategy.Indicators.Macd.SignalPeriod + 50, 200));

//            if (closes.Count < strategy.Indicators.Macd.SlowPeriod + strategy.Indicators.Macd.SignalPeriod + 2)
//                return false;

//            var macd = _indicatorService.CalculateMacd(
//                closes,
//                strategy.Indicators.Macd.FastPeriod,
//                strategy.Indicators.Macd.SlowPeriod,
//                strategy.Indicators.Macd.SignalPeriod);

//            if (macd is null)
//                return false;

//            if (direction == TradeDirection.Buy)
//            {
//                var ok = strategy.Indicators.Macd.RequireCrossover
//                    ? macd.PreviousMacd <= macd.PreviousSignal && macd.CurrentMacd > macd.CurrentSignal
//                    : macd.CurrentMacd > macd.CurrentSignal;

//                if (!ok)
//                    return false;
//            }
//            else
//            {
//                var ok = strategy.Indicators.Macd.RequireCrossover
//                    ? macd.PreviousMacd >= macd.PreviousSignal && macd.CurrentMacd < macd.CurrentSignal
//                    : macd.CurrentMacd < macd.CurrentSignal;

//                if (!ok)
//                    return false;
//            }
//        }

//        if (strategy.Indicators.Rsi.Enabled)
//        {
//            var tf = strategy.Indicators.Rsi.Timeframe.Trim().ToUpperInvariant();

//            if (!indicatorCandlesByTimeframe.TryGetValue(tf, out var candles))
//                return false;

//            var closes = GetClosedIndicatorCloses(
//                candles,
//                breakoutTimeUtc,
//                tf,
//                Math.Max(strategy.Indicators.Rsi.Period + 20, strategy.Indicators.Rsi.Period * 4));

//            if (closes.Count <= strategy.Indicators.Rsi.Period)
//                return false;

//            var rsi = _indicatorService.CalculateRsi(closes, strategy.Indicators.Rsi.Period);

//            if (!rsi.HasValue)
//                return false;

//            if (direction == TradeDirection.Buy && rsi.Value < strategy.Indicators.Rsi.BuyMin)
//                return false;

//            if (direction == TradeDirection.Sell && rsi.Value > strategy.Indicators.Rsi.SellMax)
//                return false;
//        }

//        if (strategy.Volume.Enabled)
//        {
//            var tf = strategy.Volume.Timeframe.Trim().ToUpperInvariant();

//            if (!indicatorCandlesByTimeframe.TryGetValue(tf, out var candles))
//                return false;

//            if (!ConfirmVolumeBreakout(
//                    candles,
//                    breakoutTimeUtc,
//                    tf,
//                    strategy.Volume))
//            {
//                return false;
//            }
//        }

//        if (strategy.Vwap.Enabled)
//        {
//            var tf = strategy.Vwap.Timeframe.Trim().ToUpperInvariant();

//            if (!indicatorCandlesByTimeframe.TryGetValue(tf, out var candles))
//                return false;

//            if (!ConfirmVwap(
//                    candles,
//                    breakoutTimeUtc,
//                    breakoutClosePrice,
//                    sessionStartUtc,
//                    direction,
//                    tf,
//                    strategy.Vwap,
//                    spec))
//            {
//                return false;
//            }
//        }

//        return true;
//    }
//    private static List<decimal> GetFibLevels(
//        FibonacciSettings settings,
//        decimal l382,
//        decimal l500,
//        decimal l618,
//        decimal l786)
//    {
//        var levels = new List<decimal>();
//        if (settings.UseLevel0382) levels.Add(l382);
//        if (settings.UseLevel0500) levels.Add(l500);
//        if (settings.UseLevel0618) levels.Add(l618);
//        if (settings.UseLevel0786) levels.Add(l786);
//        return levels;
//    }

//    private static decimal? BuildStopLossForBacktest(
//        TradeDirection direction,
//        OrbRange range,
//        SymbolSpecification spec,
//        RiskSettings risk)
//    {
//        var buffer = PriceHelper.PipsToPriceDistance(spec, risk.StopLossBufferPips);

//        return direction == TradeDirection.Buy
//            ? range.Low - buffer
//            : range.High + buffer;
//    }

//    private static decimal SizeVolumeForBacktest(
//    SymbolSpecification spec,
//    string instrument,
//    decimal stopDistancePrice,
//    decimal riskAmount)
//    {
//        if (stopDistancePrice <= 0 || riskAmount <= 0)
//            return 0m;

//        var contractSize = ResolveContractSize(spec, instrument);
//        var minVolume = spec.MinVolume > 0 ? spec.MinVolume : 0.01m;
//        var maxVolume = spec.MaxVolume > 0 ? spec.MaxVolume : decimal.MaxValue;
//        var volumeStep = spec.VolumeStep > 0 ? spec.VolumeStep : minVolume;

//        var lossPerLot = stopDistancePrice * contractSize;
//        if (lossPerLot <= 0)
//            return 0m;

//        var rawVolume = riskAmount / lossPerLot;
//        if (rawVolume <= 0)
//            return 0m;

//        var bounded = Math.Min(rawVolume, maxVolume);

//        var steps = Math.Floor(bounded / volumeStep);
//        var normalized = steps * volumeStep;

//        normalized = RoundToStepPrecision(normalized, volumeStep);

//        if (normalized < minVolume)
//            return 0m;

//        return normalized;
//    }

//    private static decimal RoundToStepPrecision(decimal value, decimal step)
//    {
//        var stepText = step.ToString(System.Globalization.CultureInfo.InvariantCulture);
//        var decimals = 0;

//        var dotIndex = stepText.IndexOf('.');
//        if (dotIndex >= 0)
//            decimals = stepText.Length - dotIndex - 1;

//        return Math.Round(value, decimals, MidpointRounding.ToZero);
//    }

//    private static decimal ApplyEntryCosts(
//        decimal basePrice,
//        TradeDirection direction,
//        SymbolSpecification spec,
//        decimal totalPips)
//    {
//        var distance = PriceHelper.PipsToPriceDistance(spec, totalPips);

//        return direction == TradeDirection.Buy
//            ? basePrice + distance
//            : basePrice - distance;
//    }

//    private static BacktestExit SimulateExit(
//        TradeDirection direction,
//        IReadOnlyList<Candle> futureBars,
//        decimal stopLoss,
//        decimal takeProfit,
//        SymbolSpecification spec,
//        decimal exitSpreadPips)
//    {
//        var exitAdj = PriceHelper.PipsToPriceDistance(spec, exitSpreadPips);

//        foreach (var bar in futureBars)
//        {
//            if (direction == TradeDirection.Buy)
//            {
//                var hitSl = bar.Low <= stopLoss;
//                var hitTp = bar.High >= takeProfit;

//                if (hitSl && hitTp)
//                    return new BacktestExit(stopLoss - exitAdj, bar.Time, "SameBar_SL_First");

//                if (hitSl)
//                    return new BacktestExit(stopLoss - exitAdj, bar.Time, "StopLoss");

//                if (hitTp)
//                    return new BacktestExit(takeProfit - exitAdj, bar.Time, "TakeProfit");
//            }
//            else
//            {
//                var hitSl = bar.High >= stopLoss;
//                var hitTp = bar.Low <= takeProfit;

//                if (hitSl && hitTp)
//                    return new BacktestExit(stopLoss + exitAdj, bar.Time, "SameBar_SL_First");

//                if (hitSl)
//                    return new BacktestExit(stopLoss + exitAdj, bar.Time, "StopLoss");

//                if (hitTp)
//                    return new BacktestExit(takeProfit + exitAdj, bar.Time, "TakeProfit");
//            }
//        }

//        var last = futureBars[^1];
//        return new BacktestExit(last.Close, last.Time, "SessionEnd");
//    }

//    private static int GetExpectedBarCount(string timeframe, DateTime startUtc, DateTime endUtc)
//    {
//        var span = TradingTimeHelper.GetTimeframeSpan(timeframe);
//        var total = endUtc - startUtc;
//        if (span <= TimeSpan.Zero || total <= TimeSpan.Zero)
//            return 0;

//        return (int)(total.Ticks / span.Ticks);
//    }

//    private static void BuildSummary(BacktestSummary summary)
//    {
//        summary.TotalTrades = summary.Trades.Count;
//        summary.Wins = summary.Trades.Count(t => t.ProfitLoss > 0);
//        summary.Losses = summary.Trades.Count(t => t.ProfitLoss < 0);
//        summary.Breakevens = summary.Trades.Count(t => t.ProfitLoss == 0);

//        summary.WinRatePercent = summary.TotalTrades == 0
//            ? 0
//            : (decimal)summary.Wins / summary.TotalTrades * 100m;

//        summary.GrossProfit = summary.Trades.Where(t => t.ProfitLoss > 0).Sum(t => t.ProfitLoss);
//        summary.GrossLoss = summary.Trades.Where(t => t.ProfitLoss < 0).Sum(t => Math.Abs(t.ProfitLoss));
//        summary.NetProfit = summary.EndingBalance - summary.StartingBalance;

//        summary.ProfitFactor = summary.GrossLoss == 0
//            ? 0
//            : summary.GrossProfit / summary.GrossLoss;

//        summary.AverageWin = summary.Wins == 0
//            ? 0
//            : summary.Trades.Where(t => t.ProfitLoss > 0).Average(t => t.ProfitLoss);

//        summary.AverageLoss = summary.Losses == 0
//            ? 0
//            : summary.Trades.Where(t => t.ProfitLoss < 0).Average(t => Math.Abs(t.ProfitLoss));

//        summary.ExpectancyPerTrade = summary.TotalTrades == 0
//            ? 0
//            : summary.NetProfit / summary.TotalTrades;
//    }

//    private async Task<Dictionary<string, List<Candle>>> LoadIndicatorCandlesAsync(
//    string instrument,
//    DateTime startUtc,
//    DateTime endUtc,
//    CancellationToken cancellationToken)
//    {
//        var bot = _botSettings.CurrentValue;
//        var requiredBarsByTimeframe = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

//        if (bot.Strategy.Indicators.Ema.Enabled)
//        {
//            var tf = bot.Strategy.Indicators.Ema.Timeframe.Trim().ToUpperInvariant();
//            var bars = Math.Max(bot.Strategy.Indicators.Ema.SlowPeriod + 20, bot.Strategy.Indicators.Ema.SlowPeriod * 5);
//            MergeRequiredBars(requiredBarsByTimeframe, tf, bars);
//        }

//        if (bot.Strategy.Indicators.Macd.Enabled)
//        {
//            var tf = bot.Strategy.Indicators.Macd.Timeframe.Trim().ToUpperInvariant();
//            var bars = Math.Max(bot.Strategy.Indicators.Macd.SlowPeriod + bot.Strategy.Indicators.Macd.SignalPeriod + 50, 200);
//            MergeRequiredBars(requiredBarsByTimeframe, tf, bars);
//        }

//        if (bot.Strategy.Indicators.Rsi.Enabled)
//        {
//            var tf = bot.Strategy.Indicators.Rsi.Timeframe.Trim().ToUpperInvariant();
//            var bars = Math.Max(bot.Strategy.Indicators.Rsi.Period + 20, bot.Strategy.Indicators.Rsi.Period * 4);
//            MergeRequiredBars(requiredBarsByTimeframe, tf, bars);
//        }

//        if (bot.Strategy.Volume.Enabled)
//        {
//            var tf = bot.Strategy.Volume.Timeframe.Trim().ToUpperInvariant();
//            var bars = Math.Max(bot.Strategy.Volume.LookbackCandles + 10, 50);
//            MergeRequiredBars(requiredBarsByTimeframe, tf, bars);
//        }

//        if (bot.Strategy.Vwap.Enabled)
//        {
//            var tf = bot.Strategy.Vwap.Timeframe.Trim().ToUpperInvariant();
//            // session-anchored VWAP needs enough intraday candles, not just a tiny lookback
//            var bars = 500;
//            MergeRequiredBars(requiredBarsByTimeframe, tf, bars);
//        }

//        var result = new Dictionary<string, List<Candle>>(StringComparer.OrdinalIgnoreCase);

//        foreach (var kvp in requiredBarsByTimeframe)
//        {
//            var tf = kvp.Key;
//            var bars = kvp.Value;
//            var tfSpan = TradingTimeHelper.GetTimeframeSpan(tf);

//            var preloadStartUtc = startUtc.AddTicks(-(tfSpan.Ticks * (bars + 50L)));

//            var candles = await _historicalDataProvider.GetCandlesRangeAsync(
//                instrument,
//                tf,
//                preloadStartUtc,
//                endUtc,
//                cancellationToken);

//            result[tf] = candles
//                .OrderBy(c => c.Time)
//                .ToList();
//        }

//        return result;
//    }

//    private static void MergeRequiredBars(
//        Dictionary<string, int> map,
//        string timeframe,
//        int bars)
//    {
//        if (map.TryGetValue(timeframe, out var existing))
//        {
//            map[timeframe] = Math.Max(existing, bars);
//        }
//        else
//        {
//            map[timeframe] = bars;
//        }
//    }

//    private static List<Candle> GetClosedCandlesUpTo(
//    IReadOnlyList<Candle> candles,
//    DateTime evaluationTimeUtc,
//    string timeframe,
//    int requiredBars)
//    {
//        var lastClosedTimeUtc = IndicatorCandleHelper.GetLastClosedCandleTimeUtc(
//            evaluationTimeUtc,
//            timeframe);

//        return candles
//            .Where(c => c.Time <= lastClosedTimeUtc)
//            .OrderBy(c => c.Time)
//            .TakeLast(requiredBars)
//            .ToList();
//    }

//    private static List<decimal> GetClosedIndicatorCloses(
//        IReadOnlyList<Candle> candles,
//        DateTime evaluationTimeUtc,
//        string timeframe,
//        int requiredBars)
//    {
//        return GetClosedCandlesUpTo(candles, evaluationTimeUtc, timeframe, requiredBars)
//            .Select(c => c.Close)
//            .ToList();
//    }

//    private static bool ConfirmVolumeBreakout(
//        IReadOnlyList<Candle> candles,
//        DateTime breakoutTimeUtc,
//        string timeframe,
//        VolumeFilterSettings settings)
//    {
//        var requiredBars = settings.LookbackCandles + 1;

//        var usable = GetClosedCandlesUpTo(
//            candles,
//            breakoutTimeUtc,
//            timeframe,
//            requiredBars);

//        if (usable.Count < settings.LookbackCandles + 1)
//            return false;

//        var breakoutCandle = usable[^1];
//        var previousCandles = usable.Take(usable.Count - 1).ToList();

//        if (previousCandles.Count < settings.LookbackCandles)
//            return false;

//        var breakoutVolume = GetBacktestVolume(breakoutCandle, settings.VolumeSource);
//        var previousMax = previousCandles.Max(c => GetBacktestVolume(c, settings.VolumeSource));
//        var threshold = previousMax * settings.Multiplier;

//        return breakoutVolume > threshold;
//    }

//    private static bool ConfirmVwap(
//        IReadOnlyList<Candle> candles,
//        DateTime breakoutTimeUtc,
//        decimal breakoutClosePrice,
//        DateTime sessionStartUtc,
//        TradeDirection direction,
//        string timeframe,
//        VwapFilterSettings settings,
//        SymbolSpecification spec)
//    {
//        var lastClosedTimeUtc = IndicatorCandleHelper.GetLastClosedCandleTimeUtc(
//            breakoutTimeUtc,
//            timeframe);

//        var startUtc = settings.AnchorToSessionStart
//            ? sessionStartUtc
//            : candles.FirstOrDefault()?.Time ?? sessionStartUtc;

//        var usable = candles
//            .Where(c => c.Time >= startUtc && c.Time <= lastClosedTimeUtc)
//            .OrderBy(c => c.Time)
//            .ToList();

//        if (usable.Count == 0)
//            return false;

//        decimal cumulativePv = 0m;
//        decimal cumulativeVol = 0m;

//        foreach (var candle in usable)
//        {
//            var typicalPrice = (candle.High + candle.Low + candle.Close) / 3m;
//            var vol = settings.UseTickVolume ? candle.TickVolume : candle.Volume;

//            if (vol <= 0)
//                continue;

//            cumulativePv += typicalPrice * vol;
//            cumulativeVol += vol;
//        }

//        if (cumulativeVol <= 0)
//            return false;

//        var vwap = cumulativePv / cumulativeVol;
//        var tolerance = PriceHelper.PipsToPriceDistance(spec, settings.TolerancePips);

//        return direction switch
//        {
//            TradeDirection.Buy => !settings.RequirePriceOnCorrectSide || breakoutClosePrice >= vwap - tolerance,
//            TradeDirection.Sell => !settings.RequirePriceOnCorrectSide || breakoutClosePrice <= vwap + tolerance,
//            _ => false
//        };
//    }

//    private static decimal GetBacktestVolume(Candle candle, string volumeSource)
//    {
//        return string.Equals(volumeSource, "Volume", StringComparison.OrdinalIgnoreCase)
//            ? candle.Volume
//            : candle.TickVolume;
//    }

//    private sealed record BacktestBreakoutCandidate(Candle Candle, TradeDirection Direction)
//    {
//        public DateTime TimeUtc => Candle.Time;
//    }

//    private sealed record BacktestExit(decimal ExitPrice, DateTime ExitTimeUtc, string ExitReason);

//    private static decimal ResolveContractSize(SymbolSpecification spec, string instrument)
//    {
//        if (spec.ContractSize > 0)
//            return spec.ContractSize;

//        // Fallbacks for bad or missing broker spec
//        if (instrument.Equals("XAUUSD", StringComparison.OrdinalIgnoreCase))
//            return 100m;

//        if (instrument.EndsWith("USD", StringComparison.OrdinalIgnoreCase))
//            return 100000m;

//        return 1m;
//    }

//    private void LogBacktestTradeMath(
//        string instrument,
//        TradeDirection direction,
//        decimal entryPrice,
//        decimal exitPrice,
//        decimal stopLoss,
//        decimal qty,
//        decimal contractSize,
//        decimal profitLoss,
//        decimal totalRisk,
//        decimal rMultiple)
//    {
//        _logger.LogDebug(
//            "Backtest math | Instrument={Instrument} Direction={Direction} Entry={Entry} Exit={Exit} StopLoss={StopLoss} Qty={Qty} ContractSize={ContractSize} ProfitLoss={ProfitLoss} TotalRisk={TotalRisk} R={R}",
//            instrument,
//            direction,
//            entryPrice,
//            exitPrice,
//            stopLoss,
//            qty,
//            contractSize,
//            profitLoss,
//            totalRisk,
//            rMultiple);
//    }


//    class CandleCache
//    {
//        //public List<Candle> RangeCandles { get; set; } = new();
//        public Dictionary<string, List<Candle>> IndicatorCandles { get; set; } = new();
//        public IReadOnlyList<Candle> BreakoutCandles { get; set; }
//        public List<Candle> EntryCandles { get; set; } = new();
//        public DateTime FetchedAt { get; set; }
//    }
//}






////using AlgoBot.Configuration;
////using AlgoBot.Helpers;
////using AlgoBot.Interfaces;
////using AlgoBot.Models;
////using Microsoft.Extensions.Logging;
////using Microsoft.Extensions.Options;

////namespace AlgoBot.Services;

////public sealed class BacktestEngine : IBacktestEngine
////{
////    private readonly IHistoricalDataProvider _historicalDataProvider;
////    private readonly IMarketDataProvider _marketDataProvider;
////    private readonly IIndicatorService _indicatorService;
////    private readonly IOptionsMonitor<BotSettings> _botSettings;
////    private readonly ILogger<BacktestEngine> _logger;

////    public BacktestEngine(
////        IHistoricalDataProvider historicalDataProvider,
////        IMarketDataProvider marketDataProvider,
////        IIndicatorService indicatorService,
////        IOptionsMonitor<BotSettings> botSettings,
////        ILogger<BacktestEngine> logger)
////    {
////        _historicalDataProvider = historicalDataProvider;
////        _marketDataProvider = marketDataProvider;
////        _indicatorService = indicatorService;
////        _botSettings = botSettings;
////        _logger = logger;
////    }

////    public async Task<BacktestSummary> RunAsync(
////        BacktestRequest request,
////        CancellationToken cancellationToken = default)
////    {
////        var settings = _botSettings.CurrentValue;
////        var summary = new BacktestSummary
////        {
////            StartUtc = request.StartUtc,
////            EndUtc = request.EndUtc,
////            StartingBalance = request.StartingBalance,
////            EndingBalance = request.StartingBalance
////        };

////        var sessions = settings.TradingSessions
////            .Where(s => s.Enabled)
////            .Where(s => request.SessionsToRun.Count == 0 || request.SessionsToRun.Contains(s.Name, StringComparer.OrdinalIgnoreCase))
////            .ToList();

////        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(settings.TimeZoneId);
////        var currentBalance = request.StartingBalance;
////        var equityPeak = currentBalance;

////        foreach (var session in sessions)
////        {
////            var instruments = request.InstrumentsOverride.Count > 0
////                ? request.InstrumentsOverride
////                : session.Instruments;

////            foreach (var instrument in instruments.Distinct(StringComparer.OrdinalIgnoreCase))
////            {
////                var trades = await BacktestInstrumentSessionAsync(
////                    session,
////                    instrument,
////                    request.StartUtc,
////                    request.EndUtc,
////                    currentBalance,
////                    timeZone,
////                    cancellationToken);

////                foreach (var trade in trades)
////                {
////                    summary.Trades.Add(trade);
////                    currentBalance += trade.ProfitLoss;
////                    summary.EndingBalance = currentBalance;

////                    if (currentBalance > equityPeak)
////                        equityPeak = currentBalance;

////                    var drawdownAmount = equityPeak - currentBalance;
////                    var drawdownPercent = equityPeak == 0 ? 0 : (drawdownAmount / equityPeak) * 100m;

////                    if (drawdownAmount > summary.MaxDrawdownAmount)
////                        summary.MaxDrawdownAmount = drawdownAmount;

////                    if (drawdownPercent > summary.MaxDrawdownPercent)
////                        summary.MaxDrawdownPercent = drawdownPercent;
////                }
////            }
////        }

////        BuildSummaryMetrics(summary);
////        return summary;
////    }

////    private async Task<List<BacktestTradeResult>> BacktestInstrumentSessionAsync(
////        TradingSessionSettings session,
////        string instrument,
////        DateTime startUtc,
////        DateTime endUtc,
////        decimal startingBalance,
////        TimeZoneInfo timeZone,
////        CancellationToken cancellationToken)
////    {
////        var settings = _botSettings.CurrentValue;
////        var rangeTf = settings.Strategy.RangeTimeframe;
////        var breakoutTf = settings.Strategy.BreakoutTimeframe;

////        var candles = await _historicalDataProvider.GetCandlesRangeAsync(
////            instrument,
////            breakoutTf,
////            startUtc.AddDays(-2),
////            endUtc.AddDays(1),
////            cancellationToken);

////        if (candles.Count == 0)
////            return new List<BacktestTradeResult>();

////        var spec = await _marketDataProvider.GetSymbolSpecificationAsync(instrument, cancellationToken);
////        if (spec is null)
////            return new List<BacktestTradeResult>();

////        var results = new List<BacktestTradeResult>();
////        var groupedByDay = candles
////            .GroupBy(c =>
////            {
////                var local = TimeZoneInfo.ConvertTimeFromUtc(c.Time, timeZone);
////                return DateOnly.FromDateTime(local);
////            })
////            .OrderBy(g => g.Key);

////        var equity = startingBalance;

////        foreach (var dayGroup in groupedByDay)
////        {
////            var tradingDay = dayGroup.Key;
////            var sessionWindow = TradingTimeHelper.GetSessionWindow(session, tradingDay, timeZone);

////            if (sessionWindow.SessionEndUtc < startUtc || sessionWindow.SessionStartUtc > endUtc)
////                continue;

////            var sessionCandles = candles
////                .Where(c => c.Time >= sessionWindow.SessionStartUtc && c.Time <= sessionWindow.SessionEndUtc)
////                .OrderBy(c => c.Time)
////                .ToList();

////            if (sessionCandles.Count == 0)
////                continue;

////            var orbCandles = sessionCandles
////                .Where(c => c.Time >= sessionWindow.SessionStartUtc && c.Time < sessionWindow.OrbEndUtc)
////                .OrderBy(c => c.Time)
////                .ToList();

////            var expectedRangeBars = (int)((sessionWindow.OrbEndUtc - sessionWindow.SessionStartUtc).Ticks /
////                                          TradingTimeHelper.GetTimeframeSpan(rangeTf).Ticks);

////            if (orbCandles.Count < expectedRangeBars || expectedRangeBars <= 0)
////                continue;

////            var range = new OrbRange
////            {
////                StartTimeUtc = sessionWindow.SessionStartUtc,
////                EndTimeUtc = sessionWindow.OrbEndUtc,
////                High = orbCandles.Max(x => x.High),
////                Low = orbCandles.Min(x => x.Low)
////            };

////            var postOrb = sessionCandles
////                .Where(c => c.Time >= sessionWindow.OrbEndUtc)
////                .OrderBy(c => c.Time)
////                .ToList();

////            var simulated = SimulateTradeForSession(
////                session.Name,
////                instrument,
////                tradingDay,
////                postOrb,
////                range,
////                spec,
////                equity,
////                sessionWindow);

////            if (simulated is not null)
////            {
////                equity += simulated.ProfitLoss;
////                results.Add(simulated);
////            }
////        }

////        return results;
////    }

////    private BacktestTradeResult? SimulateTradeForSession(
////        string sessionName,
////        string instrument,
////        DateOnly tradingDay,
////        IReadOnlyList<Candle> postOrbCandles,
////        OrbRange range,
////        SymbolSpecification spec,
////        decimal balance,
////        SessionWindow window)
////    {
////        var bot = _botSettings.CurrentValue;
////        var strategy = bot.Strategy;
////        var risk = bot.Risk;
////        var backtest = bot.Backtest;

////        if (postOrbCandles.Count == 0)
////            return null;

////        var breakout = FindBreakout(postOrbCandles, range, strategy, spec);
////        if (breakout is null)
////            return null;

////        var candlesAfterBreakout = postOrbCandles
////            .Where(c => c.Time > breakout.Time)
////            .OrderBy(c => c.Time)
////            .ToList();

////        if (RequiresRetest(strategy.EntryMode))
////        {
////            var retestOk = ConfirmRetest(
////                BacktestCandleExtensions.Direction(breakout),
////                range,
////                candlesAfterBreakout,
////                spec,
////                strategy.Retest);

////            if (!retestOk)
////                return null;
////        }

////        if (RequiresFib(strategy.EntryMode))
////        {
////            var fibOk = ConfirmFib(
////                BacktestCandleExtensions.Direction(breakout),
////                range,
////                candlesAfterBreakout,
////                spec,
////                strategy.Fibonacci);

////            if (!fibOk)
////                return null;
////        }

////        if (!ConfirmIndicators(
////                instrument,
////                BacktestCandleExtensions.Direction(breakout),
////                breakout.Time,
////                postOrbCandles,
////                strategy))
////        {
////            return null;
////        }

////        var entryBar = backtest.UseNextBarOpenForEntry
////            ? candlesAfterBreakout.FirstOrDefault()
////            : breakout;

////        if (entryBar is null)
////            return null;

////        var entryPrice = ApplyEntryCosts(
////            entryBar.Open > 0 && backtest.UseNextBarOpenForEntry ? entryBar.Open : breakout.Close,
////            BacktestCandleExtensions.Direction(breakout),
////            spec,
////            backtest.EntrySpreadPips + backtest.SlippagePips);

////        var stopLoss = BuildBacktestStopLoss(
////            BacktestCandleExtensions.Direction(breakout),
////            range,
////            spec,
////            risk);

////        if (!stopLoss.HasValue)
////            return null;

////        var stopDistance = Math.Abs(entryPrice - stopLoss.Value);
////        if (stopDistance <= 0)
////            return null;

////        var riskAmount = balance * (risk.RiskPerTradePercent / 100m);
////        var tickSize = spec.TickSize > 0 ? spec.TickSize : spec.Point;
////        if (tickSize <= 0)
////            return null;

////        // Backtest simplification: estimate tick value using 1 account currency per point if pip/tick
////        // value is not available from historical quote context. This keeps structure reusable but is an approximation.
////        var assumedTickValue = 1m;
////        var lossPerLot = (stopDistance / tickSize) * assumedTickValue;
////        if (lossPerLot <= 0)
////            return null;

////        var qty = NormalizeVolumeDown(riskAmount / lossPerLot, spec);
////        if (qty < spec.MinVolume)
////            return null;

////        var takeProfit = BacktestCandleExtensions.Direction(breakout) == TradeDirection.Buy
////            ? entryPrice + (stopDistance * risk.RewardRiskRatio)
////            : entryPrice - (stopDistance * risk.RewardRiskRatio);

////        var futureBars = postOrbCandles
////            .Where(c => c.Time >= entryBar.Time)
////            .OrderBy(c => c.Time)
////            .ToList();

////        if (futureBars.Count == 0)
////            return null;

////        var exit = SimulateExit(
////            BacktestCandleExtensions.Direction(breakout),
////            futureBars,
////            stopLoss.Value,
////            takeProfit,
////            spec,
////            backtest.ExitSpreadPips);

////        var pnlPerLot = CalculatePnLPerLot(
////            BacktestCandleExtensions.Direction(breakout),
////            entryPrice,
////            exit.ExitPrice);

////        var profitLoss = pnlPerLot * qty;
////        var riskPerLot = CalculatePnLPerLot(
////            BacktestCandleExtensions.Direction(breakout),
////            entryPrice,
////            stopLoss.Value);

////        var rMultiple = riskPerLot == 0 ? 0 : profitLoss / Math.Abs(riskPerLot * qty);

////        return new BacktestTradeResult
////        {
////            SessionName = sessionName,
////            Instrument = instrument,
////            Direction = BacktestCandleExtensions.Direction(breakout),
////            SignalTimeUtc = breakout.Time,
////            EntryTimeUtc = entryBar.Time,
////            ExitTimeUtc = exit.ExitTimeUtc,
////            EntryPrice = entryPrice,
////            StopLoss = stopLoss.Value,
////            TakeProfit = takeProfit,
////            ExitPrice = exit.ExitPrice,
////            Quantity = qty,
////            RiskAmount = riskAmount,
////            ProfitLoss = profitLoss,
////            RMultiple = rMultiple,
////            ExitReason = exit.ExitReason,
////            Notes = $"TradingDay={tradingDay}"
////        };
////    }

////    private static Candle? FindBreakout(
////        IReadOnlyList<Candle> candles,
////        OrbRange range,
////        StrategySettings strategy,
////        SymbolSpecification spec)
////    {
////        var buffer = PriceHelper.PipsToPriceDistance(spec, strategy.Breakout.CloseBufferPips);
////        var limit = Math.Min(strategy.Breakout.MaxBreakoutCandlesAfterOrb, candles.Count);

////        for (var i = 0; i < limit; i++)
////        {
////            var c = candles[i];

////            var buyBreak = strategy.Breakout.RequireCandleCloseOutsideRange
////                ? c.Close > range.High + buffer
////                : c.High > range.High + buffer;

////            var sellBreak = strategy.Breakout.RequireCandleCloseOutsideRange
////                ? c.Close < range.Low - buffer
////                : c.Low < range.Low - buffer;

////            if (buyBreak)
////            {
////                c = CloneWithDirectionTag(c, TradeDirection.Buy);
////                return c;
////            }

////            if (sellBreak)
////            {
////                c = CloneWithDirectionTag(c, TradeDirection.Sell);
////                return c;
////            }
////        }

////        return null;
////    }

////    private static Candle CloneWithDirectionTag(Candle candle, TradeDirection direction)
////    {
////        return new DirectionTaggedCandle(direction)
////        {
////            Time = candle.Time,
////            Open = candle.Open,
////            High = candle.High,
////            Low = candle.Low,
////            Close = candle.Close,
////            Volume = candle.Volume,
////            TickVolume = candle.TickVolume,
////            Spread = candle.Spread
////        };
////    }

////    private static bool RequiresRetest(EntryMode mode) =>
////        mode is EntryMode.BreakoutRetest or EntryMode.BreakoutRetestFibonacci;

////    private static bool RequiresFib(EntryMode mode) =>
////        mode is EntryMode.BreakoutFibonacci or EntryMode.BreakoutRetestFibonacci;

////    private static bool ConfirmRetest(
////        TradeDirection direction,
////        OrbRange range,
////        IReadOnlyList<Candle> candlesAfterBreakout,
////        SymbolSpecification spec,
////        RetestSettings settings)
////    {
////        var tolerance = PriceHelper.PipsToPriceDistance(spec, settings.TolerancePips);
////        var limit = Math.Min(settings.MaxCandlesAfterBreakout, candlesAfterBreakout.Count);

////        for (var i = 0; i < limit; i++)
////        {
////            var c = candlesAfterBreakout[i];

////            if (direction == TradeDirection.Buy)
////            {
////                if (PriceHelper.CandleTouchesLevel(c, range.High, tolerance) &&
////                    (!settings.RequireCloseInBreakoutDirection || c.Close > range.High))
////                    return true;
////            }
////            else
////            {
////                if (PriceHelper.CandleTouchesLevel(c, range.Low, tolerance) &&
////                    (!settings.RequireCloseInBreakoutDirection || c.Close < range.Low))
////                    return true;
////            }
////        }

////        return false;
////    }

////    private static bool ConfirmFib(
////        TradeDirection direction,
////        OrbRange range,
////        IReadOnlyList<Candle> candlesAfterBreakout,
////        SymbolSpecification spec,
////        FibonacciSettings settings)
////    {
////        var limit = Math.Min(settings.MaxCandlesAfterBreakout, candlesAfterBreakout.Count);
////        if (limit == 0)
////            return false;

////        var chosen = candlesAfterBreakout.Take(limit).ToList();
////        decimal zoneLower;
////        decimal zoneUpper;

////        if (direction == TradeDirection.Buy)
////        {
////            var impulseLow = range.Low;
////            var impulseHigh = chosen.Max(x => x.High);
////            var diff = impulseHigh - impulseLow;

////            var levels = GetFibLevels(settings,
////                impulseHigh - diff * 0.382m,
////                impulseHigh - diff * 0.500m,
////                impulseHigh - diff * 0.618m,
////                impulseHigh - diff * 0.786m);

////            zoneLower = levels.Min();
////            zoneUpper = levels.Max();
////        }
////        else
////        {
////            var impulseHigh = range.High;
////            var impulseLow = chosen.Min(x => x.Low);
////            var diff = impulseHigh - impulseLow;

////            var levels = GetFibLevels(settings,
////                impulseLow + diff * 0.382m,
////                impulseLow + diff * 0.500m,
////                impulseLow + diff * 0.618m,
////                impulseLow + diff * 0.786m);

////            zoneLower = levels.Min();
////            zoneUpper = levels.Max();
////        }

////        var tolerance = PriceHelper.PipsToPriceDistance(spec, settings.ZoneTolerancePips);

////        return chosen.Any(c => PriceHelper.CandleTouchesZone(c, zoneLower, zoneUpper, tolerance));
////    }

////    private bool ConfirmIndicators(
////        string instrument,
////        TradeDirection direction,
////        DateTime breakoutTimeUtc,
////        IReadOnlyList<Candle> availableCandles,
////        StrategySettings strategy)
////    {
////        var closes = availableCandles
////            .Where(c => c.Time <= breakoutTimeUtc)
////            .OrderBy(c => c.Time)
////            .Select(c => c.Close)
////            .ToList();

////        if (strategy.Indicators.Ema.Enabled)
////        {
////            var fast = _indicatorService.CalculateEma(closes, strategy.Indicators.Ema.FastPeriod);
////            var slow = _indicatorService.CalculateEma(closes, strategy.Indicators.Ema.SlowPeriod);

////            if (!fast.HasValue || !slow.HasValue)
////                return false;

////            if (direction == TradeDirection.Buy && fast <= slow)
////                return false;

////            if (direction == TradeDirection.Sell && fast >= slow)
////                return false;
////        }

////        if (strategy.Indicators.Macd.Enabled)
////        {
////            var macd = _indicatorService.CalculateMacd(
////                closes,
////                strategy.Indicators.Macd.FastPeriod,
////                strategy.Indicators.Macd.SlowPeriod,
////                strategy.Indicators.Macd.SignalPeriod);

////            if (macd is null)
////                return false;

////            if (direction == TradeDirection.Buy)
////            {
////                var ok = strategy.Indicators.Macd.RequireCrossover
////                    ? macd.PreviousMacd <= macd.PreviousSignal && macd.CurrentMacd > macd.CurrentSignal
////                    : macd.CurrentMacd > macd.CurrentSignal;

////                if (!ok)
////                    return false;
////            }
////            else
////            {
////                var ok = strategy.Indicators.Macd.RequireCrossover
////                    ? macd.PreviousMacd >= macd.PreviousSignal && macd.CurrentMacd < macd.CurrentSignal
////                    : macd.CurrentMacd < macd.CurrentSignal;

////                if (!ok)
////                    return false;
////            }
////        }

////        if (strategy.Indicators.Rsi.Enabled)
////        {
////            var rsi = _indicatorService.CalculateRsi(
////                closes,
////                strategy.Indicators.Rsi.Period);

////            if (!rsi.HasValue)
////                return false;

////            if (direction == TradeDirection.Buy && rsi < strategy.Indicators.Rsi.BuyMin)
////                return false;

////            if (direction == TradeDirection.Sell && rsi > strategy.Indicators.Rsi.SellMax)
////                return false;
////        }

////        _ = instrument;
////        return true;
////    }

////    private static List<decimal> GetFibLevels(
////        FibonacciSettings settings,
////        decimal l382,
////        decimal l500,
////        decimal l618,
////        decimal l786)
////    {
////        var levels = new List<decimal>();
////        if (settings.UseLevel0382) levels.Add(l382);
////        if (settings.UseLevel0500) levels.Add(l500);
////        if (settings.UseLevel0618) levels.Add(l618);
////        if (settings.UseLevel0786) levels.Add(l786);
////        return levels;
////    }

////    private decimal? BuildBacktestStopLoss(
////        TradeDirection direction,
////        OrbRange range,
////        SymbolSpecification spec,
////        RiskSettings risk)
////    {
////        var buffer = PriceHelper.PipsToPriceDistance(spec, risk.StopLossBufferPips);

////        return risk.StopLossMode switch
////        {
////            StopLossMode.RangeBoundary => direction == TradeDirection.Buy
////                ? range.Low - buffer
////                : range.High + buffer,

////            // For the first candle backtester, these fall back to range mode when exact live reference points are unavailable.
////            StopLossMode.RetestCandle => direction == TradeDirection.Buy
////                ? range.Low - buffer
////                : range.High + buffer,

////            StopLossMode.FibonacciLevel => direction == TradeDirection.Buy
////                ? range.Low - buffer
////                : range.High + buffer,

////            _ => null
////        };
////    }

////    private static decimal ApplyEntryCosts(
////        decimal basePrice,
////        TradeDirection direction,
////        SymbolSpecification spec,
////        decimal totalPips)
////    {
////        var distance = PriceHelper.PipsToPriceDistance(spec, totalPips);

////        return direction == TradeDirection.Buy
////            ? basePrice + distance
////            : basePrice - distance;
////    }

////    private static (decimal ExitPrice, DateTime ExitTimeUtc, string ExitReason) SimulateExit(
////        TradeDirection direction,
////        IReadOnlyList<Candle> futureBars,
////        decimal stopLoss,
////        decimal takeProfit,
////        SymbolSpecification spec,
////        decimal exitSpreadPips)
////    {
////        foreach (var bar in futureBars)
////        {
////            if (direction == TradeDirection.Buy)
////            {
////                var hitSl = bar.Low <= stopLoss;
////                var hitTp = bar.High >= takeProfit;

////                if (hitSl && hitTp)
////                {
////                    // Conservative assumption: SL first on same candle.
////                    var exit = stopLoss - PriceHelper.PipsToPriceDistance(spec, exitSpreadPips);
////                    return (exit, bar.Time, "SameBar_SL_First");
////                }

////                if (hitSl)
////                {
////                    var exit = stopLoss - PriceHelper.PipsToPriceDistance(spec, exitSpreadPips);
////                    return (exit, bar.Time, "StopLoss");
////                }

////                if (hitTp)
////                {
////                    var exit = takeProfit - PriceHelper.PipsToPriceDistance(spec, exitSpreadPips);
////                    return (exit, bar.Time, "TakeProfit");
////                }
////            }
////            else
////            {
////                var hitSl = bar.High >= stopLoss;
////                var hitTp = bar.Low <= takeProfit;

////                if (hitSl && hitTp)
////                {
////                    var exit = stopLoss + PriceHelper.PipsToPriceDistance(spec, exitSpreadPips);
////                    return (exit, bar.Time, "SameBar_SL_First");
////                }

////                if (hitSl)
////                {
////                    var exit = stopLoss + PriceHelper.PipsToPriceDistance(spec, exitSpreadPips);
////                    return (exit, bar.Time, "StopLoss");
////                }

////                if (hitTp)
////                {
////                    var exit = takeProfit + PriceHelper.PipsToPriceDistance(spec, exitSpreadPips);
////                    return (exit, bar.Time, "TakeProfit");
////                }
////            }
////        }

////        var last = futureBars[^1];
////        return (last.Close, last.Time, "SessionEnd");
////    }

////    private static decimal CalculatePnLPerLot(
////        TradeDirection direction,
////        decimal entryPrice,
////        decimal exitPrice)
////    {
////        return direction == TradeDirection.Buy
////            ? exitPrice - entryPrice
////            : entryPrice - exitPrice;
////    }

////    private static decimal NormalizeVolumeDown(decimal rawVolume, SymbolSpecification spec)
////    {
////        if (rawVolume <= 0)
////            return 0m;

////        var bounded = Math.Min(rawVolume, spec.MaxVolume);
////        if (spec.VolumeStep <= 0)
////            return bounded;

////        var steps = Math.Floor(bounded / spec.VolumeStep);
////        return steps * spec.VolumeStep;
////    }

////    private static void BuildSummaryMetrics(BacktestSummary summary)
////    {
////        summary.TotalTrades = summary.Trades.Count;
////        summary.Wins = summary.Trades.Count(t => t.ProfitLoss > 0);
////        summary.Losses = summary.Trades.Count(t => t.ProfitLoss < 0);
////        summary.Breakevens = summary.Trades.Count(t => t.ProfitLoss == 0);

////        summary.WinRatePercent = summary.TotalTrades == 0
////            ? 0
////            : (decimal)summary.Wins / summary.TotalTrades * 100m;

////        summary.GrossProfit = summary.Trades.Where(t => t.ProfitLoss > 0).Sum(t => t.ProfitLoss);
////        summary.GrossLoss = summary.Trades.Where(t => t.ProfitLoss < 0).Sum(t => Math.Abs(t.ProfitLoss));
////        summary.NetProfit = summary.EndingBalance - summary.StartingBalance;

////        summary.ProfitFactor = summary.GrossLoss == 0
////            ? 0
////            : summary.GrossProfit / summary.GrossLoss;

////        summary.AverageWin = summary.Wins == 0
////            ? 0
////            : summary.Trades.Where(t => t.ProfitLoss > 0).Average(t => t.ProfitLoss);

////        summary.AverageLoss = summary.Losses == 0
////            ? 0
////            : summary.Trades.Where(t => t.ProfitLoss < 0).Average(t => Math.Abs(t.ProfitLoss));

////        summary.ExpectancyPerTrade = summary.TotalTrades == 0
////            ? 0
////            : summary.NetProfit / summary.TotalTrades;
////    }

////    public sealed class DirectionTaggedCandle : Candle
////    {
////        public DirectionTaggedCandle(TradeDirection direction)
////        {
////            Direction = direction;
////        }

////        public TradeDirection Direction { get; }
////    }
////}

////public static class BacktestCandleExtensions
////{
////    public static TradeDirection Direction(this Candle candle)
////    {
////        return candle is BacktestEngine.DirectionTaggedCandle tagged
////            ? tagged.Direction
////            : TradeDirection.None;
////    }
////}



