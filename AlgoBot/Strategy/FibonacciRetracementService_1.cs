//using AlgoBot.Configuration;
//using AlgoBot.Helpers;
//using AlgoBot.Interfaces;
//using AlgoBot.Models;

//namespace AlgoBot.Strategy;

//public sealed class FibonacciRetracementService_1 : IFibonacciRetracementService
//{
//    public FibonacciEvaluationResult Evaluate(
//        TradeDirection direction,
//        OrbRange range,
//        IReadOnlyList<Candle> candlesAfterBreakout,
//        SymbolSpecification specification,
//        FibonacciSettings settings)
//    {
//        if (direction == TradeDirection.None)
//            return FibonacciEvaluationResult.Fail("Fibonacci cannot be evaluated without breakout direction.");

//        if (candlesAfterBreakout is null || candlesAfterBreakout.Count == 0)
//            return FibonacciEvaluationResult.Fail("No candles available after breakout for Fibonacci evaluation.");

//        var limitedCandles = candlesAfterBreakout
//            .Take(Math.Max(1, settings.MaxCandlesAfterBreakout))
//            .OrderBy(c => c.Time)
//            .ToList();

//        if (limitedCandles.Count == 0)
//            return FibonacciEvaluationResult.Fail("No post-breakout candles available within Fibonacci lookahead.");

//        FibonacciRetracementLevels levels;
//        int impulseIndex;

//        if (direction == TradeDirection.Buy)
//        {
//            var impulseLow = range.Low;
//            var impulseHigh = limitedCandles.Max(c => c.High);

//            if (impulseHigh <= impulseLow)
//                return FibonacciEvaluationResult.Fail("Bullish Fibonacci impulse is invalid.");

//            impulseIndex = limitedCandles.FindIndex(c => c.High == impulseHigh);
//            levels = BuildBullishLevels(impulseLow, impulseHigh, settings);
//        }
//        else
//        {
//            var impulseHigh = range.High;
//            var impulseLow = limitedCandles.Min(c => c.Low);

//            if (impulseHigh <= impulseLow)
//                return FibonacciEvaluationResult.Fail("Bearish Fibonacci impulse is invalid.");

//            impulseIndex = limitedCandles.FindIndex(c => c.Low == impulseLow);
//            levels = BuildBearishLevels(impulseHigh, impulseLow, settings);
//        }

//        if (impulseIndex < 0)
//            return FibonacciEvaluationResult.Fail("Could not resolve impulse candle index.", levels);

//        // Only evaluate retracement AFTER the impulse extreme is established.
//        var retracementCandles = limitedCandles
//            .Skip(impulseIndex + 1)
//            .ToList();

//        if (retracementCandles.Count == 0)
//        {
//            return FibonacciEvaluationResult.Fail(
//                "Impulse identified, but no later candles are available yet for Fibonacci retracement confirmation.",
//                levels);
//        }

//        var requestedTolerance = PriceHelper.PipsToPriceDistance(specification, settings.ZoneTolerancePips);
//        var tolerance = PriceHelper.NormalizeTolerance(specification, requestedTolerance);

//        foreach (var candle in retracementCandles)
//        {
//            if (settings.RequireTouchInZone &&
//                PriceHelper.CandleTouchesZone(candle, levels.ZoneLower, levels.ZoneUpper, tolerance))
//            {
//                var (name, price) = ResolveTouchedOrNearestLevel(candle, levels, settings, tolerance);

//                return FibonacciEvaluationResult.Success(
//                    levels,
//                    $"Fibonacci zone touched on candle {candle.Time:O}.",
//                    name,
//                    price,
//                    candle.Time);
//            }

//            if (!settings.RequireTouchInZone)
//            {
//                var (name, price) = ResolveTouchedOrNearestLevel(candle, levels, settings, tolerance, onlyExactTouch: true);
//                if (!string.IsNullOrWhiteSpace(name))
//                {
//                    return FibonacciEvaluationResult.Success(
//                        levels,
//                        $"Fibonacci level {name} touched on candle {candle.Time:O}.",
//                        name,
//                        price,
//                        candle.Time);
//                }
//            }
//        }

