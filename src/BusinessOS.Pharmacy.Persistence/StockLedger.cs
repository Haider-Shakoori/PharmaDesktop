using BusinessOS.Pharmacy.Application.Abstractions.Time;
using BusinessOS.Pharmacy.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace BusinessOS.Pharmacy.Persistence;

public sealed class StockLedger
{
    private readonly IClock _clock;

    public StockLedger(IClock clock) => _clock = clock;

    internal async Task<StockMovementEntity> RecordAsync(
        PharmacyDbContext context,
        ProductBatchEntity batch,
        decimal quantityDelta,
        string movementType,
        string sourceType,
        string sourceId,
        string idempotencyKey,
        string? actorId,
        string? sourceLineId,
        string? reason,
        decimal? unitCost,
        string? metadataJson,
        CancellationToken cancellationToken)
    {
        quantityDelta = Scale(quantityDelta);

        if (quantityDelta == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantityDelta),
                "Stock movement quantity cannot be zero.");
        }

        var existing = await context.Set<StockMovementEntity>()
            .SingleOrDefaultAsync(
                x => x.IdempotencyKey == idempotencyKey,
                cancellationToken);

        if (existing is not null)
        {
            var sameOperation =
                existing.ProductBatchId == batch.Id &&
                existing.SourceType == sourceType &&
                existing.SourceId == sourceId &&
                existing.QuantityDelta == quantityDelta;

            if (!sameOperation)
            {
                throw new InvalidOperationException(
                    "This inventory idempotency key was already used for another operation.");
            }

            return existing;
        }

        var locked = await context.Set<ProductBatchEntity>()
            .SingleAsync(x => x.Id == batch.Id, cancellationToken);

        var after = Scale(locked.AvailableQuantity + quantityDelta);
        if (after < 0)
        {
            throw new InvalidOperationException(
                "This stock movement would make the batch quantity negative.");
        }

        var now = _clock.UtcNow;
        var nextStatus = locked.Status;

        if (after == 0 && nextStatus == "active")
        {
            nextStatus = "depleted";
        }
        else if (
            quantityDelta > 0 &&
            nextStatus == "depleted" &&
            !IsExpired(locked, BusinessDate(now)))
        {
            nextStatus = "active";
        }

        locked.AvailableQuantity = after;
        locked.LastMovementAt = now;
        locked.Status = nextStatus;
        locked.UpdatedAt = now;

        var movement = new StockMovementEntity
        {
            Id = Guid.CreateVersion7().ToString(),
            ProductBatchId = locked.Id,
            MedicineId = locked.MedicineId,
            BranchId = locked.BranchId,
            StockLocationId = locked.StockLocationId,
            MovementType = movementType,
            QuantityDelta = quantityDelta,
            BalanceAfter = after,
            UnitCost = unitCost is null ? null : ScaleMoney(unitCost.Value),
            SourceType = sourceType,
            SourceId = sourceId,
            SourceLineId = sourceLineId,
            Reason = reason,
            IdempotencyKey = idempotencyKey,
            ActorId = actorId,
            OccurredAt = now,
            MetadataJson = metadataJson,
            CreatedAt = now,
            UpdatedAt = now,
        };

        context.Add(movement);
        await context.SaveChangesAsync(cancellationToken);
        return movement;
    }

    internal static decimal Scale(decimal value) =>
        decimal.Round(value, 4, MidpointRounding.AwayFromZero);

    internal static decimal ScaleMoney(decimal value) =>
        decimal.Round(value, 4, MidpointRounding.AwayFromZero);

    internal static bool IsExpired(ProductBatchEntity batch, DateOnly businessDate) =>
        batch.ExpiresAt is not null && batch.ExpiresAt.Value < businessDate;

    internal static DateOnly BusinessDate(DateTimeOffset now) =>
        DateOnly.FromDateTime(now.ToOffset(TimeSpan.FromMinutes(270)).DateTime);
}
