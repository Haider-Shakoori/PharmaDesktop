using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using BusinessOS.Pharmacy.Application.Abstractions.Sync;

namespace BusinessOS.Pharmacy.Sync;

public sealed class DesktopCloudSyncTransport : ICloudSyncTransport
{
    private const int MaxPushEventsPerRequest = 25;
    private const int MaxPullPageSize = 250;

    private static readonly string[] PullStreams =
    [
        SyncStreamCatalog.Medicines,
    ];

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly ICloudSyncSessionProvider _sessions;

    public DesktopCloudSyncTransport(
        HttpClient httpClient,
        ICloudSyncSessionProvider sessions)
    {
        _httpClient = httpClient;
        _sessions = sessions;
    }

    public async Task<IReadOnlyList<SyncPushAcknowledgement>> PushAsync(
        IReadOnlyList<SyncPushEnvelope> items,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);

        if (items.Count == 0)
        {
            return [];
        }

        var session = await _sessions.GetAsync(cancellationToken);
        var acknowledgements = new List<SyncPushAcknowledgement>(items.Count);
        var supported = new List<(SyncPushEnvelope Item, string EventType)>();

        foreach (var item in items)
        {
            var eventType = ResolveEventType(item);
            if (eventType is null)
            {
                acknowledgements.Add(new SyncPushAcknowledgement(
                    item.QueueItemId,
                    SyncPushDisposition.PermanentFailure,
                    null,
                    null,
                    "unsupported_event",
                    $"The desktop cloud API does not yet support sync stream '{item.Stream}'.",
                    null));
                continue;
            }

            supported.Add((item, eventType));
        }

        foreach (var chunk in supported.Chunk(MaxPushEventsPerRequest))
        {
            var events = chunk.Select(entry => new
            {
                idempotency_key = entry.Item.IdempotencyKey,
                event_type = entry.EventType,
                payload = ParsePayload(entry.Item.PayloadJson),
            }).ToArray();

            using var response = await SendAsync(
                session,
                HttpMethod.Post,
                "/api/v1/desktop/sync/push",
                JsonSerializer.Serialize(new { events }, JsonOptions),
                cancellationToken);

            var document = await ParseResponseAsync(response, cancellationToken);
            var resultArray = document.RootElement
                .GetProperty("data")
                .GetProperty("results");

            if (resultArray.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException(
                    "The BusinessOS desktop sync API returned an invalid push acknowledgement list.");
            }

            var byIdempotency = chunk.ToDictionary(
                x => x.Item.IdempotencyKey,
                x => x.Item,
                StringComparer.Ordinal);

            foreach (var result in resultArray.EnumerateArray())
            {
                var idempotencyKey = RequiredString(result, "idempotency_key");

                if (!byIdempotency.TryGetValue(idempotencyKey, out var source))
                {
                    continue;
                }

                var status = RequiredString(result, "status");
                var code = OptionalString(result, "code");
                var message = OptionalString(result, "message");
                var retryable = OptionalBoolean(result, "retryable");
                var disposition = MapDisposition(status, code, retryable);

                acknowledgements.Add(new SyncPushAcknowledgement(
                    source.QueueItemId,
                    disposition,
                    OptionalString(result, "server_id"),
                    OptionalString(result, "server_updated_at"),
                    code,
                    message,
                    null));
            }
        }

