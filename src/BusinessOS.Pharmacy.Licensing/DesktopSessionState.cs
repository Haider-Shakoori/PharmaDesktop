using BusinessOS.Pharmacy.Domain.Authentication;

namespace BusinessOS.Pharmacy.Licensing;

public sealed record OfflinePasswordCredential(
    string SaltBase64,
    string HashBase64,
    int Iterations);

public sealed record DesktopSessionState(
    string AccessToken,
    UserSessionSnapshot User,
    DateTimeOffset LastServerTime,
    DateTimeOffset LastTrustedLocalTime,
    OfflinePasswordCredential? OfflinePassword);

public interface IUserSessionStore
{
    Task<DesktopSessionState?> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(DesktopSessionState state, CancellationToken cancellationToken = default);
    Task ClearAsync(CancellationToken cancellationToken = default);
}
