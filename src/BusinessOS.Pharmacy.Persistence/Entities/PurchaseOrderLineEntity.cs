namespace BusinessOS.Pharmacy.Persistence.Entities;

internal sealed class PurchaseOrderLineEntity
{
    public string Id { get; set; } = string.Empty;
    public string PurchaseOrderId { get; set; } = string.Empty;
    public string MedicineId { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal OrderedQuantity { get; set; }
    public decimal ReceivedQuantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal LandedCostAllocated { get; set; }
    public decimal LineTotal { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public PurchaseOrderEntity PurchaseOrder { get; set; } = null!;
    public MedicineEntity Medicine { get; set; } = null!;
    public List<GoodsReceiptLineEntity> ReceiptLines { get; set; } = [];
}
