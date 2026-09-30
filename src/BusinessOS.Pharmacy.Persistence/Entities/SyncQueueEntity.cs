namespace BusinessOS.Pharmacy.Persistence.Entities;

internal sealed class SyncQueueEntity
{
    public string Id { get; set; } = string.Empty;
    public string Stream { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public int Operation { get; set; }
    public int ConsistencyClass { get; set; }
    public string PayloadJson { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public long LocalVersion { get; set; }
    public int AttemptCount { get; set; }
    public int State { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
    public DateTimeOffset? ClaimedAt { get; set; }
    public string? LastError { get; set; }
    public string? CloudEntityId { get; set; }
    public string? CloudVersion { get; set; }
    public DateTimeOffset? SyncedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
