using AlgoBot.Interfaces;
using AlgoBot.Models;

namespace AlgoBot.Services;

public sealed class IndicatorService : IIndicatorService
{
    public decimal? CalculateEma(IReadOnlyList<decimal> values, int period)
    {
        if (values is null || values.Count < period || period <= 0)
            return null;

        var multiplier = 2m / (period + 1m);

        decimal ema = values.Take(period).Average();

        for (var i = period; i < values.Count; i++)
        {
            ema = ((values[i] - ema) * multiplier) + ema;
        }

        return ema;
    }

    public decimal? CalculateRsi(IReadOnlyList<decimal> closes, int period)
    {
        if (closes is null || closes.Count <= period || period <= 0)
            return null;

        decimal gains = 0m;
        decimal losses = 0m;

        for (var i = 1; i <= period; i++)
        {
            var change = closes[i] - closes[i - 1];
            if (change > 0)
                gains += change;
            else
                losses += Math.Abs(change);
        }

        var averageGain = gains / period;
        var averageLoss = losses / period;

        for (var i = period + 1; i < closes.Count; i++)
        {
            var change = closes[i] - closes[i - 1];
            var gain = change > 0 ? change : 0m;
            var loss = change < 0 ? Math.Abs(change) : 0m;

            averageGain = ((averageGain * (period - 1)) + gain) / period;
            averageLoss = ((averageLoss * (period - 1)) + loss) / period;
        }

        if (averageLoss == 0m)
            return 100m;

        if (averageGain == 0m)
            return 0m;

        var rs = averageGain / averageLoss;
        var rsi = 100m - (100m / (1m + rs));
        return rsi;
    }

    public MacdSnapshot? CalculateMacd(
    IReadOnlyList<decimal> closes,
    int fastPeriod,
    int slowPeriod,
    int signalPeriod)
    {
        if (closes is null ||
            fastPeriod <= 0 ||
            slowPeriod <= 0 ||
            signalPeriod <= 0 ||
            fastPeriod >= slowPeriod ||
            closes.Count < slowPeriod + signalPeriod + 10)
        {
            return null;
        }

        var fastEmaSeries = CalculateEmaSeries(closes, fastPeriod);
        var slowEmaSeries = CalculateEmaSeries(closes, slowPeriod);

        var macdValues = new List<decimal>();

        for (var i = 0; i < closes.Count; i++)
        {
            if (fastEmaSeries[i].HasValue && slowEmaSeries[i].HasValue)
            {
                macdValues.Add(fastEmaSeries[i]!.Value - slowEmaSeries[i]!.Value);
            }
        }

        if (macdValues.Count < signalPeriod + 2)
            return null;

        var signalSeries = CalculateEmaSeries(macdValues, signalPeriod);

        var aligned = new List<(decimal Macd, decimal Signal)>();
        for (var i = 0; i < macdValues.Count; i++)
        {
            if (signalSeries[i].HasValue)
            {
                aligned.Add((macdValues[i], signalSeries[i]!.Value));
            }
        }

        if (aligned.Count < 2)
            return null;

        var current = aligned[^1];
        var previous = aligned[^2];

        return new MacdSnapshot
        {
            CurrentMacd = current.Macd,
            CurrentSignal = current.Signal,
            CurrentHistogram = current.Macd - current.Signal,
            PreviousMacd = previous.Macd,
            PreviousSignal = previous.Signal,
            PreviousHistogram = previous.Macd - previous.Signal
        };
    }

    private static List<decimal?> CalculateEmaSeries(IReadOnlyList<decimal> values, int period)
    {
        var result = new List<decimal?>(new decimal?[values.Count]);

        if (values.Count < period || period <= 0)
            return result;

        var multiplier = 2m / (period + 1m);
        decimal ema = values.Take(period).Average();

        result[period - 1] = ema;

        for (var i = period; i < values.Count; i++)
        {
            ema = ((values[i] - ema) * multiplier) + ema;
            result[i] = ema;
        }

        return result;
    }
}