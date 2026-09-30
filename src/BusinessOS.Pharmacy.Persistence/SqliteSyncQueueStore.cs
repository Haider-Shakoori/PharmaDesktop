using BusinessOS.Pharmacy.Application.Abstractions.Sync;
using BusinessOS.Pharmacy.Application.Abstractions.Time;
using BusinessOS.Pharmacy.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace BusinessOS.Pharmacy.Persistence;

public sealed class SqliteSyncQueueStore : ISyncQueueStore
{
    private static readonly TimeSpan StaleClaimAge = TimeSpan.FromMinutes(5);

    private readonly IDbContextFactory<PharmacyDbContext> _contextFactory;
    private readonly IClock _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public SqliteSyncQueueStore(
        IDbContextFactory<PharmacyDbContext> contextFactory,
        IClock clock)
    {
        _contextFactory = contextFactory;
        _clock = clock;
    }

    public async Task<string> EnqueueAsync(
        SyncEnqueueRequest request,
        CancellationToken cancellationToken = default)
    {
        Validate(request);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            var existing = await context.Set<SyncQueueEntity>()
                .SingleOrDefaultAsync(
                    x => x.IdempotencyKey == request.IdempotencyKey,
                    cancellationToken);

            if (existing is not null)
            {
                EnsureEquivalent(existing, request);
                return existing.Id;
            }

            var now = _clock.UtcNow;
            var entity = new SyncQueueEntity
            {
                Id = Guid.CreateVersion7().ToString(),
                Stream = request.Stream.Trim(),
                EntityId = request.EntityId.Trim(),
                Operation = (int)request.Operation,
                ConsistencyClass = (int)request.ConsistencyClass,
                PayloadJson = request.PayloadJson,
                IdempotencyKey = request.IdempotencyKey.Trim(),
                LocalVersion = request.LocalVersion,
                AttemptCount = 0,
                State = (int)SyncQueueState.Pending,
                OccurredAt = request.OccurredAt,
                CreatedAt = now,
                UpdatedAt = now,
            };

            context.Add(entity);
            await context.SaveChangesAsync(cancellationToken);
            return entity.Id;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<SyncQueueItem>> ClaimPendingAsync(
        int take,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        if (take < 1 || take > 500)
        {
            throw new ArgumentOutOfRangeException(nameof(take));
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

            var staleBefore = now.Subtract(StaleClaimAge);
            var stale = await context.Set<SyncQueueEntity>()
                .Where(x =>
                    x.State == (int)SyncQueueState.Syncing &&
                    x.ClaimedAt != null &&
                    x.ClaimedAt <= staleBefore)
                .ToListAsync(cancellationToken);

            foreach (var item in stale)
            {
                item.State = (int)SyncQueueState.Pending;
                item.ClaimedAt = null;
                item.NextAttemptAt = now;
                item.LastError = "Recovered a stale synchronization claim after restart or interruption.";
                item.UpdatedAt = now;
            }

            var claimed = await context.Set<SyncQueueEntity>()
                .Where(x =>
                    x.State == (int)SyncQueueState.Pending &&
                    (x.NextAttemptAt == null || x.NextAttemptAt <= now))
                .OrderBy(x => x.OccurredAt)
                .ThenBy(x => x.CreatedAt)
                .ThenBy(x => x.Id)
                .Take(take)
                .ToListAsync(cancellationToken);

            foreach (var item in claimed)
            {
                item.State = (int)SyncQueueState.Syncing;
                item.AttemptCount++;
                item.ClaimedAt = now;
                item.UpdatedAt = now;
            }

            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return claimed.Select(ToContract).ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task MarkSyncedAsync(
        string queueItemId,
        string? cloudEntityId,
        string? cloudVersion,
        DateTimeOffset syncedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queueItemId);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

            var item = await RequiredQueueItemAsync(
                context,
                queueItemId,
                cancellationToken);

            var cloudId = TrimToNull(cloudEntityId);
            var version = TrimToNull(cloudVersion);

            item.State = (int)SyncQueueState.Synced;
            item.CloudEntityId = cloudId;
            item.CloudVersion = version;
            item.SyncedAt = syncedAt;
            item.NextAttemptAt = null;
            item.ClaimedAt = null;
            item.LastError = null;
            item.UpdatedAt = syncedAt;

            if (cloudId is not null)
            {
                var conflicting = await context.Set<SyncEntityMapEntity>()
                    .AsNoTracking()
                    .AnyAsync(
                        x => x.Stream == item.Stream &&
                             x.CloudEntityId == cloudId &&
                             x.LocalEntityId != item.EntityId,
                        cancellationToken);

                if (conflicting)
                {
                    throw new InvalidOperationException(
                        "The acknowledged cloud record is already mapped to another local record.");
                }

                var mapping = await context.Set<SyncEntityMapEntity>()
                    .SingleOrDefaultAsync(
                        x => x.Stream == item.Stream &&
                             x.LocalEntityId == item.EntityId,
                        cancellationToken);

                if (mapping is null)
                {
                    context.Add(new SyncEntityMapEntity
                    {
                        Stream = item.Stream,
                        LocalEntityId = item.EntityId,
                        CloudEntityId = cloudId,
                        CloudVersion = version,
                        UpdatedAt = syncedAt,
                    });
                }
                else
                {
                    mapping.CloudEntityId = cloudId;
                    mapping.CloudVersion = version;
                    mapping.UpdatedAt = syncedAt;
                }
            }

            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task MarkRetryAsync(
        string queueItemId,
        string error,
        DateTimeOffset nextAttemptAt,
        CancellationToken cancellationToken = default) =>
        MutateAsync(
            queueItemId,
            item =>
            {
                item.State = (int)SyncQueueState.Pending;
                item.NextAttemptAt = nextAttemptAt;
                item.ClaimedAt = null;
                item.LastError = Limit(error, 2000);
            },
            cancellationToken);

    public Task MarkFailedAsync(
        string queueItemId,
        string error,
        DateTimeOffset failedAt,
        CancellationToken cancellationToken = default) =>
        MutateAsync(
            queueItemId,
            item =>
            {
                item.State = (int)SyncQueueState.Failed;
                item.NextAttemptAt = null;
                item.ClaimedAt = null;
                item.LastError = Limit(error, 2000);
                item.UpdatedAt = failedAt;
            },
            cancellationToken);

    public async Task MarkConflictAsync(
        string queueItemId,
        SyncConflictRecord conflict,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

            var queueItem = await RequiredQueueItemAsync(
                context,
                queueItemId,
                cancellationToken);

            queueItem.State = (int)SyncQueueState.Conflict;
            queueItem.NextAttemptAt = null;
            queueItem.ClaimedAt = null;
            queueItem.LastError = Limit(conflict.Reason, 2000);
            queueItem.UpdatedAt = _clock.UtcNow;

            context.Add(ToConflictEntity("push", queueItemId, conflict));
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RecordPullConflictAsync(
        SyncConflictRecord conflict,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            context.Add(ToConflictEntity("pull", null, conflict));
            await context.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyDictionary<string, string?>> GetCheckpointsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.Set<SyncCheckpointEntity>()
            .AsNoTracking()
            .OrderBy(x => x.Stream)
            .ToDictionaryAsync(
                x => x.Stream,
                x => x.Checkpoint,
                StringComparer.Ordinal,
                cancellationToken);
    }

    public async Task SetCheckpointAsync(
        string stream,
        string? checkpoint,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stream);

        if (stream.Trim().Length > 80)
        {
            throw new ArgumentOutOfRangeException(nameof(stream));
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var key = stream.Trim();

            var entity = await context.Set<SyncCheckpointEntity>()
                .SingleOrDefaultAsync(x => x.Stream == key, cancellationToken);

            if (entity is null)
            {
                context.Add(new SyncCheckpointEntity
                {
                    Stream = key,
                    Checkpoint = LimitNullable(checkpoint, 1000),
                    UpdatedAt = updatedAt,
                });
            }
            else
            {
                entity.Checkpoint = LimitNullable(checkpoint, 1000);
                entity.UpdatedAt = updatedAt;
            }

            await context.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<SyncStatusSnapshot> GetStatusAsync(
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var queue = context.Set<SyncQueueEntity>().AsNoTracking();

        var pending = await queue.CountAsync(
            x => x.State == (int)SyncQueueState.Pending,
            cancellationToken);
        var syncing = await queue.CountAsync(
            x => x.State == (int)SyncQueueState.Syncing,
            cancellationToken);
        var conflicts = await queue.CountAsync(
            x => x.State == (int)SyncQueueState.Conflict,
            cancellationToken);
        var failed = await queue.CountAsync(
            x => x.State == (int)SyncQueueState.Failed,
            cancellationToken);

        var lastSuccessful = await queue
            .Where(x => x.SyncedAt != null)
            .OrderByDescending(x => x.SyncedAt)
            .Select(x => x.SyncedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var lastError = await queue
            .Where(x => x.LastError != null)
            .OrderByDescending(x => x.UpdatedAt)
            .Select(x => x.LastError)
            .FirstOrDefaultAsync(cancellationToken);

        return new SyncStatusSnapshot(
            pending,
            syncing,
            conflicts,
            failed,
            lastSuccessful,
            lastError);
    }

    private async Task MutateAsync(
        string queueItemId,
        Action<SyncQueueEntity> mutation,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queueItemId);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var item = await RequiredQueueItemAsync(context, queueItemId, cancellationToken);
            mutation(item);
            item.UpdatedAt = _clock.UtcNow;
            await context.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static async Task<SyncQueueEntity> RequiredQueueItemAsync(
        PharmacyDbContext context,
        string queueItemId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queueItemId);

        return await context.Set<SyncQueueEntity>()
            .SingleOrDefaultAsync(x => x.Id == queueItemId, cancellationToken)
            ?? throw new InvalidOperationException("The synchronization queue item does not exist.");
    }

    private SyncConflictEntity ToConflictEntity(
        string direction,
        string? queueItemId,
        SyncConflictRecord conflict)
    {
        var now = _clock.UtcNow;

        return new SyncConflictEntity
        {
            Id = Guid.CreateVersion7().ToString(),
            Direction = direction,
            QueueItemId = queueItemId,
            Stream = Limit(conflict.Stream.Trim(), 80),
            EntityId = Limit(conflict.EntityId.Trim(), 80),
            IdempotencyKey = Limit(conflict.IdempotencyKey.Trim(), 191),
            ConsistencyClass = (int)conflict.ConsistencyClass,
            LocalPayloadJson = conflict.LocalPayloadJson,
            RemotePayloadJson = conflict.RemotePayloadJson,
            Reason = Limit(conflict.Reason, 2000),
            Status = "open",
            DetectedAt = conflict.DetectedAt,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    private static SyncQueueItem ToContract(SyncQueueEntity item) => new(
        item.Id,
        item.Stream,
        item.EntityId,
        (SyncOperation)item.Operation,
        (SyncConsistencyClass)item.ConsistencyClass,
        item.PayloadJson,
        item.IdempotencyKey,
        item.LocalVersion,
        item.AttemptCount,
        (SyncQueueState)item.State,
        item.OccurredAt,
        item.NextAttemptAt);

    private static void Validate(SyncEnqueueRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Stream);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.EntityId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.PayloadJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.IdempotencyKey);

        if (request.Stream.Trim().Length > 80)
        {
            throw new ArgumentOutOfRangeException(nameof(request.Stream));
        }

        if (request.EntityId.Trim().Length > 80)
        {
            throw new ArgumentOutOfRangeException(nameof(request.EntityId));
        }

        if (request.IdempotencyKey.Trim().Length > 191)
        {
            throw new ArgumentOutOfRangeException(nameof(request.IdempotencyKey));
        }

        if (request.LocalVersion < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request.LocalVersion));
        }

        if (!Enum.IsDefined(request.Operation) ||
            !Enum.IsDefined(request.ConsistencyClass))
        {
            throw new ArgumentException("The synchronization operation is invalid.");
        }
    }

    private static void EnsureEquivalent(
        SyncQueueEntity existing,
        SyncEnqueueRequest request)
    {
        if (existing.Stream != request.Stream.Trim() ||
            existing.EntityId != request.EntityId.Trim() ||
            existing.Operation != (int)request.Operation ||
            existing.ConsistencyClass != (int)request.ConsistencyClass ||
            existing.PayloadJson != request.PayloadJson ||
            existing.LocalVersion != request.LocalVersion)
        {
            throw new InvalidOperationException(
                "The synchronization idempotency key was already used for different data.");
        }
    }

    private static string Limit(string value, int maximum) =>
        value.Length <= maximum ? value : value[..maximum];

    private static string? LimitNullable(string? value, int maximum)
    {
        var trimmed = TrimToNull(value);
        return trimmed is null ? null : Limit(trimmed, maximum);
    }

    private static string? TrimToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
