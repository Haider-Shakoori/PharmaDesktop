namespace BusinessOS.Pharmacy.Persistence.Entities;

internal sealed class BatchStatusEventEntity
{
    public string Id { get; set; } = string.Empty;
    public string ProductBatchId { get; set; } = string.Empty;
    public string FromStatus { get; set; } = string.Empty;
    public string ToStatus { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string? ActorId { get; set; }
    public DateTimeOffset ChangedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ProductBatchEntity ProductBatch { get; set; } = null!;
}
