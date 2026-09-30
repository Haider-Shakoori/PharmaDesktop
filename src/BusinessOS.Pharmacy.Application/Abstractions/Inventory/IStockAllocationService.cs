namespace BusinessOS.Pharmacy.Application.Abstractions.Inventory;

public interface IStockAllocationService
{
    Task<IReadOnlyList<StockAllocationResult>> ConsumeFefoAsync(
        StockAllocationRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record StockAllocationRequest(
    string MedicineId,
    string StockLocationId,
    decimal Quantity,
    string SourceType,
    string SourceId,
    string IdempotencyPrefix,
    string? Reason = null,
    bool RequirePriced = false);

public sealed record StockAllocationResult(
    string ProductBatchId,
    decimal Quantity,
    string StockMovementId,
    decimal UnitCost,
    decimal? UnitPrice);
