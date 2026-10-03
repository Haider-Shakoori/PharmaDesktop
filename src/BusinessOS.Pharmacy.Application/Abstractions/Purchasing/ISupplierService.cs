namespace BusinessOS.Pharmacy.Application.Abstractions.Purchasing;

public interface ISupplierService
{
    Task<IReadOnlyList<SupplierListItem>> SearchAsync(
        SupplierSearchFilter filter,
        CancellationToken cancellationToken = default);

    Task<SupplierSummary> GetSummaryAsync(
        CancellationToken cancellationToken = default);

    Task<SupplierEditorModel?> GetAsync(
        string id,
        CancellationToken cancellationToken = default);

    Task<string> CreateAsync(
        SaveSupplierRequest request,
        CancellationToken cancellationToken = default);

    Task UpdateAsync(
        string id,
        SaveSupplierRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record SupplierSearchFilter(
    string? Search = null,
    bool? IsActive = null,
    int Take = 250);

public sealed record SupplierSummary(
    int TotalSuppliers,
    decimal TotalDealValue,
    decimal TotalPaid,
    decimal OutstandingPayable,
    decimal TotalOpeningBalance);

public sealed record SupplierListItem(
    string Id,
    string Code,
    string Name,
    string? ContactPerson,
    string? Phone,
    string? Whatsapp,
    string? Email,
    string? City,
    string? Province,
    int PaymentTermsDays,
    decimal OpeningBalance,
    bool IsActive,
    int PurchaseOrderCount,
    int InvoiceCount);

public sealed record SupplierEditorModel(
    string Id,
    string Code,
    string Name,
    string? ContactPerson,
    string? Phone,
    string? Whatsapp,
    string? Email,
    string? Address,
    string? City,
    string? Province,
    int PaymentTermsDays,
    decimal OpeningBalance,
    bool IsActive,
    string? Notes);

public sealed record SaveSupplierRequest(
    string Code,
    string Name,
    string? ContactPerson,
    string? Phone,
    string? Whatsapp,
    string? Email,
    string? Address,
    string? City,
    string? Province,
    int PaymentTermsDays,
    decimal OpeningBalance,
    bool IsActive,
    string? Notes);
