namespace BusinessOS.Pharmacy.Persistence.Entities;

internal sealed class SupplierPaymentEntity
{
    public string Id { get; set; } = string.Empty;
    public string PurchaseInvoiceId { get; set; } = string.Empty;
    public string SupplierId { get; set; } = string.Empty;
    public string PaymentNumber { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "AFN";
    public string Method { get; set; } = "cash";
    public string? Reference { get; set; }
    public DateTimeOffset PaidAt { get; set; }
    public string? IdempotencyKey { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public PurchaseInvoiceEntity PurchaseInvoice { get; set; } = null!;
    public SupplierEntity Supplier { get; set; } = null!;
}
