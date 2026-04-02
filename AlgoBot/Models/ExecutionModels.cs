namespace AlgoBot.Models;

public enum PositionLifecycleEventType
{
    None = 0,
    Open = 1,
    ClosedTakeProfit = 2,
    ClosedStopLoss = 3,
    ClosedManualOrOther = 4
}

public sealed class ExecutionOutcome
{
    public bool Success { get; init; }
    public bool Simulated { get; init; }
    public string Reason { get; init; } = string.Empty;
    public string? ClientId { get; init; }
    public string? OrderId { get; init; }
    public string? PositionId { get; init; }

    public static ExecutionOutcome Fail(string reason) =>
        new()
        {
            Success = false,
            Reason = reason
        };

    public static ExecutionOutcome SuccessResult(
        string reason,
        string? clientId,
        string? orderId,
        string? positionId,
        bool simulated = false) =>
        new()
        {
            Success = true,
            Reason = reason,
            ClientId = clientId,
            OrderId = orderId,
            PositionId = positionId,
            Simulated = simulated
        };
}

public sealed class PositionMonitorResult
{
    public bool HasTrackedPosition { get; init; }
    public bool PositionStillOpen { get; init; }
    public PositionLifecycleEventType EventType { get; init; }
    public string Reason { get; init; } = string.Empty;
    public PositionInfo? CurrentPosition { get; init; }
    public decimal? RealizedPnL { get; init; }

    public static PositionMonitorResult None(string reason) =>
        new()
        {
            HasTrackedPosition = false,
            PositionStillOpen = false,
            EventType = PositionLifecycleEventType.None,
            Reason = reason
        };

    public static PositionMonitorResult Open(PositionInfo position, string reason) =>
        new()
        {
            HasTrackedPosition = true,
            PositionStillOpen = true,
            EventType = PositionLifecycleEventType.Open,
            CurrentPosition = position,
            Reason = reason
        };

    public static PositionMonitorResult Closed(
        PositionLifecycleEventType eventType,
        decimal? realizedPnL,
        string reason) =>
        new()
        {
            HasTrackedPosition = true,
            PositionStillOpen = false,
            EventType = eventType,
            RealizedPnL = realizedPnL,
            Reason = reason
        };
}