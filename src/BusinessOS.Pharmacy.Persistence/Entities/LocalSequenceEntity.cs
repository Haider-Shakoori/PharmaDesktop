namespace BusinessOS.Pharmacy.Persistence.Entities;

internal sealed class LocalSequenceEntity
{
    public string Key { get; set; } = string.Empty;
    public long CurrentValue { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