//        return FibonacciEvaluationResult.Fail(
//            $"No Fibonacci pullback confirmation found yet. ImpulseIndex={impulseIndex}, RetracementCandles={retracementCandles.Count}, Tolerance={tolerance}.",
//            levels);
//    }

//    private static FibonacciRetracementLevels BuildBullishLevels(
//        decimal impulseLow,
//        decimal impulseHigh,
//        FibonacciSettings settings)
//    {
//        var diff = impulseHigh - impulseLow;

//        var l382 = impulseHigh - diff * 0.382m;
//        var l500 = impulseHigh - diff * 0.500m;
//        var l618 = impulseHigh - diff * 0.618m;
//        var l786 = impulseHigh - diff * 0.786m;

//        var selected = GetSelectedLevels(settings, l382, l500, l618, l786);

//        return new FibonacciRetracementLevels
//        {
//            Level0382 = l382,
//            Level0500 = l500,
//            Level0618 = l618,
//            Level0786 = l786,
//            ZoneLower = selected.Min(),
//            ZoneUpper = selected.Max()
//        };
//    }

//    private static FibonacciRetracementLevels BuildBearishLevels(
//        decimal impulseHigh,
//        decimal impulseLow,
//        FibonacciSettings settings)
//    {
//        var diff = impulseHigh - impulseLow;

//        var l382 = impulseLow + diff * 0.382m;
//        var l500 = impulseLow + diff * 0.500m;
//        var l618 = impulseLow + diff * 0.618m;
//        var l786 = impulseLow + diff * 0.786m;

//        var selected = GetSelectedLevels(settings, l382, l500, l618, l786);

//        return new FibonacciRetracementLevels
//        {
//            Level0382 = l382,
//            Level0500 = l500,
//            Level0618 = l618,
//            Level0786 = l786,
//            ZoneLower = selected.Min(),
//            ZoneUpper = selected.Max()
//        };
//    }

//    private static List<decimal> GetSelectedLevels(
//        FibonacciSettings settings,
//        decimal l382,
//        decimal l500,
//        decimal l618,
//        decimal l786)
//    {
//        var selected = new List<decimal>();

//        if (settings.UseLevel0382) selected.Add(l382);
//        if (settings.UseLevel0500) selected.Add(l500);
//        if (settings.UseLevel0618) selected.Add(l618);
//        if (settings.UseLevel0786) selected.Add(l786);

//        if (selected.Count == 0)
//            throw new InvalidOperationException("At least one Fibonacci level must be enabled.");

//        return selected;
//    }

//    private static (string? Name, decimal? Price) ResolveTouchedOrNearestLevel(
//        Candle candle,
//        FibonacciRetracementLevels levels,
//        FibonacciSettings settings,
//        decimal tolerance,
//        bool onlyExactTouch = false)
//    {
//        if (settings.UseLevel0382 && PriceHelper.CandleTouchesLevel(candle, levels.Level0382, tolerance))
//            return ("0.382", levels.Level0382);

//        if (settings.UseLevel0500 && PriceHelper.CandleTouchesLevel(candle, levels.Level0500, tolerance))
//            return ("0.500", levels.Level0500);

//        if (settings.UseLevel0618 && PriceHelper.CandleTouchesLevel(candle, levels.Level0618, tolerance))
//            return ("0.618", levels.Level0618);

//        if (settings.UseLevel0786 && PriceHelper.CandleTouchesLevel(candle, levels.Level0786, tolerance))
//            return ("0.786", levels.Level0786);

//        if (onlyExactTouch)
//            return (null, null);

//        var candidates = new List<(string Name, decimal Price)>();
//        if (settings.UseLevel0382) candidates.Add(("0.382", levels.Level0382));
//        if (settings.UseLevel0500) candidates.Add(("0.500", levels.Level0500));
//        if (settings.UseLevel0618) candidates.Add(("0.618", levels.Level0618));
//        if (settings.UseLevel0786) candidates.Add(("0.786", levels.Level0786));

//        var nearest = candidates
//            .OrderBy(x => Math.Abs(candle.Close - x.Price))
//            .FirstOrDefault();

//        return (nearest.Name, nearest.Price);
//    }
//}