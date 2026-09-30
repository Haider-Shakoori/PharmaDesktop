using BusinessOS.Pharmacy.Application.Abstractions.Licensing;
using BusinessOS.Pharmacy.Application.Abstractions.Networking;
using BusinessOS.Pharmacy.Application.Abstractions.Time;
using BusinessOS.Pharmacy.Domain.Authentication;
using BusinessOS.Pharmacy.Licensing;
using BusinessOS.Pharmacy.LocalServer.Api;

namespace BusinessOS.Pharmacy.LocalServer.Authentication;

public sealed class LanUserAuthenticationService
{
    private static readonly TimeSpan MaximumLocalSessionLifetime = TimeSpan.FromHours(12);

    private readonly IDesktopSessionClient _cloudSessions;
    private readonly IActivationStore _activationStore;
    private readonly ISignedDesktopSessionVerifier _signedSessionVerifier;
    private readonly IOfflinePasswordVerifier _passwordVerifier;
    private readonly ILicenseService _licenseService;
    private readonly ILocalLanCredentialStore _credentials;
    private readonly IClock _clock;

    public LanUserAuthenticationService(
        IDesktopSessionClient cloudSessions,
        IActivationStore activationStore,
        ISignedDesktopSessionVerifier signedSessionVerifier,
        IOfflinePasswordVerifier passwordVerifier,
        ILicenseService licenseService,
        ILocalLanCredentialStore credentials,
        IClock clock)
    {
        _cloudSessions = cloudSessions;
        _activationStore = activationStore;
        _signedSessionVerifier = signedSessionVerifier;
        _passwordVerifier = passwordVerifier;
        _licenseService = licenseService;
        _credentials = credentials;
        _clock = clock;
    }

