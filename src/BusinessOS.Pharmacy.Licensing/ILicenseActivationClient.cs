namespace BusinessOS.Pharmacy.Licensing;

public interface ILicenseActivationClient
{
    Task<LicenseActivationEnvelope> ActivateAsync(
        LicenseActivationRequest request,
        CancellationToken cancellationToken = default);

    Task<LicenseActivationEnvelope> RefreshAsync(
        string leaseToken,
        LicenseRefreshRequest request,
        CancellationToken cancellationToken = default);
}
