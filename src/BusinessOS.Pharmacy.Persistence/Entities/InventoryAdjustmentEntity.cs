namespace BusinessOS.Pharmacy.Persistence.Entities;

internal sealed class InventoryAdjustmentEntity
{
    public string Id { get; set; } = string.Empty;
    public string Number { get; set; } = string.Empty;
    public string StockLocationId { get; set; } = string.Empty;
    public string ReasonCode { get; set; } = string.Empty;
    public string Status { get; set; } = "draft";
    public string? Notes { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string? PostedBy { get; set; }
    public DateTimeOffset? PostedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public StockLocationEntity StockLocation { get; set; } = null!;
    public List<InventoryAdjustmentLineEntity> Lines { get; set; } = [];
}
