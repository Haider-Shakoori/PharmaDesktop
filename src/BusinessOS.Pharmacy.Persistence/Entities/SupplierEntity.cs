namespace BusinessOS.Pharmacy.Persistence.Entities;

internal sealed class SupplierEntity
{
    public string Id { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? ContactPerson { get; set; }
    public string? Phone { get; set; }
    public string? Whatsapp { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? Province { get; set; }
    public int PaymentTermsDays { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public List<PurchaseOrderEntity> PurchaseOrders { get; set; } = [];
    public List<GoodsReceiptEntity> GoodsReceipts { get; set; } = [];
    public List<PurchaseInvoiceEntity> Invoices { get; set; } = [];
    public List<SupplierPaymentEntity> Payments { get; set; } = [];
}
