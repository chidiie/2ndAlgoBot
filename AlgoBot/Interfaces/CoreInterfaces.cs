using AlgoBot.Configuration;
using AlgoBot.Models;

namespace AlgoBot.Interfaces;

public interface IMarketDataProvider
{
    Task<TradingAccountInfo?> GetAccountInformationAsync(
        CancellationToken cancellationToken = default);

    Task<SymbolSpecification?> GetSymbolSpecificationAsync(
        string instrument,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Candle>> GetCandlesAsync(
        string instrument,
        string timeframe,
        int limit,
        DateTime? startTimeUtc = null,
        CancellationToken cancellationToken = default);

    Task<MarketQuote?> GetQuoteAsync(
        string instrument,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PositionInfo>> GetOpenPositionsAsync(
        string? instrument = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DealInfo>> GetDealsByPositionAsync(
        string positionId,
        CancellationToken cancellationToken = default);
}

public interface ITradeExecutor
{
    Task<TradeExecutionResult> PlaceOrderAsync(
        TradeRequest request,
        CancellationToken cancellationToken = default);

    Task<TradeExecutionResult> ClosePositionAsync(
        string positionId,
        string? comment = null,
        CancellationToken cancellationToken = default);

    Task<TradeExecutionResult> ModifyPositionAsync(
    string positionId,
    decimal? stopLoss,
    decimal? takeProfit = null,
    CancellationToken cancellationToken = default);
}

public interface INotificationService
{
    Task SendInfoAsync(string message, CancellationToken cancellationToken = default);
    Task SendErrorAsync(string message, CancellationToken cancellationToken = default);
}

public interface IStrategy
{
    Task<TradeSignal> EvaluateAsync(
        TradingSessionSettings session,
        InstrumentState instrumentState,
        CancellationToken cancellationToken = default);
}

public interface IIndicatorService
{
    decimal? CalculateEma(IReadOnlyList<decimal> values, int period);

    decimal? CalculateRsi(IReadOnlyList<decimal> closes, int period);

    MacdSnapshot? CalculateMacd(
        IReadOnlyList<decimal> closes,
        int fastPeriod,
        int slowPeriod,
        int signalPeriod);
}

public interface IEntryFilter
{
    string Name { get; }

    Task<FilterEvaluationResult> EvaluateAsync(
        EntryFilterContext context,
        CancellationToken cancellationToken = default);
}

public interface ISessionStateStore
{
    SessionState EnsureSessionState(TradingSessionSettings session, DateOnly tradingDay);
    IReadOnlyCollection<SessionState> GetAll();
}

public interface IFirst4HReentryStrategyService
{
    Task<First4HSignalEvaluationResult> EvaluateAsync(
        First4HProfileSettings profile,
        string instrument,
        CancellationToken cancellationToken = default);
}

public interface IFirst4HBacktestEngine
{
    Task<BacktestSummary> RunAsync(
        BacktestRequest request,
        CancellationToken cancellationToken = default);
}

public interface IOrbRangeBuilder
{
    Task<OrbBuildResult> TryBuildAsync(
        TradingSessionSettings session,
        InstrumentState instrumentState,
        CancellationToken cancellationToken = default);
}

public interface IFibonacciRetracementService
{
    FibonacciEvaluationResult Evaluate(
        TradeDirection direction,
        OrbRange range,
        IReadOnlyList<Candle> candlesAfterBreakout,
        SymbolSpecification specification,
        FibonacciSettings settings);
}

public interface IOrbSignalEvaluator
{
    Task<OrbSignalEvaluationResult> EvaluateAsync(
        TradingSessionSettings session,
        InstrumentState instrumentState,
        CancellationToken cancellationToken = default);
}

public interface IRiskManager
{
    Task<RiskEvaluationResult> EvaluateAsync(
        TradingSessionSettings session,
        SessionState sessionState,
        InstrumentState instrumentState,
        TradeSignal signal,
        CancellationToken cancellationToken = default);
}

public interface ITradeExecutionService
{
    Task<ExecutionOutcome> ExecuteAsync(
        TradingSessionSettings session,
        InstrumentState instrumentState,
        PreparedTradePlan tradePlan,
        CancellationToken cancellationToken = default);
}

public interface IPositionMonitorService
{
    Task<PositionMonitorResult> SyncAsync(
        InstrumentState instrumentState,
        CancellationToken cancellationToken = default);
}

public interface IBotStatePersistenceService
{
    Task RestoreAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(CancellationToken cancellationToken = default);
    Task AppendJournalAsync(ExecutionJournalEntry entry, CancellationToken cancellationToken = default);
}

public interface IHistoricalDataProvider
{
    Task<IReadOnlyList<Candle>> GetCandlesRangeAsync(
        string instrument,
        string timeframe,
        DateTime startUtc,
        DateTime endUtc,
        CancellationToken cancellationToken = default);
}

public interface IBacktestEngine
{
    Task<BacktestSummary> RunAsync(
        BacktestRequest request,
        CancellationToken cancellationToken = default);
}