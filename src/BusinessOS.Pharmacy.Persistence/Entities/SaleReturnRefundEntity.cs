namespace BusinessOS.Pharmacy.Persistence.Entities;

internal sealed class SaleReturnRefundEntity
{
    public string Id { get; set; } = string.Empty;
    public string SaleReturnId { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "AFN";
    public string? Reference { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public SaleReturnEntity SaleReturn { get; set; } = null!;
}
