
using AlgoBot.Models;

namespace AlgoBot.Helpers;

public static class PriceHelper
{
    public static decimal PipsToPriceDistance(SymbolSpecification spec, decimal pips)
    {
        if (pips <= 0)
            return 0m;

        var pipSize = spec.PipSize.GetValueOrDefault();
        if (pipSize <= 0)
        {
            pipSize = spec.Digits switch
            {
                3 => 0.01m,
                5 => 0.0001m,
                _ => spec.Point > 0 ? spec.Point : 0.0001m
            };
        }

        return pips * pipSize;
    }

    public static decimal PriceDistanceToPips(SymbolSpecification spec, decimal priceDistance)
    {
        if (priceDistance <= 0)
            return 0m;

        var pipSize = spec.PipSize.GetValueOrDefault();
        if (pipSize <= 0)
        {
            pipSize = spec.Digits switch
            {
                3 => 0.01m,
                5 => 0.0001m,
                _ => spec.Point > 0 ? spec.Point : 0.0001m
            };
        }

        if (pipSize <= 0)
            return 0m;

        return priceDistance / pipSize;
    }

    public static bool CandleTouchesLevel(Candle candle, decimal level, decimal tolerance)
    {
        return candle.Low <= level + tolerance && candle.High >= level - tolerance;
    }

    public static bool CandleTouchesZone(Candle candle, decimal lower, decimal upper, decimal tolerance)
    {
        var min = Math.Min(lower, upper);
        var max = Math.Max(lower, upper);

        return candle.Low <= max + tolerance && candle.High >= min - tolerance;
    }
}