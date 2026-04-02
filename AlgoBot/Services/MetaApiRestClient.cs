using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AlgoBot.Configuration;
using AlgoBot.Interfaces;
using AlgoBot.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AlgoBot.Services;

public sealed class MetaApiRestClient : IMarketDataProvider, ITradeExecutor
{
    private const string TradingClientName = "MetaApiTrading";
    private const string MarketDataClientName = "MetaApiMarketData";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<BotSettings> _botOptions;
    private readonly ILogger<MetaApiRestClient> _logger;

    public MetaApiRestClient(
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<BotSettings> botOptions,
        ILogger<MetaApiRestClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _botOptions = botOptions;
        _logger = logger;
    }

    public async Task<TradingAccountInfo?> GetAccountInformationAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureMetaApiEnabled();

        var requestUri = BuildTradingUri("account-information");
        var response = await SendAsync<MetaApiAccountInformationResponse>(
            CreateTradingClient(),
            HttpMethod.Get,
            requestUri,
            cancellationToken);

        if (response is null)
            return null;

        return new TradingAccountInfo
        {
            Broker = response.Broker,
            Currency = response.Currency,
            Server = response.Server,
            Balance = response.Balance,
            Equity = response.Equity,
            Margin = response.Margin,
            FreeMargin = response.FreeMargin,
            Leverage = response.Leverage,
            TradeAllowed = response.TradeAllowed,
            Name = response.Name,
            Login = response.Login,
            Type = response.Type
        };
    }

    public async Task<SymbolSpecification?> GetSymbolSpecificationAsync(
        string instrument,
        CancellationToken cancellationToken = default)
    {
        EnsureMetaApiEnabled();
        if (!instrument.EndsWith("m"))
        {
            instrument = NormaliseSymbol(instrument);
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(instrument);

        var requestUri = BuildTradingUri($"symbols/{Uri.EscapeDataString(instrument)}/specification");
        var response = await SendAsync<MetaApiSymbolSpecificationResponse>(
            CreateTradingClient(),
            HttpMethod.Get,
            requestUri,
            cancellationToken);

        if (response is null)
            return null;

        return new SymbolSpecification
        {
            Symbol = response.Symbol,
            TickSize = response.TickSize,
            MinVolume = response.MinVolume,
            MaxVolume = response.MaxVolume,
            VolumeStep = response.VolumeStep,
            ContractSize = response.ContractSize,
            Digits = response.Digits,
            Point = response.Point,
            PipSize = response.PipSize,
            StopsLevel = response.StopsLevel,
            FreezeLevel = response.FreezeLevel,
            ExecutionMode = response.ExecutionMode,
            TradeMode = response.TradeMode,
            FillingModes = response.FillingModes,
            AllowedOrderTypes = response.AllowedOrderTypes
        };
    }

    public async Task<IReadOnlyList<Candle>> GetCandlesAsync(
        string instrument,
        string timeframe,
        int limit,
        DateTime? startTimeUtc = null,
        CancellationToken cancellationToken = default)
    {
        EnsureMetaApiEnabled();
        if (!instrument.EndsWith("m"))
        {
            instrument = NormaliseSymbol(instrument);
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(instrument);
        ArgumentException.ThrowIfNullOrWhiteSpace(timeframe);

        var normalizedLimit = Math.Clamp(limit, 1, 1000);
        var metaApiTimeframe = MapTimeframe(timeframe);
        //var normalizedSymbol = NormaliseSymbol(instrument);

        string query = string.Empty;
        
        if (startTimeUtc.HasValue)
        {
            query += $"?startTime={Uri.EscapeDataString(startTimeUtc.Value.ToUniversalTime().ToString("O"))}";
        }

        query += $"&limit={normalizedLimit}";

        var requestUri = BuildMarketDataUri(
            $"historical-market-data/symbols/{Uri.EscapeDataString(instrument)}/timeframes/{Uri.EscapeDataString(metaApiTimeframe)}/candles{query}");

        var response = await SendAsync<List<MetaApiCandleResponse>>(
            CreateMarketDataClient(),
            HttpMethod.Get,
            requestUri,
            cancellationToken);

        if (response is null || response.Count == 0)
            return Array.Empty<Candle>();

        return response
            .Select(c => new Candle
            {
                Time = DateTime.SpecifyKind(c.Time, DateTimeKind.Utc),
                Open = c.Open,
                High = c.High,
                Low = c.Low,
                Close = c.Close,
                Volume = c.Volume,
                TickVolume = c.TickVolume,
                Spread = c.Spread
            })
            .OrderBy(c => c.Time)
            .ToList();
    }

    public async Task<MarketQuote?> GetQuoteAsync(
        string instrument,
        CancellationToken cancellationToken = default)
    {
        EnsureMetaApiEnabled();
        ArgumentException.ThrowIfNullOrWhiteSpace(instrument);
        if (!instrument.EndsWith("m"))
        {
            instrument = NormaliseSymbol(instrument);
        }

        //var keepSubscription = _botOptions.CurrentValue.MetaApi.KeepSubscription
        //    ? "&keepSubscription=false"
        //    : string.Empty;

        var keepSubscription = _botOptions.CurrentValue.MetaApi.KeepSubscription
            ? "?keepSubscription=true"
            : string.Empty;

        var requestUri = BuildTradingUri(
            $"symbols/{Uri.EscapeDataString(instrument)}/current-price{keepSubscription}");

        var response = await SendAsync<MetaApiSymbolPriceResponse>(
            CreateTradingClient(),
            HttpMethod.Get,
            requestUri,
            cancellationToken);

        if (response is null)
            return null;

        return new MarketQuote
        {
            Instrument = response.Symbol,
            TimestampUtc = DateTime.SpecifyKind(response.Time, DateTimeKind.Utc),
            Bid = response.Bid,
            Ask = response.Ask,
            ProfitTickValue = response.ProfitTickValue,
            LossTickValue = response.LossTickValue,
            BrokerTime = response.BrokerTime
        };
    }

    public async Task<IReadOnlyList<PositionInfo>> GetOpenPositionsAsync(
        string? instrument = null,
        CancellationToken cancellationToken = default)
    {
        EnsureMetaApiEnabled();

        var refresh = _botOptions.CurrentValue.MetaApi.RefreshTerminalStateOnReads
            ? "?refreshTerminalState=true"
            : string.Empty;

        var requestUri = BuildTradingUri($"positions{refresh}");

        var response = await SendAsync<List<MetaApiPositionResponse>>(
            CreateTradingClient(),
            HttpMethod.Get,
            requestUri,
            cancellationToken);

        if (response is null || response.Count == 0)
            return Array.Empty<PositionInfo>();

        IEnumerable<MetaApiPositionResponse> positions = response;

        if (!string.IsNullOrWhiteSpace(instrument))
        {
            positions = positions.Where(p =>
                string.Equals(p.Symbol, instrument, StringComparison.OrdinalIgnoreCase));
        }

        return positions
            .Select(p => new PositionInfo
            {
                PositionId = p.Id,
                Instrument = p.Symbol,
                Direction = MapPositionDirection(p.Type),
                Volume = p.Volume,
                OpenPrice = p.OpenPrice,
                CurrentPrice = p.CurrentPrice,
                StopLoss = p.StopLoss,
                TakeProfit = p.TakeProfit,
                UnrealizedPnL = p.UnrealizedProfit,
                RealizedPnL = p.RealizedProfit,
                Commission = p.Commission,
                ClientId = p.ClientId,
                OpenedAtUtc = DateTime.SpecifyKind(p.Time, DateTimeKind.Utc),
                UpdatedAtUtc = p.UpdateTime.HasValue
                    ? DateTime.SpecifyKind(p.UpdateTime.Value, DateTimeKind.Utc)
                    : null
            })
            .ToList();
    }

    public async Task<TradeExecutionResult> PlaceOrderAsync(
        TradeRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureMetaApiEnabled();         
        if (!request.Instrument.EndsWith("m"))
        {
            request.Instrument = NormaliseSymbol(request.Instrument);
        }

        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Instrument);

        if (request.Direction == TradeDirection.None)
            throw new ArgumentException("Trade direction cannot be None.", nameof(request));

        if (request.Quantity <= 0)
            throw new ArgumentException("Trade quantity must be greater than 0.", nameof(request));

        var spec = await GetSymbolSpecificationAsync(request.Instrument, cancellationToken);
        if (spec is null)
            throw new InvalidOperationException($"Could not load symbol specification for {request.Instrument}.");

        var slippagePoints = ConvertPipsToPoints(spec, request.AllowedSlippagePips);
        var normalizedVolume = NormalizeVolume(request.Quantity, spec);

        //var payload = new MetaApiTradeRequest
        //{
        //    ActionType = request.Direction == TradeDirection.Buy
        //        ? "ORDER_TYPE_BUY"
        //        : "ORDER_TYPE_SELL",
        //    Symbol = request.Instrument,
        //    Volume = normalizedVolume,
        //    StopLoss = request.StopLoss > 0 ? request.StopLoss : null,
        //    TakeProfit = request.TakeProfit > 0 ? request.TakeProfit : null,
        //    Comment = string.IsNullOrWhiteSpace(request.Comment)
        //        ? request.StrategyTag
        //        : request.Comment,
        //    //ClientId = string.IsNullOrWhiteSpace(request.ClientId)
        //    //    ? BuildClientId(request)
        //    //    : request.ClientId,
        //    //Magic = request.MagicNumber,
        //    //Slippage = slippagePoints
        //};

        var payload = new
        {
            actionType = request.Direction == TradeDirection.Buy
                    ? "ORDER_TYPE_BUY"
                    : "ORDER_TYPE_SELL",
            symbol = request.Instrument,
            volume = normalizedVolume,
            stopLoss = (double)request.StopLoss,
            takeProfit = (double)request.TakeProfit,
            comment = string.IsNullOrWhiteSpace(request.Comment)
                    ? request.StrategyTag
                    : request.Comment
        };

        var requestUri = BuildTradingUri("trade");
        var response = await SendAsync<MetaApiTradeResponse>(
            CreateTradingClient(),
            HttpMethod.Post,
            requestUri,
            cancellationToken,
            payload);

        return MapTradeExecutionResult(response);
    }

    public async Task<TradeExecutionResult> ClosePositionAsync(
        string positionId,
        string? comment = null,
        CancellationToken cancellationToken = default)
    {
        EnsureMetaApiEnabled();
        ArgumentException.ThrowIfNullOrWhiteSpace(positionId);

        //var payload = new MetaApiTradeRequest
        //{
        //    ActionType = "POSITION_CLOSE_ID",
        //    PositionId = positionId,
        //    Comment = comment
        //};

        var payload = new 
        {
            actionType = "POSITION_CLOSE_ID",
            positionId = positionId,
            comment = comment
        };

        var requestUri = BuildTradingUri("trade");
        var response = await SendAsync<MetaApiTradeResponse>(
            CreateTradingClient(),
            HttpMethod.Post,
            requestUri,
            cancellationToken,
            payload);

        return MapTradeExecutionResult(response);
    }

    public async Task<TradeExecutionResult> ModifyPositionAsync(
    string positionId,
    decimal? stopLoss,
    decimal? takeProfit = null,
    CancellationToken cancellationToken = default)
    {
        EnsureMetaApiEnabled();
        ArgumentException.ThrowIfNullOrWhiteSpace(positionId);

        //var payload = new MetaApiTradeRequest
        //{
        //    ActionType = "POSITION_MODIFY",
        //    PositionId = positionId,
        //    StopLoss = stopLoss,
        //    TakeProfit = takeProfit
        //};

        var payload = new 
        {
            actionType = "POSITION_MODIFY",
            positionId = positionId,
            stopLoss = stopLoss,
            takeProfit = takeProfit
        };

        var requestUri = BuildTradingUri("trade");
        var response = await SendAsync<MetaApiTradeResponse>(
            CreateTradingClient(),
            HttpMethod.Post,
            requestUri,
            cancellationToken,
            payload);

        return MapTradeExecutionResult(response);
    }

    private HttpClient CreateTradingClient() => _httpClientFactory.CreateClient(TradingClientName);

    private HttpClient CreateMarketDataClient() => _httpClientFactory.CreateClient(MarketDataClientName);

    private string BuildTradingUri(string relativePath)
    {
        var accountId = _botOptions.CurrentValue.MetaApi.AccountId;
        return $"/users/current/accounts/{accountId}/{relativePath.TrimStart('/')}";
    }

    private string BuildMarketDataUri(string relativePath)
    {
        var accountId = _botOptions.CurrentValue.MetaApi.AccountId;
        return $"/users/current/accounts/{accountId}/{relativePath.TrimStart('/')}";
    }

    private async Task<T?> SendAsync<T>(
        HttpClient httpClient,
        HttpMethod method,
        string requestUri,
        CancellationToken cancellationToken,
        object? body = null)
    {
        var maxRetries = Math.Max(0, _botOptions.CurrentValue.MetaApi.MaxRetryAttempts);

        for (var attempt = 0; ; attempt++)
        {
            using var request = new HttpRequestMessage(method, requestUri);

            if (body is not null)
            {
                request.Content = JsonContent.Create(body);
            }

            try
            {
                using var response = await httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    if (response.Content.Headers.ContentLength == 0)
                        return default;

                    await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                    return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
                }

                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                var error = TryDeserializeError(responseBody);

                if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < maxRetries)
                {
                    var retryDelay = CalculateRetryDelay(response, error);
                    _logger.LogWarning(
                        "MetaApi rate limit hit. Attempt {Attempt}/{MaxAttempts}. Waiting {DelayMs}ms before retry. Uri={RequestUri}",
                        attempt + 1,
                        maxRetries + 1,
                        retryDelay.TotalMilliseconds,
                        requestUri);

                    await Task.Delay(retryDelay, cancellationToken);
                    continue;
                }

                //throw BuildException(response.StatusCode, requestUri, responseBody, error);
            }
            catch (MetaApiException)
            {
                throw;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (HttpRequestException ex) when (attempt < maxRetries)
            {
                var retryDelay = TimeSpan.FromSeconds(Math.Min(2 * (attempt + 1), 10));
                _logger.LogWarning(
                    ex,
                    "Transient HTTP error calling MetaApi. Attempt {Attempt}/{MaxAttempts}. Waiting {DelayMs}ms. Uri={RequestUri}",
                    attempt + 1,
                    maxRetries + 1,
                    retryDelay.TotalMilliseconds,
                    requestUri);

                await Task.Delay(retryDelay, cancellationToken);
            }
        }
    }

    private static MetaApiException BuildException(
        HttpStatusCode statusCode,
        string requestUri,
        string responseBody,
        MetaApiErrorResponse? error)
    {
        var message = error?.Message
            ?? $"MetaApi request failed with status {(int)statusCode} ({statusCode}) for '{requestUri}'.";

        return new MetaApiException(
            message: message,
            statusCode: statusCode,
            errorCode: error?.Error,
            responseBody: responseBody,
            recommendedRetryTimeUtc: error?.Metadata?.RecommendedRetryTime);
    }

    private static MetaApiErrorResponse? TryDeserializeError(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
            return null;

        try
        {
            return JsonSerializer.Deserialize<MetaApiErrorResponse>(responseBody, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static TimeSpan CalculateRetryDelay(
        HttpResponseMessage response,
        MetaApiErrorResponse? error)
    {
        if (response.Headers.TryGetValues("Retry-After", out var values))
        {
            var raw = values.FirstOrDefault();
            if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
            {
                return TimeSpan.FromSeconds(Math.Max(1, seconds));
            }
        }

        if (error?.Metadata?.RecommendedRetryTime is DateTimeOffset retryAt)
        {
            var delay = retryAt - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero)
                return delay;
        }

        return TimeSpan.FromSeconds(2);
    }

    private static TradeExecutionResult MapTradeExecutionResult(MetaApiTradeResponse? response)
    {
        if (response is null)
        {
            return new TradeExecutionResult
            {
                Success = false,
                Message = "Empty trade response received from MetaApi."
            };
        }

        var success = response.NumericCode == 10009 ||
                      string.Equals(response.StringCode, "TRADE_RETCODE_DONE", StringComparison.OrdinalIgnoreCase);

        return new TradeExecutionResult
        {
            Success = success,
            NumericCode = response.NumericCode,
            StringCode = response.StringCode,
            Message = response.Message,
            OrderId = response.OrderId,
            PositionId = response.PositionId
        };
    }

    private static decimal NormalizeVolume(decimal requestedVolume, SymbolSpecification spec)
    {
        var bounded = Math.Min(spec.MaxVolume, Math.Max(spec.MinVolume, requestedVolume));
        if (spec.VolumeStep <= 0)
            return bounded;

        var steps = Math.Floor(bounded / spec.VolumeStep);
        var normalized = steps * spec.VolumeStep;

        if (normalized < spec.MinVolume)
            normalized = spec.MinVolume;

        return normalized;
    }

    private static int ConvertPipsToPoints(SymbolSpecification spec, decimal pips)
    {
        if (pips <= 0)
            return 0;

        var pipSize = spec.PipSize.GetValueOrDefault();
        if (pipSize <= 0)
        {
            pipSize = spec.Digits switch
            {
                3 => 0.01m,
                5 => 0.0001m,
                _ => spec.Point > 0 ? spec.Point : 0.0001m
            };
        }

        var point = spec.Point > 0 ? spec.Point : spec.TickSize;
        if (point <= 0)
            point = 0.00001m;

        var priceDistance = pips * pipSize;
        var points = priceDistance / point;

        return (int)Math.Round(points, MidpointRounding.AwayFromZero);
    }

    private static string BuildClientId(TradeRequest request)
    {
        var symbol = new string(
            (request.Instrument ?? "SYM")
            .Where(char.IsLetterOrDigit)
            .Take(4)
            .ToArray())
            .ToUpperInvariant();

        var dir = request.Direction == TradeDirection.Buy ? "B" : "S";
        var suffix = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

        var clientId = $"O{symbol}{dir}{suffix}";
        return clientId.Length <= 20 ? clientId : clientId[..20];
    }

    private static TradeDirection MapPositionDirection(string metaApiType)
    {
        if (string.Equals(metaApiType, "POSITION_TYPE_BUY", StringComparison.OrdinalIgnoreCase))
            return TradeDirection.Buy;

        if (string.Equals(metaApiType, "POSITION_TYPE_SELL", StringComparison.OrdinalIgnoreCase))
            return TradeDirection.Sell;

        return TradeDirection.None;
    }

    private static string NormaliseSymbol(string instrument) =>
        instrument + "m";

    private static string MapTimeframe(string timeframe)
    {
        return timeframe.Trim().ToUpperInvariant() switch
        {
            "M1" => "1m",
            "M2" => "2m",
            "M3" => "3m",
            "M4" => "4m",
            "M5" => "5m",
            "M6" => "6m",
            "M10" => "10m",
            "M12" => "12m",
            "M15" => "15m",
            "M20" => "20m",
            "M30" => "30m",
            "H1" => "1h",
            "H2" => "2h",
            "H3" => "3h",
            "H4" => "4h",
            "H6" => "6h",
            "H8" => "8h",
            "H12" => "12h",
            "D1" => "1d",
            "W1" => "1w",
            "MN1" => "1mn",
            "1M" => "1m",
            "5M" => "5m",
            "15M" => "15m",
            _ => throw new ArgumentOutOfRangeException(nameof(timeframe), timeframe, "Unsupported timeframe.")
        };
    }

    private void EnsureMetaApiEnabled()
    {
        var settings = _botOptions.CurrentValue.MetaApi;

        if (!settings.Enabled)
            throw new InvalidOperationException("MetaApi is disabled in configuration.");

        if (string.IsNullOrWhiteSpace(settings.Token))
            throw new InvalidOperationException("MetaApi token is not configured.");

        if (string.IsNullOrWhiteSpace(settings.AccountId))
            throw new InvalidOperationException("MetaApi account id is not configured.");
    }

    public async Task<IReadOnlyList<DealInfo>> GetDealsByPositionAsync(
    string positionId,
    CancellationToken cancellationToken = default)
    {
        EnsureMetaApiEnabled();
        ArgumentException.ThrowIfNullOrWhiteSpace(positionId);

        var requestUri = BuildTradingUri($"history-deals/position/{Uri.EscapeDataString(positionId)}");

        var response = await SendAsync<List<MetaApiDealResponse>>(
            CreateTradingClient(),
            HttpMethod.Get,
            requestUri,
            cancellationToken);

        if (response is null || response.Count == 0)
            return Array.Empty<DealInfo>();

        return response
            .Select(d => new DealInfo
            {
                DealId = d.Id,
                OrderId = d.OrderId,
                PositionId = d.PositionId,
                ClientId = d.ClientId,
                Symbol = d.Symbol,
                Type = d.Type,
                EntryType = d.EntryType,
                Reason = d.Reason,
                Volume = d.Volume,
                Price = d.Price,
                Profit = d.Profit,
                Commission = d.Commission,
                Swap = d.Swap,
                TimeUtc = DateTime.SpecifyKind(d.Time, DateTimeKind.Utc),
                BrokerTime = d.BrokerTime
            })
            .OrderBy(d => d.TimeUtc)
            .ToList();
    }
}