using AlgoBot.Configuration;
using AlgoBot.Helpers;
using AlgoBot.Interfaces;
using AlgoBot.Models;
using AlgoBot.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AlgoBot.Workers;

public sealed class TradingBotWorker : BackgroundService
{
    private readonly IOptionsMonitor<BotSettings> _botSettings;
    private readonly ISessionStateStore _sessionStateStore;
    private readonly INotificationService _notificationService;
    private readonly IMarketDataProvider _marketDataProvider;
    private readonly IOrbRangeBuilder _orbRangeBuilder;
    private readonly IOrbSignalEvaluator _orbSignalEvaluator;
    private readonly IRiskManager _riskManager;
    private readonly ITradeExecutionService _tradeExecutionService;
    private readonly IPositionMonitorService _positionMonitorService;
    private readonly IBotStatePersistenceService _statePersistenceService;
    private readonly IBacktestEngine _backtestEngine;
    private readonly ITradeExecutor _tradeExecutor;
    private readonly IFirst4HReentryStrategyService _first4HReentryStrategyService;
    private readonly ILogger<TradingBotWorker> _logger;

    private DateTimeOffset? _lastSnapshotSaveUtc;

    public TradingBotWorker(
        IOptionsMonitor<BotSettings> botSettings,
        ISessionStateStore sessionStateStore,
        INotificationService notificationService,
        IMarketDataProvider marketDataProvider,
        IOrbRangeBuilder orbRangeBuilder,
        IOrbSignalEvaluator orbSignalEvaluator,
        IRiskManager riskManager,
        ITradeExecutionService tradeExecutionService,
        IPositionMonitorService positionMonitorService,
        IBotStatePersistenceService statePersistenceService,
        IBacktestEngine backtestEngine,
        ITradeExecutor tradeExecutor,
        IFirst4HReentryStrategyService first4HReentryStrategyService,
        ILogger<TradingBotWorker> logger)
    {
        _botSettings = botSettings;
        _sessionStateStore = sessionStateStore;
        _notificationService = notificationService;
        _marketDataProvider = marketDataProvider;
        _orbRangeBuilder = orbRangeBuilder;
        _orbSignalEvaluator = orbSignalEvaluator;
        _riskManager = riskManager;
        _tradeExecutionService = tradeExecutionService;
        _positionMonitorService = positionMonitorService;
        _statePersistenceService = statePersistenceService;
        _backtestEngine = backtestEngine;
        _tradeExecutor = tradeExecutor;
        _first4HReentryStrategyService = first4HReentryStrategyService;
        _logger = logger;
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
        {
        var settings = _botSettings.CurrentValue;

        //var summary = await _backtestEngine.RunAsync(new BacktestRequest
        //{
        //    StartUtc = new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc),
        //    EndUtc = new DateTime(2026, 3, 26, 0, 0, 0, DateTimeKind.Utc),
        //    StartingBalance = 200m,
        //    SessionsToRun = new List<string> { "London" },
        //    //InstrumentsOverride = new List<string> { "USTEC", "US500", "US30", "HK50" }
        //    InstrumentsOverride = new List<string> {"XAUUSD", "DE30", "UK100" }
        //}, cancellationToken);

        //var summary = await _backtestEngine.RunAsync(new BacktestRequest
        //{
        //    StartUtc = new DateTime(2025, 10, 1, 0, 0, 0, DateTimeKind.Utc),
        //    EndUtc = new DateTime(2026, 3, 26, 0, 0, 0, DateTimeKind.Utc),
        //    StartingBalance = 200m,
        //    SessionsToRun = new List<string> { },
        //    InstrumentsOverride = new List<string> { }
        //}, cancellationToken);

        //var summary1 = await _backtestEngine.RunAsync(new BacktestRequest
        //{
        //    StartUtc = new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc),
        //    EndUtc = new DateTime(2026, 3, 26, 0, 0, 0, DateTimeKind.Utc),
        //    StartingBalance = 200m,
        //    SessionsToRun = new List<string> { "Asia" },
        //    InstrumentsOverride = new List<string> { "HK50", "JP225" }
        //    //InstrumentsOverride = new List<string> { "USTEC", "US500", "US30", "XAUUSD" }
        //}, cancellationToken);


        _logger.LogInformation(
            "Starting bot {InstanceName}. TimeZone={TimeZoneId}, DryRun={DryRun}, PollingInterval={PollingIntervalSeconds}s",
            settings.InstanceName,
            settings.TimeZoneId,
            settings.DryRun,
            settings.PollingIntervalSeconds);

        await _statePersistenceService.RestoreAsync(cancellationToken);

        if (settings.MetaApi.Enabled)
        {
            try
            {
                var account = await _marketDataProvider.GetAccountInformationAsync(cancellationToken);
                if (account is not null)
                {
                    _logger.LogInformation(
                        "MetaApi connected. Broker={Broker}, Server={Server}, Login={Login}, Balance={Balance}, Equity={Equity}, TradeAllowed={TradeAllowed}",
                        account.Broker,
                        account.Server,
                        account.Login,
                        account.Balance,
                        account.Equity,
                        account.TradeAllowed);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MetaApi startup connectivity check failed.");
                await SafeNotifyErrorAsync($"MetaApi startup check failed: {ex.Message}", cancellationToken);
            }
        }

        if (settings.Telegram.Enabled && settings.Telegram.SendStartupAlerts)
        {
            await SafeNotifyInfoAsync(
                $"{settings.InstanceName} started. DryRun={settings.DryRun}, Sessions={settings.TradingSessions.Count}",
                cancellationToken);
        }

        await base.StartAsync(cancellationToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (_botSettings.CurrentValue.Resilience.SaveStateOnShutdown)
            {
                await _statePersistenceService.SaveAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist state during shutdown.");
        }

        await base.StopAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunCycleAsync(stoppingToken);
                await TrySavePeriodicSnapshotAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error in trading bot worker cycle.");
                await SafeNotifyErrorAsync($"Unhandled worker error: {ex.Message}", stoppingToken);
            }

            var delaySeconds = Math.Max(1, _botSettings.CurrentValue.PollingIntervalSeconds);
            await Task.Delay(TimeSpan.FromSeconds(delaySeconds), stoppingToken);
        }
    }

    private async Task RunCycleAsync(CancellationToken cancellationToken)
    {
        var settings = _botSettings.CurrentValue;
        var activeStrategy = settings.ActiveStrategy?.Trim();

        if (string.Equals(activeStrategy, StrategyNames.First4HReentry, StringComparison.OrdinalIgnoreCase))
        {
            await RunFirst4HCycleAsync(cancellationToken);
            return;
        }

        await RunOrbCycleAsync(cancellationToken);
    }

    //private async Task RunCycleAsync(CancellationToken cancellationToken)
    //{
    //    var settings = _botSettings.CurrentValue;
    //    var nowUtc = DateTimeOffset.UtcNow;
    //    var timeZone = ResolveTimeZone(settings.TimeZoneId);
    //    var nowLocal = TimeZoneInfo.ConvertTime(nowUtc, timeZone);

    //    foreach (var session in settings.TradingSessions)
    //    {
    //        var sessionTradingDay = TradingTimeHelper.GetSessionTradingDay(session, nowLocal);

    //        await ProcessSessionAsync(
    //            session,
    //            sessionTradingDay,
    //            nowLocal,
    //            cancellationToken);
    //    }
    //}

    private async Task RunOrbCycleAsync(CancellationToken cancellationToken)
    {
        var settings = _botSettings.CurrentValue;
        var nowUtc = DateTimeOffset.UtcNow;
        var timeZone = ResolveTimeZone(settings.TimeZoneId);
        var nowLocal = TimeZoneInfo.ConvertTime(nowUtc, timeZone);

        foreach (var session in settings.TradingSessions)
        {
            var sessionTradingDay = TradingTimeHelper.GetSessionTradingDay(session, nowLocal);

            await ProcessSessionAsync(
                session,
                sessionTradingDay,
                nowLocal,
                cancellationToken);
        }
    }

    private async Task RunFirst4HCycleAsync(CancellationToken cancellationToken)
    {
        var settings = _botSettings.CurrentValue;
        var first4H = settings.First4HReentry;

        if (!first4H.Enabled)
            return;

        foreach (var profile in first4H.Profiles.Where(p => p.Enabled))
        {
            var anchorTz = TimeZoneInfo.FindSystemTimeZoneById(profile.AnchorTimeZoneId);
            var nowAnchor = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, anchorTz);
            var tradingDay = DateOnly.FromDateTime(nowAnchor.Date);

            var syntheticSession = new TradingSessionSettings
            {
                Enabled = true,
                Name = $"First4H:{profile.Name}",
                StartTime = TimeSpan.Zero,
                EndTime = new TimeSpan(23, 59, 59),
                OrbMinutes = 240,
                Instruments = profile.Instruments.ToList()
            };

            var sessionState = _sessionStateStore.EnsureSessionState(syntheticSession, tradingDay);
            sessionState.IsEnabled = true;
            sessionState.IsActive = true;
            sessionState.LastHeartbeatUtc = DateTimeOffset.UtcNow;

            foreach (var instrument in profile.Instruments.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                await ProcessFirst4HInstrumentAsync(
                    profile,
                    syntheticSession,
                    sessionState,
                    instrument,
                    cancellationToken);
            }
        }
    }

