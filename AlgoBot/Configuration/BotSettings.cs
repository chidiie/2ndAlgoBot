using System.ComponentModel.DataAnnotations;

namespace AlgoBot.Configuration;

public sealed class BotSettings
{
    public const string SectionName = "Bot";

    [Required]
    public string InstanceName { get; set; } = "OrbTradingBot";

    [Range(1, 3600)]
    public int PollingIntervalSeconds { get; set; } = 5;

    [Required]
    public string TimeZoneId { get; set; } = "Africa/Lagos";

    public bool DryRun { get; set; } = true;

    public MetaApiSettings MetaApi { get; set; } = new();

    public TelegramSettings Telegram { get; set; } = new();

    public StrategySettings Strategy { get; set; } = new();

    public RiskSettings Risk { get; set; } = new();
    public string ActiveStrategy { get; set; } = "First4HReentry";
    public First4HReentrySettings First4HReentry {  get; set; } = new();

    public List<TradingSessionSettings> TradingSessions { get; set; } = new();
    public ResilienceSettings Resilience { get; set; } = new();
    public BacktestSettings Backtest { get; set; } = new();
}

public sealed class BacktestSettings
{
    public bool Enabled { get; set; } = true;

    // Candle-driven execution model for Phase 10
    public bool UseNextBarOpenForEntry { get; set; } = true;

    // Approximate spread/slippage application in backtests
    public decimal EntrySpreadPips { get; set; } = 0.5m;
    public decimal ExitSpreadPips { get; set; } = 0.5m;
    public decimal SlippagePips { get; set; } = 0.0m;

    // Historical fetch batch size when pulling candles from MetaApi
    public int CandleBatchSize { get; set; } = 1000;

    // If true, skips weekends in summary expectancy assumptions only.
    public bool IgnoreWeekendStats { get; set; } = true;
}

public sealed class ResilienceSettings
{
    public bool Enabled { get; set; } = true;
    public bool LoadStateOnStartup { get; set; } = true;
    public bool SaveStateOnShutdown { get; set; } = true;
    public int SaveIntervalSeconds { get; set; } = 30;

    public string StateFilePath { get; set; } = "data/bot-state.json";
    public string JournalFilePath { get; set; } = "data/execution-journal.ndjson";
}

public sealed class MetaApiSettings
{
    public bool Enabled { get; set; } = false;
    public string TradingApiBaseUrl { get; set; } =
        "https://mt-client-api-v1.new-york.agiliumtrade.ai";
    public string MarketDataApiBaseUrl { get; set; } =
        "https://mt-market-data-client-api-v1.new-york.agiliumtrade.ai";
    public string Token { get; set; } = string.Empty;
    public string AccountId { get; set; } = string.Empty;

    [Range(5, 300)]
    public int TimeoutSeconds { get; set; } = 30;

    public bool KeepSubscription { get; set; } = true;
    public bool RefreshTerminalStateOnReads { get; set; } = false;

    [Range(0, 10)]
    public int MaxRetryAttempts { get; set; } = 3;
}

public sealed class TelegramSettings
{
    public bool Enabled { get; set; } = false;

    public string BaseUrl { get; set; } = "https://api.telegram.org";

    public string BotToken { get; set; } = string.Empty;

    // Can be chat id like "-1001234567890" or "@channelusername"
    public string ChatId { get; set; } = string.Empty;

    [Range(5, 300)]
    public int TimeoutSeconds { get; set; } = 30;

    [Range(0, 10)]
    public int MaxRetryAttempts { get; set; } = 3;

    public bool DisableNotification { get; set; } = false;

    // Leave empty for plain text. Supported by Telegram if you want HTML or MarkdownV2 later.
    public string ParseMode { get; set; } = string.Empty;

    public bool SendStartupAlerts { get; set; } = true;
    public bool SendTradeAlerts { get; set; } = true;
    public bool SendErrorAlerts { get; set; } = true;
    public bool SendDailySummary { get; set; } = true;
}

public sealed class First4HReentrySettings
{
    public bool Enabled { get; set; } = false;
    public List<First4HProfileSettings> Profiles { get; set; } = new();

    public string RangeTimeframe { get; set; }
    public string SignalTimeframe { get; set; } = "M5";
    public int RangeCount { get; set; } = 1;

    public decimal RewardRiskRatio { get; set; } = 2m;
    public decimal StopBufferPips { get; set; } = 0.5m;

    public int MaxTradesPerInstrumentPerDay { get; set; } = 2;

    public bool UseKeyLevelFallback { get; set; } = true;
    public int PivotStrength { get; set; } = 2;
    public int KeyLevelLookbackBars { get; set; } = 50;

    public decimal MaxStopAsRangeMultiple { get; set; } = 1.5m;
    public decimal MaxStopDistancePips { get; set; } = 0m;

    public bool ForceCloseAtDayEnd { get; set; } = true;
}

public sealed class First4HProfileSettings
{
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;

    public string AnchorTimeZoneId { get; set; } = "America/New_York";
    public TimeSpan DayStartTime { get; set; } = TimeSpan.Zero;

    public List<string> Instruments { get; set; } = new();
}

public sealed class StrategySettings
{
    public string RangeTimeframe { get; set; } = "M1";
    public string BreakoutTimeframe { get; set; } = "M1";
    public string EntryTimeframe { get; set; } = "";

    public EntryMode EntryMode { get; set; } = EntryMode.BreakoutOnly;

    public BreakoutSettings Breakout { get; set; } = new();
    public RetestSettings Retest { get; set; } = new();
    public FibonacciSettings Fibonacci { get; set; } = new();
    public IndicatorSettings Indicators { get; set; } = new();
    public VolumeFilterSettings Volume { get; set; } = new();
    public VwapFilterSettings Vwap { get; set; } = new();
}

