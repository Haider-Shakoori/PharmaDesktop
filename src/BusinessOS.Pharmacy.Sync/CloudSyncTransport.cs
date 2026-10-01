using BusinessOS.Pharmacy.Application.Abstractions.Sync;

namespace BusinessOS.Pharmacy.Sync;

public interface ICloudSyncTransport
{
    Task<IReadOnlyList<CloudSyncPushAcknowledgement>> PushAsync(
        string accessToken,
        IReadOnlyList<CloudSyncOutboxItem> events,
        CancellationToken cancellationToken = default);

    Task<CloudSyncPullPage> PullAsync(
        string accessToken,
        string stream,
        string? cursor,
        int limit,
        CancellationToken cancellationToken = default);
}

public sealed record CloudSyncPushAcknowledgement(
    string IdempotencyKey,
    string Status,
    string? Code,
    string? Message,
    bool Retryable,
    string? ServerId,
    DateTimeOffset? ServerUpdatedAt);

public sealed record CloudSyncPullPage(
    string Stream,
    IReadOnlyList<CloudSyncRemoteRecord> Records,
    string? NextCursor,
    bool HasMore,
    DateTimeOffset ServerTime);

public sealed class CloudSyncTransportException(
    string message,
    bool retryable,
    Exception? innerException = null)
    : Exception(message, innerException)
{
    public bool Retryable { get; } = retryable;
}

public sealed class CloudSyncAuthorizationException(string message)
    : Exception(message);
