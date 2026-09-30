namespace BusinessOS.Pharmacy.Persistence.Entities;

internal sealed class SyncCheckpointEntity
{
    public string Stream { get; set; } = string.Empty;
    public string? Checkpoint { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
