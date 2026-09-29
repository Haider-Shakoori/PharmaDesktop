namespace BusinessOS.Pharmacy.Application.Abstractions.Medicines;

public interface IMedicineCsvService
{
    IReadOnlyList<string> Columns { get; }

    Task WriteTemplateAsync(
        string path,
        CancellationToken cancellationToken = default);

    Task<MedicineCsvPreview> PreviewAsync(
        string path,
        CancellationToken cancellationToken = default);

    Task<MedicineCsvImportResult> ImportAsync(
        string path,
        CancellationToken cancellationToken = default);
}

public sealed record MedicineCsvPreview(
    int TotalRows,
    int ValidRows,
    int InvalidRows,
    IReadOnlyList<MedicineCsvRowResult> Rows);

public sealed record MedicineCsvRowResult(
    int RowNumber,
    string? MedicineCode,
    string? BrandName,
    bool IsValid,
    IReadOnlyList<string> Errors);

public sealed record MedicineCsvImportResult(
    int Imported,
    int Rejected,
    IReadOnlyList<MedicineCsvRowResult> Rows);
