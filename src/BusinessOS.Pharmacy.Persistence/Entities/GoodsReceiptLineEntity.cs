namespace BusinessOS.Pharmacy.Persistence.Entities;

internal sealed class GoodsReceiptLineEntity
{
    public string Id { get; set; } = string.Empty;
    public string GoodsReceiptId { get; set; } = string.Empty;
    public string PurchaseOrderLineId { get; set; } = string.Empty;
    public string MedicineId { get; set; } = string.Empty;
    public decimal ReceivedQuantity { get; set; }
    public decimal BonusQuantity { get; set; }
    public string? BatchNumber { get; set; }
    public DateOnly? ManufacturedAt { get; set; }
    public DateOnly? ExpiresAt { get; set; }
    public decimal UnitCost { get; set; }
    public decimal? SalePrice { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public GoodsReceiptEntity GoodsReceipt { get; set; } = null!;
    public PurchaseOrderLineEntity PurchaseOrderLine { get; set; } = null!;
    public MedicineEntity Medicine { get; set; } = null!;
}
