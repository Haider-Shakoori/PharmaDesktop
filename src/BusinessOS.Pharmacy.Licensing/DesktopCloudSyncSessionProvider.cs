using BusinessOS.Pharmacy.Application.Abstractions.Authentication;
using BusinessOS.Pharmacy.Application.Abstractions.Sync;
using BusinessOS.Pharmacy.Application.Abstractions.Time;
using Microsoft.Extensions.Options;

namespace BusinessOS.Pharmacy.Licensing;

public sealed class DesktopCloudSyncSessionProvider : ICloudSyncSessionProvider
{
    private static readonly TimeSpan RefreshWindow = TimeSpan.FromMinutes(2);

    private readonly IUserSessionStore _store;
    private readonly IUserSessionService _sessions;
    private readonly IClock _clock;
    private readonly LicenseApiOptions _options;

    public DesktopCloudSyncSessionProvider(
        IUserSessionStore store,
        IUserSessionService sessions,
        IClock clock,
        IOptions<LicenseApiOptions> options)
    {
        _store = store;
        _sessions = sessions;
        _clock = clock;
        _options = options.Value;
    }

    public async Task<CloudSyncSession> GetAsync(
        CancellationToken cancellationToken = default)
    {
        var state = await _store.LoadAsync(cancellationToken)
            ?? throw new InvalidOperationException(
                "A pharmacy user must sign in online before cloud synchronization.");

        if (state.User.ExpiresAt <= _clock.UtcNow.Add(RefreshWindow))
        {
            await _sessions.RefreshAsync(cancellationToken);
            state = await _store.LoadAsync(cancellationToken)
                ?? throw new InvalidOperationException(
                    "The refreshed pharmacy session could not be loaded.");
        }

        if (string.IsNullOrWhiteSpace(state.AccessToken))
        {
            throw new InvalidOperationException(
                "The pharmacy cloud session does not contain an access token.");
        }

        return new CloudSyncSession(
            new Uri(_options.BaseUrl, UriKind.Absolute),
            state.AccessToken,
            state.User.TenantId,
            state.User.DeviceId,
            state.User.UserId,
            state.User.ExpiresAt);
    }
}
