using System.Collections.Concurrent;

namespace AlgoBot.Models;

public sealed class SessionState
{
    public SessionState(string sessionName, DateOnly tradingDay)
    {
        SessionName = sessionName;
        TradingDay = tradingDay;
    }

    public string SessionName { get; }
    public DateOnly TradingDay { get; private set; }
    public bool IsEnabled { get; set; }
    public bool IsActive { get; set; }
    public int TradesTaken { get; set; }
    public bool DailyLossLimitReached { get; set; }
    public DateTimeOffset? LastHeartbeatUtc { get; set; }

    public ConcurrentDictionary<string, InstrumentState> InstrumentStates { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    public void ResetForNewTradingDay(DateOnly tradingDay, IEnumerable<string> instruments)
    {
        TradingDay = tradingDay;
        TradesTaken = 0;
        DailyLossLimitReached = false;
        InstrumentStates.Clear();

        foreach (var instrument in instruments.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            InstrumentStates[instrument] = new InstrumentState(instrument, tradingDay);
        }
    }
}

public sealed class InstrumentState
{
    public InstrumentState(string instrument, DateOnly tradingDay)
    {
        Instrument = instrument;
        TradingDay = tradingDay;
    }
    public DateTimeOffset? LastTrailingStopUpdateUtc {  get; set; }
    public string Instrument { get; }
    public DateOnly TradingDay { get; private set; }

    public bool RangeBuilt { get; set; }
    public bool BreakoutDetected { get; set; }
    public TradeDirection BreakoutDirection { get; set; } = TradeDirection.None;
    public DateTime? BreakoutCandleTimeUtc { get; set; }
    public decimal? BreakoutClosePrice { get; set; }

    public bool RetestConfirmed { get; set; }
    public DateTime? RetestTimeUtc { get; set; }
    public decimal? RetestReferencePrice { get; set; }

    public bool FibonacciConfirmed { get; set; }
    public string? FibonacciTouchedLevel { get; set; }
    public DateTime? FibonacciTouchTimeUtc { get; set; }
    public decimal? FibonacciReferencePrice { get; set; }

    public bool EntrySignalReady { get; set; }
    public TradeDirection PendingSignalDirection { get; set; } = TradeDirection.None;
    public DateTimeOffset? SignalTriggeredAtUtc { get; set; }

    public bool RiskApproved { get; set; }
    public PreparedTradePlan? PendingTradePlan { get; set; }
    public bool TradePlanNotificationSent { get; set; }

    // Execution + lifecycle tracking
    public bool TradeTaken { get; set; }
    public bool DryRunExecution { get; set; }
    public string? ManagedOrderId { get; set; }
    public string? ManagedPositionId { get; set; }
    public string? ManagedClientId { get; set; }
    public DateTimeOffset? EntryExecutedAtUtc { get; set; }
    public decimal? LastKnownUnrealizedPnL { get; set; }

    public int TradesTaken { get; set; }
    public OrbRange? OrbRange { get; set; }

    public DateTimeOffset? LastProcessedUtc { get; set; }
    public DateTimeOffset? LastQuoteSyncUtc { get; set; }
    public DateTimeOffset? LastPositionsSyncUtc { get; set; }
    public DateTimeOffset? LastRangeBuildAttemptUtc { get; set; }
    public DateTimeOffset? LastSignalEvaluationUtc { get; set; }

    public decimal? LastBid { get; set; }
    public decimal? LastAsk { get; set; }
    public int LastKnownOpenPositions { get; set; }

    public SessionWindow? LastSessionWindow { get; set; }

    public string LastDecisionReason { get; set; } = string.Empty;

    public void ResetForNewTradingDay(DateOnly tradingDay)
    {
        TradingDay = tradingDay;
        RangeBuilt = false;
        BreakoutDetected = false;
        BreakoutDirection = TradeDirection.None;
        BreakoutCandleTimeUtc = null;
        BreakoutClosePrice = null;
        RetestConfirmed = false;
        RetestTimeUtc = null;
        RetestReferencePrice = null;
        FibonacciConfirmed = false;
        FibonacciTouchedLevel = null;
        FibonacciTouchTimeUtc = null;
        FibonacciReferencePrice = null;
        EntrySignalReady = false;
        PendingSignalDirection = TradeDirection.None;
        SignalTriggeredAtUtc = null;
        RiskApproved = false;
        PendingTradePlan = null;
        TradePlanNotificationSent = false;
        TradeTaken = false;
        DryRunExecution = false;
        ManagedOrderId = null;
        ManagedPositionId = null;
        ManagedClientId = null;
        EntryExecutedAtUtc = null;
        LastKnownUnrealizedPnL = null;
        TradesTaken = 0;
        OrbRange = null;
        LastProcessedUtc = null;
        LastQuoteSyncUtc = null;
        LastPositionsSyncUtc = null;
        LastRangeBuildAttemptUtc = null;
        LastSignalEvaluationUtc = null;
        LastTrailingStopUpdateUtc = null;
        LastBid = null;
        LastAsk = null;
        LastKnownOpenPositions = 0;
        LastSessionWindow = null;
        LastDecisionReason = string.Empty;
    }
}