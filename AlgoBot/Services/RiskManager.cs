using System.Collections.Concurrent;
using AlgoBot.Configuration;
using AlgoBot.Helpers;
using AlgoBot.Interfaces;
using AlgoBot.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AlgoBot.Services;

public sealed class RiskManager : IRiskManager
{
    private readonly IMarketDataProvider _marketDataProvider;
    private readonly ISessionStateStore _sessionStateStore;
    private readonly IOptionsMonitor<BotSettings> _botSettings;
    private readonly ILogger<RiskManager> _logger;

    private readonly ConcurrentDictionary<string, SymbolSpecification> _specCache =
        new(StringComparer.OrdinalIgnoreCase);

    public RiskManager(
        IMarketDataProvider marketDataProvider,
        ISessionStateStore sessionStateStore,
        IOptionsMonitor<BotSettings> botSettings,
        ILogger<RiskManager> logger)
    {
        _marketDataProvider = marketDataProvider;
        _sessionStateStore = sessionStateStore;
        _botSettings = botSettings;
        _logger = logger;
    }

    public async Task<RiskEvaluationResult> EvaluateAsync(
        TradingSessionSettings session,
        SessionState sessionState,
        InstrumentState instrumentState,
        TradeSignal signal,
        CancellationToken cancellationToken = default)
    {
        var passed = new List<string>();
        var failed = new List<string>();
        var riskSettings = _botSettings.CurrentValue.Risk;

        if (!signal.ShouldTrade || signal.Direction == TradeDirection.None)
        {
            failed.Add("Signal");
            return RiskEvaluationResult.Fail(
                passed,
                failed,
                "Signal is not trade-ready.");
        }

        var account = await _marketDataProvider.GetAccountInformationAsync(cancellationToken);
        if (account is null)
        {
            failed.Add("AccountInfo");
            return RiskEvaluationResult.Fail(
                passed,
                failed,
                "Could not retrieve account information.");
        }

        if (!account.TradeAllowed)
        {
            failed.Add("TradeAllowed");
            return RiskEvaluationResult.Fail(
                passed,
                failed,
                "Trading is not allowed on the account.");
        }

        passed.Add("TradeAllowed");

        //if (sessionState.TradesTaken >= riskSettings.MaxTradesPerSession)
        //{
        //    failed.Add("MaxTradesPerSession");
        //    return RiskEvaluationResult.Fail(
        //        passed,
        //        failed,
        //        $"Max trades per session reached for {session.Name}.");
        //}

        //passed.Add("MaxTradesPerSession");

        var isFirst4HSession = session.Name.StartsWith("First4H:", StringComparison.OrdinalIgnoreCase);

        if (!isFirst4HSession)
        {
            if (sessionState.TradesTaken >= riskSettings.MaxTradesPerSession)
            {
                failed.Add("MaxTradesPerSession");
                return RiskEvaluationResult.Fail(
                    passed,
                    failed,
                    $"Max trades per session reached for {session.Name}.");
            }

            passed.Add("MaxTradesPerSession");
        }
        else
        {
            passed.Add("MaxTradesPerSession");
        }

        var tradesToday = _sessionStateStore.GetAll()
            .Where(s => s.TradingDay == sessionState.TradingDay)
            .Sum(s => s.TradesTaken);

        if (tradesToday >= riskSettings.MaxTradesPerDay)
        {
            failed.Add("MaxTradesPerDay");
            return RiskEvaluationResult.Fail(
                passed,
                failed,
                "Max trades per day reached.");
        }

        passed.Add("MaxTradesPerDay");

        var equityDrawdownPercent = CalculateEquityDrawdownPercent(account);
        if (equityDrawdownPercent >= riskSettings.DailyLossLimitPercent)
        {
            failed.Add("DailyLossLimit");
            sessionState.DailyLossLimitReached = true;

            return RiskEvaluationResult.Fail(
                passed,
                failed,
                $"Daily loss guard hit. Equity drawdown is {equityDrawdownPercent:F2}%.");
        }

        passed.Add("DailyLossLimit");

        var quote = await _marketDataProvider.GetQuoteAsync(instrumentState.Instrument, cancellationToken);
        if (quote is null)
        {
            failed.Add("Quote");
            return RiskEvaluationResult.Fail(
                passed,
                failed,
                $"Could not retrieve quote for {instrumentState.Instrument}.");
        }

        var spec = await GetSpecificationAsync(instrumentState.Instrument, cancellationToken);

        var spreadPips = PriceHelper.PriceDistanceToPips(spec, quote.Spread);
        //if (spreadPips > riskSettings.MaxSpreadPips)
        //{
        //    failed.Add("Spread");
        //    return RiskEvaluationResult.Fail(
        //        passed,
        //        failed,
        //        $"Spread too wide. Current spread is {spreadPips:F2} pips.");
        //}

        //passed.Add("Spread");

        var entryPrice = signal.Direction == TradeDirection.Buy ? quote.Ask : quote.Bid;

        var stopLossResult = BuildStopLoss(
            signal.Direction,
            instrumentState,
            spec,
            riskSettings);

        if (!stopLossResult.Successful || !stopLossResult.StopLossPrice.HasValue)
        {
            failed.Add("StopLoss");
            return RiskEvaluationResult.Fail(
                passed,
                failed,
                stopLossResult.Reason);
        }

        var stopLoss = stopLossResult.StopLossPrice.Value;

        if (signal.Direction == TradeDirection.Buy && stopLoss >= entryPrice)
        {
            failed.Add("StopLoss");
            return RiskEvaluationResult.Fail(
                passed,
                failed,
                $"Buy stop loss {stopLoss} must be below entry price {entryPrice}.");
        }

        if (signal.Direction == TradeDirection.Sell && stopLoss <= entryPrice)
        {
            failed.Add("StopLoss");
            return RiskEvaluationResult.Fail(
                passed,
                failed,
                $"Sell stop loss {stopLoss} must be above entry price {entryPrice}.");
        }

        passed.Add("StopLoss");

        var stopDistancePrice = Math.Abs(entryPrice - stopLoss);
        if (stopDistancePrice <= 0)
        {
            failed.Add("StopDistance");
            return RiskEvaluationResult.Fail(
                passed,
                failed,
                "Stop distance cannot be zero.");
        }

        var stopDistancePips = PriceHelper.PriceDistanceToPips(spec, stopDistancePrice);

        var capitalBase = Math.Min(account.Balance, account.Equity);
        var riskAmount = capitalBase * (riskSettings.RiskPerTradePercent / 100m);

        if (riskAmount <= 0)
        {
            failed.Add("RiskAmount");
            return RiskEvaluationResult.Fail(
                passed,
                failed,
                "Calculated risk amount is zero or negative.");
        }

        var tickSize = spec.TickSize > 0 ? spec.TickSize : spec.Point;
        var tickValue = quote.LossTickValue ?? quote.ProfitTickValue;

        if (tickSize <= 0 || !tickValue.HasValue || tickValue.Value <= 0)
        {
            failed.Add("TickValue");
            return RiskEvaluationResult.Fail(
                passed,
                failed,
                "Cannot calculate lot size because tickSize or tickValue is invalid.");
        }

        var lossPerLot = (stopDistancePrice / tickSize) * tickValue.Value;

        if (lossPerLot <= 0)
        {
            failed.Add("LotSizing");
            return RiskEvaluationResult.Fail(
                passed,
                failed,
                "Calculated loss per lot is invalid.");
        }

        var rawVolume = riskAmount / lossPerLot;
        var normalizedVolume = NormalizeVolumeDown(rawVolume, spec);

        if (rawVolume < spec.MinVolume || normalizedVolume < spec.MinVolume)
        {
            failed.Add("MinVolume");
            return RiskEvaluationResult.Fail(
                passed,
                failed,
                $"Risk amount is too small for broker minimum volume. Raw={rawVolume:F4}, Min={spec.MinVolume:F4}.");
        }

        if (normalizedVolume <= 0)
        {
            failed.Add("LotSizing");
            return RiskEvaluationResult.Fail(
                passed,
                failed,
                "Normalized volume is zero.");
        }

        passed.Add("LotSizing");

        var takeProfit = signal.Direction == TradeDirection.Buy
            ? entryPrice + (stopDistancePrice * riskSettings.RewardRiskRatio)
            : entryPrice - (stopDistancePrice * riskSettings.RewardRiskRatio);

        var plan = new PreparedTradePlan
        {
            SessionName = session.Name,
            Instrument = instrumentState.Instrument,
            Direction = signal.Direction,
            EntryPrice = entryPrice,
            StopLoss = stopLoss,
            TakeProfit = takeProfit,
            Quantity = normalizedVolume,
            RiskAmount = riskAmount,
            RewardRiskRatio = riskSettings.RewardRiskRatio,
            StopDistancePrice = stopDistancePrice,
            StopDistancePips = stopDistancePips,
            SpreadPips = spreadPips,
            StopLossModeUsed = riskSettings.StopLossMode.ToString(),
            Reason = $"Risk approved using {riskSettings.StopLossMode}."
        };

        _logger.LogInformation(
            "Risk approved | Session={SessionName} Instrument={Instrument} Direction={Direction} Qty={Qty} Entry={Entry} SL={SL} TP={TP} Risk={RiskAmount}",
            session.Name,
            instrumentState.Instrument,
            signal.Direction,
            normalizedVolume,
            entryPrice,
            stopLoss,
            takeProfit,
            riskAmount);

        return RiskEvaluationResult.Success(
            plan,
            passed,
            $"Risk approved. Qty={normalizedVolume}, SL={stopLoss}, TP={takeProfit}.");
    }

