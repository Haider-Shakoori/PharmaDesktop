namespace BusinessOS.Pharmacy.Persistence.Entities;

internal sealed class SalePaymentEntity
{
    public string Id { get; set; } = string.Empty;
    public string SaleId { get; set; } = string.Empty;
    public string PaymentNumber { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "AFN";
    public string? Reference { get; set; }
    public DateTimeOffset PaidAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public SaleEntity Sale { get; set; } = null!;
}
