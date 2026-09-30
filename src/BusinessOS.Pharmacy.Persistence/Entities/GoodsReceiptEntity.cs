namespace BusinessOS.Pharmacy.Persistence.Entities;

internal sealed class GoodsReceiptEntity
{
    public string Id { get; set; } = string.Empty;
    public string PurchaseOrderId { get; set; } = string.Empty;
    public string SupplierId { get; set; } = string.Empty;
    public string? StockLocationId { get; set; }
    public string ReceiptNumber { get; set; } = string.Empty;
    public string Status { get; set; } = "pending_inventory";
    public DateTimeOffset ReceivedAt { get; set; }
    public string? IdempotencyKey { get; set; }
    public DateTimeOffset? InventoryPostedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public PurchaseOrderEntity PurchaseOrder { get; set; } = null!;
    public SupplierEntity Supplier { get; set; } = null!;
    public StockLocationEntity? StockLocation { get; set; }
    public List<GoodsReceiptLineEntity> Lines { get; set; } = [];
    public List<PurchaseInvoiceEntity> Invoices { get; set; } = [];
}
