namespace BusinessOS.Pharmacy.Licensing;

public interface IDesktopSessionClient
{
    Task<DesktopSessionEnvelope> LoginAsync(
        string leaseToken,
        DesktopSessionLoginRequest request,
        CancellationToken cancellationToken = default);

    Task<DesktopSessionEnvelope> RefreshAsync(
        string accessToken,
        DesktopSessionRefreshRequest request,
        CancellationToken cancellationToken = default);
}
