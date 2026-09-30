using BusinessOS.Pharmacy.Application.Abstractions.Networking;
using BusinessOS.Pharmacy.Application.Abstractions.Sync;
using BusinessOS.Pharmacy.Application.Abstractions.Time;

namespace BusinessOS.Pharmacy.Sync;

public sealed class CloudSyncEngine : ISyncEngine
{
    private readonly NetworkConfiguration _network;
    private readonly ISyncQueueStore _queue;
    private readonly ICloudSyncTransport _transport;
    private readonly ISyncInboxApplier _inbox;
    private readonly IClock _clock;
    private readonly SyncRetryPolicy _retryPolicy;

    public CloudSyncEngine(
        NetworkConfiguration network,
        ISyncQueueStore queue,
        ICloudSyncTransport transport,
        ISyncInboxApplier inbox,
        IClock clock,
        SyncRetryPolicy retryPolicy)
    {
        _network = network;
        _queue = queue;
        _transport = transport;
        _inbox = inbox;
        _clock = clock;
        _retryPolicy = retryPolicy;
    }

    public async Task<SyncRunResult> RunOnceAsync(
        int pushBatchSize = 100,
        int pullBatchSize = 200,
        CancellationToken cancellationToken = default)
    {
        ValidateBatchSize(pushBatchSize, nameof(pushBatchSize), 500);
        ValidateBatchSize(pullBatchSize, nameof(pullBatchSize), 1000);

        if (!SyncOwnershipPolicy.CanOwnCloudSync(_network.Mode))
        {
            return new SyncRunResult(
                false, 0, 0, 0, 0, 0, false, _clock.UtcNow,
                "Client Terminals synchronize through the Main Pharmacy Server, not directly with the cloud.");
        }

        var pushed = 0;
        var pulled = 0;
        var conflicts = 0;
        var retryScheduled = 0;
        var failed = 0;
        var now = _clock.UtcNow;

        var claimed = await _queue.ClaimPendingAsync(
            pushBatchSize,
            now,
            cancellationToken);

        if (claimed.Count > 0)
        {
            IReadOnlyList<SyncPushAcknowledgement> acknowledgements;
            try
            {
                acknowledgements = await _transport.PushAsync(
                    claimed.Select(ToEnvelope).ToList(),
                    cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                foreach (var item in claimed)
                {
                    await ScheduleRetryAsync(item, ex.Message, cancellationToken);
                    retryScheduled++;
                }

                return new SyncRunResult(
                    true, 0, 0, 0, retryScheduled, 0, true, _clock.UtcNow,
                    "Cloud push is temporarily unavailable. Pending changes remain queued locally.");
            }

            var acknowledgementByItem = acknowledgements
                .GroupBy(x => x.QueueItemId, StringComparer.Ordinal)
                .ToDictionary(x => x.Key, x => x.Single(), StringComparer.Ordinal);

            foreach (var item in claimed)
            {
                if (!acknowledgementByItem.TryGetValue(item.Id, out var acknowledgement))
                {
                    await ScheduleRetryAsync(
                        item,
                        "The cloud did not acknowledge this queued change.",
                        cancellationToken);
                    retryScheduled++;
                    continue;
                }

                switch (acknowledgement.Disposition)
                {
                    case SyncPushDisposition.Synced:
                        await _queue.MarkSyncedAsync(
                            item.Id,
                            acknowledgement.CloudEntityId,
                            acknowledgement.CloudVersion,
                            _clock.UtcNow,
                            cancellationToken);
                        pushed++;
                        break;

                    case SyncPushDisposition.Conflict:
                        await _queue.MarkConflictAsync(
                            item.Id,
                            new SyncConflictRecord(
                                item.Stream,
                                item.EntityId,
                                item.IdempotencyKey,
                                item.ConsistencyClass,
                                item.PayloadJson,
                                acknowledgement.RemotePayloadJson,
                                acknowledgement.Message ?? "Cloud conflict.",
                                _clock.UtcNow),
                            cancellationToken);
                        conflicts++;
                        break;

                    case SyncPushDisposition.RetryableFailure:
                        await ScheduleRetryAsync(
                            item,
                            acknowledgement.Message ?? acknowledgement.ErrorCode ?? "Retryable cloud failure.",
                            cancellationToken);
                        retryScheduled++;
                        break;

                    case SyncPushDisposition.PermanentFailure:
                        await _queue.MarkFailedAsync(
                            item.Id,
                            acknowledgement.Message ?? acknowledgement.ErrorCode ?? "Cloud rejected the change.",
                            _clock.UtcNow,
                            cancellationToken);
                        failed++;
                        break;

                    default:
                        throw new InvalidOperationException("Unsupported sync acknowledgement.");
                }
            }
        }

        SyncPullBatch pullBatch;
        try
        {
            var checkpoints = await _queue.GetCheckpointsAsync(cancellationToken);
            pullBatch = await _transport.PullAsync(
                new SyncPullRequest(checkpoints, pullBatchSize),
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new SyncRunResult(
                true, pushed, 0, conflicts, retryScheduled, failed, true, _clock.UtcNow,
                $"Cloud pull is temporarily unavailable: {ex.Message}");
        }

        foreach (var item in pullBatch.Items)
        {
            var result = await _inbox.ApplyAsync(item, cancellationToken);

            if (result.Disposition == SyncApplyDisposition.Conflict)
            {
                await _queue.RecordPullConflictAsync(
                    new SyncConflictRecord(
                        item.Stream,
                        item.EntityId,
                        item.IdempotencyKey,
                        item.ConsistencyClass,
                        result.LocalPayloadJson,
                        item.PayloadJson,
                        result.Message ?? "Local/cloud conflict.",
                        _clock.UtcNow),
                    cancellationToken);
                conflicts++;
            }

            pulled++;
        }

        foreach (var checkpoint in pullBatch.Checkpoints)
        {
            await _queue.SetCheckpointAsync(
                checkpoint.Key,
                checkpoint.Value,
                _clock.UtcNow,
                cancellationToken);
        }

        return new SyncRunResult(
            true,
            pushed,
            pulled,
            conflicts,
            retryScheduled,
            failed,
            pullBatch.HasMore,
            _clock.UtcNow,
            null);
    }

    private async Task ScheduleRetryAsync(
        SyncQueueItem item,
        string error,
        CancellationToken cancellationToken)
    {
        var next = _retryPolicy.GetNextAttempt(
            _clock.UtcNow,
            Math.Max(1, item.AttemptCount));

        await _queue.MarkRetryAsync(
            item.Id,
            error,
            next,
            cancellationToken);
    }

    private static SyncPushEnvelope ToEnvelope(SyncQueueItem item) => new(
        item.Id,
        item.Stream,
        item.EntityId,
        item.Operation,
        item.ConsistencyClass,
        item.PayloadJson,
        item.IdempotencyKey,
        item.LocalVersion,
        item.OccurredAt);

    private static void ValidateBatchSize(
        int value,
        string parameterName,
        int maximum)
    {
        if (value < 1 || value > maximum)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}
