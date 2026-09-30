namespace BusinessOS.Pharmacy.Application.Abstractions.Networking;

public interface ILocalServerServiceController
{
    Task<LocalServerServiceStatus> GetStatusAsync(
        CancellationToken cancellationToken = default);

    Task<LocalServerServiceStatus> StartAsync(
        CancellationToken cancellationToken = default);

    Task<NetworkProfileStatus> GetNetworkProfileStatusAsync(
        CancellationToken cancellationToken = default);

    Task<FirewallConfigurationResult> GetPrivateFirewallRuleStatusAsync(
        int port,
        CancellationToken cancellationToken = default);

    Task<FirewallConfigurationResult> EnsurePrivateFirewallRuleAsync(
        int port,
        CancellationToken cancellationToken = default);
}

public sealed record LocalServerServiceStatus(
    bool IsWindows,
    bool IsInstalled,
    bool IsRunning,
    string Message);

public sealed record NetworkProfileStatus(
    bool IsWindows,
    bool HasConnectedNetwork,
    bool HasPrivateOrDomainNetwork,
    bool HasPublicNetwork,
    string Message);

public sealed record FirewallConfigurationResult(
    bool Success,
    string Message);