    public async Task<LocalLoginResponse> LoginAsync(
        string terminalId,
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(terminalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        var now = _clock.UtcNow;
        var entitlement = await _licenseService.GetCachedEntitlementAsync(cancellationToken)
            ?? throw new InvalidOperationException(
                "The Main Pharmacy Server license is not currently valid.");

        if (!entitlement.IsOfflineLeaseValidAt(now))
        {
            throw new InvalidOperationException(
                "The Main Pharmacy Server license must be verified before LAN login can continue.");
        }

        try
        {
            return await LoginOnlineAsync(
                entitlement,
                terminalId,
                email.Trim(),
                password,
                now,
                cancellationToken);
        }
        catch (LicenseApiException exception) when (exception.IsRetryable)
        {
            return await LoginOfflineAsync(
                entitlement,
                terminalId,
                email.Trim(),
                password,
                now,
                cancellationToken);
        }
    }

    private async Task<LocalLoginResponse> LoginOnlineAsync(
        BusinessOS.Pharmacy.Domain.Licensing.EntitlementSnapshot entitlement,
        string terminalId,
        string email,
        string password,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var activation = await _activationStore.LoadAsync(cancellationToken)
            ?? throw new InvalidOperationException(
                "The Main Pharmacy Server does not have a protected activation state.");

        var envelope = await _cloudSessions.LoginAsync(
            activation.LeaseToken,
            new DesktopSessionLoginRequest(
                activation.DeviceId,
                email,
                password),
            cancellationToken);

        var signed = _signedSessionVerifier.Verify(envelope.Data.AccessToken);
        ValidateCloudUser(signed, envelope.Data.User, activation, entitlement);

        var offlinePassword = _passwordVerifier.Create(password);
        var identityValidUntil = signed.ExpiresAt < entitlement.ExpiresAt
            ? signed.ExpiresAt
            : entitlement.ExpiresAt;

        await _credentials.UpsertUserAsync(
            new CachedLanUser(
                signed.UserId,
                signed.TenantId,
                signed.Name,
                signed.Email,
                signed.Roles,
                signed.Permissions,
                offlinePassword.SaltBase64,
                offlinePassword.HashBase64,
                offlinePassword.Iterations,
                envelope.ServerTime,
                identityValidUntil,
                true),
            cancellationToken);

        return await CreateLocalSessionAsync(
            terminalId,
            signed.UserId,
            signed.TenantId,
            signed.Name,
            signed.Email,
            signed.Roles,
            signed.Permissions,
            now,
            identityValidUntil,
            cancellationToken);
    }

    private async Task<LocalLoginResponse> LoginOfflineAsync(
        BusinessOS.Pharmacy.Domain.Licensing.EntitlementSnapshot entitlement,
        string terminalId,
        string email,
        string password,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var cached = await _credentials.FindUserByEmailAsync(email, cancellationToken)
            ?? throw new LicenseApiException(
                "No verified offline LAN login is available for this pharmacy user.",
                isRetryable: false);

        if (!cached.IsActive ||
            !string.Equals(cached.TenantId, entitlement.TenantId, StringComparison.Ordinal) ||
            now >= cached.IdentityValidUntil)
        {
            throw new LicenseApiException(
                "The cached pharmacy user identity is no longer valid. Connect the Main Pharmacy Server to the internet and sign in again.",
                isRetryable: false);
        }

        var verifier = new OfflinePasswordCredential(
            cached.PasswordSaltBase64,
            cached.PasswordHashBase64,
            cached.PasswordIterations);

        if (!_passwordVerifier.Verify(password, verifier))
        {
            throw new LicenseApiException(
                "The pharmacy user credentials are invalid.",
                isRetryable: false);
        }

        var validUntil = cached.IdentityValidUntil < entitlement.ExpiresAt
            ? cached.IdentityValidUntil
            : entitlement.ExpiresAt;

        return await CreateLocalSessionAsync(
            terminalId,
            cached.UserId,
            cached.TenantId,
            cached.Name,
            cached.Email,
            cached.Roles,
            cached.Permissions,
            now,
            validUntil,
            cancellationToken);
    }

    private async Task<LocalLoginResponse> CreateLocalSessionAsync(
        string terminalId,
        string userId,
        string tenantId,
        string name,
        string email,
        IReadOnlySet<string> roles,
        IReadOnlySet<string> permissions,
        DateTimeOffset now,
        DateTimeOffset validUntil,
        CancellationToken cancellationToken)
    {
        var lifetime = validUntil - now;
        if (lifetime > MaximumLocalSessionLifetime)
        {
            lifetime = MaximumLocalSessionLifetime;
        }

        if (lifetime <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                "The pharmacy user or server entitlement has expired.");
        }

        var principal = new LocalLanSessionPrincipal(
            string.Empty,
            terminalId,
            userId,
            tenantId,
            name,
            email,
            roles,
            permissions,
            now,
            now.Add(lifetime),
            null);

        var session = await _credentials.CreateSessionAsync(
            principal,
            lifetime,
            cancellationToken);

        return new LocalLoginResponse(
            session.SessionToken,
            session.Principal.ExpiresAt,
            new LocalUserResponse(
                session.Principal.UserId,
                session.Principal.Name,
                session.Principal.Email,
                session.Principal.Roles,
                session.Principal.Permissions));
    }

    private static void ValidateCloudUser(
        UserSessionSnapshot signed,
        DesktopUserSummary summary,
        ActivationState activation,
        BusinessOS.Pharmacy.Domain.Licensing.EntitlementSnapshot entitlement)
    {
        if (!string.Equals(signed.TenantId, entitlement.TenantId, StringComparison.Ordinal) ||
            !string.Equals(signed.ActivationId, entitlement.ActivationId, StringComparison.Ordinal) ||
            !string.Equals(signed.DeviceId, activation.DeviceId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(signed.UserId, summary.Id.ToString(), StringComparison.Ordinal) ||
            !string.Equals(signed.Name, summary.Name, StringComparison.Ordinal) ||
            !string.Equals(signed.Email, summary.Email, StringComparison.OrdinalIgnoreCase) ||
            !signed.Roles.SetEquals(summary.Roles) ||
            !signed.Permissions.SetEquals(summary.Permissions))
        {
            throw new InvalidOperationException(
                "The SaaS authentication response does not match its signed pharmacy-user identity.");
        }
    }
}

internal static class LanSetExtensions
{
    public static bool SetEquals(
        this IReadOnlySet<string> source,
        IEnumerable<string> other) =>
        source.SetEquals(other.ToHashSet(StringComparer.OrdinalIgnoreCase));
}
