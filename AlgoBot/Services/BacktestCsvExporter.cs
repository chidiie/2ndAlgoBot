using System.Globalization;
using System.Text;
using AlgoBot.Models;
using Microsoft.Extensions.Logging;

namespace AlgoBot.Services;

public sealed class BacktestCsvExporter
{
    private readonly ILogger<BacktestCsvExporter> _logger;

    public BacktestCsvExporter(ILogger<BacktestCsvExporter> logger)
    {
        _logger = logger;
    }

    public async Task<BacktestExportResult> ExportAsync(
        BacktestSummary summary,
        string outputDirectory,
        string filePrefix,
        CancellationToken cancellationToken = default)
    {
        if (summary is null)
            throw new ArgumentNullException(nameof(summary));

        if (string.IsNullOrWhiteSpace(outputDirectory))
            throw new ArgumentException("Output directory is required.", nameof(outputDirectory));

        if (string.IsNullOrWhiteSpace(filePrefix))
            filePrefix = "backtest";

        Directory.CreateDirectory(outputDirectory);

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);

        var tradesPath = Path.Combine(outputDirectory, $"{filePrefix}_trades_{timestamp}.csv");
        var summaryPath = Path.Combine(outputDirectory, $"{filePrefix}_summary_{timestamp}.csv");

        await File.WriteAllTextAsync(
            tradesPath,
            BuildTradesCsv(summary),
            Encoding.UTF8,
            cancellationToken);

        await File.WriteAllTextAsync(
            summaryPath,
            BuildSummaryCsv(summary),
            Encoding.UTF8,
            cancellationToken);

        _logger.LogInformation(
            "Backtest CSV export complete. Trades={TradesPath}, Summary={SummaryPath}",
            tradesPath,
            summaryPath);

        return new BacktestExportResult
        {
            TradesCsvPath = tradesPath,
            SummaryCsvPath = summaryPath
        };
    }

    private static string BuildTradesCsv(BacktestSummary summary)
    {
        var sb = new StringBuilder();

        sb.AppendLine(string.Join(",",
            "SessionName",
            "Instrument",
            "Direction",
            "SignalTimeUtc",
            "EntryTimeUtc",
            "ExitTimeUtc",
            "EntryPrice",
            "StopLoss",
            "TakeProfit",
            "ExitPrice",
            "Quantity",
            "RiskAmount",
            "ProfitLoss",
            "RMultiple",
            "ExitReason",
            "Notes"));

        foreach (var trade in summary.Trades.OrderBy(t => t.EntryTimeUtc))
        {
            sb.AppendLine(string.Join(",",
                Csv(trade.SessionName),
                Csv(trade.Instrument),
                Csv(trade.Direction.ToString()),
                Csv(FormatDate(trade.SignalTimeUtc)),
                Csv(FormatDate(trade.EntryTimeUtc)),
                Csv(FormatDate(trade.ExitTimeUtc)),
                Csv(FormatDecimal(trade.EntryPrice)),
                Csv(FormatDecimal(trade.StopLoss)),
                Csv(FormatDecimal(trade.TakeProfit)),
                Csv(FormatDecimal(trade.ExitPrice)),
                Csv(FormatDecimal(trade.Quantity)),
                Csv(FormatDecimal(trade.RiskAmount)),
                Csv(FormatDecimal(trade.ProfitLoss)),
                Csv(FormatDecimal(trade.RMultiple)),
                Csv(trade.ExitReason),
                Csv(trade.Notes)));
        }

        return sb.ToString();
    }

    private static string BuildSummaryCsv(BacktestSummary summary)
    {
        var sb = new StringBuilder();

        sb.AppendLine("Metric,Value");
        sb.AppendLine($"StartUtc,{Csv(FormatDate(summary.StartUtc))}");
        sb.AppendLine($"EndUtc,{Csv(FormatDate(summary.EndUtc))}");
        sb.AppendLine($"StartingBalance,{Csv(FormatDecimal(summary.StartingBalance))}");
        sb.AppendLine($"EndingBalance,{Csv(FormatDecimal(summary.EndingBalance))}");
        sb.AppendLine($"TotalTrades,{Csv(summary.TotalTrades.ToString(CultureInfo.InvariantCulture))}");
        sb.AppendLine($"Wins,{Csv(summary.Wins.ToString(CultureInfo.InvariantCulture))}");
        sb.AppendLine($"Losses,{Csv(summary.Losses.ToString(CultureInfo.InvariantCulture))}");
        sb.AppendLine($"Breakevens,{Csv(summary.Breakevens.ToString(CultureInfo.InvariantCulture))}");
        sb.AppendLine($"WinRatePercent,{Csv(FormatDecimal(summary.WinRatePercent))}");
        sb.AppendLine($"NetProfit,{Csv(FormatDecimal(summary.NetProfit))}");
        sb.AppendLine($"GrossProfit,{Csv(FormatDecimal(summary.GrossProfit))}");
        sb.AppendLine($"GrossLoss,{Csv(FormatDecimal(summary.GrossLoss))}");
        sb.AppendLine($"ProfitFactor,{Csv(FormatDecimal(summary.ProfitFactor))}");
        sb.AppendLine($"AverageWin,{Csv(FormatDecimal(summary.AverageWin))}");
        sb.AppendLine($"AverageLoss,{Csv(FormatDecimal(summary.AverageLoss))}");
        sb.AppendLine($"ExpectancyPerTrade,{Csv(FormatDecimal(summary.ExpectancyPerTrade))}");
        sb.AppendLine($"MaxDrawdownAmount,{Csv(FormatDecimal(summary.MaxDrawdownAmount))}");
        sb.AppendLine($"MaxDrawdownPercent,{Csv(FormatDecimal(summary.MaxDrawdownPercent))}");

        return sb.ToString();
    }

    private static string FormatDate(DateTime value)
    {
        return value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    }

    private static string FormatDecimal(decimal value)
    {
        return value.ToString("0.############################", CultureInfo.InvariantCulture);
    }

    private static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "\"\"";

        var escaped = value.Replace("\"", "\"\"");
        return $"\"{escaped}\"";
    }
}