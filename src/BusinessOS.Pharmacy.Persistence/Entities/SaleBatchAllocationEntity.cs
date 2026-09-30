namespace BusinessOS.Pharmacy.Persistence.Entities;

internal sealed class SaleBatchAllocationEntity
{
    public string Id { get; set; } = string.Empty;
    public string SaleLineId { get; set; } = string.Empty;
    public string ProductBatchId { get; set; } = string.Empty;
    public string StockMovementId { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public SaleLineEntity SaleLine { get; set; } = null!;
    public ProductBatchEntity ProductBatch { get; set; } = null!;
}
