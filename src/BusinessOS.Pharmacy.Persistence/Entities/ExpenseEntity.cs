namespace BusinessOS.Pharmacy.Persistence.Entities;
internal sealed class ExpenseEntity
{
    public string Id { get; set; } = string.Empty;
    public string ExpenseNumber { get; set; } = string.Empty;
    public string ExpenseAccountId { get; set; } = string.Empty;
    public string PaymentAccountId { get; set; } = string.Empty;
    public string? StockLocationId { get; set; }
    public DateOnly BusinessDate { get; set; }
    public string Currency { get; set; } = "AFN";
    public decimal Amount { get; set; }
    public string? Payee { get; set; }
    public string? Reference { get; set; }
    public string? Notes { get; set; }
    public string Status { get; set; } = "posted";
    public string IdempotencyKey { get; set; } = string.Empty;
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset PostedAt { get; set; }
    public DateTimeOffset? ReversedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public LedgerAccountEntity ExpenseAccount { get; set; } = null!;
    public LedgerAccountEntity PaymentAccount { get; set; } = null!;
    public StockLocationEntity? StockLocation { get; set; }
}
