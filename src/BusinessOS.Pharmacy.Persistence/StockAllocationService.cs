using System.Data;
using System.Data.Common;
using System.Globalization;
using BusinessOS.Pharmacy.Application.Abstractions.Inventory;
using BusinessOS.Pharmacy.Application.Abstractions.Time;
using Microsoft.EntityFrameworkCore;

namespace BusinessOS.Pharmacy.Persistence;

public sealed class StockAllocationService : IStockAllocationService
{
    private readonly IDbContextFactory<PharmacyDbContext> _contextFactory;
    private readonly IClock _clock;

    public StockAllocationService(
        IDbContextFactory<PharmacyDbContext> contextFactory,
        IClock clock)
    {
        _contextFactory = contextFactory;
        _clock = clock;
    }

    public async Task<IReadOnlyList<StockAllocationItem>> ConsumeFefoAsync(
        StockAllocationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var medicineId = Required(request.MedicineId, 36, nameof(request.MedicineId));
        var locationId = Required(request.StockLocationId, 36, nameof(request.StockLocationId));
        var movementType = Required(request.MovementType, 48, nameof(request.MovementType));
        var sourceType = Required(request.SourceType, 100, nameof(request.SourceType));
        var sourceId = Required(request.SourceId, 64, nameof(request.SourceId));
        var prefix = Required(request.IdempotencyPrefix, 145, nameof(request.IdempotencyPrefix));
        var actorId = Optional(request.ActorId, 64, nameof(request.ActorId));
        var reason = Optional(request.Reason, 255, nameof(request.Reason));
        var quantity = StockLedger.Scale4(request.Quantity);

        if (quantity <= 0m)
        {
            throw new InventoryValidationException(
                "Requested stock quantity must be greater than zero.");
        }

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var connection = context.Database.GetDbConnection();

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await StockLedger.BeginImmediateAsync(connection, cancellationToken);

        try
        {
            var replay = await ReadExistingAllocationsAsync(
                connection,
                prefix,
                movementType,
                sourceType,
                sourceId,
                quantity,
                cancellationToken);

            if (replay is not null)
            {
                await StockLedger.CommitAsync(connection, cancellationToken);
                return replay;
            }

            var businessDate = DateOnly.FromDateTime(
                _clock.UtcNow.ToOffset(TimeSpan.FromMinutes(270)).DateTime);

            var batches = await ReadEligibleBatchesAsync(
                connection,
                medicineId,
                locationId,
                businessDate,
                request.RequirePriced,
                cancellationToken);

            var available = batches.Sum(x => x.AvailableQuantity);
            if (available < quantity)
            {
                throw new InventoryValidationException(
                    "Insufficient eligible stock. Expired, quarantined, recalled, damaged and depleted batches are excluded.");
            }

            var remaining = quantity;
            var now = _clock.UtcNow;
            var allocations = new List<StockAllocationItem>();

            foreach (var batch in batches)
            {
                if (remaining == 0m)
                {
                    break;
                }

                var take = Math.Min(batch.AvailableQuantity, remaining);
                take = StockLedger.Scale4(take);
                var after = StockLedger.Scale4(batch.AvailableQuantity - take);
                var status = after == 0m ? "depleted" : batch.Status;

                await StockLedger.UpdateBatchAsync(
                    connection,
                    batch.Id,
                    after,
                    status,
                    now,
                    cancellationToken);

                var movementId = Guid.CreateVersion7().ToString();
                var idempotencyKey = $"{prefix}:{batch.Id}";
                var movement = new StockMovementItem(
                    movementId,
                    movementType,
                    -take,
                    after,
                    batch.PurchaseCost,
                    sourceType,
                    sourceId,
                    null,
                    reason ?? "FEFO stock allocation",
                    idempotencyKey,
                    actorId,
                    now,
                    null);

                await StockLedger.InsertMovementAsync(
                    connection,
                    movement,
                    batch,
                    now,
                    cancellationToken);

                allocations.Add(new StockAllocationItem(
                    batch.Id,
                    take,
                    movementId,
                    batch.PurchaseCost,
                    batch.SalePrice));

                remaining = StockLedger.Scale4(remaining - take);
            }

            if (remaining != 0m)
            {
                throw new InventoryValidationException(
                    "Inventory allocation did not satisfy the requested quantity.");
            }

            await StockLedger.CommitAsync(connection, cancellationToken);
            return allocations;
        }
        catch
        {
            await StockLedger.RollbackQuietlyAsync(connection, cancellationToken);
            throw;
        }
    }

