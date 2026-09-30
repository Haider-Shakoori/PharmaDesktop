namespace BusinessOS.Pharmacy.Application.Abstractions.Sync;

public interface ISyncQueueStore
{
    Task<string> EnqueueAsync(
        SyncEnqueueRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SyncQueueItem>> ClaimPendingAsync(
        int take,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    Task MarkSyncedAsync(
        string queueItemId,
        string? cloudEntityId,
        string? cloudVersion,
        DateTimeOffset syncedAt,
        CancellationToken cancellationToken = default);

    Task MarkRetryAsync(
        string queueItemId,
        string error,
        DateTimeOffset nextAttemptAt,
        CancellationToken cancellationToken = default);

    Task MarkFailedAsync(
        string queueItemId,
        string error,
        DateTimeOffset failedAt,
        CancellationToken cancellationToken = default);

    Task MarkConflictAsync(
        string queueItemId,
        SyncConflictRecord conflict,
        CancellationToken cancellationToken = default);

    Task RecordPullConflictAsync(
        SyncConflictRecord conflict,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<string, string?>> GetCheckpointsAsync(
        CancellationToken cancellationToken = default);

    Task SetCheckpointAsync(
        string stream,
        string? checkpoint,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default);

    Task<SyncStatusSnapshot> GetStatusAsync(
        CancellationToken cancellationToken = default);
}
