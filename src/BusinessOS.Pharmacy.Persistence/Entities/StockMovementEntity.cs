namespace BusinessOS.Pharmacy.Persistence.Entities;

internal sealed class StockMovementEntity
{
    public string Id { get; set; } = string.Empty;
    public string ProductBatchId { get; set; } = string.Empty;
    public string MedicineId { get; set; } = string.Empty;
    public string BranchId { get; set; } = string.Empty;
    public string StockLocationId { get; set; } = string.Empty;
    public string MovementType { get; set; } = string.Empty;
    public decimal QuantityDelta { get; set; }
    public decimal BalanceAfter { get; set; }
    public decimal? UnitCost { get; set; }
    public string SourceType { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
    public string? SourceLineId { get; set; }
    public string? Reason { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string? ActorId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string? MetadataJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ProductBatchEntity ProductBatch { get; set; } = null!;
}