    private async Task<SymbolSpecification> GetSpecificationAsync(
        string instrument,
        CancellationToken cancellationToken)
    {
        if (_specCache.TryGetValue(instrument, out var cached))
            return cached;

        var spec = await _marketDataProvider.GetSymbolSpecificationAsync(instrument, cancellationToken);
        if (spec is null)
            throw new InvalidOperationException($"Could not retrieve symbol specification for {instrument}.");

        _specCache[instrument] = spec;
        return spec;
    }

    private StopLossResult BuildStopLoss(
        TradeDirection direction,
        InstrumentState instrumentState,
        SymbolSpecification spec,
        RiskSettings riskSettings)
    {
        var buffer = PriceHelper.PipsToPriceDistance(spec, riskSettings.StopLossBufferPips);

        return riskSettings.StopLossMode switch
        {
            StopLossMode.RangeBoundary => BuildRangeStop(direction, instrumentState, buffer),
            StopLossMode.RetestCandle => BuildRetestStop(direction, instrumentState, buffer),
            StopLossMode.FibonacciLevel => BuildFibStop(direction, instrumentState, buffer),
            _ => StopLossResult.Fail("Unsupported stop loss mode.")
        };
    }

    private static StopLossResult BuildRangeStop(
        TradeDirection direction,
        InstrumentState instrumentState,
        decimal buffer)
    {
        if (instrumentState.OrbRange is null)
            return StopLossResult.Fail("Cannot build range stop loss because ORB range is missing.");

        var stopLoss = direction switch
        {
            TradeDirection.Buy => instrumentState.OrbRange.Low - buffer,
            TradeDirection.Sell => instrumentState.OrbRange.High + buffer,
            _ => 0m
        };

        if (stopLoss <= 0)
            return StopLossResult.Fail("Invalid range-based stop loss.");

        return StopLossResult.Success(stopLoss, "Range boundary stop loss built.");
    }

