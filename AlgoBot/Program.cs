using AlgoBot.Extensions;
using AlgoBot.Services;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddTradingBotServices(builder.Configuration);

var host = builder.Build();
using (var scope = host.Services.CreateScope())
{
    //var runner = scope.ServiceProvider.GetRequiredService<BacktestRunner>();
    //await runner.RunAsync();
}
await host.RunAsync();