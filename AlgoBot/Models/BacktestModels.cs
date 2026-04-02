namespace AlgoBot.Models;

public sealed class BacktestRequest
{
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }

    public decimal StartingBalance { get; set; } = 1000m;

    // Optional override. If empty, use configured TradingSessions.
    public List<string> SessionsToRun { get; set; } = new();

    // Optional override. If empty, use configured instruments per session.
    public List<string> InstrumentsOverride { get; set; } = new();
}

public sealed class BacktestTradeResult
{
    public string SessionName { get; set; } = string.Empty;
    public string Instrument { get; set; } = string.Empty;
    public TradeDirection Direction { get; set; } = TradeDirection.None;

    public DateTime SignalTimeUtc { get; set; }
    public DateTime EntryTimeUtc { get; set; }
    public DateTime ExitTimeUtc { get; set; }

    public decimal EntryPrice { get; set; }
    public decimal StopLoss { get; set; }
    public decimal TakeProfit { get; set; }
    public decimal ExitPrice { get; set; }

    public decimal Quantity { get; set; }
    public decimal RiskAmount { get; set; }
    public decimal ProfitLoss { get; set; }
    public decimal RMultiple { get; set; }

    public string ExitReason { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
}

public sealed class BacktestSummary
{
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }

    public decimal StartingBalance { get; set; }
    public decimal EndingBalance { get; set; }

    public int TotalTrades { get; set; }
    public int Wins { get; set; }
    public int Losses { get; set; }
    public int Breakevens { get; set; }

    public decimal WinRatePercent { get; set; }
    public decimal NetProfit { get; set; }
    public decimal GrossProfit { get; set; }
    public decimal GrossLoss { get; set; }
    public decimal ProfitFactor { get; set; }
    public decimal AverageWin { get; set; }
    public decimal AverageLoss { get; set; }
    public decimal ExpectancyPerTrade { get; set; }
    public decimal MaxDrawdownAmount { get; set; }
    public decimal MaxDrawdownPercent { get; set; }

    public List<BacktestTradeResult> Trades { get; set; } = new();
}

public sealed class BacktestSessionContext
{
    public string SessionName { get; set; } = string.Empty;
    public string Instrument { get; set; } = string.Empty;
    public DateOnly TradingDay { get; set; }
    public SessionWindow Window { get; set; } = new();
}

public sealed class SimulatedTradePlan
{
    public string SessionName { get; set; } = string.Empty;
    public string Instrument { get; set; } = string.Empty;
    public TradeDirection Direction { get; set; } = TradeDirection.None;

    public DateTime SignalTimeUtc { get; set; }
    public DateTime PlannedEntryTimeUtc { get; set; }

    public decimal EntryPrice { get; set; }
    public decimal StopLoss { get; set; }
    public decimal TakeProfit { get; set; }
    public decimal Quantity { get; set; }
    public decimal RiskAmount { get; set; }
}