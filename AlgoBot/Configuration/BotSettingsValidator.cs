using Microsoft.Extensions.Options;

namespace AlgoBot.Configuration;

public sealed class BotSettingsValidator : IValidateOptions<BotSettings>
{
    public ValidateOptionsResult Validate(string? name, BotSettings options)
    {
        var errors = new List<string>();

        if (options is null)
        {
            errors.Add("Bot settings are missing.");
            return ValidateOptionsResult.Fail(errors);
        }

        if (string.IsNullOrWhiteSpace(options.InstanceName))
            errors.Add("Bot:InstanceName is required.");

        if (options.PollingIntervalSeconds <= 0)
            errors.Add("Bot:PollingIntervalSeconds must be greater than 0.");

        if (string.IsNullOrWhiteSpace(options.TimeZoneId))
        {
            errors.Add("Bot:TimeZoneId is required.");
        }
        else
        {
            try
            {
                TimeZoneInfo.FindSystemTimeZoneById(options.TimeZoneId);
            }
            catch
            {
                errors.Add($"Bot:TimeZoneId '{options.TimeZoneId}' is invalid on this server.");
            }
        }

        if (options.MetaApi.Enabled)
        {
            if (string.IsNullOrWhiteSpace(options.MetaApi.Token))
                errors.Add("Bot:MetaApi:Token is required when MetaApi is enabled.");

            if (string.IsNullOrWhiteSpace(options.MetaApi.AccountId))
                errors.Add("Bot:MetaApi:AccountId is required when MetaApi is enabled.");

            if (!Uri.TryCreate(options.MetaApi.TradingApiBaseUrl, UriKind.Absolute, out _))
                errors.Add("Bot:MetaApi:TradingApiBaseUrl must be a valid absolute URL.");

            if (!Uri.TryCreate(options.MetaApi.MarketDataApiBaseUrl, UriKind.Absolute, out _))
                errors.Add("Bot:MetaApi:MarketDataApiBaseUrl must be a valid absolute URL.");
        }

        if (string.IsNullOrWhiteSpace(options.Strategy.EntryTimeframe))
            errors.Add("Bot:Strategy:EntryTimeframe is required.");

        if (options.Telegram.Enabled)
        {
            if (!Uri.TryCreate(options.Telegram.BaseUrl, UriKind.Absolute, out _))
                errors.Add("Bot:Telegram:BaseUrl must be a valid absolute URL.");

            if (string.IsNullOrWhiteSpace(options.Telegram.BotToken))
                errors.Add("Bot:Telegram:BotToken is required when Telegram is enabled.");

            if (string.IsNullOrWhiteSpace(options.Telegram.ChatId))
                errors.Add("Bot:Telegram:ChatId is required when Telegram is enabled.");
        }

        if (options.TradingSessions is null || options.TradingSessions.Count == 0)
            errors.Add("At least one trading session must be configured.");

        var seenSessionNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var session in options.TradingSessions)
        {
            if (string.IsNullOrWhiteSpace(session.Name))
                errors.Add("Each trading session must have a Name.");

            if (!seenSessionNames.Add(session.Name))
                errors.Add($"Duplicate session name found: '{session.Name}'.");

            if (session.StartTime == session.EndTime)
                errors.Add($"Session '{session.Name}' cannot have the same StartTime and EndTime.");

            if (session.OrbMinutes <= 0)
                errors.Add($"Session '{session.Name}' must have OrbMinutes greater than 0.");

            if (session.Instruments is null || session.Instruments.Count == 0)
                errors.Add($"Session '{session.Name}' must contain at least one instrument.");

            var seenInstruments = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var instrument in session.Instruments)
            {
                if (string.IsNullOrWhiteSpace(instrument))
                    errors.Add($"Session '{session.Name}' contains an empty instrument value.");

                if (!seenInstruments.Add(instrument))
                    errors.Add($"Session '{session.Name}' contains duplicate instrument '{instrument}'.");
            }
        }

        if (options.Risk.RiskPerTradePercent <= 0)
            errors.Add("Bot:Risk:RiskPerTradePercent must be greater than 0.");

        if (options.Risk.RewardRiskRatio <= 0)
            errors.Add("Bot:Risk:RewardRiskRatio must be greater than 0.");

        if (options.Risk.MaxTradesPerSession <= 0)
            errors.Add("Bot:Risk:MaxTradesPerSession must be greater than 0.");

        if (options.Risk.MaxTradesPerDay <= 0)
            errors.Add("Bot:Risk:MaxTradesPerDay must be greater than 0.");

        if (options.Risk.DailyLossLimitPercent <= 0)
            errors.Add("Bot:Risk:DailyLossLimitPercent must be greater than 0.");

        if (options.Risk.MaxSpreadPips < 0)
            errors.Add("Bot:Risk:MaxSpreadPips cannot be negative.");

        if (options.Risk.SlippageTolerancePips < 0)
            errors.Add("Bot:Risk:SlippageTolerancePips cannot be negative.");

        if (options.Risk.StopLossBufferPips < 0)
            errors.Add("Bot:Risk:StopLossBufferPips cannot be negative.");

        if (options.Risk.SessionExit.MinutesBeforeSessionEnd < 0)
            errors.Add("Bot:Risk:SessionExit:MinutesBeforeSessionEnd cannot be negative.");

        if (options.Risk.TrailingStop.Enabled)
        {
            if (options.Risk.TrailingStop.ActivationR < 0)
                errors.Add("Bot:Risk:TrailingStop:ActivationR cannot be negative.");

            if (options.Risk.TrailingStop.DistanceR <= 0)
                errors.Add("Bot:Risk:TrailingStop:DistanceR must be greater than 0.");

            if (options.Risk.TrailingStop.LockInR < 0)
                errors.Add("Bot:Risk:TrailingStop:LockInR cannot be negative.");

            if (options.Risk.TrailingStop.MinimumSecondsBetweenUpdates < 0)
                errors.Add("Bot:Risk:TrailingStop:MinimumSecondsBetweenUpdates cannot be negative.");
        }

        if (options.Strategy.Breakout.CloseBufferPips < 0)
            errors.Add("Bot:Strategy:Breakout:CloseBufferPips cannot be negative.");

        if (options.Strategy.Breakout.MaxBreakoutCandlesAfterOrb <= 0)
            errors.Add("Bot:Strategy:Breakout:MaxBreakoutCandlesAfterOrb must be greater than 0.");

        if (options.Strategy.Retest.TolerancePips < 0)
            errors.Add("Bot:Strategy:Retest:TolerancePips cannot be negative.");

        if (options.Strategy.Retest.MaxCandlesAfterBreakout <= 0)
            errors.Add("Bot:Strategy:Retest:MaxCandlesAfterBreakout must be greater than 0.");

        if (options.Strategy.Fibonacci.ZoneTolerancePips < 0)
            errors.Add("Bot:Strategy:Fibonacci:ZoneTolerancePips cannot be negative.");

        if (options.Strategy.Fibonacci.MaxCandlesAfterBreakout <= 0)
            errors.Add("Bot:Strategy:Fibonacci:MaxCandlesAfterBreakout must be greater than 0.");

        var fibRequired = options.Strategy.EntryMode is EntryMode.BreakoutFibonacci or EntryMode.BreakoutRetestFibonacci;
        if (fibRequired && !options.Strategy.Fibonacci.Enabled)
            errors.Add("Fibonacci must be enabled when EntryMode requires Fibonacci.");

        if (options.Strategy.Fibonacci.Enabled &&
            !options.Strategy.Fibonacci.UseLevel0382 &&
            !options.Strategy.Fibonacci.UseLevel0500 &&
            !options.Strategy.Fibonacci.UseLevel0618 &&
            !options.Strategy.Fibonacci.UseLevel0786)
        {
            errors.Add("At least one Fibonacci level must be enabled when Fibonacci is enabled.");
        }

        if (options.Strategy.Indicators.Ema.Enabled)
        {
            if (string.IsNullOrWhiteSpace(options.Strategy.Indicators.Ema.Timeframe))
                errors.Add("Bot:Strategy:Indicators:Ema:Timeframe is required.");

            if (options.Strategy.Indicators.Ema.FastPeriod <= 0 ||
                options.Strategy.Indicators.Ema.SlowPeriod <= 0)
            {
                errors.Add("EMA periods must be greater than 0.");
            }

            if (options.Strategy.Indicators.Ema.FastPeriod >= options.Strategy.Indicators.Ema.SlowPeriod)
            {
                errors.Add("EMA FastPeriod must be less than SlowPeriod.");
            }
        }

        if (options.Strategy.Volume.Enabled)
        {
            if (string.IsNullOrWhiteSpace(options.Strategy.Volume.Timeframe))
                errors.Add("Bot:Strategy:Volume:Timeframe is required.");

            if (options.Strategy.Volume.LookbackCandles <= 0)
                errors.Add("Bot:Strategy:Volume:LookbackCandles must be greater than 0.");

            if (options.Strategy.Volume.Multiplier <= 0)
                errors.Add("Bot:Strategy:Volume:Multiplier must be greater than 0.");

            var source = options.Strategy.Volume.VolumeSource?.Trim();
            if (!string.Equals(source, "TickVolume", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(source, "Volume", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add("Bot:Strategy:Volume:VolumeSource must be TickVolume or Volume.");
            }
        }

        if (options.Strategy.Vwap.Enabled)
        {
            if (string.IsNullOrWhiteSpace(options.Strategy.Vwap.Timeframe))
                errors.Add("Bot:Strategy:Vwap:Timeframe is required.");

            if (options.Strategy.Vwap.TolerancePips < 0)
                errors.Add("Bot:Strategy:Vwap:TolerancePips cannot be negative.");
        }

        if (options.Strategy.Indicators.Macd.Enabled)
        {
            if (string.IsNullOrWhiteSpace(options.Strategy.Indicators.Macd.Timeframe))
                errors.Add("Bot:Strategy:Indicators:Macd:Timeframe is required.");

            if (options.Strategy.Indicators.Macd.FastPeriod <= 0 ||
                options.Strategy.Indicators.Macd.SlowPeriod <= 0 ||
                options.Strategy.Indicators.Macd.SignalPeriod <= 0)
            {
                errors.Add("MACD periods must be greater than 0.");
            }

            if (options.Strategy.Indicators.Macd.FastPeriod >= options.Strategy.Indicators.Macd.SlowPeriod)
            {
                errors.Add("MACD FastPeriod must be less than SlowPeriod.");
            }
        }

        if (options.Resilience.Enabled)
        {
            if (options.Resilience.SaveIntervalSeconds <= 0)
                errors.Add("Bot:Resilience:SaveIntervalSeconds must be greater than 0.");

            if (string.IsNullOrWhiteSpace(options.Resilience.StateFilePath))
                errors.Add("Bot:Resilience:StateFilePath is required when resilience is enabled.");

            if (string.IsNullOrWhiteSpace(options.Resilience.JournalFilePath))
                errors.Add("Bot:Resilience:JournalFilePath is required when resilience is enabled.");
        }

        if (options.Backtest.Enabled)
        {
            if (options.Backtest.CandleBatchSize <= 0 || options.Backtest.CandleBatchSize > 1000)
                errors.Add("Bot:Backtest:CandleBatchSize must be between 1 and 1000.");

            if (options.Backtest.EntrySpreadPips < 0)
                errors.Add("Bot:Backtest:EntrySpreadPips cannot be negative.");

            if (options.Backtest.ExitSpreadPips < 0)
                errors.Add("Bot:Backtest:ExitSpreadPips cannot be negative.");

            if (options.Backtest.SlippagePips < 0)
                errors.Add("Bot:Backtest:SlippagePips cannot be negative.");
        }

        if (options.Strategy.Indicators.Rsi.Enabled)
        {
            if (string.IsNullOrWhiteSpace(options.Strategy.Indicators.Rsi.Timeframe))
                errors.Add("Bot:Strategy:Indicators:Rsi:Timeframe is required.");

            if (options.Strategy.Indicators.Rsi.Period <= 0)
                errors.Add("RSI period must be greater than 0.");

            if (options.Strategy.Indicators.Rsi.BuyMin < 0 || options.Strategy.Indicators.Rsi.BuyMin > 100)
                errors.Add("RSI BuyMin must be between 0 and 100.");

            if (options.Strategy.Indicators.Rsi.SellMax < 0 || options.Strategy.Indicators.Rsi.SellMax > 100)
                errors.Add("RSI SellMax must be between 0 and 100.");
        }

        if (string.IsNullOrWhiteSpace(options.ActiveStrategy))
            errors.Add("Bot:ActiveStrategy is required.");

        if (!string.Equals(options.ActiveStrategy, StrategyNames.Orb, StringComparison.OrdinalIgnoreCase) &&
    !string.Equals(options.ActiveStrategy, StrategyNames.First4HReentry, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"Bot:ActiveStrategy must be '{StrategyNames.Orb}' or '{StrategyNames.First4HReentry}'.");
        }

        if (options.First4HReentry.Enabled)
        {
            if (string.IsNullOrWhiteSpace(options.First4HReentry.RangeTimeframe))
                errors.Add("Bot:First4HReentry:RangeTimeframe is required.");

            if (string.IsNullOrWhiteSpace(options.First4HReentry.SignalTimeframe))
                errors.Add("Bot:First4HReentry:SignalTimeframe is required.");

            if (options.First4HReentry.RewardRiskRatio <= 0)
                errors.Add("Bot:First4HReentry:RewardRiskRatio must be greater than 0.");

            if (options.First4HReentry.StopBufferPips < 0)
                errors.Add("Bot:First4HReentry:StopBufferPips cannot be negative.");

            if (options.First4HReentry.MaxTradesPerInstrumentPerDay <= 0)
                errors.Add("Bot:First4HReentry:MaxTradesPerInstrumentPerDay must be greater than 0.");

            if (options.First4HReentry.PivotStrength <= 0)
                errors.Add("Bot:First4HReentry:PivotStrength must be greater than 0.");

            if (options.First4HReentry.KeyLevelLookbackBars <= 0)
                errors.Add("Bot:First4HReentry:KeyLevelLookbackBars must be greater than 0.");

            if (options.First4HReentry.MaxStopAsRangeMultiple <= 0)
                errors.Add("Bot:First4HReentry:MaxStopAsRangeMultiple must be greater than 0.");

            if (options.First4HReentry.MaxStopDistancePips < 0)
                errors.Add("Bot:First4HReentry:MaxStopDistancePips cannot be negative.");

            if (options.First4HReentry.Profiles is null || options.First4HReentry.Profiles.Count == 0)
                errors.Add("Bot:First4HReentry:Profiles must contain at least one profile.");

            foreach (var profile in options.First4HReentry.Profiles)
            {
                if (string.IsNullOrWhiteSpace(profile.Name))
                    errors.Add("Bot:First4HReentry:Profiles:Name is required.");

                if (string.IsNullOrWhiteSpace(profile.AnchorTimeZoneId))
                    errors.Add($"Bot:First4HReentry profile '{profile.Name}' must have AnchorTimeZoneId.");

                if (profile.Instruments is null || profile.Instruments.Count == 0)
                    errors.Add($"Bot:First4HReentry profile '{profile.Name}' must contain at least one instrument.");
            }
        }

        return errors.Count > 0
            ? ValidateOptionsResult.Fail(errors)
            : ValidateOptionsResult.Success;
    }
}