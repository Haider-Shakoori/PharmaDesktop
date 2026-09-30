namespace BusinessOS.Pharmacy.Persistence.Entities;

internal sealed class NetworkAuditEntity
{
    public long Id { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string Operation { get; set; } = string.Empty;
    public string Outcome { get; set; } = string.Empty;
    public string? TerminalId { get; set; }
    public string? UserId { get; set; }
    public string? RecordUuid { get; set; }
    public string? RemoteAddress { get; set; }
    public string? Detail { get; set; }
}
