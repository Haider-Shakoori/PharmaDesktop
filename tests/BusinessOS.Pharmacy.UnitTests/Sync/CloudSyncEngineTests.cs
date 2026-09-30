using BusinessOS.Pharmacy.Application.Abstractions.Networking;
using BusinessOS.Pharmacy.Application.Abstractions.Sync;
using BusinessOS.Pharmacy.Application.Abstractions.Time;
using BusinessOS.Pharmacy.Sync;
using Xunit;

namespace BusinessOS.Pharmacy.UnitTests.Sync;

public sealed class CloudSyncEngineTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 30, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Cloud_sync_ownership_is_limited_to_standalone_and_server()
    {
        Assert.True(SyncOwnershipPolicy.CanOwnCloudSync(DeploymentMode.Standalone));
        Assert.True(SyncOwnershipPolicy.CanOwnCloudSync(DeploymentMode.Server));
        Assert.False(SyncOwnershipPolicy.CanOwnCloudSync(DeploymentMode.Client));
    }

    [Fact]
    public void Retry_policy_uses_bounded_exponential_backoff()
    {
        var policy = new SyncRetryPolicy();

        Assert.Equal(Now.AddSeconds(5), policy.GetNextAttempt(Now, 1));
        Assert.Equal(Now.AddSeconds(10), policy.GetNextAttempt(Now, 2));
        Assert.Equal(Now.AddSeconds(20), policy.GetNextAttempt(Now, 3));
        Assert.Equal(Now.AddMinutes(15), policy.GetNextAttempt(Now, 20));
    }

    [Fact]
    public async Task Client_terminal_never_touches_cloud_sync_queue_or_transport()
    {
        var queue = new TestQueueStore();
        var transport = new TestTransport();
        var inbox = new TestInbox();

        var engine = CreateEngine(
            DeploymentMode.Client,
            queue,
            transport,
            inbox);

        var result = await engine.RunOnceAsync();

        Assert.False(result.IsOwner);
        Assert.Equal(0, queue.ClaimCalls);
        Assert.Equal(0, transport.PushCalls);
        Assert.Equal(0, transport.PullCalls);
        Assert.Contains("Main Pharmacy Server", result.Message);
    }

    [Fact]
    public async Task Server_pushes_idempotent_queue_and_advances_pull_checkpoint()
    {
        var queued = QueueItem(
            "q-1",
            SyncStreamCatalog.Sales,
            SyncConsistencyClass.ImmutableTransaction);

        var queue = new TestQueueStore
        {
            ClaimResult = [queued],
        };

        var transport = new TestTransport
        {
            PushResult =
            [
                new SyncPushAcknowledgement(
                    queued.Id,
                    SyncPushDisposition.Synced,
                    "cloud-sale-1",
                    "v7",
                    null,
                    null,
                    null),
            ],
            PullResult = new SyncPullBatch(
                [
                    new SyncPullItem(
                        SyncStreamCatalog.Medicines,
                        "medicine-1",
                        SyncOperation.Upsert,
                        SyncConsistencyClass.VersionedMasterData,
                        "{\"brand_name\":\"Paracetamol\"}",
                        "v2",
                        "remote-med-1-v2",
                        Now),
                ],
                new Dictionary<string, string?>
                {
                    [SyncStreamCatalog.Medicines] = "cursor-2",
                },
                false),
        };

        var inbox = new TestInbox();
        var engine = CreateEngine(
            DeploymentMode.Server,
            queue,
            transport,
            inbox);

        var result = await engine.RunOnceAsync();

        Assert.True(result.IsOwner);
        Assert.Equal(1, result.Pushed);
        Assert.Equal(1, result.Pulled);
        Assert.Equal(0, result.Conflicts);
        Assert.Contains("q-1", queue.Synced);
        Assert.Equal("cursor-2", queue.Checkpoints[SyncStreamCatalog.Medicines]);
        Assert.Equal(1, inbox.ApplyCalls);
    }

    [Fact]
    public async Task Retryable_push_failure_stays_queued_with_backoff()
    {
        var queued = QueueItem(
            "q-retry",
            SyncStreamCatalog.InventoryMovements,
            SyncConsistencyClass.ImmutableTransaction);

        var queue = new TestQueueStore
        {
            ClaimResult = [queued],
        };

        var transport = new TestTransport
        {
            PushResult =
            [
                new SyncPushAcknowledgement(
                    queued.Id,
                    SyncPushDisposition.RetryableFailure,
                    null,
                    null,
                    "timeout",
                    "Temporary timeout.",
                    null),
            ],
        };

        var engine = CreateEngine(
            DeploymentMode.Standalone,
            queue,
            transport,
            new TestInbox());

        var result = await engine.RunOnceAsync();

        Assert.Equal(1, result.RetryScheduled);
        Assert.Single(queue.Retries);
        Assert.Equal("q-retry", queue.Retries[0].Id);
        Assert.Equal(Now.AddSeconds(5), queue.Retries[0].NextAttemptAt);
        Assert.Empty(queue.Failed);
    }

    [Fact]
    public async Task Conflicts_are_recorded_instead_of_last_write_wins()
    {
        var queued = QueueItem(
            "q-conflict",
            SyncStreamCatalog.DailyClosings,
            SyncConsistencyClass.ImmutableTransaction);

        var queue = new TestQueueStore
        {
            ClaimResult = [queued],
        };

        var transport = new TestTransport
        {
            PushResult =
            [
                new SyncPushAcknowledgement(
                    queued.Id,
                    SyncPushDisposition.Conflict,
                    null,
                    "remote-v9",
                    "immutable_conflict",
                    "The cloud already has a different closing.",
                    "{\"number\":\"DC-1\"}"),
            ],
            PullResult = new SyncPullBatch(
                [
                    new SyncPullItem(
                        SyncStreamCatalog.Medicines,
                        "medicine-2",
                        SyncOperation.Upsert,
                        SyncConsistencyClass.VersionedMasterData,
                        "{\"brand_name\":\"Remote\"}",
                        "v8",
                        "remote-med-2-v8",
                        Now),
                ],
                new Dictionary<string, string?>(),
                false),
        };

        var inbox = new TestInbox
        {
            Result = new SyncApplyResult(
                SyncApplyDisposition.Conflict,
                "medicine-2",
                "Local medicine was edited offline.",
                "{\"brand_name\":\"Local\"}"),
        };

        var engine = CreateEngine(
            DeploymentMode.Server,
            queue,
            transport,
            inbox);

        var result = await engine.RunOnceAsync();

        Assert.Equal(2, result.Conflicts);
        Assert.Single(queue.PushConflicts);
        Assert.Single(queue.PullConflicts);
        Assert.Equal(
            SyncConsistencyClass.ImmutableTransaction,
            queue.PushConflicts[0].ConsistencyClass);
        Assert.Equal(
            SyncConsistencyClass.VersionedMasterData,
            queue.PullConflicts[0].ConsistencyClass);
    }

    private static CloudSyncEngine CreateEngine(
        DeploymentMode mode,
        TestQueueStore queue,
        TestTransport transport,
        TestInbox inbox) =>
        new(
            new NetworkConfiguration
            {
                Mode = mode,
                IsConfigured = true,
            },
            queue,
            transport,
            inbox,
            new TestClock(Now),
            new SyncRetryPolicy());

    private static SyncQueueItem QueueItem(
        string id,
        string stream,
        SyncConsistencyClass consistencyClass) =>
        new(
            id,
            stream,
            "entity-1",
            SyncOperation.Append,
            consistencyClass,
            "{\"id\":\"entity-1\"}",
            $"idem-{id}",
            1,
            1,
            SyncQueueState.Syncing,
            Now,
            null);

    private sealed class TestClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class TestQueueStore : ISyncQueueStore
    {
        public IReadOnlyList<SyncQueueItem> ClaimResult { get; init; } = [];
        public int ClaimCalls { get; private set; }
        public List<string> Synced { get; } = [];
        public List<(string Id, DateTimeOffset NextAttemptAt)> Retries { get; } = [];
        public List<string> Failed { get; } = [];
        public List<SyncConflictRecord> PushConflicts { get; } = [];
        public List<SyncConflictRecord> PullConflicts { get; } = [];
        public Dictionary<string, string?> Checkpoints { get; } = [];

        public Task<string> EnqueueAsync(
            SyncEnqueueRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Guid.CreateVersion7().ToString());

        public Task<IReadOnlyList<SyncQueueItem>> ClaimPendingAsync(
            int take,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            ClaimCalls++;
            return Task.FromResult(ClaimResult);
        }

        public Task MarkSyncedAsync(
            string queueItemId,
            string? cloudEntityId,
            string? cloudVersion,
            DateTimeOffset syncedAt,
            CancellationToken cancellationToken = default)
        {
            Synced.Add(queueItemId);
            return Task.CompletedTask;
        }

        public Task MarkRetryAsync(
            string queueItemId,
            string error,
            DateTimeOffset nextAttemptAt,
            CancellationToken cancellationToken = default)
        {
            Retries.Add((queueItemId, nextAttemptAt));
            return Task.CompletedTask;
        }

        public Task MarkFailedAsync(
            string queueItemId,
            string error,
            DateTimeOffset failedAt,
            CancellationToken cancellationToken = default)
        {
            Failed.Add(queueItemId);
            return Task.CompletedTask;
        }

        public Task MarkConflictAsync(
            string queueItemId,
            SyncConflictRecord conflict,
            CancellationToken cancellationToken = default)
        {
            PushConflicts.Add(conflict);
            return Task.CompletedTask;
        }

        public Task RecordPullConflictAsync(
            SyncConflictRecord conflict,
            CancellationToken cancellationToken = default)
        {
            PullConflicts.Add(conflict);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyDictionary<string, string?>> GetCheckpointsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, string?>>(Checkpoints);

        public Task SetCheckpointAsync(
            string stream,
            string? checkpoint,
            DateTimeOffset updatedAt,
            CancellationToken cancellationToken = default)
        {
            Checkpoints[stream] = checkpoint;
            return Task.CompletedTask;
        }

        public Task<SyncStatusSnapshot> GetStatusAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new SyncStatusSnapshot(0, 0, 0, 0, null, null));
    }

    private sealed class TestTransport : ICloudSyncTransport
    {
        public IReadOnlyList<SyncPushAcknowledgement> PushResult { get; init; } = [];
        public SyncPullBatch PullResult { get; init; } = new(
            [],
            new Dictionary<string, string?>(),
            false);
        public int PushCalls { get; private set; }
        public int PullCalls { get; private set; }

        public Task<IReadOnlyList<SyncPushAcknowledgement>> PushAsync(
            IReadOnlyList<SyncPushEnvelope> items,
            CancellationToken cancellationToken = default)
        {
            PushCalls++;
            return Task.FromResult(PushResult);
        }

        public Task<SyncPullBatch> PullAsync(
            SyncPullRequest request,
            CancellationToken cancellationToken = default)
        {
            PullCalls++;
            return Task.FromResult(PullResult);
        }
    }

    private sealed class TestInbox : ISyncInboxApplier
    {
        public SyncApplyResult Result { get; init; } = new(
            SyncApplyDisposition.Applied,
            null,
            null,
            null);
        public int ApplyCalls { get; private set; }

        public Task<SyncApplyResult> ApplyAsync(
            SyncPullItem item,
            CancellationToken cancellationToken = default)
        {
            ApplyCalls++;
            return Task.FromResult(Result);
        }
    }
}
