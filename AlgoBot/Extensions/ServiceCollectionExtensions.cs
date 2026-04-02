using System.Net.Http.Headers;
using AlgoBot.Configuration;
using AlgoBot.Interfaces;
using AlgoBot.Services;
using AlgoBot.Strategy;
using AlgoBot.Workers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AlgoBot.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTradingBotServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton<IValidateOptions<BotSettings>, BotSettingsValidator>();

        services.AddOptions<BotSettings>()
            .Bind(configuration.GetSection(BotSettings.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddHttpClient("MetaApiTrading", (sp, client) =>
        {
            var settings = sp.GetRequiredService<IOptionsMonitor<BotSettings>>().CurrentValue.MetaApi;

            client.BaseAddress = new Uri(settings.TradingApiBaseUrl.TrimEnd('/'));
            client.Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds);

            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));

            client.DefaultRequestHeaders.Remove("auth-token");
            if (!string.IsNullOrWhiteSpace(settings.Token))
            {
                client.DefaultRequestHeaders.Add("auth-token", settings.Token);
            }
        });

        services.AddHttpClient("MetaApiMarketData", (sp, client) =>
        {
            var settings = sp.GetRequiredService<IOptionsMonitor<BotSettings>>().CurrentValue.MetaApi;

            client.BaseAddress = new Uri(settings.MarketDataApiBaseUrl.TrimEnd('/'));
            client.Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds);

            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));

            client.DefaultRequestHeaders.Remove("auth-token");
            if (!string.IsNullOrWhiteSpace(settings.Token))
            {
                client.DefaultRequestHeaders.Add("auth-token", settings.Token);
            }
        });

        services.AddHttpClient("TelegramBotApi", (sp, client) =>
        {
            var settings = sp.GetRequiredService<IOptionsMonitor<BotSettings>>().CurrentValue.Telegram;

            client.BaseAddress = new Uri(settings.BaseUrl.TrimEnd('/'));
            client.Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds);

            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));
        });

        services.AddSingleton<ISessionStateStore, SessionStateStore>();

        services.AddSingleton<MetaApiRestClient>();
        services.AddSingleton<IMarketDataProvider>(sp => sp.GetRequiredService<MetaApiRestClient>());
        services.AddSingleton<ITradeExecutor>(sp => sp.GetRequiredService<MetaApiRestClient>());

        services.AddSingleton<IIndicatorService, IndicatorService>();

        services.AddSingleton<IEntryFilter, EmaFilter>();
        services.AddSingleton<IEntryFilter, MacdFilter>();
        services.AddSingleton<IEntryFilter, RsiFilter>();


        services.AddSingleton<IOrbRangeBuilder, OrbRangeBuilder>();
        services.AddSingleton<IFibonacciRetracementService, FibonacciRetracementService>();
        services.AddSingleton<IOrbSignalEvaluator, OrbSignalEvaluator>();
        services.AddSingleton<IRiskManager, RiskManager>();
        services.AddSingleton<ITradeExecutionService, TradeExecutionService>();
        services.AddSingleton<IPositionMonitorService, PositionMonitorService>();
        services.AddSingleton<IBotStatePersistenceService, JsonBotStatePersistenceService>();
        services.AddSingleton<IBacktestEngine, BacktestEngine>();
        services.AddSingleton<IHistoricalDataProvider, MetaApiHistoricalDataProvider>();
        services.AddSingleton<IFirst4HReentryStrategyService, First4HReentryStrategyService>();
        services.AddSingleton<IFirst4HBacktestEngine, First4HBacktestEngine>();
        services.AddSingleton<BacktestCsvExporter>();
        services.AddSingleton<BacktestRunner>();
        services.Configure<First4HReentrySettings>(configuration.GetSection("Bot:First4HReentry"));


        services.AddSingleton<INotificationService, TelegramNotifier>();

        services.AddHostedService<TradingBotWorker>();

        return services;
    }
}