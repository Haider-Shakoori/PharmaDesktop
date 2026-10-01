using BusinessOS.Pharmacy.Application.Abstractions.Sync;
using BusinessOS.Pharmacy.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace BusinessOS.Pharmacy.Persistence;

public sealed class CloudSyncStore(
    IDbContextFactory<PharmacyDbContext> contextFactory) : ICloudSyncStore
{
    public async Task<IReadOnlyList<CloudSyncOutboxItem>> GetPendingAsync(
        string tenantId,
        string actorUserId,
        int take,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        tenantId = Required(tenantId, nameof(tenantId));
        actorUserId = Required(actorUserId, nameof(actorUserId));
        take = Math.Clamp(take, 1, 100);

        await using var context =
            await contextFactory.CreateDbContextAsync(cancellationToken);

        var rows = await context.Set<CloudSyncOutboxEntity>()
            .AsNoTracking()
            .Where(x =>
                x.TenantId == tenantId &&
                x.ActorUserId == actorUserId &&
                x.Status == "pending")
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);

        return rows
            .Where(x =>
                x.NextAttemptAt is null ||
                x.NextAttemptAt <= now)
            .Take(take)
            .Select(x => new CloudSyncOutboxItem(
                x.Id,
                x.TenantId,
                x.ActorUserId,
                x.EventType,
                x.IdempotencyKey,
                x.PayloadJson,
                x.AttemptCount,
                x.CreatedAt))
            .ToList();
    }

    public async Task MarkAcceptedAsync(
        string tenantId,
        string idempotencyKey,
        string? serverId,
        DateTimeOffset? serverUpdatedAt,
        CancellationToken cancellationToken = default)
    {
        tenantId = Required(tenantId, nameof(tenantId));
        idempotencyKey = Required(idempotencyKey, nameof(idempotencyKey));

        await using var context =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        var row = await context.Set<CloudSyncOutboxEntity>()
            .SingleOrDefaultAsync(
                x => x.TenantId == tenantId &&
                     x.IdempotencyKey == idempotencyKey,
                cancellationToken);

        if (row is null)
            return;

        row.Status = "synced";
        row.ServerId = TrimOptional(serverId, 100);
        row.ServerUpdatedAt = serverUpdatedAt;
        row.NextAttemptAt = null;
        row.LastErrorCode = null;
        row.LastErrorMessage = null;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkRejectedAsync(
        string tenantId,
        string idempotencyKey,
        string code,
        string message,
        bool retryable,
        DateTimeOffset? nextAttemptAt,
        CancellationToken cancellationToken = default)
    {
        tenantId = Required(tenantId, nameof(tenantId));
        idempotencyKey = Required(idempotencyKey, nameof(idempotencyKey));

        await using var context =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        var row = await context.Set<CloudSyncOutboxEntity>()
            .SingleOrDefaultAsync(
                x => x.TenantId == tenantId &&
                     x.IdempotencyKey == idempotencyKey,
                cancellationToken);

        if (row is null)
            return;

        row.AttemptCount += 1;
        row.Status = retryable ? "pending" : "conflict";
        row.NextAttemptAt = retryable ? nextAttemptAt : null;
        row.LastErrorCode = TrimOptional(code, 100);
        row.LastErrorMessage = TrimOptional(message, 2000);
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeferAsync(
        string tenantId,
        IReadOnlyCollection<string> idempotencyKeys,
        string message,
        DateTimeOffset nextAttemptAt,
        CancellationToken cancellationToken = default)
    {
        tenantId = Required(tenantId, nameof(tenantId));
        if (idempotencyKeys.Count == 0)
            return;

        var keys = idempotencyKeys
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (keys.Count == 0)
            return;

        await using var context =
            await contextFactory.CreateDbContextAsync(cancellationToken);

        var rows = await context.Set<CloudSyncOutboxEntity>()
            .Where(x =>
                x.TenantId == tenantId &&
                x.Status == "pending" &&
                keys.Contains(x.IdempotencyKey))
            .ToListAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        foreach (var row in rows)
        {
            row.AttemptCount += 1;
            row.NextAttemptAt = nextAttemptAt;
            row.LastErrorCode = "transport";
            row.LastErrorMessage = TrimOptional(message, 2000);
            row.UpdatedAt = now;
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<string?> GetCursorAsync(
        string tenantId,
        string stream,
        CancellationToken cancellationToken = default)
    {
        tenantId = Required(tenantId, nameof(tenantId));
        stream = Required(stream, nameof(stream)).ToLowerInvariant();

        await using var context =
            await contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.Set<CloudSyncCursorEntity>()
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.Stream == stream)
            .Select(x => x.Cursor)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task SaveRemotePageAsync(
        string tenantId,
        string stream,
        string? nextCursor,
        IReadOnlyList<CloudSyncRemoteRecord> records,
        CancellationToken cancellationToken = default)
    {
        tenantId = Required(tenantId, nameof(tenantId));
        stream = Required(stream, nameof(stream)).ToLowerInvariant();
        if (nextCursor?.Length > 1000)
            throw new ArgumentOutOfRangeException(nameof(nextCursor));

        await using var context =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction =
            await InventoryWriteTransaction.BeginAsync(context, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        foreach (var record in records)
        {
            var serverId = Required(record.ServerId, nameof(record.ServerId));
            var existing = await context.Set<CloudSyncRemoteRecordEntity>()
                .SingleOrDefaultAsync(
                    x => x.TenantId == tenantId &&
                         x.Stream == stream &&
                         x.ServerId == serverId,
                    cancellationToken);

            if (existing is null)
            {
                context.Add(new CloudSyncRemoteRecordEntity
                {
                    Id = Guid.CreateVersion7().ToString(),
                    TenantId = tenantId,
                    Stream = stream,
                    ServerId = serverId,
                    PayloadJson = record.PayloadJson,
                    ServerUpdatedAt = record.ServerUpdatedAt,
                    ReceivedAt = now,
                });
            }
            else
            {
                existing.PayloadJson = record.PayloadJson;
                existing.ServerUpdatedAt = record.ServerUpdatedAt;
                existing.ReceivedAt = now;
            }
        }

        var cursor = await context.Set<CloudSyncCursorEntity>()
            .SingleOrDefaultAsync(
                x => x.TenantId == tenantId && x.Stream == stream,
                cancellationToken);

        if (cursor is null)
        {
            context.Add(new CloudSyncCursorEntity
            {
                Id = Guid.CreateVersion7().ToString(),
                TenantId = tenantId,
                Stream = stream,
                Cursor = nextCursor,
                UpdatedAt = now,
            });
        }
        else
        {
            cursor.Cursor = nextCursor;
            cursor.UpdatedAt = now;
        }

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<CloudSyncQueueSnapshot> GetQueueSnapshotAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        tenantId = Required(tenantId, nameof(tenantId));

        await using var context =
            await contextFactory.CreateDbContextAsync(cancellationToken);

        var rows = await context.Set<CloudSyncOutboxEntity>()
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId)
            .Select(x => new
            {
                x.Status,
                x.CreatedAt,
            })
            .ToListAsync(cancellationToken);

        return new CloudSyncQueueSnapshot(
            rows.Count(x => x.Status == "pending"),
            rows.Count(x => x.Status == "conflict"),
            rows.Count(x => x.Status == "failed"),
            rows.Where(x => x.Status == "pending")
                .Select(x => (DateTimeOffset?)x.CreatedAt)
                .Min());
    }

    private static string Required(string value, string parameter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameter);
        return value.Trim();
    }

    private static string? TrimOptional(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength
            ? trimmed
            : trimmed[..maxLength];
    }
}
