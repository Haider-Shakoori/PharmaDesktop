namespace BusinessOS.Pharmacy.Application.Abstractions.Sync;

public interface ICloudSyncSessionProvider
{
    Task<CloudSyncSession> GetAsync(
        CancellationToken cancellationToken = default);
}

public sealed record CloudSyncSession(
    Uri BaseUri,
    string AccessToken,
    string TenantId,
    string DeviceId,
    string UserId,
    DateTimeOffset ExpiresAt);
