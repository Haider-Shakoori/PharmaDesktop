namespace BusinessOS.Pharmacy.Application.Abstractions.Sync;

public enum SyncQueueState
{
    Pending = 0,
    Syncing = 1,
    Synced = 2,
    Conflict = 3,
    Failed = 4,
}

public enum SyncOperation
{
    Upsert = 0,
    Delete = 1,
    Append = 2,
}

public enum SyncConsistencyClass
{
    VersionedMasterData = 0,
    ImmutableTransaction = 1,
}

public enum SyncPushDisposition
{
    Synced = 0,
    Conflict = 1,
    RetryableFailure = 2,
    PermanentFailure = 3,
}

public enum SyncApplyDisposition
{
    Applied = 0,
    AlreadyApplied = 1,
    Conflict = 2,
}

public sealed record SyncEnqueueRequest(
    string Stream,
    string EntityId,
    SyncOperation Operation,
    SyncConsistencyClass ConsistencyClass,
    string PayloadJson,
    string IdempotencyKey,
    long LocalVersion,
    DateTimeOffset OccurredAt);

public sealed record SyncQueueItem(
    string Id,
    string Stream,
    string EntityId,
    SyncOperation Operation,
    SyncConsistencyClass ConsistencyClass,
    string PayloadJson,
    string IdempotencyKey,
    long LocalVersion,
    int AttemptCount,
    SyncQueueState State,
    DateTimeOffset OccurredAt,
    DateTimeOffset? NextAttemptAt);

public sealed record SyncPushEnvelope(
    string QueueItemId,
    string Stream,
    string EntityId,
    SyncOperation Operation,
    SyncConsistencyClass ConsistencyClass,
    string PayloadJson,
    string IdempotencyKey,
    long LocalVersion,
    DateTimeOffset OccurredAt);

public sealed record SyncPushAcknowledgement(
    string QueueItemId,
    SyncPushDisposition Disposition,
    string? CloudEntityId,
    string? CloudVersion,
    string? ErrorCode,
    string? Message,
    string? RemotePayloadJson);

public sealed record SyncPullRequest(
    IReadOnlyDictionary<string, string?> Checkpoints,
    int Take);

public sealed record SyncPullItem(
    string Stream,
    string EntityId,
    SyncOperation Operation,
    SyncConsistencyClass ConsistencyClass,
    string PayloadJson,
    string CloudVersion,
    string IdempotencyKey,
    DateTimeOffset OccurredAt);

public sealed record SyncPullBatch(
    IReadOnlyList<SyncPullItem> Items,
    IReadOnlyDictionary<string, string?> Checkpoints,
    bool HasMore);

public sealed record SyncApplyResult(
    SyncApplyDisposition Disposition,
    string? LocalEntityId,
    string? Message,
    string? LocalPayloadJson);

public sealed record SyncConflictRecord(
    string Stream,
    string EntityId,
    string IdempotencyKey,
    SyncConsistencyClass ConsistencyClass,
    string? LocalPayloadJson,
    string? RemotePayloadJson,
    string Reason,
    DateTimeOffset DetectedAt);

public sealed record SyncStatusSnapshot(
    int Pending,
    int Syncing,
    int Conflicts,
    int Failed,
    DateTimeOffset? LastSuccessfulSyncAt,
    string? LastError);

public sealed record SyncRunResult(
    bool IsOwner,
    int Pushed,
    int Pulled,
    int Conflicts,
    int RetryScheduled,
    int Failed,
    bool HasMore,
    DateTimeOffset CompletedAt,
    string? Message);
