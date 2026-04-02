namespace AlgoBot.Models;

public sealed class SessionWindow
{
    public DateOnly TradingDay { get; init; }

    public DateTime SessionStartLocal { get; init; }
    public DateTime SessionEndLocal { get; init; }
    public DateTime OrbEndLocal { get; init; }

    public DateTime SessionStartUtc { get; init; }
    public DateTime SessionEndUtc { get; init; }
    public DateTime OrbEndUtc { get; init; }
}

public sealed class OrbBuildResult
{
    public bool Built { get; init; }
    public string Reason { get; init; } = string.Empty;
    public OrbRange? Range { get; init; }
    public int CandlesUsed { get; init; }
    public SessionWindow? Window { get; init; }

    public static OrbBuildResult Pending(string reason, SessionWindow? window = null) =>
        new()
        {
            Built = false,
            Reason = reason,
            Window = window
        };

    public static OrbBuildResult Success(
        OrbRange range,
        int candlesUsed,
        SessionWindow window,
        string reason) =>
        new()
        {
            Built = true,
            Reason = reason,
            Range = range,
            CandlesUsed = candlesUsed,
            Window = window
        };
}

public sealed class FibonacciRetracementLevels
{
    public decimal Level0382 { get; init; }
    public decimal Level0500 { get; init; }
    public decimal Level0618 { get; init; }
    public decimal Level0786 { get; init; }

    public decimal ZoneLower { get; init; }
    public decimal ZoneUpper { get; init; }
}

public sealed class FibonacciEvaluationResult
{
    public bool Confirmed { get; init; }
    public FibonacciRetracementLevels? Levels { get; init; }
    public string Reason { get; init; } = string.Empty;
    public string? TouchedLevelName { get; init; }
    public decimal? TouchedLevelPrice { get; init; }
    public DateTime? TouchTimeUtc { get; init; }

    public static FibonacciEvaluationResult Fail(string reason, FibonacciRetracementLevels? levels = null) =>
        new()
        {
            Confirmed = false,
            Reason = reason,
            Levels = levels
        };

    public static FibonacciEvaluationResult Success(
        FibonacciRetracementLevels levels,
        string reason,
        string? touchedLevelName,
        decimal? touchedLevelPrice,
        DateTime? touchTimeUtc) =>
        new()
        {
            Confirmed = true,
            Levels = levels,
            Reason = reason,
            TouchedLevelName = touchedLevelName,
            TouchedLevelPrice = touchedLevelPrice,
            TouchTimeUtc = touchTimeUtc
        };
}

public sealed class OrbSignalEvaluationResult
{
    public TradeSignal Signal { get; init; } = new();
    public bool BreakoutDetected { get; init; }
    public TradeDirection BreakoutDirection { get; init; } = TradeDirection.None;
    public DateTime? BreakoutTimeUtc { get; init; }
    public decimal? BreakoutClosePrice { get; init; }

    public bool RetestConfirmed { get; init; }
    public DateTime? RetestTimeUtc { get; init; }
    public decimal? RetestReferencePrice { get; init; }

    public bool FibonacciConfirmed { get; init; }
    public string? FibonacciTouchedLevel { get; init; }
    public decimal? FibonacciReferencePrice { get; init; }
    public DateTime? FibonacciTouchTimeUtc { get; init; }
}