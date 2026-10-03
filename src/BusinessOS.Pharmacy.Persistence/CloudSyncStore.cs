using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
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

    public async Task<int> RepairReferenceConflictsAsync(
        string tenantId,
        string actorUserId,
        CancellationToken cancellationToken = default)
    {
        tenantId = Required(tenantId, nameof(tenantId));
        actorUserId = Required(actorUserId, nameof(actorUserId));

        await using var context =
            await contextFactory.CreateDbContextAsync(cancellationToken);

        var rows = await context.Set<CloudSyncOutboxEntity>()
            .Where(x =>
                x.TenantId == tenantId &&
                x.ActorUserId == actorUserId &&
                (x.Status == "conflict" || x.Status == "pending") &&
                x.EventType == "sale.completed" &&
                x.LastErrorCode == "reference_missing")
            .ToListAsync(cancellationToken);

        rows = rows
            .OrderBy(x => x.CreatedAt)
            .ToList();

        var repaired = 0;
        var now = DateTimeOffset.UtcNow;

        foreach (var row in rows)
        {
            JsonObject? payload;
            try
            {
                payload = JsonNode.Parse(row.PayloadJson) as JsonObject;
            }
            catch (JsonException)
            {
                continue;
            }

            if (payload is null)
            {
                continue;
            }

            var locationId = ReadString(payload, "stock_location_id");
            if (string.IsNullOrWhiteSpace(locationId))
                continue;

            var location = await context.Set<StockLocationEntity>()
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    x => x.Id == locationId,
                    cancellationToken);
            if (location is null)
                continue;

            payload["stock_location_code"] = location.Code;

            var customerId = ReadString(payload, "customer_id");
            if (!string.IsNullOrWhiteSpace(customerId))
            {
                var customer = await context.Set<CustomerEntity>()
                    .AsNoTracking()
                    .SingleOrDefaultAsync(
                        x => x.Id == customerId,
                        cancellationToken);
                if (customer is null)
                    continue;

                payload["customer_name"] = customer.Name;
                payload["customer_phone"] = customer.Phone;
                payload["customer_email"] = customer.Email;
            }

            if (payload["lines"] is not JsonArray lines || lines.Count == 0)
                continue;

            var resolvedAllLines = true;
            foreach (var node in lines)
            {
                if (node is not JsonObject line)
                {
                    resolvedAllLines = false;
                    break;
                }

                var medicineId = ReadString(line, "medicine_id");
                if (string.IsNullOrWhiteSpace(medicineId))
                {
                    resolvedAllLines = false;
                    break;
                }

                var medicine = await context.Set<MedicineEntity>()
                    .AsNoTracking()
                    .SingleOrDefaultAsync(
                        x => x.Id == medicineId,
                        cancellationToken);
                if (medicine is null)
                {
                    resolvedAllLines = false;
                    break;
                }

                line["medicine_code"] = medicine.MedicineCode;
            }

            if (!resolvedAllLines)
                continue;

            var localId = ReadString(payload, "local_id");
            if (!string.IsNullOrWhiteSpace(localId))
            {
                var sale = await context.Set<SaleEntity>()
                    .AsNoTracking()
                    .SingleOrDefaultAsync(
                        x => x.Id == localId,
                        cancellationToken);
                if (sale is not null)
                {
                    payload["prescription_reference"] = sale.PrescriptionReference;
                    payload["prescriber_name"] = sale.PrescriberName;
                    payload["prescription_date"] = sale.PrescriptionDate?
                        .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                }
            }

            payload["reference_resolution_v"] = 1;
            row.PayloadJson = payload.ToJsonString();
            row.Status = "pending";
            row.AttemptCount = 0;
            row.NextAttemptAt = null;
            row.LastErrorCode = null;
            row.LastErrorMessage = null;
            row.UpdatedAt = now;
            repaired++;
        }

        if (repaired > 0)
            await context.SaveChangesAsync(cancellationToken);

        return repaired;
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
        string actorUserId,
        CancellationToken cancellationToken = default)
    {
        tenantId = Required(tenantId, nameof(tenantId));
        actorUserId = Required(actorUserId, nameof(actorUserId));

        await using var context =
            await contextFactory.CreateDbContextAsync(cancellationToken);

        var rows = await context.Set<CloudSyncOutboxEntity>()
            .AsNoTracking()
            .Where(x =>
                x.TenantId == tenantId &&
                x.ActorUserId == actorUserId)
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

    public async Task<IReadOnlyList<CloudSyncConflictItem>> GetConflictsAsync(
        string tenantId,
        int take,
        CancellationToken cancellationToken = default)
    {
        tenantId = Required(tenantId, nameof(tenantId));
        take = Math.Clamp(take, 1, 200);

        await using var context =
            await contextFactory.CreateDbContextAsync(cancellationToken);

        var rows = await context.Set<CloudSyncOutboxEntity>()
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.Status == "conflict")
            .Select(x => new
            {
                x.IdempotencyKey,
                x.EventType,
                x.ActorUserId,
                x.PayloadJson,
                x.LastErrorCode,
                x.LastErrorMessage,
                x.AttemptCount,
                x.CreatedAt,
            })
            .ToListAsync(cancellationToken);

        return rows
            .OrderByDescending(x => x.CreatedAt)
            .Take(take)
            .Select(x =>
            {
                var (localId, businessDate) = ReadConflictReferences(x.PayloadJson);
                return new CloudSyncConflictItem(
                    x.IdempotencyKey,
                    x.EventType,
                    x.ActorUserId,
                    localId,
                    businessDate,
                    x.LastErrorCode,
                    x.LastErrorMessage,
                    x.AttemptCount,
                    x.CreatedAt,
                    CanRetry: true);
            })
            .ToList();
    }

    public async Task<bool> RetryConflictAsync(
        string tenantId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        tenantId = Required(tenantId, nameof(tenantId));
        idempotencyKey = Required(idempotencyKey, nameof(idempotencyKey));

        await using var context =
            await contextFactory.CreateDbContextAsync(cancellationToken);

        var row = await context.Set<CloudSyncOutboxEntity>()
            .SingleOrDefaultAsync(
                x => x.TenantId == tenantId &&
                     x.IdempotencyKey == idempotencyKey &&
                     x.Status == "conflict",
                cancellationToken);

        if (row is null)
            return false;

        var needsReferenceRepair =
            string.Equals(row.EventType, "sale.completed", StringComparison.Ordinal) &&
            string.Equals(row.LastErrorCode, "reference_missing", StringComparison.OrdinalIgnoreCase);

        row.Status = "pending";
        row.AttemptCount = 0;
        row.NextAttemptAt = null;
        if (!needsReferenceRepair)
        {
            row.LastErrorCode = null;
            row.LastErrorMessage = null;
        }
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DismissConflictAsync(
        string tenantId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        tenantId = Required(tenantId, nameof(tenantId));
        idempotencyKey = Required(idempotencyKey, nameof(idempotencyKey));

        await using var context =
            await contextFactory.CreateDbContextAsync(cancellationToken);

        var row = await context.Set<CloudSyncOutboxEntity>()
            .SingleOrDefaultAsync(
                x => x.TenantId == tenantId &&
                     x.IdempotencyKey == idempotencyKey &&
                     x.Status == "conflict",
                cancellationToken);

        if (row is null)
            return false;

        row.Status = "dismissed";
        row.NextAttemptAt = null;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static (string? LocalId, string? BusinessDate) ReadConflictReferences(
        string payloadJson)
    {
        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            var root = document.RootElement;
            return (
                ReadString(root, "local_id"),
                ReadString(root, "business_date"));
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    private static string? ReadString(JsonObject value, string property)
    {
        if (!value.TryGetPropertyValue(property, out var node) ||
            node is null)
        {
            return null;
        }

        try
        {
            return node.GetValue<string>();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static string? ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

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
