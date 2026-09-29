namespace BusinessOS.Pharmacy.Application.Abstractions.Medicines;

public interface IMedicineCatalogService
{
    Task<IReadOnlyList<MedicineListItem>> SearchAsync(
        MedicineSearchFilter filter,
        CancellationToken cancellationToken = default);

    Task<MedicineEditorModel?> GetAsync(
        string id,
        CancellationToken cancellationToken = default);

    Task<MedicineReferenceData> GetReferenceDataAsync(
        CancellationToken cancellationToken = default);

    Task<string> CreateAsync(
        SaveMedicineRequest request,
        CancellationToken cancellationToken = default);

    Task UpdateAsync(
        string id,
        SaveMedicineRequest request,
        CancellationToken cancellationToken = default);

    Task<string> CreateCategoryAsync(
        string name,
        CancellationToken cancellationToken = default);

    Task<string> CreateManufacturerAsync(
        string name,
        string? country,
        CancellationToken cancellationToken = default);
}

public sealed record MedicineSearchFilter(
    string? Search = null,
    string? CategoryId = null,
    bool? IsActive = null,
    int Take = 250);

public sealed record MedicineListItem(
    string Id,
    string MedicineCode,
    string BrandName,
    string? GenericName,
    string? Strength,
    string? DosageForm,
    string? CategoryName,
    string? ManufacturerName,
    string PurchaseUnit,
    string SaleUnit,
    decimal UnitsPerPurchaseUnit,
    decimal ReorderLevel,
    bool BatchTrackingRequired,
    bool ExpiryTrackingRequired,
    bool IsActive);

public sealed record MedicineEditorModel(
    string Id,
    string? MedicineCategoryId,
    string? ManufacturerId,
    string MedicineCode,
    string? Barcode,
    string BrandName,
    string? GenericName,
    string? Strength,
    string? DosageForm,
    string PurchaseUnit,
    string SaleUnit,
    decimal UnitsPerPurchaseUnit,
    decimal ReorderLevel,
    bool PrescriptionRequired,
    bool BatchTrackingRequired,
    bool ExpiryTrackingRequired,
    bool IsActive,
    string? Notes);

public sealed record SaveMedicineRequest(
    string? MedicineCategoryId,
    string? ManufacturerId,
    string MedicineCode,
    string? Barcode,
    string BrandName,
    string? GenericName,
    string? Strength,
    string? DosageForm,
    string PurchaseUnit,
    string SaleUnit,
    decimal UnitsPerPurchaseUnit,
    decimal ReorderLevel,
    bool PrescriptionRequired,
    bool BatchTrackingRequired,
    bool ExpiryTrackingRequired,
    bool IsActive,
    string? Notes);

public sealed record MedicineReferenceData(
    IReadOnlyList<MedicineReferenceItem> Categories,
    IReadOnlyList<ManufacturerReferenceItem> Manufacturers);

public sealed record MedicineReferenceItem(
    string Id,
    string Name,
    bool IsActive);

public sealed record ManufacturerReferenceItem(
    string Id,
    string Name,
    string? Country,
    bool IsActive);
