using System.Net;

namespace AlgoBot.Services;

public sealed class MetaApiException : Exception
{
    public MetaApiException(
        string message,
        HttpStatusCode statusCode,
        string? errorCode = null,
        string? responseBody = null,
        DateTimeOffset? recommendedRetryTimeUtc = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
        ResponseBody = responseBody;
        RecommendedRetryTimeUtc = recommendedRetryTimeUtc;
    }

    public HttpStatusCode StatusCode { get; }
    public string? ErrorCode { get; }
    public string? ResponseBody { get; }
    public DateTimeOffset? RecommendedRetryTimeUtc { get; }
}