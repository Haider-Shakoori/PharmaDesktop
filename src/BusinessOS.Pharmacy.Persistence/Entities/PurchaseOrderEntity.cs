namespace BusinessOS.Pharmacy.Persistence.Entities;

internal sealed class PurchaseOrderEntity
{
    public string Id { get; set; } = string.Empty;
    public string SupplierId { get; set; } = string.Empty;
    public string Number { get; set; } = string.Empty;
    public string Status { get; set; } = "draft";
    public DateOnly OrderDate { get; set; }
    public DateOnly? ExpectedDate { get; set; }
    public string Currency { get; set; } = "AFN";
    public decimal Subtotal { get; set; }
    public decimal DiscountTotal { get; set; }
    public decimal LandedCostTotal { get; set; }
    public decimal GrandTotal { get; set; }
    public string? Notes { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string? ApprovedBy { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public SupplierEntity Supplier { get; set; } = null!;
    public List<PurchaseOrderLineEntity> Lines { get; set; } = [];
    public List<GoodsReceiptEntity> Receipts { get; set; } = [];
    public List<PurchaseInvoiceEntity> Invoices { get; set; } = [];
}
