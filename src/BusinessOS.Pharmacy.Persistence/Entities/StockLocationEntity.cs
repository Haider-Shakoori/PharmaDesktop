namespace BusinessOS.Pharmacy.Persistence.Entities;

internal sealed class StockLocationEntity
{
    public string Id { get; set; } = string.Empty;
    public string BranchId { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Kind { get; set; } = "store";
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public BranchEntity Branch { get; set; } = null!;
    public List<ProductBatchEntity> ProductBatches { get; set; } = [];
}