    private async Task ProcessFirst4HInstrumentAsync(
    First4HProfileSettings profile,
    TradingSessionSettings syntheticSession,
    SessionState sessionState,
    string instrument,
    CancellationToken cancellationToken)
    {
        if (!sessionState.InstrumentStates.TryGetValue(instrument, out var instrumentState))
            return;

        instrumentState.LastProcessedUtc = DateTimeOffset.UtcNow;

        // For First4H, only block new entries while a position is still being managed/open.
        // After it closes, the instrument can trade again the same day until strategy max is reached.
        if (HasActiveManagedTrade(instrumentState))
        {
            await MonitorExecutedTradeAsync(syntheticSession, sessionState, instrumentState, cancellationToken);
            return;
        }

        var signalResult = await _first4HReentryStrategyService.EvaluateAsync(
            profile,
            instrument,
            cancellationToken);

        instrumentState.LastDecisionReason = signalResult.Reason;

        if (!signalResult.SignalReady || !signalResult.Signal.ShouldTrade)
            return;

        var riskResult = await _riskManager.EvaluateAsync(
            syntheticSession,
            sessionState,
            instrumentState,
            signalResult.Signal,
            cancellationToken);

        instrumentState.RiskApproved = riskResult.Approved;
        instrumentState.PendingTradePlan = riskResult.TradePlan;
        instrumentState.LastDecisionReason = riskResult.Reason;

        if (!riskResult.Approved || riskResult.TradePlan is null)
            return;

        var execution = await _tradeExecutionService.ExecuteAsync(
            syntheticSession,
            instrumentState,
            riskResult.TradePlan,
            cancellationToken);

        if (!execution.Success)
        {
            instrumentState.LastDecisionReason = execution.Reason;

            await _statePersistenceService.AppendJournalAsync(
                new ExecutionJournalEntry
                {
                    TimeUtc = DateTimeOffset.UtcNow,
                    EventType = "ExecutionFailed",
                    SessionName = syntheticSession.Name,
                    Instrument = instrument,
                    Message = execution.Reason
                },
                cancellationToken);

            return;
        }

        instrumentState.TradeTaken = true;
        instrumentState.DryRunExecution = execution.Simulated;
        instrumentState.ManagedOrderId = execution.OrderId;
        instrumentState.ManagedPositionId = execution.PositionId;
        instrumentState.ManagedClientId = execution.ClientId;
        instrumentState.EntryExecutedAtUtc = DateTimeOffset.UtcNow;
        instrumentState.PendingTradePlan = riskResult.TradePlan;
        instrumentState.TradesTaken += 1;
        sessionState.TradesTaken += 1;
        instrumentState.LastDecisionReason = execution.Reason;

        await _statePersistenceService.AppendJournalAsync(
            new ExecutionJournalEntry
            {
                TimeUtc = DateTimeOffset.UtcNow,
                EventType = "TradeExecuted",
                SessionName = syntheticSession.Name,
                Instrument = instrument,
                Message = execution.Reason,
                Direction = riskResult.TradePlan.Direction.ToString(),
                OrderId = execution.OrderId,
                PositionId = execution.PositionId,
                ClientId = execution.ClientId,
                Quantity = riskResult.TradePlan.Quantity,
                EntryPrice = riskResult.TradePlan.EntryPrice,
                StopLoss = riskResult.TradePlan.StopLoss,
                TakeProfit = riskResult.TradePlan.TakeProfit,
                Simulated = execution.Simulated
            },
            cancellationToken);

        await _statePersistenceService.SaveAsync(cancellationToken);

        if (_botSettings.CurrentValue.Telegram.SendTradeAlerts)
        {
            await SafeNotifyInfoAsync(
                BuildEntryNotificationMessage(riskResult.TradePlan, execution),
                cancellationToken);
        }

        // Important:
        // For First4H dry-run we should release the "active trade" lock immediately
        // so the bot can continue finding more valid setups that day.
        if (execution.Simulated)
        {
            instrumentState.TradeTaken = false;
            instrumentState.DryRunExecution = false;
            instrumentState.ManagedOrderId = null;
            instrumentState.ManagedPositionId = null;
            instrumentState.ManagedClientId = null;
            instrumentState.EntryExecutedAtUtc = null;
            instrumentState.PendingTradePlan = null;
            instrumentState.RiskApproved = false;
            instrumentState.LastKnownUnrealizedPnL = null;
            instrumentState.LastTrailingStopUpdateUtc = null;
            instrumentState.LastDecisionReason = "Dry-run trade completed and released.";
        }
    }

