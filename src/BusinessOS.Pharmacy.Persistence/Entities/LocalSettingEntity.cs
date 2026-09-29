namespace BusinessOS.Pharmacy.Persistence.Entities;

internal sealed class LocalSettingEntity
{
    public string Key { get; set; } = string.Empty;
    public string? Value { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
