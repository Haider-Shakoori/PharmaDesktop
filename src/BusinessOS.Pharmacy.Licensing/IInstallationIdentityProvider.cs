namespace BusinessOS.Pharmacy.Licensing;

public interface IInstallationIdentityProvider
{
    Task<string> GetOrCreateAsync(CancellationToken cancellationToken = default);
}