using System.Text.Json.Serialization;

namespace AlgoBot.Models;

internal sealed class MetaApiAccountInformationResponse
{
    [JsonPropertyName("broker")]
    public string Broker { get; set; } = string.Empty;

    [JsonPropertyName("currency")]
    public string Currency { get; set; } = string.Empty;

    [JsonPropertyName("server")]
    public string Server { get; set; } = string.Empty;

    [JsonPropertyName("balance")]
    public decimal Balance { get; set; }

    [JsonPropertyName("equity")]
    public decimal Equity { get; set; }

    [JsonPropertyName("margin")]
    public decimal Margin { get; set; }

    [JsonPropertyName("freeMargin")]
    public decimal FreeMargin { get; set; }

    [JsonPropertyName("leverage")]
    public int Leverage { get; set; }

    [JsonPropertyName("tradeAllowed")]
    public bool TradeAllowed { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("login")]
    public long Login { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;
}

internal sealed class MetaApiSymbolPriceResponse
{
    [JsonPropertyName("symbol")]
    public string Symbol { get; set; } = string.Empty;

    [JsonPropertyName("bid")]
    public decimal Bid { get; set; }

    [JsonPropertyName("ask")]
    public decimal Ask { get; set; }

    [JsonPropertyName("profitTickValue")]
    public decimal? ProfitTickValue { get; set; }

    [JsonPropertyName("lossTickValue")]
    public decimal? LossTickValue { get; set; }

    [JsonPropertyName("time")]
    public DateTime Time { get; set; }

    [JsonPropertyName("brokerTime")]
    public string BrokerTime { get; set; } = string.Empty;
}

internal sealed class MetaApiCandleResponse
{
    [JsonPropertyName("symbol")]
    public string Symbol { get; set; } = string.Empty;

    [JsonPropertyName("timeframe")]
    public string Timeframe { get; set; } = string.Empty;

    [JsonPropertyName("time")]
    public DateTime Time { get; set; }

    [JsonPropertyName("open")]
    public decimal Open { get; set; }

    [JsonPropertyName("high")]
    public decimal High { get; set; }

    [JsonPropertyName("low")]
    public decimal Low { get; set; }

    [JsonPropertyName("close")]
    public decimal Close { get; set; }

    [JsonPropertyName("tickVolume")]
    public long TickVolume { get; set; }

    [JsonPropertyName("spread")]
    public int Spread { get; set; }

    [JsonPropertyName("volume")]
    public decimal Volume { get; set; }
}

internal sealed class MetaApiPositionResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("symbol")]
    public string Symbol { get; set; } = string.Empty;

    [JsonPropertyName("time")]
    public DateTime Time { get; set; }

    [JsonPropertyName("updateTime")]
    public DateTime? UpdateTime { get; set; }

    [JsonPropertyName("openPrice")]
    public decimal OpenPrice { get; set; }

    [JsonPropertyName("currentPrice")]
    public decimal CurrentPrice { get; set; }

    [JsonPropertyName("stopLoss")]
    public decimal? StopLoss { get; set; }

    [JsonPropertyName("takeProfit")]
    public decimal? TakeProfit { get; set; }

    [JsonPropertyName("volume")]
    public decimal Volume { get; set; }

    [JsonPropertyName("unrealizedProfit")]
    public decimal UnrealizedProfit { get; set; }

    [JsonPropertyName("realizedProfit")]
    public decimal RealizedProfit { get; set; }

    [JsonPropertyName("commission")]
    public decimal Commission { get; set; }

    [JsonPropertyName("clientId")]
    public string? ClientId { get; set; }
}

internal sealed class MetaApiSymbolSpecificationResponse
{
    [JsonPropertyName("symbol")]
    public string Symbol { get; set; } = string.Empty;

    [JsonPropertyName("tickSize")]
    public decimal TickSize { get; set; }

    [JsonPropertyName("minVolume")]
    public decimal MinVolume { get; set; }

    [JsonPropertyName("maxVolume")]
    public decimal MaxVolume { get; set; }

    [JsonPropertyName("volumeStep")]
    public decimal VolumeStep { get; set; }

    [JsonPropertyName("contractSize")]
    public decimal ContractSize { get; set; }

    [JsonPropertyName("digits")]
    public int Digits { get; set; }

    [JsonPropertyName("point")]
    public decimal Point { get; set; }

    [JsonPropertyName("pipSize")]
    public decimal? PipSize { get; set; }

    [JsonPropertyName("stopsLevel")]
    public int StopsLevel { get; set; }

    [JsonPropertyName("freezeLevel")]
    public int FreezeLevel { get; set; }

    [JsonPropertyName("executionMode")]
    public string ExecutionMode { get; set; } = string.Empty;

    [JsonPropertyName("tradeMode")]
    public string TradeMode { get; set; } = string.Empty;

    [JsonPropertyName("fillingModes")]
    public List<string> FillingModes { get; set; } = new();

    [JsonPropertyName("allowedOrderTypes")]
    public List<string> AllowedOrderTypes { get; set; } = new();
}

internal sealed class MetaApiTradeRequest
{
    [JsonPropertyName("actionType")]
    public string ActionType { get; set; } = string.Empty;

    [JsonPropertyName("symbol")]
    public string? Symbol { get; set; }

    [JsonPropertyName("volume")]
    public decimal? Volume { get; set; }

    [JsonPropertyName("stopLoss")]
    public decimal? StopLoss { get; set; }

    [JsonPropertyName("takeProfit")]
    public decimal? TakeProfit { get; set; }

    [JsonPropertyName("comment")]
    public string? Comment { get; set; }

    //[JsonPropertyName("clientId")]
    //public string? ClientId { get; set; }

    //[JsonPropertyName("magic")]
    //public long? Magic { get; set; }

    //[JsonPropertyName("slippage")]
    //public int? Slippage { get; set; }

    [JsonPropertyName("positionId")]
    public string? PositionId { get; set; }
}

internal sealed class MetaApiTradeResponse
{
    [JsonPropertyName("numericCode")]
    public int NumericCode { get; set; }

    [JsonPropertyName("stringCode")]
    public string StringCode { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("orderId")]
    public string? OrderId { get; set; }

    [JsonPropertyName("positionId")]
    public string? PositionId { get; set; }
}

internal sealed class MetaApiErrorResponse
{
    [JsonPropertyName("id")]
    public int? Id { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("metadata")]
    public MetaApiErrorMetadata? Metadata { get; set; }
}

internal sealed class MetaApiErrorMetadata
{
    [JsonPropertyName("recommendedRetryTime")]
    public DateTimeOffset? RecommendedRetryTime { get; set; }
}

internal sealed class MetaApiDealResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("orderId")]
    public string OrderId { get; set; } = string.Empty;

    [JsonPropertyName("positionId")]
    public string PositionId { get; set; } = string.Empty;

    [JsonPropertyName("clientId")]
    public string? ClientId { get; set; }

    [JsonPropertyName("symbol")]
    public string Symbol { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("entryType")]
    public string EntryType { get; set; } = string.Empty;

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = string.Empty;

    [JsonPropertyName("volume")]
    public decimal Volume { get; set; }

    [JsonPropertyName("price")]
    public decimal Price { get; set; }

    [JsonPropertyName("profit")]
    public decimal Profit { get; set; }

    [JsonPropertyName("commission")]
    public decimal Commission { get; set; }

    [JsonPropertyName("swap")]
    public decimal Swap { get; set; }

    [JsonPropertyName("time")]
    public DateTime Time { get; set; }

    [JsonPropertyName("brokerTime")]
    public string BrokerTime { get; set; } = string.Empty;
}