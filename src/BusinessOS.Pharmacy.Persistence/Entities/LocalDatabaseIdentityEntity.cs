namespace BusinessOS.Pharmacy.Persistence.Entities;

internal sealed class LocalDatabaseIdentityEntity
{
    public int Id { get; set; } = 1;
    public Guid DatabaseInstanceId { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastOpenedAt { get; set; }
}
