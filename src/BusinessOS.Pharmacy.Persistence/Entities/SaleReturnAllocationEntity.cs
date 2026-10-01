namespace BusinessOS.Pharmacy.Persistence.Entities;

internal sealed class SaleReturnAllocationEntity
{
    public string Id { get; set; } = string.Empty;
    public string SaleReturnLineId { get; set; } = string.Empty;
    public string SaleBatchAllocationId { get; set; } = string.Empty;
    public string ProductBatchId { get; set; } = string.Empty;
    public string? StockMovementId { get; set; }
    public decimal Quantity { get; set; }
    public bool Restocked { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public SaleReturnLineEntity SaleReturnLine { get; set; } = null!;
    public SaleBatchAllocationEntity SaleBatchAllocation { get; set; } = null!;
    public ProductBatchEntity ProductBatch { get; set; } = null!;
}
