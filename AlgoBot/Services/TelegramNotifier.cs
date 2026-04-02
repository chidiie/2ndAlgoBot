using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AlgoBot.Configuration;
using AlgoBot.Interfaces;
using AlgoBot.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AlgoBot.Services;

public sealed class TelegramNotifier : INotificationService
{
    private const string TelegramClientName = "TelegramBotApi";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<BotSettings> _botSettings;
    private readonly ILogger<TelegramNotifier> _logger;
    private readonly SemaphoreSlim _initializationLock = new(1, 1);

    private volatile bool _validated;
    private string? _botDisplayName;

    public TelegramNotifier(
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<BotSettings> botSettings,
        ILogger<TelegramNotifier> logger)
    {
        _httpClientFactory = httpClientFactory;
        _botSettings = botSettings;
        _logger = logger;
    }

    public async Task SendInfoAsync(string message, CancellationToken cancellationToken = default)
    {
        var settings = _botSettings.CurrentValue.Telegram;
        if (!settings.Enabled)
        {
            _logger.LogDebug("Telegram disabled. Info notification skipped.");
            return;
        }

        var formatted = FormatMessage("INFO", message);
        await SendMessageInternalAsync(formatted, cancellationToken);
    }

    public async Task SendErrorAsync(string message, CancellationToken cancellationToken = default)
    {
        var settings = _botSettings.CurrentValue.Telegram;
        if (!settings.Enabled)
        {
            _logger.LogDebug("Telegram disabled. Error notification skipped.");
            return;
        }

        var formatted = FormatMessage("ERROR", message);
        await SendMessageInternalAsync(formatted, cancellationToken);
    }

    private async Task SendMessageInternalAsync(string message, CancellationToken cancellationToken)
    {
        var botSettings = _botSettings.CurrentValue;
        var telegram = botSettings.Telegram;

        if (!telegram.Enabled)
            return;

        if (string.IsNullOrWhiteSpace(telegram.BotToken))
            throw new InvalidOperationException("Telegram is enabled but BotToken is missing.");

        if (string.IsNullOrWhiteSpace(telegram.ChatId))
            throw new InvalidOperationException("Telegram is enabled but ChatId is missing.");

        await EnsureValidatedAsync(cancellationToken);

        var request = new TelegramSendMessageRequest
        {
            ChatId = telegram.ChatId,
            Text = TrimMessage(message, 4096),
            ParseMode = string.IsNullOrWhiteSpace(telegram.ParseMode) ? null : telegram.ParseMode,
            DisableNotification = telegram.DisableNotification
        };

        var client = _httpClientFactory.CreateClient(TelegramClientName);
        var path = $"/bot{telegram.BotToken}/sendMessage";

        await SendWithRetryAsync<TelegramMessage>(
            client,
            path,
            request,
            cancellationToken);

        _logger.LogDebug("Telegram notification sent successfully.");
    }

    private async Task EnsureValidatedAsync(CancellationToken cancellationToken)
    {
        if (_validated)
            return;

        await _initializationLock.WaitAsync(cancellationToken);
        try
        {
            if (_validated)
                return;

            var settings = _botSettings.CurrentValue.Telegram;
            var client = _httpClientFactory.CreateClient(TelegramClientName);
            var path = $"/bot{settings.BotToken}/getMe";

            var response = await SendWithRetryAsync<TelegramUser>(
                client,
                path,
                body: null,
                cancellationToken: cancellationToken,
                useGet: true);

            _botDisplayName = !string.IsNullOrWhiteSpace(response.Username)
                ? $"@{response.Username}"
                : response.FirstName;

            _validated = true;

            _logger.LogInformation(
                "Telegram bot validated successfully. Bot={BotDisplayName}",
                _botDisplayName);
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    private async Task<T> SendWithRetryAsync<T>(
        HttpClient client,
        string path,
        object? body,
        CancellationToken cancellationToken,
        bool useGet = false)
    {
        var maxRetries = Math.Max(0, _botSettings.CurrentValue.Telegram.MaxRetryAttempts);

        for (var attempt = 0; ; attempt++)
        {
            using var request = new HttpRequestMessage(
                useGet ? HttpMethod.Get : HttpMethod.Post,
                path);

            if (!useGet && body is not null)
            {
                request.Content = JsonContent.Create(body);
            }

            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            var payload = await response.Content.ReadAsStringAsync(cancellationToken);

            TelegramApiResponse<T>? telegramResponse = null;
            if (!string.IsNullOrWhiteSpace(payload))
            {
                try
                {
                    telegramResponse = JsonSerializer.Deserialize<TelegramApiResponse<T>>(payload, JsonOptions);
                }
                catch (JsonException ex)
                {
                    _logger.LogError(ex, "Failed to deserialize Telegram response. Payload={Payload}", payload);
                }
            }

            if (response.IsSuccessStatusCode && telegramResponse?.Ok == true && telegramResponse.Result is not null)
            {
                return telegramResponse.Result;
            }

            var retryAfterSeconds = telegramResponse?.Parameters?.RetryAfter;
            if ((response.StatusCode == HttpStatusCode.TooManyRequests || retryAfterSeconds.HasValue) &&
                attempt < maxRetries)
            {
                var delaySeconds = Math.Max(1, retryAfterSeconds ?? 2);

                _logger.LogWarning(
                    "Telegram flood control hit. Attempt {Attempt}/{MaxAttempts}. Waiting {DelaySeconds}s.",
                    attempt + 1,
                    maxRetries + 1,
                    delaySeconds);

                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
                continue;
            }

            var description = telegramResponse?.Description
                              ?? $"Telegram API request failed with status {(int)response.StatusCode} ({response.StatusCode}).";

            throw new InvalidOperationException(description);
        }
    }

    private string FormatMessage(string level, string message)
    {
        var bot = _botSettings.CurrentValue;
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm:ss 'UTC'");

        return
$@"[{level}] {bot.InstanceName}
Time: {timestamp}
{message}";
    }

    private static string TrimMessage(string message, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(message))
            return "(empty message)";

        if (message.Length <= maxLength)
            return message;

        return message[..(maxLength - 3)] + "...";
    }
}