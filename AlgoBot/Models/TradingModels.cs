namespace AlgoBot.Models;

public class Candle
{
    public DateTime Time { get; set; }
    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }
    public decimal Volume { get; set; }
    public long TickVolume { get; set; }
    public int Spread { get; set; }
}

public sealed class MarketQuote
{
    public string Instrument { get; set; } = string.Empty;
    public DateTime TimestampUtc { get; set; }
    public decimal Bid { get; set; }
    public decimal Ask { get; set; }
    public decimal Mid => (Bid + Ask) / 2m;
    public decimal Spread => Ask - Bid;
    public decimal? ProfitTickValue { get; set; }
    public decimal? LossTickValue { get; set; }
    public string BrokerTime { get; set; } = string.Empty;
}

public sealed class TradingAccountInfo
{
    public string Broker { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public string Server { get; set; } = string.Empty;
    public decimal Balance { get; set; }
    public decimal Equity { get; set; }
    public decimal Margin { get; set; }
    public decimal FreeMargin { get; set; }
    public int Leverage { get; set; }
    public bool TradeAllowed { get; set; }
    public string Name { get; set; } = string.Empty;
    public long Login { get; set; }
    public string Type { get; set; } = string.Empty;
}

public sealed class SymbolSpecification
{
    public string Symbol { get; set; } = string.Empty;
    public decimal TickSize { get; set; }
    public decimal MinVolume { get; set; }
    public decimal MaxVolume { get; set; }
    public decimal VolumeStep { get; set; }
    public decimal ContractSize { get; set; }
    public int Digits { get; set; }
    public decimal Point { get; set; }
    public decimal? PipSize { get; set; }
    public int StopsLevel { get; set; }
    public int FreezeLevel { get; set; }
    public string ExecutionMode { get; set; } = string.Empty;
    public string TradeMode { get; set; } = string.Empty;
    public List<string> FillingModes { get; set; } = new();
    public List<string> AllowedOrderTypes { get; set; } = new();
}

public sealed class DealInfo
{
    public string DealId { get; set; } = string.Empty;
    public string OrderId { get; set; } = string.Empty;
    public string PositionId { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public string? ClientId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string EntryType { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public decimal Volume { get; set; }
    public decimal Price { get; set; }
    public decimal Profit { get; set; }
    public decimal Commission { get; set; }
    public decimal Swap { get; set; }
    public DateTime TimeUtc { get; set; }
    public string BrokerTime { get; set; } = string.Empty;
}

public enum TradeDirection
{
    None = 0,
    Buy = 1,
    Sell = 2
}

public sealed class OrbRange
{
    public DateTime StartTimeUtc { get; set; }
    public DateTime EndTimeUtc { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Size => High - Low;
}

public sealed class TradeSignal
{
    public string SessionName { get; set; } = string.Empty;
    public string Instrument { get; set; } = string.Empty;
    public TradeDirection Direction { get; set; } = TradeDirection.None;
    public bool ShouldTrade { get; set; }
    public decimal? EntryPrice { get; set; }
    public decimal? StopLoss { get; set; }
    public decimal? TakeProfit { get; set; }
    public List<string> PassedConditions { get; set; } = new();
    public List<string> FailedConditions { get; set; } = new();
    public string Reason { get; set; } = string.Empty;
}

public sealed class TradeRequest
{
    public string SessionName { get; set; } = string.Empty;
    public string Instrument { get; set; } = string.Empty;
    public TradeDirection Direction { get; set; } = TradeDirection.None;
    public decimal Quantity { get; set; }
    public decimal EntryPrice { get; set; }
    public decimal StopLoss { get; set; }
    public decimal TakeProfit { get; set; }
    public decimal AllowedSlippagePips { get; set; }
    public string StrategyTag { get; set; } = "ORB";
    public string Comment { get; set; } = string.Empty;
    public string? ClientId { get; set; }
    public long? MagicNumber { get; set; }
}

public sealed class TradeExecutionResult
{
    public bool Success { get; set; }
    public int NumericCode { get; set; }
    public string StringCode { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? OrderId { get; set; }
    public string? PositionId { get; set; }
}

public sealed class PositionInfo
{
    public string PositionId { get; set; } = string.Empty;
    public string Instrument { get; set; } = string.Empty;
    public TradeDirection Direction { get; set; } = TradeDirection.None;
    public decimal Volume { get; set; }
    public decimal OpenPrice { get; set; }
    public decimal CurrentPrice { get; set; }
    public decimal? StopLoss { get; set; }
    public decimal? TakeProfit { get; set; }
    public decimal UnrealizedPnL { get; set; }
    public decimal RealizedPnL { get; set; }
    public decimal Commission { get; set; }
    public string? ClientId { get; set; }
    public DateTime OpenedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
}

public sealed class EntryFilterContext
{
    public string SessionName { get; set; } = string.Empty;
    public string Instrument { get; set; } = string.Empty;
    public IReadOnlyList<Candle> Candles { get; set; } = Array.Empty<Candle>();
    public OrbRange? Range { get; set; }
    public TradeDirection BreakoutDirection { get; set; } = TradeDirection.None;
    public DateTime? BreakoutTimeUtc { get; set; }
    public decimal? BreakoutClosePrice { get; set; }
    public DateTime EvaluationTimeUtc { get; set; }

    public DateTime SessionStartUtc { get; set; }
}

public sealed class FilterEvaluationResult
{
    public string FilterName { get; set; } = string.Empty;
    public bool Passed { get; set; }
    public string Reason { get; set; } = string.Empty;

    public static FilterEvaluationResult Success(string filterName, string reason) =>
        new()
        {
            FilterName = filterName,
            Passed = true,
            Reason = reason
        };

    public static FilterEvaluationResult Fail(string filterName, string reason) =>
        new()
        {
            FilterName = filterName,
            Passed = false,
            Reason = reason
        };
}