namespace BusinessOS.Pharmacy.Application.Abstractions.Networking;

public interface ILocalServerDiscovery
{
    Task<IReadOnlyList<LocalServerDiscoveryAdvertisement>> DiscoverAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
}
