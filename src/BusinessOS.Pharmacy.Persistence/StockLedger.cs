using System.Data;
using System.Data.Common;
using System.Globalization;
using BusinessOS.Pharmacy.Application.Abstractions.Inventory;
using BusinessOS.Pharmacy.Application.Abstractions.Time;
using Microsoft.EntityFrameworkCore;

namespace BusinessOS.Pharmacy.Persistence;

public sealed class StockLedger : IStockLedger
{
    private readonly IDbContextFactory<PharmacyDbContext> _contextFactory;
    private readonly IClock _clock;

    public StockLedger(
        IDbContextFactory<PharmacyDbContext> contextFactory,
        IClock clock)
    {
        _contextFactory = contextFactory;
        _clock = clock;
    }

    public async Task<StockMovementItem> RecordAsync(
        string productBatchId,
        decimal quantityDelta,
        string movementType,
        string sourceType,
        string sourceId,
        string idempotencyKey,
        string? actorId,
        string? sourceLineId = null,
        string? reason = null,
        decimal? unitCost = null,
        string? metadataJson = null,
        CancellationToken cancellationToken = default)
    {
        productBatchId = Required(productBatchId, 36, nameof(productBatchId));
        movementType = Required(movementType, 48, nameof(movementType));
        sourceType = Required(sourceType, 100, nameof(sourceType));
        sourceId = Required(sourceId, 64, nameof(sourceId));
        idempotencyKey = Required(idempotencyKey, 191, nameof(idempotencyKey));
        sourceLineId = Optional(sourceLineId, 64, nameof(sourceLineId));
        reason = Optional(reason, 255, nameof(reason));
        actorId = Optional(actorId, 64, nameof(actorId));

        quantityDelta = Scale4(quantityDelta);
        if (quantityDelta == 0m)
        {
            throw new InventoryValidationException("Stock movement quantity cannot be zero.");
        }

        if (unitCost is < 0m)
        {
            throw new InventoryValidationException("Stock movement unit cost cannot be negative.");
        }

        if (metadataJson?.Length > 8000)
        {
            throw new ArgumentOutOfRangeException(nameof(metadataJson));
        }

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var connection = context.Database.GetDbConnection();

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await BeginImmediateAsync(connection, cancellationToken);

        try
        {
            var existing = await FindByIdempotencyAsync(
                connection,
                idempotencyKey,
                cancellationToken);

            if (existing is not null)
            {
                var sameOperation =
                    existing.ProductBatchId == productBatchId &&
                    existing.SourceType == sourceType &&
                    existing.SourceId == sourceId &&
                    Scale4(existing.QuantityDelta) == quantityDelta;

                if (!sameOperation)
                {
                    throw new InventoryValidationException(
                        "This inventory idempotency key was already used for another operation.");
                }

                await CommitAsync(connection, cancellationToken);
                return existing;
            }

            var batch = await LoadBatchAsync(
                connection,
                productBatchId,
                cancellationToken)
                ?? throw new InventoryValidationException("Inventory batch was not found.");

            var after = Scale4(batch.AvailableQuantity + quantityDelta);
            if (after < 0m)
            {
                throw new InventoryValidationException(
                    "This stock movement would make the batch quantity negative.");
            }

            var status = batch.Status;
            if (after == 0m && status == "active")
            {
                status = "depleted";
            }
            else if (
                quantityDelta > 0m &&
                status == "depleted" &&
                !IsExpired(batch.ExpiresAt))
            {
                status = "active";
            }

            var now = _clock.UtcNow;
            await UpdateBatchAsync(
                connection,
                batch.Id,
                after,
                status,
                now,
                cancellationToken);

            var movement = new StockMovementItem(
                Guid.CreateVersion7().ToString(),
                movementType,
                quantityDelta,
                after,
                unitCost is null ? null : Scale4(unitCost.Value),
                sourceType,
                sourceId,
                sourceLineId,
                reason,
                idempotencyKey,
                actorId,
                now,
                metadataJson);

            await InsertMovementAsync(
                connection,
                movement,
                batch,
                now,
                cancellationToken);

            await CommitAsync(connection, cancellationToken);
            return movement;
        }
        catch
        {
            await RollbackQuietlyAsync(connection, cancellationToken);
            throw;
        }
    }

