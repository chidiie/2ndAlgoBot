using AlgoBot.Interfaces;
using Microsoft.Extensions.Logging;

namespace AlgoBot.Services;

public sealed class NullNotificationService : INotificationService
{
    private readonly ILogger<NullNotificationService> _logger;

    public NullNotificationService(ILogger<NullNotificationService> logger)
    {
        _logger = logger;
    }

    public Task SendInfoAsync(string message, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Notification skipped (disabled/not yet configured): {Message}", message);
        return Task.CompletedTask;
    }

    public Task SendErrorAsync(string message, CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("Error notification skipped (disabled/not yet configured): {Message}", message);
        return Task.CompletedTask;
    }
}