namespace BusinessOS.Pharmacy.Application.Abstractions.Inventory;

public interface IInventoryService
{
    Task EnsureDefaultsAsync(CancellationToken cancellationToken = default);

    Task<InventoryReferenceData> GetReferenceDataAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<InventoryBatchListItem>> SearchBatchesAsync(
        InventoryBatchFilter filter,
        CancellationToken cancellationToken = default);

    Task<InventoryBatchDetail?> GetBatchAsync(
        string id,
        CancellationToken cancellationToken = default);

    Task<string> CreateOpeningStockAsync(
        CreateOpeningStockRequest request,
        CancellationToken cancellationToken = default);

    Task<InventoryAdjustmentResult> AdjustAsync(
        InventoryAdjustmentRequest request,
        CancellationToken cancellationToken = default);

    Task ChangeBatchStatusAsync(
        ChangeBatchStatusRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record InventoryBatchFilter(
    string? Search = null,
    string? Status = null,
    string? Expiry = null,
    string? StockLocationId = null,
    DateOnly? BusinessDate = null,
    int NearExpiryDays = 90,
    int Take = 500);

public sealed record InventoryReferenceData(
    IReadOnlyList<BranchReferenceItem> Branches,
    IReadOnlyList<StockLocationReferenceItem> Locations);

public sealed record BranchReferenceItem(
    string Id,
    string Code,
    string Name,
    bool IsDefault,
    bool IsActive);

public sealed record StockLocationReferenceItem(
    string Id,
    string BranchId,
    string BranchName,
    string Code,
    string Name,
    string Kind,
    bool IsDefault,
    bool IsActive);

public sealed record InventoryBatchListItem(
    string Id,
    string MedicineId,
    string MedicineCode,
    string BrandName,
    string? GenericName,
    string? Strength,
    string StockLocationId,
    string StockLocationName,
    string? BatchNumber,
    DateOnly? ManufacturedAt,
    DateOnly? ExpiresAt,
    string Status,
    decimal ReceivedQuantity,
    decimal AvailableQuantity,
    decimal PurchaseCost,
    decimal? SalePrice,
    DateTimeOffset? LastMovementAt,
    bool IsExpired,
    bool IsNearExpiry,
    bool IsSellable);

public sealed record InventoryBatchDetail(
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
    DateTimeOffset OccurredAt);

public sealed record BatchStatusEventItem(
    string Id,
    string FromStatus,
    string ToStatus,
    string Reason,
    string? ActorId,
    DateTimeOffset ChangedAt);

public sealed record CreateOpeningStockRequest(
    string MedicineId,
    string StockLocationId,
    string? BatchNumber,
    DateOnly? ManufacturedAt,
    DateOnly? ExpiresAt,
    decimal Quantity,
    decimal PurchaseCost,
    decimal? SalePrice,
    string? Notes);

public sealed record InventoryAdjustmentRequest(
    string ProductBatchId,
    decimal QuantityDelta,
    string ReasonCode,
    string Reason);

public sealed record InventoryAdjustmentResult(
    string AdjustmentId,
    string Number,
    decimal BalanceAfter,
    DateTimeOffset PostedAt);

public sealed record ChangeBatchStatusRequest(
    string ProductBatchId,
    string Status,
    string Reason);
