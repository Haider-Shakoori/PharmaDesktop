namespace BusinessOS.Pharmacy.Persistence.Entities;

internal sealed class SaleLineEntity
{
    public string Id { get; set; } = string.Empty;
    public string SaleId { get; set; } = string.Empty;
    public string MedicineId { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string SaleUnit { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal LineTotal { get; set; }
    public decimal CostTotal { get; set; }
    public bool PrescriptionRequired { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public SaleEntity Sale { get; set; } = null!;
    public MedicineEntity Medicine { get; set; } = null!;
    public List<SaleBatchAllocationEntity> Allocations { get; set; } = [];
}