    private static StopLossResult BuildRetestStop(
        TradeDirection direction,
        InstrumentState instrumentState,
        decimal buffer)
    {
        if (!instrumentState.RetestReferencePrice.HasValue)
            return StopLossResult.Fail("Retest stop loss mode requires a confirmed retest reference price.");

        var stopLoss = direction switch
        {
            TradeDirection.Buy => instrumentState.RetestReferencePrice.Value - buffer,
            TradeDirection.Sell => instrumentState.RetestReferencePrice.Value + buffer,
            _ => 0m
        };

        if (stopLoss <= 0)
            return StopLossResult.Fail("Invalid retest-based stop loss.");

        return StopLossResult.Success(stopLoss, "Retest candle stop loss built.");
    }

    private static StopLossResult BuildFibStop(
        TradeDirection direction,
        InstrumentState instrumentState,
        decimal buffer)
    {
        if (!instrumentState.FibonacciReferencePrice.HasValue)
            return StopLossResult.Fail("Fibonacci stop loss mode requires a confirmed Fibonacci reference price.");

        var stopLoss = direction switch
        {
            TradeDirection.Buy => instrumentState.FibonacciReferencePrice.Value - buffer,
            TradeDirection.Sell => instrumentState.FibonacciReferencePrice.Value + buffer,
            _ => 0m
        };

        if (stopLoss <= 0)
            return StopLossResult.Fail("Invalid Fibonacci-based stop loss.");

        return StopLossResult.Success(stopLoss, "Fibonacci level stop loss built.");
    }

    private static decimal NormalizeVolumeDown(decimal rawVolume, SymbolSpecification spec)
    {
        if (rawVolume <= 0)
            return 0m;

        var bounded = Math.Min(rawVolume, spec.MaxVolume);

        if (spec.VolumeStep <= 0)
            return bounded;

        var steps = Math.Floor(bounded / spec.VolumeStep);
        return steps * spec.VolumeStep;
    }

    private static decimal CalculateEquityDrawdownPercent(TradingAccountInfo account)
    {
        if (account.Balance <= 0)
            return 0m;

        if (account.Equity >= account.Balance)
            return 0m;

        return ((account.Balance - account.Equity) / account.Balance) * 100m;
    }

    private sealed class StopLossResult
    {
        public bool Successful { get; init; }
        public decimal? StopLossPrice { get; init; }
        public string Reason { get; init; } = string.Empty;

        public static StopLossResult Success(decimal stopLossPrice, string reason) =>
            new()
            {
                Successful = true,
                StopLossPrice = stopLossPrice,
                Reason = reason
            };

        public static StopLossResult Fail(string reason) =>
            new()
            {
                Successful = false,
                Reason = reason
            };
    }
}