namespace BusinessOS.Pharmacy.Persistence.Entities;

internal sealed class SaleReturnLineEntity
{
    public string Id { get; set; } = string.Empty;
    public string SaleReturnId { get; set; } = string.Empty;
    public string SaleLineId { get; set; } = string.Empty;
    public string MedicineId { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal RefundAmount { get; set; }
    public string Disposition { get; set; } = "restock";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public SaleReturnEntity SaleReturn { get; set; } = null!;
    public SaleLineEntity SaleLine { get; set; } = null!;
    public MedicineEntity Medicine { get; set; } = null!;
    public List<SaleReturnAllocationEntity> Allocations { get; set; } = [];
}
