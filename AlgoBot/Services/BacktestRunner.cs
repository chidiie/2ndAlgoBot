using AlgoBot.Interfaces;
using AlgoBot.Models;
using Microsoft.Extensions.Logging;

namespace AlgoBot.Services;

public sealed class BacktestRunner
{
    private readonly IFirst4HBacktestEngine _first4HBacktestEngine;
    private readonly BacktestCsvExporter _csvExporter;
    private readonly ILogger<BacktestRunner> _logger;

    public BacktestRunner(
        IFirst4HBacktestEngine first4HBacktestEngine,
        BacktestCsvExporter csvExporter,
        ILogger<BacktestRunner> logger)
    {
        _first4HBacktestEngine = first4HBacktestEngine;
        _csvExporter = csvExporter;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var summary = await _first4HBacktestEngine.RunAsync(
            new BacktestRequest
            {
                StartUtc = new DateTime(2026, 2, 10, 0, 0, 0, DateTimeKind.Utc),
                EndUtc = new DateTime(2026, 3, 31, 23, 59, 59, DateTimeKind.Utc),
                StartingBalance = 200m,
                SessionsToRun = new List<string> { "NewYorkFX" },
                InstrumentsOverride = new List<string> { "EURUSD" }
            },
            cancellationToken);

        _logger.LogInformation(
            "Backtest complete | Trades={Trades} NetProfit={NetProfit} EndingBalance={EndingBalance} WinRate={WinRate:F2}%",
            summary.TotalTrades,
            summary.NetProfit,
            summary.EndingBalance,
            summary.WinRatePercent);

        var export = await _csvExporter.ExportAsync(
            summary,
            outputDirectory: "backtests",
            filePrefix: "first4h_xauusd_jan_mar",
            cancellationToken: cancellationToken);

        _logger.LogInformation("Trades CSV: {Path}", export.TradesCsvPath);
        _logger.LogInformation("Summary CSV: {Path}", export.SummaryCsvPath);
    }
}