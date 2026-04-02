using System.Text.Json;
using AlgoBot.Configuration;
using AlgoBot.Interfaces;
using AlgoBot.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AlgoBot.Services;

public sealed class JsonBotStatePersistenceService : IBotStatePersistenceService
{
    private readonly ISessionStateStore _sessionStateStore;
    private readonly IOptionsMonitor<BotSettings> _botSettings;
    private readonly ILogger<JsonBotStatePersistenceService> _logger;
    private readonly SemaphoreSlim _ioLock = new(1, 1);

    private static readonly JsonSerializerOptions SnapshotJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private static readonly JsonSerializerOptions JournalJsonOptions = new()
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = true
    };

    public JsonBotStatePersistenceService(
        ISessionStateStore sessionStateStore,
        IOptionsMonitor<BotSettings> botSettings,
        ILogger<JsonBotStatePersistenceService> logger)
    {
        _sessionStateStore = sessionStateStore;
        _botSettings = botSettings;
        _logger = logger;
    }

    public async Task RestoreAsync(CancellationToken cancellationToken = default)
    {
        var settings = _botSettings.CurrentValue.Resilience;
        if (!settings.Enabled || !settings.LoadStateOnStartup)
            return;

        var path = settings.StateFilePath;
        if (!File.Exists(path))
        {
            _logger.LogInformation("No persisted state file found at {Path}. Starting fresh.", path);
            return;
        }

        await _ioLock.WaitAsync(cancellationToken);
        try
        {
            await using var stream = File.OpenRead(path);
            var snapshot = await JsonSerializer.DeserializeAsync<PersistedBotState>(
                stream,
                SnapshotJsonOptions,
                cancellationToken);

            if (snapshot is null || snapshot.Sessions.Count == 0)
            {
                _logger.LogWarning("Persisted state file was empty or invalid: {Path}", path);
                return;
            }

            var configuredSessions = _botSettings.CurrentValue.TradingSessions
                .ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);

            var restoredSessions = 0;
            var restoredInstruments = 0;

            foreach (var persistedSession in snapshot.Sessions)
            {
                if (!configuredSessions.TryGetValue(persistedSession.SessionName, out var sessionConfig))
                    continue;

                if (!DateOnly.TryParse(persistedSession.TradingDay, out var tradingDay))
                    continue;

                var sessionState = _sessionStateStore.EnsureSessionState(sessionConfig, tradingDay);
                sessionState.TradesTaken = persistedSession.TradesTaken;
                sessionState.DailyLossLimitReached = persistedSession.DailyLossLimitReached;
                sessionState.IsEnabled = sessionConfig.Enabled;
                sessionState.IsActive = false;

                foreach (var persistedInstrument in persistedSession.Instruments)
                {
                    if (!sessionState.InstrumentStates.TryGetValue(persistedInstrument.Instrument, out var instrumentState))
                        continue;

                    ApplyPersistedInstrumentState(instrumentState, persistedInstrument);
                    restoredInstruments++;
                }

                restoredSessions++;
            }

            _logger.LogInformation(
                "Restored persisted bot state from {Path}. Sessions={Sessions}, Instruments={Instruments}, SavedAt={SavedAtUtc:O}",
                path,
                restoredSessions,
                restoredInstruments,
                snapshot.SavedAtUtc);
        }
        finally
        {
            _ioLock.Release();
        }
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        var settings = _botSettings.CurrentValue.Resilience;
        if (!settings.Enabled)
            return;

        var snapshot = BuildSnapshot();

        var path = settings.StateFilePath;
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = path + ".tmp";

        await _ioLock.WaitAsync(cancellationToken);
        try
        {
            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    snapshot,
                    SnapshotJsonOptions,
                    cancellationToken);
            }

            File.Move(tempPath, path, overwrite: true);

            _logger.LogDebug(
                "Persisted bot state snapshot to {Path}. Sessions={Count}",
                path,
                snapshot.Sessions.Count);
        }
        finally
        {
            _ioLock.Release();
        }
    }

    public async Task AppendJournalAsync(
        ExecutionJournalEntry entry,
        CancellationToken cancellationToken = default)
    {
        var settings = _botSettings.CurrentValue.Resilience;
        if (!settings.Enabled)
            return;

        var path = settings.JournalFilePath;
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var line = JsonSerializer.Serialize(entry, JournalJsonOptions) + Environment.NewLine;

        await _ioLock.WaitAsync(cancellationToken);
        try
        {
            await File.AppendAllTextAsync(path, line, cancellationToken);
        }
        finally
        {
            _ioLock.Release();
        }
    }

    private PersistedBotState BuildSnapshot()
    {
        var sessions = _sessionStateStore.GetAll()
            .OrderBy(x => x.SessionName)
            .Select(s => new PersistedSessionState
            {
                SessionName = s.SessionName,
                TradingDay = s.TradingDay.ToString("yyyy-MM-dd"),
                TradesTaken = s.TradesTaken,
                DailyLossLimitReached = s.DailyLossLimitReached,
                Instruments = s.InstrumentStates.Values
                    .OrderBy(i => i.Instrument)
                    .Select(i => new PersistedInstrumentState
                    {
                        Instrument = i.Instrument,
                        RangeBuilt = i.RangeBuilt,
                        BreakoutDetected = i.BreakoutDetected,
                        BreakoutDirection = i.BreakoutDirection,
                        BreakoutCandleTimeUtc = i.BreakoutCandleTimeUtc,
                        BreakoutClosePrice = i.BreakoutClosePrice,
                        RetestConfirmed = i.RetestConfirmed,
                        RetestTimeUtc = i.RetestTimeUtc,
                        RetestReferencePrice = i.RetestReferencePrice,
                        FibonacciConfirmed = i.FibonacciConfirmed,
                        FibonacciTouchedLevel = i.FibonacciTouchedLevel,
                        FibonacciTouchTimeUtc = i.FibonacciTouchTimeUtc,
                        FibonacciReferencePrice = i.FibonacciReferencePrice,
                        EntrySignalReady = i.EntrySignalReady,
                        PendingSignalDirection = i.PendingSignalDirection,
                        SignalTriggeredAtUtc = i.SignalTriggeredAtUtc,
                        RiskApproved = i.RiskApproved,
                        PendingTradePlan = i.PendingTradePlan,
                        TradePlanNotificationSent = i.TradePlanNotificationSent,
                        TradeTaken = i.TradeTaken,
                        DryRunExecution = i.DryRunExecution,
                        ManagedOrderId = i.ManagedOrderId,
                        ManagedPositionId = i.ManagedPositionId,
                        ManagedClientId = i.ManagedClientId,
                        EntryExecutedAtUtc = i.EntryExecutedAtUtc,
                        LastKnownUnrealizedPnL = i.LastKnownUnrealizedPnL,
                        TradesTaken = i.TradesTaken,
                        OrbRange = i.OrbRange,
                        LastQuoteSyncUtc = i.LastQuoteSyncUtc,
                        LastPositionsSyncUtc = i.LastPositionsSyncUtc,
                        LastRangeBuildAttemptUtc = i.LastRangeBuildAttemptUtc,
                        LastSignalEvaluationUtc = i.LastSignalEvaluationUtc,
                        LastBid = i.LastBid,
                        LastAsk = i.LastAsk,
                        LastKnownOpenPositions = i.LastKnownOpenPositions,
                        LastDecisionReason = i.LastDecisionReason
                    })
                    .ToList()
            })
            .ToList();

        return new PersistedBotState
        {
            SavedAtUtc = DateTimeOffset.UtcNow,
            Sessions = sessions
        };
    }

    private static void ApplyPersistedInstrumentState(
        InstrumentState instrumentState,
        PersistedInstrumentState persisted)
    {
        instrumentState.RangeBuilt = persisted.RangeBuilt;
        instrumentState.BreakoutDetected = persisted.BreakoutDetected;
        instrumentState.BreakoutDirection = persisted.BreakoutDirection;
        instrumentState.BreakoutCandleTimeUtc = persisted.BreakoutCandleTimeUtc;
        instrumentState.BreakoutClosePrice = persisted.BreakoutClosePrice;
        instrumentState.RetestConfirmed = persisted.RetestConfirmed;
        instrumentState.RetestTimeUtc = persisted.RetestTimeUtc;
        instrumentState.RetestReferencePrice = persisted.RetestReferencePrice;
        instrumentState.FibonacciConfirmed = persisted.FibonacciConfirmed;
        instrumentState.FibonacciTouchedLevel = persisted.FibonacciTouchedLevel;
        instrumentState.FibonacciTouchTimeUtc = persisted.FibonacciTouchTimeUtc;
        instrumentState.FibonacciReferencePrice = persisted.FibonacciReferencePrice;
        instrumentState.EntrySignalReady = persisted.EntrySignalReady;
        instrumentState.PendingSignalDirection = persisted.PendingSignalDirection;
        instrumentState.SignalTriggeredAtUtc = persisted.SignalTriggeredAtUtc;
        instrumentState.RiskApproved = persisted.RiskApproved;
        instrumentState.PendingTradePlan = persisted.PendingTradePlan;
        instrumentState.TradePlanNotificationSent = persisted.TradePlanNotificationSent;
        instrumentState.TradeTaken = persisted.TradeTaken;
        instrumentState.DryRunExecution = persisted.DryRunExecution;
        instrumentState.ManagedOrderId = persisted.ManagedOrderId;
        instrumentState.ManagedPositionId = persisted.ManagedPositionId;
        instrumentState.ManagedClientId = persisted.ManagedClientId;
        instrumentState.EntryExecutedAtUtc = persisted.EntryExecutedAtUtc;
        instrumentState.LastKnownUnrealizedPnL = persisted.LastKnownUnrealizedPnL;
        instrumentState.TradesTaken = persisted.TradesTaken;
        instrumentState.OrbRange = persisted.OrbRange;
        instrumentState.LastQuoteSyncUtc = persisted.LastQuoteSyncUtc;
        instrumentState.LastPositionsSyncUtc = persisted.LastPositionsSyncUtc;
        instrumentState.LastRangeBuildAttemptUtc = persisted.LastRangeBuildAttemptUtc;
        instrumentState.LastSignalEvaluationUtc = persisted.LastSignalEvaluationUtc;
        instrumentState.LastBid = persisted.LastBid;
        instrumentState.LastAsk = persisted.LastAsk;
        instrumentState.LastKnownOpenPositions = persisted.LastKnownOpenPositions;
        instrumentState.LastDecisionReason = persisted.LastDecisionReason;
    }
}