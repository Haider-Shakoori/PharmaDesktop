namespace BusinessOS.Pharmacy.Persistence.Entities;

internal sealed class MedicineEntity
{
    public string Id { get; set; } = string.Empty;
    public string? MedicineCategoryId { get; set; }
    public string? ManufacturerId { get; set; }
    public string MedicineCode { get; set; } = string.Empty;
    public string? Barcode { get; set; }
    public string BrandName { get; set; } = string.Empty;
    public string? GenericName { get; set; }
    public string? Strength { get; set; }
    public string? DosageForm { get; set; }
    public string PurchaseUnit { get; set; } = "pack";
    public string SaleUnit { get; set; } = "unit";
    public decimal UnitsPerPurchaseUnit { get; set; } = 1m;
    public decimal ReorderLevel { get; set; }
    public bool PrescriptionRequired { get; set; }
    public bool BatchTrackingRequired { get; set; } = true;
    public bool ExpiryTrackingRequired { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public MedicineCategoryEntity? Category { get; set; }
    public ManufacturerEntity? Manufacturer { get; set; }
}
