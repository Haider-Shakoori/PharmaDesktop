namespace BusinessOS.Pharmacy.Persistence.Entities;

internal sealed class SyncEntityMapEntity
{
    public string Stream { get; set; } = string.Empty;
    public string LocalEntityId { get; set; } = string.Empty;
    public string CloudEntityId { get; set; } = string.Empty;
    public string? CloudVersion { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
