namespace BusinessOS.Pharmacy.Application.Abstractions.Customers;

public interface ICustomerService
{
    Task<IReadOnlyList<CustomerListItem>> SearchAsync(
        CustomerSearchFilter filter,
        CancellationToken cancellationToken = default);

    Task<CustomerSummary> GetSummaryAsync(
        CancellationToken cancellationToken = default);

    Task<CustomerEditorModel?> GetAsync(
        string id,
        CancellationToken cancellationToken = default);

    Task<string> CreateAsync(
        SaveCustomerRequest request,
        CancellationToken cancellationToken = default);

    Task UpdateAsync(
        string id,
        SaveCustomerRequest request,
        CancellationToken cancellationToken = default);
}

public interface ICustomerCreditPolicy
{
    Task<CustomerCreditProfile> ValidateAsync(
        string customerId,
        decimal creditAmount,
        CancellationToken cancellationToken = default);
}

public sealed record CustomerSearchFilter(
    string? Search = null,
    bool? IsActive = null,
    int Take = 250);

public sealed record CustomerSummary(
    int TotalCustomers,
    int ActiveCustomers,
    int InactiveCustomers,
    decimal TotalCreditLimit);

public sealed record CustomerListItem(
    string Id,
    string Name,
    string? Phone,
    string? Email,
    decimal CreditLimit,
    bool IsActive);

public sealed record CustomerEditorModel(
    string Id,
    string Name,
    string? Phone,
    string? Email,
    decimal CreditLimit,
    bool IsActive,
    string? Notes);

public sealed record SaveCustomerRequest(
    string Name,
    string? Phone,
    string? Email,
    decimal CreditLimit,
    bool IsActive,
    string? Notes);

public sealed record CustomerCreditProfile(
    string Id,
    string Name,
    decimal CreditLimit,
    decimal RequestedCredit,
    decimal RemainingPerSaleCapacity);
