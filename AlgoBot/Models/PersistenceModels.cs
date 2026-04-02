namespace AlgoBot.Models;

public sealed class PersistedBotState
{
    public DateTimeOffset SavedAtUtc { get; set; }
    public List<PersistedSessionState> Sessions { get; set; } = new();
}

public sealed class PersistedSessionState
{
    public string SessionName { get; set; } = string.Empty;
    public string TradingDay { get; set; } = string.Empty;
    public int TradesTaken { get; set; }
    public bool DailyLossLimitReached { get; set; }
    public List<PersistedInstrumentState> Instruments { get; set; } = new();
}

public sealed class PersistedInstrumentState
{
    public string Instrument { get; set; } = string.Empty;

    public bool RangeBuilt { get; set; }
    public bool BreakoutDetected { get; set; }
    public TradeDirection BreakoutDirection { get; set; }
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
    public TradeDirection PendingSignalDirection { get; set; }
    public DateTimeOffset? SignalTriggeredAtUtc { get; set; }

    public bool RiskApproved { get; set; }
    public PreparedTradePlan? PendingTradePlan { get; set; }
    public bool TradePlanNotificationSent { get; set; }

    public bool TradeTaken { get; set; }
    public bool DryRunExecution { get; set; }
    public string? ManagedOrderId { get; set; }
    public string? ManagedPositionId { get; set; }
    public string? ManagedClientId { get; set; }
    public DateTimeOffset? EntryExecutedAtUtc { get; set; }
    public decimal? LastKnownUnrealizedPnL { get; set; }

    public int TradesTaken { get; set; }
    public OrbRange? OrbRange { get; set; }

    public DateTimeOffset? LastQuoteSyncUtc { get; set; }
    public DateTimeOffset? LastPositionsSyncUtc { get; set; }
    public DateTimeOffset? LastRangeBuildAttemptUtc { get; set; }
    public DateTimeOffset? LastSignalEvaluationUtc { get; set; }

    public decimal? LastBid { get; set; }
    public decimal? LastAsk { get; set; }
    public int LastKnownOpenPositions { get; set; }

    public string LastDecisionReason { get; set; } = string.Empty;
}

public sealed class ExecutionJournalEntry
{
    public DateTimeOffset TimeUtc { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string SessionName { get; set; } = string.Empty;
    public string Instrument { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;

    public string? Direction { get; set; }
    public string? OrderId { get; set; }
    public string? PositionId { get; set; }
    public string? ClientId { get; set; }

    public decimal? Quantity { get; set; }
    public decimal? EntryPrice { get; set; }
    public decimal? StopLoss { get; set; }
    public decimal? TakeProfit { get; set; }
    public decimal? RealizedPnL { get; set; }

    public bool? Simulated { get; set; }
}