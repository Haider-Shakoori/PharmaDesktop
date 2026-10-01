namespace BusinessOS.Pharmacy.Persistence.Entities;

internal sealed class SaleEntity
{
    public string Id { get; set; } = string.Empty;
    public string SaleNumber { get; set; } = string.Empty;
    public string StockLocationId { get; set; } = string.Empty;
    public string? CustomerId { get; set; }
    public string? PrescriptionReference { get; set; }
    public string? PrescriberName { get; set; }
    public DateOnly? PrescriptionDate { get; set; }
    public DateOnly BusinessDate { get; set; }
    public string Status { get; set; } = "processing";
    public string Currency { get; set; } = "AFN";
    public decimal Subtotal { get; set; }
    public decimal DiscountTotal { get; set; }
    public decimal TaxTotal { get; set; }
    public decimal GrandTotal { get; set; }
    public decimal PaidTotal { get; set; }
    public decimal DueTotal { get; set; }
    public decimal ChangeTotal { get; set; }
    public string PaymentStatus { get; set; } = "unpaid";
    public string IdempotencyKey { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset? HeldAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public StockLocationEntity StockLocation { get; set; } = null!;
    public CustomerEntity? Customer { get; set; }
    public List<SaleLineEntity> Lines { get; set; } = [];
    public List<SalePaymentEntity> Payments { get; set; } = [];
}
