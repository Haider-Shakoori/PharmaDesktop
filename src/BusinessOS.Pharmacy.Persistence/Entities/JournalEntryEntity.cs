namespace BusinessOS.Pharmacy.Persistence.Entities;
internal sealed class JournalEntryEntity
{
    public string Id { get; set; } = string.Empty;
    public string JournalNumber { get; set; } = string.Empty;
    public DateOnly BusinessDate { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string Status { get; set; } = "posted";
    public string Currency { get; set; } = "AFN";
    public string SourceType { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
    public string? SourceEvent { get; set; }
    public string? SourceNumber { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string? Reference { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal TotalDebit { get; set; }
    public decimal TotalCredit { get; set; }
    public string? PostedBy { get; set; }
    public string? ReversalOfId { get; set; }
    public string? ReversalReason { get; set; }
    public DateTimeOffset PostedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public JournalEntryEntity? ReversalOf { get; set; }
    public List<JournalLineEntity> Lines { get; set; } = [];
}
