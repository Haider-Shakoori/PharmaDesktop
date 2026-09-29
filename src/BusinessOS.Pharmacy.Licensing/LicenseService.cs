using System.Reflection;
using BusinessOS.Pharmacy.Application.Abstractions.Licensing;
using BusinessOS.Pharmacy.Application.Abstractions.Time;
using BusinessOS.Pharmacy.Domain.Licensing;
using Microsoft.Extensions.Options;

namespace BusinessOS.Pharmacy.Licensing;

public sealed class LicenseService : ILicenseService
{
    private readonly ILicenseActivationClient _client;
    private readonly ISignedLeaseVerifier _verifier;
    private readonly IActivationStore _store;
    private readonly IInstallationIdentityProvider _installationIdentity;
    private readonly IClock _clock;
    private readonly LicenseApiOptions _options;

    public LicenseService(
        ILicenseActivationClient client,
        ISignedLeaseVerifier verifier,
        IActivationStore store,
        IInstallationIdentityProvider installationIdentity,
        IClock clock,
        IOptions<LicenseApiOptions> options)
    {
        _client = client;
        _verifier = verifier;
        _store = store;
        _installationIdentity = installationIdentity;
        _clock = clock;
        _options = options.Value;
    }

    public async Task<EntitlementSnapshot?> GetCachedEntitlementAsync(CancellationToken cancellationToken = default)
    {
        var state = await _store.LoadAsync(cancellationToken);
        if (state is null)
        {
            return null;
        }

        var now = _clock.UtcNow;
        var tolerance = TimeSpan.FromMinutes(_options.ClockRollbackToleranceMinutes);
        if (now + tolerance < state.LastTrustedLocalTime)
        {
            throw new ClockRollbackDetectedException();
        }

        var entitlement = _verifier.Verify(state.LeaseToken);
        if (!string.Equals(entitlement.DeviceId, state.DeviceId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The cached entitlement belongs to another installation.");
        }

        var trustedNow = now > state.LastServerTime ? now : state.LastServerTime;
        return entitlement.IsOfflineLeaseValidAt(trustedNow) ? entitlement : null;
    }

    public async Task<EntitlementSnapshot> ActivateAsync(string licenseKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(licenseKey))
        {
            throw new ArgumentException("A license key is required.", nameof(licenseKey));
        }

        var deviceId = await _installationIdentity.GetOrCreateAsync(cancellationToken);
        var envelope = await _client.ActivateAsync(BuildActivationRequest(licenseKey.Trim(), deviceId), cancellationToken);
        return await AcceptServerLeaseAsync(envelope, deviceId, cancellationToken);
    }

    public async Task<EntitlementSnapshot> RefreshAsync(CancellationToken cancellationToken = default)
    {
        var current = await _store.LoadAsync(cancellationToken)
            ?? throw new InvalidOperationException("This installation has not been activated.");

        var deviceId = await _installationIdentity.GetOrCreateAsync(cancellationToken);
        if (!string.Equals(deviceId, current.DeviceId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The protected activation does not match this installation.");
        }

        var envelope = await _client.RefreshAsync(
            current.LeaseToken,
            BuildRefreshRequest(deviceId),
            cancellationToken);

        return await AcceptServerLeaseAsync(envelope, deviceId, cancellationToken);
    }

    private async Task<EntitlementSnapshot> AcceptServerLeaseAsync(
        LicenseActivationEnvelope envelope,
        string deviceId,
        CancellationToken cancellationToken)
    {
        var entitlement = _verifier.Verify(envelope.Data.LeaseToken);
        if (!string.Equals(entitlement.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(entitlement.ActivationId, envelope.Data.ActivationId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The licensing server returned an entitlement for another activation.");
        }

        var now = _clock.UtcNow;
        var state = new ActivationState(
            envelope.Data.LeaseToken,
            deviceId,
            envelope.ServerTime,
            now,
            entitlement);

        await _store.SaveAsync(state, cancellationToken);
        return entitlement;
    }

    private static LicenseActivationRequest BuildActivationRequest(string licenseKey, string deviceId) =>
        new(
            licenseKey,
            deviceId,
            Environment.MachineName,
            GetApplicationVersion(),
            Environment.MachineName,
            Environment.OSVersion.VersionString,
            GetApplicationVersion(),
            "windows");

    private static LicenseRefreshRequest BuildRefreshRequest(string deviceId) =>
        new(
            deviceId,
            Environment.MachineName,
            GetApplicationVersion(),
            Environment.MachineName,
            Environment.OSVersion.VersionString,
            GetApplicationVersion());

    private static string GetApplicationVersion() =>
        Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "0.0.0";
}