namespace BusinessOS.Pharmacy.Desktop.Medicines;

public sealed record MedicineCsvRowViewModel(
    int RowNumber,
    string MedicineCode,
    string BrandName,
    string Status,
    string Errors);