public enum EntryMode
{
    BreakoutOnly = 1,
    BreakoutRetest = 2,
    BreakoutFibonacci = 3,
    BreakoutRetestFibonacci = 4
}

public sealed class BreakoutSettings
{
    public bool RequireCandleCloseOutsideRange { get; set; } = true;
    public decimal CloseBufferPips { get; set; } = 0m;
    public int MaxBreakoutCandlesAfterOrb { get; set; } = 50;
}

public sealed class RetestSettings
{
    public decimal TolerancePips { get; set; } = 1m;
    public int MaxCandlesAfterBreakout { get; set; } = 5;
    public bool RequireCloseInBreakoutDirection { get; set; } = true;
}

public sealed class VolumeFilterSettings
{
    public bool Enabled { get; set; } = false;

    // Usually use EntryTimeframe, but configurable
    public string Timeframe { get; set; } = "M1";

    // "TickVolume" or "Volume"
    public string VolumeSource { get; set; } = "TickVolume";

    // breakout candle > max(previous N candles)
    public int LookbackCandles { get; set; } = 3;

    // optional strength factor: breakoutVol >= previousMax * Multiplier
    public decimal Multiplier { get; set; } = 1.0m;
}

public sealed class VwapFilterSettings
{
    public bool Enabled { get; set; } = false;

    // Usually use EntryTimeframe, but configurable
    public string Timeframe { get; set; } = "M1";

    // Build VWAP from session start
    public bool AnchorToSessionStart { get; set; } = true;

    // For buy require close >= VWAP, for sell require close <= VWAP
    public bool RequirePriceOnCorrectSide { get; set; } = true;

    // Optional distance tolerance in pips
    public decimal TolerancePips { get; set; } = 0m;

    // Use TickVolume when true, otherwise use Volume
    public bool UseTickVolume { get; set; } = true;
}

public sealed class FibonacciSettings
{
    public bool Enabled { get; set; } = false;
    public bool RequireTouchInZone { get; set; } = true;
    public bool UseLevel0382 { get; set; } = true;
    public bool UseLevel0500 { get; set; } = true;
    public bool UseLevel0618 { get; set; } = true;
    public bool UseLevel0786 { get; set; } = false;
    public decimal ZoneTolerancePips { get; set; } = 2m;
    public int MaxCandlesAfterBreakout { get; set; } = 8;
}

public sealed class IndicatorSettings
{
    public EmaSettings Ema { get; set; } = new();
    public MacdSettings Macd { get; set; } = new();
    public RsiSettings Rsi { get; set; } = new();
}

public sealed class EmaSettings
{
    public bool Enabled { get; set; } = false;
    public string Timeframe { get; set; } = "M5";
    public int FastPeriod { get; set; } = 20;
    public int SlowPeriod { get; set; } = 50;
    public bool RequireTrendAlignment { get; set; } = true;
}

public sealed class MacdSettings
{
    public bool Enabled { get; set; } = false;
    public string Timeframe { get; set; } = "M5";
    public int FastPeriod { get; set; } = 12;
    public int SlowPeriod { get; set; } = 26;
    public int SignalPeriod { get; set; } = 9;
    public bool RequireCrossover { get; set; } = true;
}

public sealed class RsiSettings
{
    public bool Enabled { get; set; } = false;
    public string Timeframe { get; set; } = "M5";
    public int Period { get; set; } = 14;
    public decimal BuyMin { get; set; } = 50m;
    public decimal SellMax { get; set; } = 50m;
}

public sealed class RiskSettings
{
    public decimal RiskPerTradePercent { get; set; } = 1m;
    public decimal RewardRiskRatio { get; set; } = 2m;

    public int MaxTradesPerSession { get; set; } = 1;
    public int MaxTradesPerDay { get; set; } = 3;

    // Practical guard for now: equity drawdown vs balance
    public decimal DailyLossLimitPercent { get; set; } = 3m;

    public decimal MaxSpreadPips { get; set; } = 3m;
    public decimal SlippageTolerancePips { get; set; } = 1m;

    public StopLossMode StopLossMode { get; set; } = StopLossMode.RangeBoundary;
    public decimal StopLossBufferPips { get; set; } = 0.5m;
    public SessionExitSettings SessionExit { get; set; } = new();
    public TrailingStopSettings TrailingStop { get; set; } = new();
}

public sealed class SessionExitSettings
{
    public bool Enabled { get; set; } = false;

    // 0 = at session end, 5 = 5 minutes before end, etc.
    public int MinutesBeforeSessionEnd { get; set; } = 0;
}

public sealed class TrailingStopSettings
{
    public bool Enabled { get; set; } = false;

    // Start trailing once price has moved this many initial-R multiples
    public decimal ActivationR { get; set; } = 1.0m;

    // Distance from current price in R units
    public decimal DistanceR { get; set; } = 1.0m;

    // Minimum locked profit in R units once trailing activates
    // 0 = breakeven, 0.5 = lock half-R, 1 = lock full 1R
    public decimal LockInR { get; set; } = 0m;

    // Avoid modifying too frequently
    public int MinimumSecondsBetweenUpdates { get; set; } = 10;
}

public enum StopLossMode
{
    RangeBoundary = 1,
    RetestCandle = 2,
    FibonacciLevel = 3
}

public sealed class TradingSessionSettings
{
    public bool Enabled { get; set; } = true;

    [Required]
    public string Name { get; set; } = string.Empty;

    public TimeSpan StartTime { get; set; }

    public TimeSpan EndTime { get; set; }

    [Range(1, 240)]
    public int OrbMinutes { get; set; } = 5;

    public List<string> Instruments { get; set; } = new();
}

