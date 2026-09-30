namespace BusinessOS.Pharmacy.Persistence.Entities;

internal sealed class InventoryAdjustmentLineEntity
{
    public string Id { get; set; } = string.Empty;
    public string InventoryAdjustmentId { get; set; } = string.Empty;
    public string ProductBatchId { get; set; } = string.Empty;
    public decimal QuantityDelta { get; set; }
    public string? Reason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public InventoryAdjustmentEntity InventoryAdjustment { get; set; } = null!;
    public ProductBatchEntity ProductBatch { get; set; } = null!;
}
