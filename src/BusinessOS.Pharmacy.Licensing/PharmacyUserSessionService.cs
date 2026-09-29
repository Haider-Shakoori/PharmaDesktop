using BusinessOS.Pharmacy.Application.Abstractions.Authentication;
using BusinessOS.Pharmacy.Application.Abstractions.Licensing;
using BusinessOS.Pharmacy.Application.Abstractions.Time;
using BusinessOS.Pharmacy.Domain.Authentication;
using Microsoft.Extensions.Options;

namespace BusinessOS.Pharmacy.Licensing;

public sealed class PharmacyUserSessionService : IUserSessionService
{
    private readonly IDesktopSessionClient _client;
    private readonly IActivationStore _activationStore;
    private readonly IUserSessionStore _sessionStore;
    private readonly ISignedDesktopSessionVerifier _sessionVerifier;
    private readonly IInstallationIdentityProvider _installationIdentity;
    private readonly IOfflinePasswordVerifier _passwordVerifier;
    private readonly ILicenseService _licenseService;
    private readonly IClock _clock;
    private readonly LicenseApiOptions _options;

    public PharmacyUserSessionService(
        IDesktopSessionClient client,
        IActivationStore activationStore,
        IUserSessionStore sessionStore,
        ISignedDesktopSessionVerifier sessionVerifier,
        IInstallationIdentityProvider installationIdentity,
        IOfflinePasswordVerifier passwordVerifier,
        ILicenseService licenseService,
        IClock clock,
        IOptions<LicenseApiOptions> options)
    {
        _client = client;
        _activationStore = activationStore;
        _sessionStore = sessionStore;
        _sessionVerifier = sessionVerifier;
        _installationIdentity = installationIdentity;
        _passwordVerifier = passwordVerifier;
        _licenseService = licenseService;
        _clock = clock;
        _options = options.Value;
    }

    public UserSessionSnapshot? Current { get; private set; }

    public async Task<UserSessionSnapshot> LoginAsync(
        string email,
        string password,
        bool allowOfflineSignIn,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        try
        {
            var activation = await _activationStore.LoadAsync(cancellationToken)
                ?? throw new InvalidOperationException("This installation has not been activated.");

            var deviceId = await _installationIdentity.GetOrCreateAsync(cancellationToken);
            EnsureDeviceMatches(deviceId, activation.DeviceId);

            var envelope = await _client.LoginAsync(
                activation.LeaseToken,
                new DesktopSessionLoginRequest(deviceId, email.Trim(), password),
                cancellationToken);

            var credential = allowOfflineSignIn
                ? _passwordVerifier.Create(password)
                : null;

            return await AcceptServerSessionAsync(
                envelope,
                activation,
                credential,
                cancellationToken);
        }
        catch (LicenseApiException exception) when (exception.IsRetryable && allowOfflineSignIn)
        {
            return await LoginOfflineAsync(email, password, cancellationToken);
        }
    }