        return acknowledgements;
    }

    public async Task<SyncPullBatch> PullAsync(
        SyncPullRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Take < 1 || request.Take > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(request.Take));
        }

        var session = await _sessions.GetAsync(cancellationToken);
        var items = new List<SyncPullItem>();
        var checkpoints = new Dictionary<string, string?>(StringComparer.Ordinal);
        var hasMore = false;
        var perStreamLimit = Math.Clamp(
            request.Take / PullStreams.Length,
            1,
            MaxPullPageSize);

        foreach (var stream in PullStreams)
        {
            request.Checkpoints.TryGetValue(stream, out var cursor);

            var path = BuildPullPath(stream, cursor, perStreamLimit);
            using var response = await SendAsync(
                session,
                HttpMethod.Get,
                path,
                null,
                cancellationToken);

            var document = await ParseResponseAsync(response, cancellationToken);
            var data = document.RootElement.GetProperty("data");
            var returnedStream = RequiredString(data, "stream");

            if (!string.Equals(returnedStream, stream, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The BusinessOS desktop sync API returned the wrong stream.");
            }

            var rows = data.GetProperty("data");
            if (rows.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException(
                    "The BusinessOS desktop sync API returned an invalid pull page.");
            }

            foreach (var row in rows.EnumerateArray())
            {
                var cloudId = RequiredString(row, "id");
                var cloudVersion = RequiredString(row, "server_updated_at");
                var occurredAt = DateTimeOffset.Parse(
                    cloudVersion,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

                items.Add(new SyncPullItem(
                    stream,
                    cloudId,
                    OptionalBoolean(row, "is_deleted")
                        ? SyncOperation.Delete
                        : SyncOperation.Upsert,
                    SyncStreamCatalog.GetConsistencyClass(stream),
                    row.GetRawText(),
                    cloudVersion,
                    $"pull:{stream}:{cloudId}:{cloudVersion}",
                    occurredAt));
            }

            checkpoints[stream] = OptionalString(data, "next_cursor") ?? cursor;
            hasMore |= OptionalBoolean(data, "has_more");
        }

        return new SyncPullBatch(items, checkpoints, hasMore);
    }

    private async Task<HttpResponseMessage> SendAsync(
        CloudSyncSession session,
        HttpMethod method,
        string path,
        string? json,
        CancellationToken cancellationToken)
    {
        var uri = new Uri(session.BaseUri, path);
        using var request = new HttpRequestMessage(method, uri);
        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", session.AccessToken);

        if (json is not null)
        {
            request.Content = new StringContent(
                json,
                Encoding.UTF8,
                "application/json");
        }

        var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            response.Dispose();

            throw new HttpRequestException(
                $"BusinessOS desktop synchronization failed with HTTP {(int)response.StatusCode}: {ExtractApiMessage(body)}",
                null,
                response.StatusCode);
        }

        return response;
    }

    private static async Task<JsonDocument> ParseResponseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);

        try
        {
            return await JsonDocument.ParseAsync(
                stream,
                cancellationToken: cancellationToken);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                "The BusinessOS desktop sync API returned invalid JSON.",
                exception);
        }
    }

    private static JsonElement ParsePayload(string payloadJson)
    {
        try
        {
            using var document = JsonDocument.Parse(payloadJson);

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException(
                    "Synchronization payloads must be JSON objects.");
            }

            return document.RootElement.Clone();
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                "The queued synchronization payload is invalid JSON.",
                exception);
        }
    }

    private static string? ResolveEventType(SyncPushEnvelope item) =>
        item.Stream == SyncStreamCatalog.Sales &&
        item.Operation == SyncOperation.Append
            ? "sale.completed"
            : null;

    private static SyncPushDisposition MapDisposition(
        string status,
        string? code,
        bool retryable)
    {
        if (string.Equals(status, "accepted", StringComparison.OrdinalIgnoreCase))
        {
            return SyncPushDisposition.Synced;
        }

        if (retryable)
        {
            return SyncPushDisposition.RetryableFailure;
        }

        if (string.Equals(code, "stock_conflict", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(code, "daily_closing_conflict", StringComparison.OrdinalIgnoreCase))
        {
            return SyncPushDisposition.Conflict;
        }

        return SyncPushDisposition.PermanentFailure;
    }

    private static string BuildPullPath(
        string stream,
        string? cursor,
        int limit)
    {
        var path = $"/api/v1/desktop/sync/pull/{Uri.EscapeDataString(stream)}?limit={limit}";

        if (!string.IsNullOrWhiteSpace(cursor))
        {
            path += $"&cursor={Uri.EscapeDataString(cursor)}";
        }

        return path;
    }

    private static string RequiredString(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value) ||
            value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new InvalidOperationException(
                $"The BusinessOS desktop sync API omitted '{property}'.");
        }

        return value.GetString()!;
    }

    private static string? OptionalString(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value) ||
            value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : value.ToString();
    }

    private static bool OptionalBoolean(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.True;

    private static string ExtractApiMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return "empty response";
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("message", out var message) &&
                message.ValueKind == JsonValueKind.String)
            {
                return message.GetString() ?? "request rejected";
            }
        }
        catch (JsonException)
        {
        }

        return "request rejected";
    }
}