    internal static async Task BeginImmediateAsync(
        DbConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "BEGIN IMMEDIATE;";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    internal static async Task CommitAsync(
        DbConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "COMMIT;";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    internal static async Task RollbackQuietlyAsync(
        DbConnection connection,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "ROLLBACK;";
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch
        {
            // Preserve the original operation error.
        }
    }

    internal static async Task<BatchRow?> LoadBatchAsync(
        DbConnection connection,
        string batchId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
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
            WHERE id = $id
            LIMIT 1;
            """;
        AddParameter(command, "$id", batchId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new BatchRow(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            ReadDecimal(reader, 5),
            ReadDecimal(reader, 6),
            reader.IsDBNull(7) ? null : ReadDecimal(reader, 7),
            reader.IsDBNull(8)
                ? null
                : DateOnly.Parse(
                    Convert.ToString(reader.GetValue(8), CultureInfo.InvariantCulture)!,
                    CultureInfo.InvariantCulture));
    }

    internal static async Task UpdateBatchAsync(
        DbConnection connection,
        string batchId,
        decimal availableQuantity,
        string status,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE product_batches
            SET
                available_quantity = $available,
                status = $status,
                last_movement_at = $lastMovementAt,
                updated_at = $updatedAt
            WHERE id = $id;
            """;
        AddParameter(command, "$available", FormatDecimal(availableQuantity));
        AddParameter(command, "$status", status);
        AddParameter(command, "$lastMovementAt", now.ToString("O", CultureInfo.InvariantCulture));
        AddParameter(command, "$updatedAt", now.ToString("O", CultureInfo.InvariantCulture));
        AddParameter(command, "$id", batchId);

        var affected = await command.ExecuteNonQueryAsync(cancellationToken);
        if (affected != 1)
        {
            throw new InventoryValidationException("Inventory batch could not be updated.");
        }
    }

    internal static async Task InsertMovementAsync(
        DbConnection connection,
        StockMovementItem movement,
        BatchRow batch,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO stock_movements (
                id,
                product_batch_id,
                medicine_id,
                branch_id,
                stock_location_id,
                movement_type,
                quantity_delta,
                balance_after,
                unit_cost,
                source_type,
                source_id,
                source_line_id,
                reason,
                idempotency_key,
                actor_id,
                occurred_at,
                metadata,
                created_at,
                updated_at
            )
            VALUES (
                $id,
                $productBatchId,
                $medicineId,
                $branchId,
                $stockLocationId,
                $movementType,
                $quantityDelta,
                $balanceAfter,
                $unitCost,
                $sourceType,
                $sourceId,
                $sourceLineId,
                $reason,
                $idempotencyKey,
                $actorId,
                $occurredAt,
                $metadata,
                $createdAt,
                $updatedAt
            );
            """;

        AddParameter(command, "$id", movement.Id);
        AddParameter(command, "$productBatchId", batch.Id);
        AddParameter(command, "$medicineId", batch.MedicineId);
        AddParameter(command, "$branchId", batch.BranchId);
        AddParameter(command, "$stockLocationId", batch.StockLocationId);
        AddParameter(command, "$movementType", movement.MovementType);
        AddParameter(command, "$quantityDelta", FormatDecimal(movement.QuantityDelta));
        AddParameter(command, "$balanceAfter", FormatDecimal(movement.BalanceAfter));
        AddParameter(command, "$unitCost",
            movement.UnitCost is null
                ? DBNull.Value
                : FormatDecimal(movement.UnitCost.Value));
        AddParameter(command, "$sourceType", movement.SourceType);
        AddParameter(command, "$sourceId", movement.SourceId);
        AddParameter(command, "$sourceLineId", (object?)movement.SourceLineId ?? DBNull.Value);
        AddParameter(command, "$reason", (object?)movement.Reason ?? DBNull.Value);
        AddParameter(command, "$idempotencyKey", movement.IdempotencyKey);
        AddParameter(command, "$actorId", (object?)movement.ActorId ?? DBNull.Value);
        AddParameter(command, "$occurredAt", movement.OccurredAt.ToString("O", CultureInfo.InvariantCulture));
        AddParameter(command, "$metadata", (object?)movement.MetadataJson ?? DBNull.Value);
        AddParameter(command, "$createdAt", now.ToString("O", CultureInfo.InvariantCulture));
        AddParameter(command, "$updatedAt", now.ToString("O", CultureInfo.InvariantCulture));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    internal static decimal Scale4(decimal value) =>
        Math.Round(value, 4, MidpointRounding.AwayFromZero);

    internal static string FormatDecimal(decimal value) =>
        Scale4(value).ToString("0.0000", CultureInfo.InvariantCulture);

    internal static decimal ReadDecimal(DbDataReader reader, int ordinal)
    {
        var value = reader.GetValue(ordinal);
        if (value is decimal decimalValue)
        {
            return Scale4(decimalValue);
        }

        return Scale4(decimal.Parse(
            Convert.ToString(value, CultureInfo.InvariantCulture)!,
            NumberStyles.Number,
            CultureInfo.InvariantCulture));
    }

    internal static void AddParameter(
        DbCommand command,
        string name,
        object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private async Task<StockMovementItem?> FindByIdempotencyAsync(
        DbConnection connection,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                id,
                product_batch_id,
                movement_type,
                quantity_delta,
                balance_after,
                unit_cost,
                source_type,
                source_id,
                source_line_id,
                reason,
                idempotency_key,
                actor_id,
                occurred_at,
                metadata
            FROM stock_movements
            WHERE idempotency_key = $key
            LIMIT 1;
            """;
        AddParameter(command, "$key", idempotencyKey);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new StockMovementItem(
            reader.GetString(0),
            reader.GetString(2),
            ReadDecimal(reader, 3),
            ReadDecimal(reader, 4),
            reader.IsDBNull(5) ? null : ReadDecimal(reader, 5),
            reader.GetString(6),
            reader.GetString(7),
            reader.IsDBNull(8) ? null : reader.GetString(8),
            reader.IsDBNull(9) ? null : reader.GetString(9),
            reader.GetString(10),
            reader.IsDBNull(11) ? null : reader.GetString(11),
            DateTimeOffset.Parse(
                Convert.ToString(reader.GetValue(12), CultureInfo.InvariantCulture)!,
                CultureInfo.InvariantCulture),
            reader.IsDBNull(13) ? null : reader.GetString(13))
        {
        };
    }

    private bool IsExpired(DateOnly? expiresAt)
    {
        if (expiresAt is null)
        {
            return false;
        }

        var businessDate = DateOnly.FromDateTime(
            _clock.UtcNow.ToOffset(TimeSpan.FromMinutes(270)).DateTime);

        return expiresAt.Value < businessDate;
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

    internal sealed record BatchRow(
        string Id,
        string MedicineId,
        string BranchId,
        string StockLocationId,
        string Status,
        decimal AvailableQuantity,
        decimal PurchaseCost,
        decimal? SalePrice,
        DateOnly? ExpiresAt);
}
