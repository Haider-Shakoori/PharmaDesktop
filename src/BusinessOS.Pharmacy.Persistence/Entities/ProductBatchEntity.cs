namespace BusinessOS.Pharmacy.Persistence.Entities;

internal sealed class ProductBatchEntity
{
    public string Id { get; set; } = string.Empty;
    public string MedicineId { get; set; } = string.Empty;
    public string? SupplierId { get; set; }
    public string? PurchaseOrderId { get; set; }
    public string? GoodsReceiptId { get; set; }
    public string BranchId { get; set; } = string.Empty;
    public string StockLocationId { get; set; } = string.Empty;
    public string? BatchNumber { get; set; }
    public string BatchKey { get; set; } = string.Empty;
    public DateOnly? ManufacturedAt { get; set; }
    public DateOnly? ExpiresAt { get; set; }
    public string Status { get; set; } = "active";
    public decimal ReceivedQuantity { get; set; }
    public decimal AvailableQuantity { get; set; }
    public decimal PurchaseCost { get; set; }
    public decimal? SalePrice { get; set; }
    public DateTimeOffset? LastMovementAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public MedicineEntity Medicine { get; set; } = null!;
    public BranchEntity Branch { get; set; } = null!;
    public StockLocationEntity StockLocation { get; set; } = null!;
    public List<StockMovementEntity> Movements { get; set; } = [];
    public List<BatchStatusEventEntity> StatusEvents { get; set; } = [];
}
