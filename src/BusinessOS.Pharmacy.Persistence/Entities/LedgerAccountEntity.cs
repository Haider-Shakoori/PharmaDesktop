namespace BusinessOS.Pharmacy.Persistence.Entities;
internal sealed class LedgerAccountEntity
{
    public string Id { get; set; } = string.Empty;
    public string? ParentId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string NormalBalance { get; set; } = string.Empty;
    public string? SystemKey { get; set; }
    public string Currency { get; set; } = "AFN";
    public bool IsSystem { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Description { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public LedgerAccountEntity? Parent { get; set; }
}
