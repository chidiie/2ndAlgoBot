using AlgoBot.Interfaces;
using AlgoBot.Models;
using Microsoft.Extensions.Logging;

namespace AlgoBot.Services;

public sealed class PositionMonitorService : IPositionMonitorService
{
    private readonly IMarketDataProvider _marketDataProvider;
    private readonly ILogger<PositionMonitorService> _logger;

    public PositionMonitorService(
        IMarketDataProvider marketDataProvider,
        ILogger<PositionMonitorService> logger)
    {
        _marketDataProvider = marketDataProvider;
        _logger = logger;
    }

    public async Task<PositionMonitorResult> SyncAsync(
        InstrumentState instrumentState,
        CancellationToken cancellationToken = default)
    {
        if (!instrumentState.TradeTaken)
            return PositionMonitorResult.None("No executed trade to monitor.");

        if (instrumentState.DryRunExecution)
            return PositionMonitorResult.None("Dry-run execution has no live position to monitor.");

        if (string.IsNullOrWhiteSpace(instrumentState.ManagedPositionId) &&
            string.IsNullOrWhiteSpace(instrumentState.ManagedClientId))
        {
            return PositionMonitorResult.None("No tracked position identifiers available.");
        }

        var openPositions = await _marketDataProvider.GetOpenPositionsAsync(
            instrumentState.Instrument,
            cancellationToken);

        var tracked = openPositions.FirstOrDefault(p =>
            (!string.IsNullOrWhiteSpace(instrumentState.ManagedPositionId) &&
             string.Equals(p.PositionId, instrumentState.ManagedPositionId, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrWhiteSpace(instrumentState.ManagedClientId) &&
             string.Equals(p.ClientId, instrumentState.ManagedClientId, StringComparison.OrdinalIgnoreCase)));

        if (tracked is not null)
        {
            _logger.LogDebug(
                "Tracked position still open | Instrument={Instrument} PositionId={PositionId} PnL={PnL}",
                instrumentState.Instrument,
                tracked.PositionId,
                tracked.UnrealizedPnL);

            return PositionMonitorResult.Open(
                tracked,
                "Tracked position is still open.");
        }

        if (!string.IsNullOrWhiteSpace(instrumentState.ManagedPositionId))
        {
            var deals = await _marketDataProvider.GetDealsByPositionAsync(
                instrumentState.ManagedPositionId,
                cancellationToken);

            if (deals.Count > 0)
            {
                var realizedPnL = deals.Sum(d => d.Profit + d.Commission + d.Swap);

                var closingDeal = deals
                    .Where(d => !string.Equals(d.EntryType, "DEAL_ENTRY_IN", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(d => d.TimeUtc)
                    .FirstOrDefault()
                    ?? deals.OrderByDescending(d => d.TimeUtc).First();

                var eventType = closingDeal.Reason switch
                {
                    "DEAL_REASON_TP" => PositionLifecycleEventType.ClosedTakeProfit,
                    "DEAL_REASON_SL" => PositionLifecycleEventType.ClosedStopLoss,
                    _ => PositionLifecycleEventType.ClosedManualOrOther
                };

                return PositionMonitorResult.Closed(
                    eventType,
                    realizedPnL,
                    $"Position closed. Deal reason={closingDeal.Reason}.");
            }
        }

        return PositionMonitorResult.Closed(
            PositionLifecycleEventType.ClosedManualOrOther,
            realizedPnL: null,
            reason: "Tracked position no longer appears in open positions and no closing deal could be resolved.");
    }
}