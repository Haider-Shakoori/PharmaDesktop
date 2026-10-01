namespace BusinessOS.Pharmacy.Application.Abstractions.Sync;

public interface ICloudSyncStore
{
    Task<IReadOnlyList<CloudSyncOutboxItem>> GetPendingAsync(
        string tenantId,
        string actorUserId,
        int take,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    Task MarkAcceptedAsync(
        string tenantId,
        string idempotencyKey,
        string? serverId,
        DateTimeOffset? serverUpdatedAt,
        CancellationToken cancellationToken = default);

    Task MarkRejectedAsync(
        string tenantId,
        string idempotencyKey,
        string code,
        string message,
        bool retryable,
        DateTimeOffset? nextAttemptAt,
        CancellationToken cancellationToken = default);

    Task DeferAsync(
        string tenantId,
        IReadOnlyCollection<string> idempotencyKeys,
        string message,
        DateTimeOffset nextAttemptAt,
        CancellationToken cancellationToken = default);

    Task<string?> GetCursorAsync(
        string tenantId,
        string stream,
        CancellationToken cancellationToken = default);

    Task SaveRemotePageAsync(
        string tenantId,
        string stream,
        string? nextCursor,
        IReadOnlyList<CloudSyncRemoteRecord> records,
        CancellationToken cancellationToken = default);

    Task<CloudSyncQueueSnapshot> GetQueueSnapshotAsync(
        string tenantId,
        CancellationToken cancellationToken = default);
}

public interface ICloudSyncService
{
    Task<CloudSyncRunResult> SyncOnceAsync(
        CancellationToken cancellationToken = default);

    CloudSyncRunResult LastResult { get; }
}

public sealed record CloudSyncOutboxItem(
    string Id,
    string TenantId,
    string ActorUserId,
    string EventType,
    string IdempotencyKey,
    string PayloadJson,
    int AttemptCount,
    DateTimeOffset CreatedAt);

public sealed record CloudSyncRemoteRecord(
    string ServerId,
    string PayloadJson,
    DateTimeOffset? ServerUpdatedAt);

public sealed record CloudSyncQueueSnapshot(
    int Pending,
    int Conflicts,
    int Failed,
    DateTimeOffset? OldestPendingAt);

public enum CloudSyncRunState
{
    NeverRun = 0,
    DisabledForClientTerminal = 1,
    WaitingForSignIn = 2,
    Offline = 3,
    Synced = 4,
    Conflicts = 5,
    LicenseRejected = 6,
    Failed = 7,
}

public sealed record CloudSyncRunResult(
    CloudSyncRunState State,
    string Message,
    DateTimeOffset? AttemptedAt = null,
    DateTimeOffset? SucceededAt = null,
    int Pushed = 0,
    int Pulled = 0,
    int Conflicts = 0)
{
    public static CloudSyncRunResult Initial { get; } =
        new(CloudSyncRunState.NeverRun, "Cloud synchronization has not run yet.");
}
