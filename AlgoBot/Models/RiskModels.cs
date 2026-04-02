namespace AlgoBot.Models;

public sealed class PreparedTradePlan
{
    public string SessionName { get; set; } = string.Empty;
    public string Instrument { get; set; } = string.Empty;
    public TradeDirection Direction { get; set; } = TradeDirection.None;

    public decimal EntryPrice { get; set; }
    public decimal StopLoss { get; set; }
    public decimal TakeProfit { get; set; }
    public decimal Quantity { get; set; }

    public decimal RiskAmount { get; set; }
    public decimal RewardRiskRatio { get; set; }
    public decimal StopDistancePrice { get; set; }
    public decimal StopDistancePips { get; set; }
    public decimal SpreadPips { get; set; }

    public string StopLossModeUsed { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

public sealed class RiskEvaluationResult
{
    public bool Approved { get; init; }
    public string Reason { get; init; } = string.Empty;
    public PreparedTradePlan? TradePlan { get; init; }
    public List<string> PassedChecks { get; init; } = new();
    public List<string> FailedChecks { get; init; } = new();

    public static RiskEvaluationResult Success(
        PreparedTradePlan tradePlan,
        IEnumerable<string> passedChecks,
        string reason) =>
        new()
        {
            Approved = true,
            TradePlan = tradePlan,
            PassedChecks = passedChecks.ToList(),
            Reason = reason
        };

    public static RiskEvaluationResult Fail(
        IEnumerable<string> passedChecks,
        IEnumerable<string> failedChecks,
        string reason) =>
        new()
        {
            Approved = false,
            PassedChecks = passedChecks.ToList(),
            FailedChecks = failedChecks.ToList(),
            Reason = reason
        };
}