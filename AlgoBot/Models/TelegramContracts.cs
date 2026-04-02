using System.Text.Json.Serialization;

namespace AlgoBot.Models;

internal sealed class TelegramApiResponse<T>
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("result")]
    public T? Result { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("error_code")]
    public int? ErrorCode { get; set; }

    [JsonPropertyName("parameters")]
    public TelegramResponseParameters? Parameters { get; set; }
}

internal sealed class TelegramResponseParameters
{
    [JsonPropertyName("retry_after")]
    public int? RetryAfter { get; set; }

    [JsonPropertyName("migrate_to_chat_id")]
    public long? MigrateToChatId { get; set; }
}

internal sealed class TelegramUser
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("is_bot")]
    public bool IsBot { get; set; }

    [JsonPropertyName("first_name")]
    public string FirstName { get; set; } = string.Empty;

    [JsonPropertyName("last_name")]
    public string? LastName { get; set; }

    [JsonPropertyName("username")]
    public string? Username { get; set; }

    [JsonPropertyName("can_join_groups")]
    public bool? CanJoinGroups { get; set; }

    [JsonPropertyName("can_read_all_group_messages")]
    public bool? CanReadAllGroupMessages { get; set; }

    [JsonPropertyName("supports_inline_queries")]
    public bool? SupportsInlineQueries { get; set; }
}

internal sealed class TelegramMessage
{
    [JsonPropertyName("message_id")]
    public long MessageId { get; set; }

    [JsonPropertyName("date")]
    public long Date { get; set; }
}

internal sealed class TelegramSendMessageRequest
{
    [JsonPropertyName("chat_id")]
    public string ChatId { get; set; } = string.Empty;

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("parse_mode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ParseMode { get; set; }

    [JsonPropertyName("disable_notification")]
    public bool DisableNotification { get; set; }

    [JsonPropertyName("protect_content")]
    public bool ProtectContent { get; set; } = false;
}