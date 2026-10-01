namespace BusinessOS.Pharmacy.Persistence.Entities;

internal sealed class CloudSyncRemoteRecordEntity
{
    public string Id { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string Stream { get; set; } = string.Empty;
    public string ServerId { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = string.Empty;
    public DateTimeOffset? ServerUpdatedAt { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
}
