namespace BusinessOS.Pharmacy.Application.Abstractions.Networking;

public interface ILocalServerConnectionMonitor
{
    LocalServerConnectionStatus Current { get; }

    event EventHandler<LocalServerConnectionStatus>? StatusChanged;

    Task<LocalServerConnectionStatus> CheckNowAsync(
        CancellationToken cancellationToken = default);
}
