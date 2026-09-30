namespace BusinessOS.Pharmacy.Persistence.Entities;

internal sealed class SyncConflictEntity
{
    public string Id { get; set; } = string.Empty;
    public string Direction { get; set; } = string.Empty;
    public string? QueueItemId { get; set; }
    public string Stream { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public int ConsistencyClass { get; set; }
    public string? LocalPayloadJson { get; set; }
    public string? RemotePayloadJson { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string Status { get; set; } = "open";
    public DateTimeOffset DetectedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
