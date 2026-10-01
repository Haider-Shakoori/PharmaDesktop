using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Authentication;
using System.Text.Json;
using System.Text.Json.Serialization;
using BusinessOS.Pharmacy.Application.Abstractions.Sync;

namespace BusinessOS.Pharmacy.Sync;

public sealed class CloudSyncClient : ICloudSyncTransport, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly CloudSyncOptions _options;
    private readonly bool _ownsClient;

    public CloudSyncClient(
        CloudSyncOptions options,
        HttpMessageHandler? handler = null)
    {
        options.Validate();
        _options = options;
        _ownsClient = true;
        _httpClient = handler is null
            ? new HttpClient(CreateDefaultHandler(options), disposeHandler: true)
            : new HttpClient(handler, disposeHandler: true);
        _httpClient.BaseAddress = new Uri(options.BaseUrl, UriKind.Absolute);
        _httpClient.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Darmaltoon-Pharmacy-Desktop/1.0");
    }

    public CloudSyncClient(
        HttpClient httpClient,
        CloudSyncOptions options)
    {
        options.Validate();
        _options = options;
        _httpClient = httpClient;
        _ownsClient = false;

        if (_httpClient.BaseAddress is null)
            _httpClient.BaseAddress = new Uri(options.BaseUrl, UriKind.Absolute);
    }

    private static SocketsHttpHandler CreateDefaultHandler(CloudSyncOptions options) =>
        new()
        {
            AllowAutoRedirect = false,
            AutomaticDecompression =
                DecompressionMethods.GZip |
                DecompressionMethods.Deflate |
                DecompressionMethods.Brotli,
            ConnectTimeout = TimeSpan.FromSeconds(Math.Min(5, options.TimeoutSeconds)),
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
            MaxConnectionsPerServer = 8,
            EnableMultipleHttp2Connections = true,
            SslOptions = new()
            {
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            },
        };

    public async Task<IReadOnlyList<CloudSyncPushAcknowledgement>> PushAsync(
        string accessToken,
        IReadOnlyList<CloudSyncOutboxItem> events,
        CancellationToken cancellationToken = default)
    {
        if (events.Count == 0)
            return [];

        var payloadEvents = new List<PushEvent>(events.Count);
        foreach (var item in events)
        {
            using var document = JsonDocument.Parse(item.PayloadJson);
            payloadEvents.Add(new PushEvent(
                item.IdempotencyKey,
                item.EventType,
                document.RootElement.Clone()));
        }

        using var request = CreateRequest(
            HttpMethod.Post,
            _options.PushPath,
            accessToken);
        request.Content = JsonContent.Create(
            new PushRequest(payloadEvents),
            options: JsonOptions);

        using var response = await SendAsync(request, cancellationToken);
        var envelope = await response.Content.ReadFromJsonAsync<PushEnvelope>(
            JsonOptions,
            cancellationToken)
            ?? throw new CloudSyncTransportException(
                "The BusinessOS sync server returned an empty push response.",
                retryable: true);

        return envelope.Data.Results
            .Select(x => new CloudSyncPushAcknowledgement(
                x.IdempotencyKey,
                x.Status,
                x.Code,
                x.Message,
                x.Retryable,
                x.ServerId,
                x.ServerUpdatedAt))
            .ToList();
    }

    public async Task<CloudSyncPullPage> PullAsync(
        string accessToken,
        string stream,
        string? cursor,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stream);
        limit = Math.Clamp(limit, 1, 250);

        var path = $"{_options.PullPath.TrimEnd('/')}/{Uri.EscapeDataString(stream)}?limit={limit}";
        if (!string.IsNullOrWhiteSpace(cursor))
            path += "&cursor=" + Uri.EscapeDataString(cursor);

        using var request = CreateRequest(
            HttpMethod.Get,
            path,
            accessToken);

        using var response = await SendAsync(request, cancellationToken);
        var envelope = await response.Content.ReadFromJsonAsync<PullEnvelope>(
            JsonOptions,
            cancellationToken)
            ?? throw new CloudSyncTransportException(
                "The BusinessOS sync server returned an empty pull response.",
                retryable: true);

        if (!string.Equals(
                envelope.Data.Stream,
                stream,
                StringComparison.OrdinalIgnoreCase))
            throw new CloudSyncTransportException(
                "The BusinessOS sync server returned the wrong synchronization stream.",
                retryable: false);

        var records = new List<CloudSyncRemoteRecord>(
            envelope.Data.Data.Count);

        foreach (var item in envelope.Data.Data)
        {
            if (!item.TryGetProperty("id", out var idElement))
                throw new CloudSyncTransportException(
                    $"Cloud sync stream '{stream}' returned a record without an id.",
                    retryable: false);

            var serverId = idElement.ValueKind switch
            {
                JsonValueKind.String => idElement.GetString(),
                JsonValueKind.Number => idElement.GetRawText(),
                _ => null,
            };

            if (string.IsNullOrWhiteSpace(serverId))
                throw new CloudSyncTransportException(
                    $"Cloud sync stream '{stream}' returned an invalid record id.",
                    retryable: false);

            DateTimeOffset? updatedAt = null;
            if (item.TryGetProperty("server_updated_at", out var updatedElement) &&
                updatedElement.ValueKind == JsonValueKind.String &&
                DateTimeOffset.TryParse(updatedElement.GetString(), out var parsed))
                updatedAt = parsed;

            records.Add(new CloudSyncRemoteRecord(
                serverId,
                item.GetRawText(),
                updatedAt));
        }

        return new CloudSyncPullPage(
            envelope.Data.Stream,
            records,
            envelope.Data.NextCursor,
            envelope.Data.HasMore,
            envelope.ServerTime);
    }

    private HttpRequestMessage CreateRequest(
        HttpMethod method,
        string path,
        string accessToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
            throw new CloudSyncAuthorizationException(
                "A protected desktop access token is required for cloud synchronization.");

        var request = new HttpRequestMessage(method, path);
        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (response.StatusCode is HttpStatusCode.Unauthorized or
                HttpStatusCode.Forbidden)
            {
                var message = await ReadErrorAsync(response, cancellationToken);
                response.Dispose();
                throw new CloudSyncAuthorizationException(
                    message ?? "The BusinessOS desktop synchronization session is no longer authorized.");
            }

            if (!response.IsSuccessStatusCode)
            {
                var message = await ReadErrorAsync(response, cancellationToken);
                var retryable =
                    response.StatusCode == HttpStatusCode.RequestTimeout ||
                    (int)response.StatusCode == 429 ||
                    (int)response.StatusCode >= 500;
                var statusCode = (int)response.StatusCode;
                response.Dispose();
                throw new CloudSyncTransportException(
                    message ??
                    $"BusinessOS cloud synchronization failed with HTTP {statusCode}.",
                    retryable);
            }

            return response;
        }
        catch (CloudSyncAuthorizationException)
        {
            throw;
        }
        catch (CloudSyncTransportException)
        {
            throw;
        }
        catch (OperationCanceledException exception)
            when (!cancellationToken.IsCancellationRequested)
        {
            throw new CloudSyncTransportException(
                "BusinessOS cloud synchronization timed out.",
                retryable: true,
                exception);
        }
        catch (HttpRequestException exception)
        {
            throw new CloudSyncTransportException(
                "BusinessOS cloud synchronization is temporarily unreachable.",
                retryable: true,
                exception);
        }
    }

    private static async Task<string?> ReadErrorAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            using var document = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(cancellationToken));

            if (document.RootElement.TryGetProperty(
                    "message",
                    out var message) &&
                message.ValueKind == JsonValueKind.String)
                return message.GetString();
        }
        catch (JsonException)
        {
        }

        return null;
    }

    public void Dispose()
    {
        if (_ownsClient)
            _httpClient.Dispose();
    }

    private sealed record PushRequest(
        [property: JsonPropertyName("events")]
        IReadOnlyList<PushEvent> Events);

    private sealed record PushEvent(
        [property: JsonPropertyName("idempotency_key")]
        string IdempotencyKey,
        [property: JsonPropertyName("event_type")]
        string EventType,
        [property: JsonPropertyName("payload")]
        JsonElement Payload);

    private sealed record PushEnvelope(
        [property: JsonPropertyName("data")]
        PushData Data,
        [property: JsonPropertyName("server_time")]
        DateTimeOffset ServerTime);

    private sealed record PushData(
        [property: JsonPropertyName("results")]
        IReadOnlyList<PushResult> Results);

    private sealed record PushResult(
        [property: JsonPropertyName("idempotency_key")]
        string IdempotencyKey,
        [property: JsonPropertyName("status")]
        string Status,
        [property: JsonPropertyName("code")]
        string? Code,
        [property: JsonPropertyName("message")]
        string? Message,
        [property: JsonPropertyName("retryable")]
        bool Retryable,
        [property: JsonPropertyName("server_id")]
        string? ServerId,
        [property: JsonPropertyName("server_updated_at")]
        DateTimeOffset? ServerUpdatedAt);

    private sealed record PullEnvelope(
        [property: JsonPropertyName("data")]
        PullData Data,
        [property: JsonPropertyName("server_time")]
        DateTimeOffset ServerTime);

    private sealed record PullData(
        [property: JsonPropertyName("stream")]
        string Stream,
        [property: JsonPropertyName("data")]
        IReadOnlyList<JsonElement> Data,
        [property: JsonPropertyName("next_cursor")]
        string? NextCursor,
        [property: JsonPropertyName("has_more")]
        bool HasMore);
}
