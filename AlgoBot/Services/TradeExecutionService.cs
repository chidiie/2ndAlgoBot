using AlgoBot.Configuration;
using AlgoBot.Interfaces;
using AlgoBot.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AlgoBot.Services;

public sealed class TradeExecutionService : ITradeExecutionService
{
    private readonly ITradeExecutor _tradeExecutor;
    private readonly IMarketDataProvider _marketDataProvider;
    private readonly IOptionsMonitor<BotSettings> _botSettings;
    private readonly ILogger<TradeExecutionService> _logger;

    public TradeExecutionService(
        ITradeExecutor tradeExecutor,
        IMarketDataProvider marketDataProvider,
        IOptionsMonitor<BotSettings> botSettings,
        ILogger<TradeExecutionService> logger)
    {
        _tradeExecutor = tradeExecutor;
        _marketDataProvider = marketDataProvider;
        _botSettings = botSettings;
        _logger = logger;
    }

    public async Task<ExecutionOutcome> ExecuteAsync(
        TradingSessionSettings session,
        InstrumentState instrumentState,
        PreparedTradePlan tradePlan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tradePlan);

        if (instrumentState.TradeTaken)
        {
            return ExecutionOutcome.Fail("Trade already executed for this instrument today.");
        }

        if (instrumentState.LastKnownOpenPositions > 0)
        {
            return ExecutionOutcome.Fail(
                $"Open position already exists for {tradePlan.Instrument}. Skipping duplicate entry.");
        }

        var clientId = BuildShortClientId(session.Name, tradePlan.Instrument, tradePlan.Direction);

        if (_botSettings.CurrentValue.DryRun)
        {
            _logger.LogInformation(
                "Dry-run execution | Session={SessionName} Instrument={Instrument} Direction={Direction} Qty={Qty} Entry={Entry} SL={SL} TP={TP}",
                tradePlan.SessionName,
                tradePlan.Instrument,
                tradePlan.Direction,
                tradePlan.Quantity,
                tradePlan.EntryPrice,
                tradePlan.StopLoss,
                tradePlan.TakeProfit);

            return ExecutionOutcome.SuccessResult(
                reason: "Dry-run execution simulated successfully.",
                clientId: clientId,
                orderId: $"DRYRUN-{Guid.NewGuid():N}"[..18],
                positionId: $"DRYRUN-{Guid.NewGuid():N}"[..18],
                simulated: true);
        }

        var request = new TradeRequest
        {
            SessionName = tradePlan.SessionName,
            Instrument = tradePlan.Instrument,
            Direction = tradePlan.Direction,
            Quantity = tradePlan.Quantity,
            EntryPrice = tradePlan.EntryPrice,
            StopLoss = tradePlan.StopLoss,
            TakeProfit = tradePlan.TakeProfit,
            AllowedSlippagePips = _botSettings.CurrentValue.Risk.SlippageTolerancePips,
            StrategyTag = "ORB",
            Comment = "OB",
            ClientId = clientId
        };

        var apiResult = await _tradeExecutor.PlaceOrderAsync(request, cancellationToken);
        if (!apiResult.Success)
        {
            _logger.LogWarning(
                "Execution rejected | Instrument={Instrument} Code={Code} StringCode={StringCode} Message={Message}",
                tradePlan.Instrument,
                apiResult.NumericCode,
                apiResult.StringCode,
                apiResult.Message);

            return ExecutionOutcome.Fail(
                $"Execution rejected: {apiResult.StringCode} - {apiResult.Message}");
        }

        var resolvedPositionId = apiResult.PositionId;
        if (string.IsNullOrWhiteSpace(resolvedPositionId))
        {
            resolvedPositionId = await ResolvePositionIdAsync(
                tradePlan.Instrument,
                clientId,
                cancellationToken);
        }

        _logger.LogInformation(
            "Execution success | Session={SessionName} Instrument={Instrument} OrderId={OrderId} PositionId={PositionId} ClientId={ClientId}",
            tradePlan.SessionName,
            tradePlan.Instrument,
            apiResult.OrderId,
            resolvedPositionId,
            clientId);

        return ExecutionOutcome.SuccessResult(
            reason: "Trade executed successfully.",
            clientId: clientId,
            orderId: apiResult.OrderId,
            positionId: resolvedPositionId,
            simulated: false);
    }

    private async Task<string?> ResolvePositionIdAsync(
        string instrument,
        string clientId,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var positions = await _marketDataProvider.GetOpenPositionsAsync(instrument, cancellationToken);

            var tracked = positions
                .Where(p => string.Equals(p.ClientId, clientId, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(p => p.OpenedAtUtc)
                .FirstOrDefault();

            if (tracked is not null)
                return tracked.PositionId;

            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
        }

        return null;
    }

    private static string BuildShortClientId(
        string sessionName,
        string instrument,
        TradeDirection direction)
    {
        var session = new string(sessionName.Where(char.IsLetterOrDigit).Take(2).ToArray()).ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(session))
            session = "SS";

        var symbol = new string(instrument.Where(char.IsLetterOrDigit).Take(4).ToArray()).ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(symbol))
            symbol = "SYM";

        var dir = direction == TradeDirection.Buy ? "B" : "S";
        var suffix = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

        var clientId = $"{session}{symbol}{dir}{suffix}";
        return clientId.Length <= 20 ? clientId : clientId[..20];
    }
}