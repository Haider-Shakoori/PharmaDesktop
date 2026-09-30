namespace BusinessOS.Pharmacy.Application.Abstractions.Inventory;

public interface IStockLedger
{
    Task<StockMovementItem> RecordAsync(
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
        CancellationToken cancellationToken = default);
}