    private static async Task<IReadOnlyList<StockAllocationItem>?> ReadExistingAllocationsAsync(
        DbConnection connection,
        string prefix,
        string movementType,
        string sourceType,
        string sourceId,
        decimal requestedQuantity,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                m.product_batch_id,
                m.quantity_delta,
                m.id,
                m.unit_cost,
                b.sale_price,
                m.movement_type,
                m.source_type,
                m.source_id
            FROM stock_movements m
            INNER JOIN product_batches b ON b.id = m.product_batch_id
            WHERE instr(m.idempotency_key, $prefix) = 1
            ORDER BY m.occurred_at, m.id;
            """;
        StockLedger.AddParameter(command, "$prefix", prefix + ":");

        var allocations = new List<StockAllocationItem>();
        decimal consumed = 0m;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var existingMovementType = reader.GetString(5);
            var existingSourceType = reader.GetString(6);
            var existingSourceId = reader.GetString(7);

            if (!string.Equals(existingMovementType, movementType, StringComparison.Ordinal) ||
                !string.Equals(existingSourceType, sourceType, StringComparison.Ordinal) ||
                !string.Equals(existingSourceId, sourceId, StringComparison.Ordinal))
            {
                throw new InventoryValidationException(
                    "This FEFO idempotency prefix was already used for another operation.");
            }

            var delta = StockLedger.ReadDecimal(reader, 1);
            if (delta >= 0m)
            {
                throw new InventoryValidationException(
                    "Existing FEFO allocation contains an invalid stock movement.");
            }

            var quantity = StockLedger.Scale4(-delta);
            consumed = StockLedger.Scale4(consumed + quantity);

            allocations.Add(new StockAllocationItem(
                reader.GetString(0),
                quantity,
                reader.GetString(2),
                reader.IsDBNull(3) ? 0m : StockLedger.ReadDecimal(reader, 3),
                reader.IsDBNull(4) ? null : StockLedger.ReadDecimal(reader, 4)));
        }

        if (allocations.Count == 0)
        {
            return null;
        }

        if (consumed != requestedQuantity)
        {
            throw new InventoryValidationException(
                "This FEFO idempotency prefix was already committed for a different quantity.");
        }

        return allocations;
    }

    private static async Task<IReadOnlyList<StockLedger.BatchRow>> ReadEligibleBatchesAsync(
        DbConnection connection,
        string medicineId,
        string locationId,
        DateOnly businessDate,
        bool requirePriced,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT
                id,
                medicine_id,
                branch_id,
                stock_location_id,
                status,
                available_quantity,
                purchase_cost,
                sale_price,
                expires_at
            FROM product_batches
            WHERE medicine_id = $medicineId
              AND stock_location_id = $locationId
              AND status = 'active'
              AND CAST(available_quantity AS NUMERIC) > 0
              AND (expires_at IS NULL OR date(expires_at) >= date($businessDate))
              {(requirePriced ? "AND sale_price IS NOT NULL" : string.Empty)}
            ORDER BY
                CASE WHEN expires_at IS NULL THEN 1 ELSE 0 END,
                date(expires_at),
                created_at,
                id;
            """;
        StockLedger.AddParameter(command, "$medicineId", medicineId);
        StockLedger.AddParameter(command, "$locationId", locationId);
        StockLedger.AddParameter(
            command,
            "$businessDate",
            businessDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        var result = new List<StockLedger.BatchRow>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new StockLedger.BatchRow(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                StockLedger.ReadDecimal(reader, 5),
                StockLedger.ReadDecimal(reader, 6),
                reader.IsDBNull(7) ? null : StockLedger.ReadDecimal(reader, 7),
                reader.IsDBNull(8)
                    ? null
                    : DateOnly.Parse(
                        Convert.ToString(reader.GetValue(8), CultureInfo.InvariantCulture)!,
                        CultureInfo.InvariantCulture)));
        }

        return result;
    }

    private static string Required(string value, int maxLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        value = value.Trim();

        if (value.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }

        return value;
    }

    private static string? Optional(string? value, int maxLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        value = value.Trim();
        if (value.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }

        return value;
    }
}
