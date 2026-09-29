using BusinessOS.Pharmacy.Domain.Licensing;

namespace BusinessOS.Pharmacy.Application.Abstractions.Licensing;

public interface ILicenseService
{
    Task<EntitlementSnapshot?> GetCachedEntitlementAsync(CancellationToken cancellationToken = default);
    Task<EntitlementSnapshot> ActivateAsync(string licenseKey, CancellationToken cancellationToken = default);
    Task<EntitlementSnapshot> RefreshAsync(CancellationToken cancellationToken = default);
}
