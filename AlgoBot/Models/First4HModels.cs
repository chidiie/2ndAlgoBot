namespace AlgoBot.Models;

public sealed class First4HDayRange
{
    public DateOnly TradingDay { get; set; }
    public DateTime RangeStartUtc { get; set; }
    public DateTime RangeEndUtc { get; set; }

    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Size => High - Low;
}

public enum First4HBreakoutDirection
{
    None = 0,
    AboveRange = 1,
    BelowRange = 2
}

public sealed class First4HSetupState
{
    public string Instrument { get; set; } = string.Empty;
    public DateOnly TradingDay { get; set; }

    public bool RangeBuilt { get; set; }
    public First4HDayRange? Range { get; set; }

    public bool BreakoutActive { get; set; }
    public First4HBreakoutDirection BreakoutDirection { get; set; } = First4HBreakoutDirection.None;
    public DateTime? BreakoutTimeUtc { get; set; }

    // Highest high after upside breakout, or lowest low after downside breakout
    public decimal? BreakoutExtremePrice { get; set; }

    public int TradesTakenToday { get; set; }

    public void ResetForNewDay(DateOnly tradingDay)
    {
        TradingDay = tradingDay;
        RangeBuilt = false;
        Range = null;
        BreakoutActive = false;
        BreakoutDirection = First4HBreakoutDirection.None;
        BreakoutTimeUtc = null;
        BreakoutExtremePrice = null;
        TradesTakenToday = 0;
    }
}

public sealed class First4HSignalEvaluationResult
{
    public bool SignalReady { get; set; }
    public string Reason { get; set; } = string.Empty;
    public TradeSignal Signal { get; set; } = new();
}