    public async Task<UserSessionSnapshot> RefreshAsync(CancellationToken cancellationToken = default)
    {
        var state = await _sessionStore.LoadAsync(cancellationToken)
            ?? throw new InvalidOperationException("No pharmacy user session is stored.");

        var activation = await _activationStore.LoadAsync(cancellationToken)
            ?? throw new InvalidOperationException("This installation has not been activated.");

        var deviceId = await _installationIdentity.GetOrCreateAsync(cancellationToken);
        EnsureDeviceMatches(deviceId, activation.DeviceId);

        var envelope = await _client.RefreshAsync(
            state.AccessToken,
            new DesktopSessionRefreshRequest(deviceId),
            cancellationToken);

        return await AcceptServerSessionAsync(
            envelope,
            activation,
            state.OfflinePassword,
            cancellationToken);
    }

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        Current = null;
        await _sessionStore.ClearAsync(cancellationToken);
    }

    private async Task<UserSessionSnapshot> LoginOfflineAsync(
        string email,
        string password,
        CancellationToken cancellationToken)
    {
        var state = await _sessionStore.LoadAsync(cancellationToken)
            ?? throw new LicenseApiException(
                "No offline pharmacy login is available on this PC.",
                isRetryable: false);

        if (state.OfflinePassword is null)
        {
            throw new LicenseApiException(
                "Offline sign-in was not enabled for the saved pharmacy user.",
                isRetryable: false);
        }

        var now = _clock.UtcNow;
        var tolerance = TimeSpan.FromMinutes(_options.ClockRollbackToleranceMinutes);
        if (now + tolerance < state.LastTrustedLocalTime)
        {
            throw new ClockRollbackDetectedException();
        }

        var entitlement = await _licenseService.GetCachedEntitlementAsync(cancellationToken);
        if (entitlement is null)
        {
            throw new LicenseApiException(
                "The pharmacy license must be verified online before offline staff sign-in can continue.",
                isRetryable: false);
        }

        var signedUser = _sessionVerifier.Verify(state.AccessToken);
        var trustedNow = now > state.LastServerTime ? now : state.LastServerTime;

        if (!signedUser.IsValidAt(trustedNow) ||
            !string.Equals(signedUser.Email, email.Trim(), StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(signedUser.DeviceId, entitlement.DeviceId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(signedUser.TenantId, entitlement.TenantId, StringComparison.Ordinal) ||
            !string.Equals(signedUser.ActivationId, entitlement.ActivationId, StringComparison.Ordinal) ||
            !_passwordVerifier.Verify(password, state.OfflinePassword))
        {
            throw new LicenseApiException(
                "The offline pharmacy credentials are invalid or have expired.",
                isRetryable: false);
        }

        var ratcheted = state with
        {
            User = signedUser,
            LastTrustedLocalTime = now > state.LastTrustedLocalTime
                ? now
                : state.LastTrustedLocalTime,
        };
        await _sessionStore.SaveAsync(ratcheted, cancellationToken);

        Current = signedUser;
        return signedUser;
    }

    private async Task<UserSessionSnapshot> AcceptServerSessionAsync(
        DesktopSessionEnvelope envelope,
        ActivationState activation,
        OfflinePasswordCredential? offlinePassword,
        CancellationToken cancellationToken)
    {
        var signedUser = _sessionVerifier.Verify(envelope.Data.AccessToken);

        EnsureDeviceMatches(signedUser.DeviceId, activation.DeviceId);

        if (!string.Equals(signedUser.TenantId, activation.Entitlement.TenantId, StringComparison.Ordinal) ||
            !string.Equals(signedUser.ActivationId, activation.Entitlement.ActivationId, StringComparison.Ordinal) ||
            !string.Equals(signedUser.UserId, envelope.Data.User.Id.ToString(), StringComparison.Ordinal) ||
            !string.Equals(signedUser.Email, envelope.Data.User.Email, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(signedUser.Name, envelope.Data.User.Name, StringComparison.Ordinal) ||
            !signedUser.Roles.SetEquals(envelope.Data.User.Roles) ||
            !signedUser.Permissions.SetEquals(envelope.Data.User.Permissions))
        {
            throw new InvalidOperationException("The authentication server returned a user snapshot that does not match its signed session.");
        }

        var now = _clock.UtcNow;
        var state = new DesktopSessionState(
            envelope.Data.AccessToken,
            signedUser,
            envelope.ServerTime,
            now,
            offlinePassword);

        await _sessionStore.SaveAsync(state, cancellationToken);
        Current = signedUser;
        return signedUser;
    }

    private static void EnsureDeviceMatches(string actual, string expected)
    {
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The pharmacy user session belongs to another Windows installation.");
        }
    }
}

internal static class ReadOnlySetExtensions
{
    public static bool SetEquals(this IReadOnlySet<string> source, IEnumerable<string> other) =>
        source.SetEquals(other.ToHashSet(StringComparer.OrdinalIgnoreCase));
}
