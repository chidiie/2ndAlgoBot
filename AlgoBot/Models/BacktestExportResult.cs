namespace AlgoBot.Models;

public sealed class BacktestExportResult
{
    public string TradesCsvPath { get; set; } = string.Empty;
    public string SummaryCsvPath { get; set; } = string.Empty;
}