    private async Task ProcessSessionAsync(
        TradingSessionSettings session,
        DateOnly tradingDay,
        DateTimeOffset nowLocal,
        CancellationToken cancellationToken)
    {
        var sessionState = _sessionStateStore.EnsureSessionState(session, tradingDay);
        sessionState.LastHeartbeatUtc = DateTimeOffset.UtcNow;

        var wasActive = sessionState.IsActive;
        sessionState.IsActive = session.Enabled && IsSessionActive(nowLocal.TimeOfDay, session);

        if (wasActive != sessionState.IsActive)
        {
            _logger.LogInformation(
                "Session state changed: {SessionName} => Active={IsActive}",
                session.Name,
                sessionState.IsActive);
        }

        if (!session.Enabled)
        {
            _logger.LogDebug("Session {SessionName} is disabled. Skipping.", session.Name);
            return;
        }

        foreach (var instrument in session.Instruments.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            //string instrumentM = NormaliseSymbol(instrument);
            await ProcessInstrumentAsync(session, sessionState, instrument, cancellationToken);
        }
    }

    private async Task ProcessInstrumentAsync(
        TradingSessionSettings session,
        SessionState sessionState,
        string instrument,
        CancellationToken cancellationToken)
    {
        if (!sessionState.InstrumentStates.TryGetValue(instrument, out var instrumentState))
        {
            _logger.LogWarning(
                "Instrument state missing for Session={SessionName}, Instrument={Instrument}.",
                session.Name,
                instrument);
            return;
        }

        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["Session"] = session.Name,
            ["Instrument"] = instrument
        });

        instrumentState.LastProcessedUtc = DateTimeOffset.UtcNow;

        if (!sessionState.IsActive)
        {
            _logger.LogDebug("Session inactive.");
            return;
        }

        try
        {
            var quote = await _marketDataProvider.GetQuoteAsync(instrument, cancellationToken);
            if (quote is not null)
            {
                instrumentState.LastBid = quote.Bid;
                instrumentState.LastAsk = quote.Ask;
                instrumentState.LastQuoteSyncUtc = DateTimeOffset.UtcNow;
            }

            if (ShouldRefreshPositions(instrumentState.LastPositionsSyncUtc))
            {
                var openPositions = await _marketDataProvider.GetOpenPositionsAsync(instrument, cancellationToken);
                instrumentState.LastKnownOpenPositions = openPositions.Count;
                instrumentState.LastPositionsSyncUtc = DateTimeOffset.UtcNow;
            }

            if (instrumentState.TradeTaken)
            {
                await MonitorExecutedTradeAsync(session, sessionState, instrumentState, cancellationToken);
                return;
            }

            if (!instrumentState.RangeBuilt)
            {
                instrumentState.LastRangeBuildAttemptUtc = DateTimeOffset.UtcNow;

                var orbResult = await _orbRangeBuilder.TryBuildAsync(
                    session,
                    instrumentState,
                    cancellationToken);

                instrumentState.LastSessionWindow = orbResult.Window;
                instrumentState.LastDecisionReason = orbResult.Reason;

                if (orbResult.Built && orbResult.Range is not null)
                {
                    instrumentState.OrbRange = orbResult.Range;
                    instrumentState.RangeBuilt = true;

                    _logger.LogInformation(
                        "ORB built. High={High} Low={Low} StartUtc={StartUtc:O} EndUtc={EndUtc:O}",
                        orbResult.Range.High,
                        orbResult.Range.Low,
                        orbResult.Range.StartTimeUtc,
                        orbResult.Range.EndTimeUtc);

                    await _statePersistenceService.AppendJournalAsync(
                        new ExecutionJournalEntry
                        {
                            TimeUtc = DateTimeOffset.UtcNow,
                            EventType = "RangeBuilt",
                            SessionName = session.Name,
                            Instrument = instrument,
                            Message = "ORB range built successfully."
                        },
                        cancellationToken);

                    await _statePersistenceService.SaveAsync(cancellationToken);
                }

                return;
            }

            if (!ShouldEvaluateSignal(instrumentState.LastSignalEvaluationUtc))
                return;

            instrumentState.LastSignalEvaluationUtc = DateTimeOffset.UtcNow;

            var signalResult = await _orbSignalEvaluator.EvaluateAsync(
                session,
                instrumentState,
                cancellationToken);

            instrumentState.BreakoutDetected = signalResult.BreakoutDetected;
            instrumentState.BreakoutDirection = signalResult.BreakoutDirection;
            instrumentState.BreakoutCandleTimeUtc = signalResult.BreakoutTimeUtc;
            instrumentState.BreakoutClosePrice = signalResult.BreakoutClosePrice;
            instrumentState.RetestConfirmed = signalResult.RetestConfirmed;
            instrumentState.RetestTimeUtc = signalResult.RetestTimeUtc;
            instrumentState.RetestReferencePrice = signalResult.RetestReferencePrice;
            instrumentState.FibonacciConfirmed = signalResult.FibonacciConfirmed;
            instrumentState.FibonacciTouchedLevel = signalResult.FibonacciTouchedLevel;
            instrumentState.FibonacciTouchTimeUtc = signalResult.FibonacciTouchTimeUtc;
            instrumentState.FibonacciReferencePrice = signalResult.FibonacciReferencePrice;
            instrumentState.EntrySignalReady = signalResult.Signal.ShouldTrade;
            instrumentState.PendingSignalDirection = signalResult.Signal.Direction;
            instrumentState.LastDecisionReason = signalResult.Signal.Reason;

            if (!signalResult.Signal.ShouldTrade)
            {
                instrumentState.RiskApproved = false;
                instrumentState.PendingTradePlan = null;
                return;
            }

            var riskResult = await _riskManager.EvaluateAsync(
                session,
                sessionState,
                instrumentState,
                signalResult.Signal,
                cancellationToken);

            instrumentState.RiskApproved = riskResult.Approved;
            instrumentState.PendingTradePlan = riskResult.TradePlan;
            instrumentState.LastDecisionReason = riskResult.Reason;

            if (!riskResult.Approved || riskResult.TradePlan is null)
            {
                await _statePersistenceService.AppendJournalAsync(
                    new ExecutionJournalEntry
                    {
                        TimeUtc = DateTimeOffset.UtcNow,
                        EventType = "RiskBlocked",
                        SessionName = session.Name,
                        Instrument = instrument,
                        Message = riskResult.Reason
                    },
                    cancellationToken);

                return;
            }

            var execution = await _tradeExecutionService.ExecuteAsync(
                session,
                instrumentState,
                riskResult.TradePlan,
                cancellationToken);

            if (!execution.Success)
            {
                instrumentState.LastDecisionReason = execution.Reason;

                await _statePersistenceService.AppendJournalAsync(
                    new ExecutionJournalEntry
                    {
                        TimeUtc = DateTimeOffset.UtcNow,
                        EventType = "ExecutionFailed",
                        SessionName = session.Name,
                        Instrument = instrument,
                        Message = execution.Reason
                    },
                    cancellationToken);

                await SafeNotifyErrorAsync(
                    $"Execution failed | Session={session.Name} | Instrument={instrument} | Reason={execution.Reason}",
                    cancellationToken);

                await _statePersistenceService.SaveAsync(cancellationToken);
                return;
            }

            instrumentState.TradeTaken = true;
            instrumentState.DryRunExecution = execution.Simulated;
            instrumentState.ManagedOrderId = execution.OrderId;
            instrumentState.ManagedPositionId = execution.PositionId;
            instrumentState.ManagedClientId = execution.ClientId;
            instrumentState.EntryExecutedAtUtc = DateTimeOffset.UtcNow;
            instrumentState.PendingTradePlan = riskResult.TradePlan;
            instrumentState.TradesTaken += 1;
            sessionState.TradesTaken += 1;
            instrumentState.LastDecisionReason = execution.Reason;

            await _statePersistenceService.AppendJournalAsync(
                new ExecutionJournalEntry
                {
                    TimeUtc = DateTimeOffset.UtcNow,
                    EventType = "TradeExecuted",
                    SessionName = session.Name,
                    Instrument = instrument,
                    Message = execution.Reason,
                    Direction = riskResult.TradePlan.Direction.ToString(),
                    OrderId = execution.OrderId,
                    PositionId = execution.PositionId,
                    ClientId = execution.ClientId,
                    Quantity = riskResult.TradePlan.Quantity,
                    EntryPrice = riskResult.TradePlan.EntryPrice,
                    StopLoss = riskResult.TradePlan.StopLoss,
                    TakeProfit = riskResult.TradePlan.TakeProfit,
                    Simulated = execution.Simulated
                },
                cancellationToken);

            await _statePersistenceService.SaveAsync(cancellationToken);

            if (_botSettings.CurrentValue.Telegram.SendTradeAlerts)
            {
                await SafeNotifyInfoAsync(
                    BuildEntryNotificationMessage(riskResult.TradePlan, execution),
                    cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Instrument processing failed.");
            instrumentState.LastDecisionReason = ex.Message;
        }
    }

    private async Task MonitorExecutedTradeAsync(
    TradingSessionSettings session,
    SessionState sessionState,
    InstrumentState instrumentState,
    CancellationToken cancellationToken)
    {
        var monitorResult = await _positionMonitorService.SyncAsync(instrumentState, cancellationToken);

        if (!monitorResult.HasTrackedPosition)
            return;

        if (monitorResult.PositionStillOpen && monitorResult.CurrentPosition is not null)
        {
            instrumentState.LastKnownUnrealizedPnL = monitorResult.CurrentPosition.UnrealizedPnL;

            await ManageOpenPositionAsync(
                session,
                instrumentState,
                monitorResult.CurrentPosition,
                cancellationToken);

            return;
        }

        _logger.LogInformation(
            "Position closed. Event={EventType} Reason={Reason}",
            monitorResult.EventType,
            monitorResult.Reason);

        await _statePersistenceService.AppendJournalAsync(
            new ExecutionJournalEntry
            {
                TimeUtc = DateTimeOffset.UtcNow,
                EventType = "PositionClosed",
                SessionName = session.Name,
                Instrument = instrumentState.Instrument,
                Message = monitorResult.Reason,
                PositionId = instrumentState.ManagedPositionId,
                ClientId = instrumentState.ManagedClientId,
                RealizedPnL = monitorResult.RealizedPnL
            },
            cancellationToken);

        if (_botSettings.CurrentValue.Telegram.SendTradeAlerts)
        {
            await SafeNotifyInfoAsync(
                BuildExitNotificationMessage(session.Name, instrumentState.Instrument, monitorResult),
                cancellationToken);
        }

        var isFirst4H = IsFirst4HSession(session);

        instrumentState.DryRunExecution = false;
        instrumentState.ManagedOrderId = null;
        instrumentState.ManagedPositionId = null;
        instrumentState.ManagedClientId = null;
        instrumentState.EntryExecutedAtUtc = null;
        instrumentState.PendingTradePlan = null;
        instrumentState.RiskApproved = false;
        instrumentState.LastKnownUnrealizedPnL = null;
        instrumentState.LastTrailingStopUpdateUtc = null;
        instrumentState.LastDecisionReason = monitorResult.Reason;

        if (isFirst4H)
        {
            // Release the lock so another valid setup can trade later the same day.
            // We do NOT reset TradesTaken; that remains the daily count.
            instrumentState.TradeTaken = false;
        }
        else
        {
            // ORB remains one trade per instrument/session/day.
            instrumentState.TradeTaken = true;
        }

        await _statePersistenceService.SaveAsync(cancellationToken);

        _ = sessionState;
    }

    //private async Task MonitorExecutedTradeAsync(
    //TradingSessionSettings session,
    //SessionState sessionState,
    //InstrumentState instrumentState,
    //CancellationToken cancellationToken)
    //{
    //    var monitorResult = await _positionMonitorService.SyncAsync(instrumentState, cancellationToken);

    //    if (!monitorResult.HasTrackedPosition)
    //        return;

    //    if (monitorResult.PositionStillOpen && monitorResult.CurrentPosition is not null)
    //    {
    //        instrumentState.LastKnownUnrealizedPnL = monitorResult.CurrentPosition.UnrealizedPnL;

    //        await ManageOpenPositionAsync(
    //            session,
    //            instrumentState,
    //            monitorResult.CurrentPosition,
    //            cancellationToken);

    //        return;
    //    }

    //    _logger.LogInformation(
    //        "Position closed. Event={EventType} Reason={Reason}",
    //        monitorResult.EventType,
    //        monitorResult.Reason);

    //    await _statePersistenceService.AppendJournalAsync(
    //        new ExecutionJournalEntry
    //        {
    //            TimeUtc = DateTimeOffset.UtcNow,
    //            EventType = "PositionClosed",
    //            SessionName = session.Name,
    //            Instrument = instrumentState.Instrument,
    //            Message = monitorResult.Reason,
    //            PositionId = instrumentState.ManagedPositionId,
    //            ClientId = instrumentState.ManagedClientId,
    //            RealizedPnL = monitorResult.RealizedPnL
    //        },
    //        cancellationToken);

    //    if (_botSettings.CurrentValue.Telegram.SendTradeAlerts)
    //    {
    //        await SafeNotifyInfoAsync(
    //            BuildExitNotificationMessage(session.Name, instrumentState.Instrument, monitorResult),
    //            cancellationToken);
    //    }

    //    instrumentState.DryRunExecution = false;
    //    instrumentState.ManagedOrderId = null;
    //    instrumentState.ManagedPositionId = null;
    //    instrumentState.ManagedClientId = null;
    //    instrumentState.EntryExecutedAtUtc = null;
    //    instrumentState.PendingTradePlan = null;
    //    instrumentState.RiskApproved = false;
    //    instrumentState.LastKnownUnrealizedPnL = null;
    //    instrumentState.LastTrailingStopUpdateUtc = null;
    //    instrumentState.LastDecisionReason = monitorResult.Reason;

    //    await _statePersistenceService.SaveAsync(cancellationToken);

    //    _ = sessionState;
    //}

    //private async Task MonitorExecutedTradeAsync(
    //    TradingSessionSettings session,
    //    SessionState sessionState,
    //    InstrumentState instrumentState,
    //    CancellationToken cancellationToken)
    //{
    //    var monitorResult = await _positionMonitorService.SyncAsync(instrumentState, cancellationToken);

    //    if (!monitorResult.HasTrackedPosition)
    //        return;

    //    if (monitorResult.PositionStillOpen && monitorResult.CurrentPosition is not null)
    //    {
    //        instrumentState.LastKnownUnrealizedPnL = monitorResult.CurrentPosition.UnrealizedPnL;
    //        return;
    //    }

    //    _logger.LogInformation(
    //        "Position closed. Event={EventType} Reason={Reason}",
    //        monitorResult.EventType,
    //        monitorResult.Reason);

    //    await _statePersistenceService.AppendJournalAsync(
    //        new ExecutionJournalEntry
    //        {
    //            TimeUtc = DateTimeOffset.UtcNow,
    //            EventType = "PositionClosed",
    //            SessionName = session.Name,
    //            Instrument = instrumentState.Instrument,
    //            Message = monitorResult.Reason,
    //            PositionId = instrumentState.ManagedPositionId,
    //            ClientId = instrumentState.ManagedClientId,
    //            RealizedPnL = monitorResult.RealizedPnL
    //        },
    //        cancellationToken);

    //    if (_botSettings.CurrentValue.Telegram.SendTradeAlerts)
    //    {
    //        await SafeNotifyInfoAsync(
    //            BuildExitNotificationMessage(session.Name, instrumentState.Instrument, monitorResult),
    //            cancellationToken);
    //    }

    //    instrumentState.DryRunExecution = false;
    //    instrumentState.ManagedOrderId = null;
    //    instrumentState.ManagedPositionId = null;
    //    instrumentState.ManagedClientId = null;
    //    instrumentState.EntryExecutedAtUtc = null;
    //    instrumentState.PendingTradePlan = null;
    //    instrumentState.RiskApproved = false;
    //    instrumentState.LastKnownUnrealizedPnL = null;
    //    instrumentState.LastDecisionReason = monitorResult.Reason;

    //    await _statePersistenceService.SaveAsync(cancellationToken);

    //    _ = sessionState;
    //}

    private async Task TrySavePeriodicSnapshotAsync(CancellationToken cancellationToken)
    {
        var resilience = _botSettings.CurrentValue.Resilience;
        if (!resilience.Enabled)
            return;

        if (!_lastSnapshotSaveUtc.HasValue)
        {
            _lastSnapshotSaveUtc = DateTimeOffset.UtcNow;
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(1, resilience.SaveIntervalSeconds));
        if (DateTimeOffset.UtcNow - _lastSnapshotSaveUtc.Value < interval)
            return;

        await _statePersistenceService.SaveAsync(cancellationToken);
        _lastSnapshotSaveUtc = DateTimeOffset.UtcNow;
    }

    private bool ShouldEvaluateSignal(DateTimeOffset? lastEvaluationUtc)
    {
        var strategy = _botSettings.CurrentValue.Strategy;

        var breakoutSpan = TradingTimeHelper.GetTimeframeSpan(strategy.BreakoutTimeframe);
        var entrySpan = TradingTimeHelper.GetTimeframeSpan(
            string.IsNullOrWhiteSpace(strategy.EntryTimeframe)
                ? strategy.BreakoutTimeframe
                : strategy.EntryTimeframe);

        var evaluationSpan = breakoutSpan <= entrySpan ? breakoutSpan : entrySpan;

        if (!lastEvaluationUtc.HasValue)
            return true;

        return DateTimeOffset.UtcNow - lastEvaluationUtc.Value >= evaluationSpan;
    }

    private static bool ShouldRefreshPositions(DateTimeOffset? lastSyncUtc)
    {
        if (!lastSyncUtc.HasValue)
            return true;

        return DateTimeOffset.UtcNow - lastSyncUtc.Value >= TimeSpan.FromSeconds(30);
    }

    private static bool IsSessionActive(TimeSpan currentTime, TradingSessionSettings session)
    {
        if (session.EndTime > session.StartTime)
        {
            return currentTime >= session.StartTime && currentTime <= session.EndTime;
        }

        return currentTime >= session.StartTime || currentTime <= session.EndTime;
    }

    private static TimeZoneInfo ResolveTimeZone(string timeZoneId)
    {
        return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
    }

    private static string BuildEntryNotificationMessage(
        PreparedTradePlan plan,
        ExecutionOutcome execution)
    {
        return
$@"TRADE ENTERED
Session: {plan.SessionName}
Instrument: {plan.Instrument}
Direction: {plan.Direction}
Qty: {plan.Quantity}
Entry: {plan.EntryPrice}
SL: {plan.StopLoss}
TP: {plan.TakeProfit}
OrderId: {execution.OrderId}
PositionId: {execution.PositionId}
ClientId: {execution.ClientId}
Simulated: {execution.Simulated}
Reason: {execution.Reason}";
    }

    private static string BuildExitNotificationMessage(
        string sessionName,
        string instrument,
        PositionMonitorResult result)
    {
        return
$@"POSITION CLOSED
Session: {sessionName}
Instrument: {instrument}
ExitType: {result.EventType}
RealizedPnL: {result.RealizedPnL}
Reason: {result.Reason}";
    }

    private async Task SafeNotifyInfoAsync(string message, CancellationToken cancellationToken)
    {
        try
        {
            await _notificationService.SendInfoAsync(message, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send info notification.");
        }
    }

    private async Task SafeNotifyErrorAsync(string message, CancellationToken cancellationToken)
    {
        try
        {
            await _notificationService.SendErrorAsync(message, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send error notification.");
        }
    }



    private async Task ManageOpenPositionAsync(
    TradingSessionSettings session,
    InstrumentState instrumentState,
    PositionInfo position,
    CancellationToken cancellationToken)
    {
        var nowUtc = DateTimeOffset.UtcNow;
        var bot = _botSettings.CurrentValue;
        var risk = bot.Risk;

        // 1) Session-end forced close
        if (risk.SessionExit.Enabled &&
            ShouldForceSessionClose(session, nowUtc))
        {
            var closeResult = await _tradeExecutor.ClosePositionAsync(
                position.PositionId,
                comment: "SessionExit",
                cancellationToken: cancellationToken);

            if (closeResult.Success)
            {
                _logger.LogInformation(
                    "Forced session-end close submitted. Session={SessionName} Instrument={Instrument} PositionId={PositionId}",
                    session.Name,
                    instrumentState.Instrument,
                    position.PositionId);

                await _statePersistenceService.AppendJournalAsync(
                    new ExecutionJournalEntry
                    {
                        TimeUtc = nowUtc,
                        EventType = "SessionExitCloseSubmitted",
                        SessionName = session.Name,
                        Instrument = instrumentState.Instrument,
                        Message = "Session-end close submitted.",
                        PositionId = position.PositionId,
                        ClientId = instrumentState.ManagedClientId
                    },
                    cancellationToken);

                return;
            }

            _logger.LogWarning(
                "Session-end close failed. Session={SessionName} Instrument={Instrument} PositionId={PositionId} Reason={Reason}",
                session.Name,
                instrumentState.Instrument,
                position.PositionId,
                closeResult.Message);
        }

        // 2) Bot-managed trailing stop
        if (!risk.TrailingStop.Enabled || instrumentState.PendingTradePlan is null)
            return;

        if (instrumentState.LastTrailingStopUpdateUtc.HasValue &&
            nowUtc - instrumentState.LastTrailingStopUpdateUtc.Value <
            TimeSpan.FromSeconds(Math.Max(0, risk.TrailingStop.MinimumSecondsBetweenUpdates)))
        {
            return;
        }

        var candidateStop = TryCalculateTrailingStop(
            position,
            instrumentState.PendingTradePlan,
            risk.TrailingStop);

        if (!candidateStop.HasValue)
            return;

        var currentStop = position.StopLoss ?? instrumentState.PendingTradePlan.StopLoss;

        if (!IsBetterStop(position.Direction, currentStop, candidateStop.Value))
            return;

        var modifyResult = await _tradeExecutor.ModifyPositionAsync(
            position.PositionId,
            stopLoss: candidateStop.Value,
            takeProfit: position.TakeProfit,
            cancellationToken: cancellationToken);

        if (!modifyResult.Success)
        {
            _logger.LogWarning(
                "Trailing stop modify failed. Instrument={Instrument} PositionId={PositionId} CurrentSL={CurrentSL} CandidateSL={CandidateSL} Reason={Reason}",
                instrumentState.Instrument,
                position.PositionId,
                currentStop,
                candidateStop.Value,
                modifyResult.Message);

            return;
        }

        instrumentState.LastTrailingStopUpdateUtc = nowUtc;
        instrumentState.LastDecisionReason = $"Trailing stop updated to {candidateStop.Value}";

        _logger.LogInformation(
            "Trailing stop updated. Instrument={Instrument} PositionId={PositionId} OldSL={OldSL} NewSL={NewSL}",
            instrumentState.Instrument,
            position.PositionId,
            currentStop,
            candidateStop.Value);

        await _statePersistenceService.AppendJournalAsync(
            new ExecutionJournalEntry
            {
                TimeUtc = nowUtc,
                EventType = "TrailingStopUpdated",
                SessionName = session.Name,
                Instrument = instrumentState.Instrument,
                Message = $"Trailing stop updated from {currentStop} to {candidateStop.Value}.",
                PositionId = position.PositionId,
                ClientId = instrumentState.ManagedClientId,
                StopLoss = candidateStop.Value
            },
            cancellationToken);

        await _statePersistenceService.SaveAsync(cancellationToken);
    }

    private bool ShouldForceSessionClose(
    TradingSessionSettings session,
    DateTimeOffset nowUtc)
    {
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(_botSettings.CurrentValue.TimeZoneId);
        var nowLocal = TimeZoneInfo.ConvertTime(nowUtc, timeZone);

        var tradingDay = TradingTimeHelper.GetSessionTradingDay(session, nowLocal);
        var window = TradingTimeHelper.GetSessionWindow(session, tradingDay, timeZone);

        var cutoffLocal = window.SessionEndLocal.AddMinutes(
            -_botSettings.CurrentValue.Risk.SessionExit.MinutesBeforeSessionEnd);

        return nowLocal.DateTime >= cutoffLocal;
    }

    private static decimal? TryCalculateTrailingStop(
        PositionInfo position,
        PreparedTradePlan tradePlan,
        TrailingStopSettings settings)
    {
        var entry = tradePlan.EntryPrice;
        var initialStop = tradePlan.StopLoss;
        var initialRisk = Math.Abs(entry - initialStop);

        if (initialRisk <= 0)
            return null;

        var activationDistance = initialRisk * settings.ActivationR;
        var trailDistance = initialRisk * settings.DistanceR;

        if (position.Direction == TradeDirection.Buy)
        {
            var moved = position.CurrentPrice - entry;
            if (moved < activationDistance)
                return null;

            var lockPrice = entry + (initialRisk * settings.LockInR);
            var candidate = position.CurrentPrice - trailDistance;

            if (candidate < lockPrice)
                candidate = lockPrice;

            return candidate;
        }

        if (position.Direction == TradeDirection.Sell)
        {
            var moved = entry - position.CurrentPrice;
            if (moved < activationDistance)
                return null;

            var lockPrice = entry - (initialRisk * settings.LockInR);
            var candidate = position.CurrentPrice + trailDistance;

            if (candidate > lockPrice)
                candidate = lockPrice;

            return candidate;
        }

        return null;
    }

    private static bool IsBetterStop(
        TradeDirection direction,
        decimal currentStop,
        decimal candidateStop)
    {
        return direction switch
        {
            TradeDirection.Buy => candidateStop > currentStop,
            TradeDirection.Sell => candidateStop < currentStop,
            _ => false
        };
    }

    private static bool IsFirst4HSession(TradingSessionSettings session)
    {
        return session.Name.StartsWith("First4H:", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasActiveManagedTrade(InstrumentState instrumentState)
    {
        return instrumentState.TradeTaken &&
               (instrumentState.DryRunExecution ||
                !string.IsNullOrWhiteSpace(instrumentState.ManagedPositionId) ||
                !string.IsNullOrWhiteSpace(instrumentState.ManagedClientId));
    }

    //private static string NormaliseSymbol(string instrument) =>
    //instrument + "m";
}