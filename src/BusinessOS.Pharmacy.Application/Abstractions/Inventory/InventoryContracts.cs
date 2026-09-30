namespace BusinessOS.Pharmacy.Application.Abstractions.Inventory;

public sealed record InventoryPolicy(
    int LowStockThreshold = 10,
    int NearExpiryDays = 90,
    bool FefoEnabled = true,
    bool BlockExpiredSales = true)
{
    public void Validate()
    {
        if (LowStockThreshold < 0 || LowStockThreshold > 1_000_000)
        {
            throw new ArgumentOutOfRangeException(nameof(LowStockThreshold));
        }

        if (NearExpiryDays < 1 || NearExpiryDays > 3650)
        {
            throw new ArgumentOutOfRangeException(nameof(NearExpiryDays));
        }
    }
}

public sealed record InventorySearchFilter(
    string? Search = null,
    string? Status = null,
    string? Expiry = null,
    string? StockLocationId = null,
    int Take = 500);

public sealed record InventoryBatchListItem(
    string Id,
    string MedicineId,
    string MedicineCode,
    string BrandName,
    string? GenericName,
    string? Strength,
    string? BatchNumber,
    string BranchName,
    string LocationName,
    string Status,
    DateOnly? ExpiresAt,
    decimal ReceivedQuantity,
    decimal AvailableQuantity,
    decimal PurchaseCost,
    decimal? SalePrice,
    decimal ReorderLevel,
    DateTimeOffset? LastMovementAt)
{
    public bool IsExpired(DateOnly businessDate) =>
        ExpiresAt is not null && ExpiresAt.Value < businessDate;

    public bool IsNearExpiry(DateOnly businessDate, int nearExpiryDays) =>
        ExpiresAt is not null &&
        ExpiresAt.Value >= businessDate &&
        ExpiresAt.Value <= businessDate.AddDays(nearExpiryDays);
}

public sealed record InventoryBatchDetails(
    InventoryBatchListItem Batch,
    IReadOnlyList<StockMovementItem> Movements,
    IReadOnlyList<BatchStatusEventItem> StatusEvents);

public sealed record StockMovementItem(
    string Id,
    string MovementType,
    decimal QuantityDelta,
    decimal BalanceAfter,
    decimal? UnitCost,
    string SourceType,
    string SourceId,
    string? SourceLineId,
    string? Reason,
    string IdempotencyKey,
    string? ActorId,
    DateTimeOffset OccurredAt,
    string? MetadataJson);

public sealed record BatchStatusEventItem(
    string Id,
    string FromStatus,
    string ToStatus,
    string Reason,
    string? ActorId,
    DateTimeOffset ChangedAt);

public sealed record BranchReference(
    string Id,
    string Code,
    string Name,
    bool IsDefault,
    bool IsActive);

public sealed record StockLocationReference(
    string Id,
    string BranchId,
    string BranchName,
    string Code,
    string Name,
    string Kind,
    bool IsDefault,
    bool IsActive);

public sealed record InventoryMedicineReference(
    string Id,
    string MedicineCode,
    string BrandName,
    string? GenericName,
    string? Strength,
    bool BatchTrackingRequired,
    bool ExpiryTrackingRequired,
    bool IsActive);

public sealed record InventoryReferenceData(
    IReadOnlyList<BranchReference> Branches,
    IReadOnlyList<StockLocationReference> Locations,
    IReadOnlyList<InventoryMedicineReference> Medicines,
    InventoryPolicy Policy);

public sealed record CreateOpeningStockRequest(
    string MedicineId,
    string StockLocationId,
    string? BatchNumber,
    DateOnly? ManufacturedAt,
    DateOnly? ExpiresAt,
    decimal Quantity,
    decimal PurchaseCost,
    decimal? SalePrice,
    string Reason,
    string IdempotencyKey);

public sealed record InventoryAdjustmentRequest(
    string ProductBatchId,
    decimal QuantityDelta,
    string ReasonCode,
    string Reason,
    string IdempotencyKey);

public sealed record ChangeBatchStatusRequest(
    string ProductBatchId,
    string Status,
    string Reason);

public sealed record CreateStockLocationRequest(
    string BranchId,
    string Code,
    string Name,
    string Kind = "store");

public sealed record StockAllocationRequest(
    string MedicineId,
    string StockLocationId,
    decimal Quantity,
    string MovementType,
    string SourceType,
    string SourceId,
    string IdempotencyPrefix,
    string? ActorId,
    string? Reason = null,
    bool RequirePriced = false);

public sealed record StockAllocationItem(
    string ProductBatchId,
    decimal Quantity,
    string StockMovementId,
    decimal PurchaseCost,
    decimal? SalePrice);
