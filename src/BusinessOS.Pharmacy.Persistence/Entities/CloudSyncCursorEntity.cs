namespace BusinessOS.Pharmacy.Persistence.Entities;

internal sealed class CloudSyncCursorEntity
{
    public string Id { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string Stream { get; set; } = string.Empty;
    public string? Cursor { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
