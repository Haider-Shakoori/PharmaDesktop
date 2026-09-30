namespace BusinessOS.Pharmacy.Persistence.Entities;
internal sealed class JournalLineEntity
{
    public string Id { get; set; } = string.Empty;
    public string JournalEntryId { get; set; } = string.Empty;
    public string LedgerAccountId { get; set; } = string.Empty;
    public string? StockLocationId { get; set; }
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public string? CounterpartyType { get; set; }
    public string? CounterpartyId { get; set; }
    public string? Memo { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public JournalEntryEntity JournalEntry { get; set; } = null!;
    public LedgerAccountEntity LedgerAccount { get; set; } = null!;
}
