namespace BusinessOS.Pharmacy.Persistence.Entities;

internal sealed class PurchaseInvoiceEntity
{
    public string Id { get; set; } = string.Empty;
    public string SupplierId { get; set; } = string.Empty;
    public string? PurchaseOrderId { get; set; }
    public string? GoodsReceiptId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public string? SupplierInvoiceNumber { get; set; }
    public DateOnly InvoiceDate { get; set; }
    public DateOnly? DueDate { get; set; }
    public string Currency { get; set; } = "AFN";
    public string Status { get; set; } = "open";
    public decimal Subtotal { get; set; }
    public decimal DiscountTotal { get; set; }
    public decimal LandedCostTotal { get; set; }
    public decimal GrandTotal { get; set; }
    public decimal PaidTotal { get; set; }
    public decimal BalanceDue { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public SupplierEntity Supplier { get; set; } = null!;
    public PurchaseOrderEntity? PurchaseOrder { get; set; }
    public GoodsReceiptEntity? GoodsReceipt { get; set; }
    public List<SupplierPaymentEntity> Payments { get; set; } = [];
}
