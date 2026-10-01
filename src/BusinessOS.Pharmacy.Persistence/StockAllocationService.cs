using BusinessOS.Pharmacy.Application.Abstractions.Authentication;
using BusinessOS.Pharmacy.Application.Abstractions.Inventory;
using BusinessOS.Pharmacy.Application.Abstractions.Time;
using BusinessOS.Pharmacy.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace BusinessOS.Pharmacy.Persistence;

public sealed class StockAllocationService : IStockAllocationService
{
    private readonly IDbContextFactory<PharmacyDbContext> _contextFactory;
    private readonly IPermissionAuthorizer _permissions;
    private readonly IUserSessionService _sessions;
    private readonly IClock _clock;
    private readonly StockLedger _ledger;

    public StockAllocationService(
        IDbContextFactory<PharmacyDbContext> contextFactory,
        IPermissionAuthorizer permissions,
        IUserSessionService sessions,
        IClock clock,
        StockLedger ledger)
    {
        _contextFactory = contextFactory;
        _permissions = permissions;
        _sessions = sessions;
        _clock = clock;
        _ledger = ledger;
    }

    public async Task<IReadOnlyList<StockAllocationResult>> ConsumeFefoAsync(
        StockAllocationRequest request,
        CancellationToken cancellationToken = default)
    {
        _permissions.Demand("pos.sell");
        Validate(request);

        var requestedQuantity = StockLedger.Scale(request.Quantity);
        var actorId = _sessions.Current?.UserId
            ?? throw new InvalidOperationException("A pharmacy user must be signed in.");
        var prefix = request.IdempotencyPrefix.Trim();

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await InventoryWriteTransaction.BeginAsync(
            context,
            cancellationToken);

        var existing = await context.Set<StockMovementEntity>()
            .AsNoTracking()
            .Include(x => x.ProductBatch)
            .Where(x =>
                x.MovementType == "sale" &&
                x.IdempotencyKey.StartsWith(prefix + ":"))
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);

        if (existing.Count > 0)
        {
            var alreadyConsumed = StockLedger.Scale(
                existing.Sum(x => decimal.Abs(x.QuantityDelta)));

            if (alreadyConsumed != requestedQuantity ||
                existing.Any(x =>
                    x.SourceType != request.SourceType ||
                    x.SourceId != request.SourceId ||
                    x.QuantityDelta >= 0))
            {
                throw new InvalidOperationException(
                    "The inventory idempotency prefix was already used for another allocation.");
            }

            await transaction.CommitAsync(cancellationToken);

            return existing.Select(x => new StockAllocationResult(
                x.ProductBatchId,
                decimal.Abs(x.QuantityDelta),
                x.Id,
                x.UnitCost ?? 0m,
                x.ProductBatch.SalePrice)).ToList();
        }

        var businessDate = StockLedger.BusinessDate(_clock.UtcNow);

        var query = context.Set<ProductBatchEntity>()
            .Include(x => x.Medicine)
            .Where(x =>
                x.MedicineId == request.MedicineId &&
                x.StockLocationId == request.StockLocationId &&
                x.Status == "active" &&
                (x.ExpiresAt == null || x.ExpiresAt >= businessDate));

        if (request.RequirePriced)
        {
            query = query.Where(x => x.SalePrice != null);
        }

        var candidates = await query
            .OrderBy(x => x.ExpiresAt == null)
            .ThenBy(x => x.ExpiresAt)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

        var remaining = requestedQuantity;
        var allocations = new List<StockAllocationResult>();

        foreach (var batch in candidates.Where(x => x.AvailableQuantity > 0m))
        {
            if (remaining == 0m)
            {
                break;
            }

            var take = batch.AvailableQuantity < remaining
                ? batch.AvailableQuantity
                : remaining;
            take = StockLedger.Scale(take);

            var movement = await _ledger.RecordAsync(
                context,
                batch,
                -take,
                "sale",
                request.SourceType.Trim(),
                request.SourceId.Trim(),
                $"{prefix}:{batch.Id}",
                actorId,
                null,
                string.IsNullOrWhiteSpace(request.Reason)
                    ? "FEFO stock allocation"
                    : request.Reason.Trim(),
                batch.PurchaseCost,
                null,
                cancellationToken);

            allocations.Add(new StockAllocationResult(
                batch.Id,
                take,
                movement.Id,
                batch.PurchaseCost,
                batch.SalePrice));

            remaining = StockLedger.Scale(remaining - take);
        }

        if (remaining != 0m)
        {
            throw new InvalidOperationException(
                "Insufficient eligible stock. Expired, quarantined, recalled and damaged batches are excluded.");
        }

        await transaction.CommitAsync(cancellationToken);
        return allocations;
    }

    private static void Validate(StockAllocationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.MedicineId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.StockLocationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SourceType);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.IdempotencyPrefix);

        if (request.Quantity <= 0 || request.Quantity > 999_999_999m)
        {
            throw new ArgumentOutOfRangeException(nameof(request.Quantity));
        }

        if (request.SourceType.Trim().Length > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(request.SourceType));
        }

        if (request.SourceId.Trim().Length > 64)
        {
            throw new ArgumentOutOfRangeException(nameof(request.SourceId));
        }

        if (request.IdempotencyPrefix.Trim().Length > 145)
        {
            throw new ArgumentOutOfRangeException(nameof(request.IdempotencyPrefix));
        }

        if (request.Reason?.Trim().Length > 255)
        {
            throw new ArgumentOutOfRangeException(nameof(request.Reason));
        }
    }
}
