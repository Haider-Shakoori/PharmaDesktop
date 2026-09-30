using System.Net.Http.Json;
using BusinessOS.Pharmacy.Application.Abstractions.Networking;

namespace BusinessOS.Pharmacy.LocalClient;

public sealed class LanTerminalPairingClient
{
    private readonly INetworkConfigurationStore _configurationStore;
    private readonly INetworkSecretStore _secretStore;

    public LanTerminalPairingClient(
        INetworkConfigurationStore configurationStore,
        INetworkSecretStore secretStore)
    {
        _configurationStore = configurationStore;
        _secretStore = secretStore;
    }

    public async Task<NetworkConfiguration> PairAsync(
        string host,
        int port,
        string serverId,
        string certificateSha256,
        string pairingCode,
        string terminalName,
        string terminalRole,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentException.ThrowIfNullOrWhiteSpace(serverId);
        ArgumentException.ThrowIfNullOrWhiteSpace(certificateSha256);
        ArgumentException.ThrowIfNullOrWhiteSpace(pairingCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(terminalName);
        ArgumentException.ThrowIfNullOrWhiteSpace(terminalRole);

        var existing = await _configurationStore.LoadAsync(cancellationToken);
        var terminalId = !string.IsNullOrWhiteSpace(existing.TerminalId) &&
                         Guid.TryParse(existing.TerminalId, out var parsed)
            ? parsed.ToString()
            : Guid.CreateVersion7().ToString();

        using var client = PinnedLocalServerTransport.CreatePinnedClient(
            host.Trim(),
            port,
            certificateSha256);

        using var response = await client.PostAsJsonAsync(
            "pairing/complete",
            new PairingRequest(
                pairingCode.Trim(),
                terminalId,
                terminalName.Trim(),
                Environment.MachineName,
                terminalRole.Trim()),
            cancellationToken);

        var body = await response.Content.ReadFromJsonAsync<PairingResponse>(
            cancellationToken: cancellationToken);

        if (!response.IsSuccessStatusCode || body is null)
        {
            throw new InvalidOperationException(
                body?.Message ?? $"Terminal pairing failed with HTTP {(int)response.StatusCode}.");
        }

        if (!string.Equals(body.ServerId, serverId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The pairing response came from a different pharmacy server identity.");
        }

        var configuration = existing with
        {
            Mode = DeploymentMode.Client,
            ServerHost = host.Trim(),
            ServerPort = port,
            ServerId = body.ServerId,
            ServerCertificateSha256 = NormalizeFingerprint(certificateSha256),
            TenantId = body.TenantId,
            TerminalId = body.TerminalId,
            TerminalName = terminalName.Trim(),
            TerminalRole = terminalRole.Trim(),
            DiscoveryEnabled = true,
            IsConfigured = true,
        };

        await _secretStore.SaveTerminalPairingAsync(
            new TerminalPairingSecret(
                body.TerminalId,
                body.TerminalSecret,
                body.ServerId,
                configuration.ServerCertificateSha256!),
            cancellationToken);

        await _configurationStore.SaveAsync(configuration, cancellationToken);
        return configuration;
    }

    public async Task<LocalServerConnectionStatus> TestConnectionAsync(
        CancellationToken cancellationToken = default)
    {
        var configuration = await _configurationStore.LoadAsync(cancellationToken);
        var pairing = await _secretStore.LoadTerminalPairingAsync(cancellationToken);

        if (configuration.Mode != DeploymentMode.Client ||
            pairing is null ||
            string.IsNullOrWhiteSpace(configuration.ServerId))
        {
            return new LocalServerConnectionStatus(
                false, null, null, null, null, "Client Terminal is not paired.");
        }

        var started = DateTimeOffset.UtcNow;

        try
        {
            using var client = PinnedLocalServerTransport.CreatePinnedClient(
                configuration.ServerHost!,
                configuration.ServerPort,
                configuration.ServerCertificateSha256!);

            using var response = await client.GetAsync("server-info", cancellationToken);
            response.EnsureSuccessStatusCode();

            var info = await response.Content.ReadFromJsonAsync<ServerInfoResponse>(
                cancellationToken: cancellationToken)
                ?? throw new InvalidOperationException("The server returned an empty identity response.");

            if (!string.Equals(info.ServerId, configuration.ServerId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The configured host now resolves to a different pharmacy server.");
            }

            var latency = DateTimeOffset.UtcNow - started;

            return new LocalServerConnectionStatus(
                true,
                info.ServerId,
                info.ServerName,
                latency,
                DateTimeOffset.UtcNow,
                "Connected");
        }
        catch (Exception exception)
        {
            return new LocalServerConnectionStatus(
                false,
                configuration.ServerId,
                configuration.ServerName,
                null,
                null,
                exception.Message);
        }
    }

    public async Task<Version> GetServerApplicationVersionAsync(
        CancellationToken cancellationToken = default)
    {
        var configuration = await _configurationStore.LoadAsync(cancellationToken);
        if (configuration.Mode != DeploymentMode.Client ||
            string.IsNullOrWhiteSpace(configuration.ServerId) ||
            string.IsNullOrWhiteSpace(configuration.ServerHost) ||
            string.IsNullOrWhiteSpace(configuration.ServerCertificateSha256))
        {
            throw new InvalidOperationException("Client Terminal is not paired with a Main Pharmacy Server.");
        }

        using var client = PinnedLocalServerTransport.CreatePinnedClient(
            configuration.ServerHost,
            configuration.ServerPort,
            configuration.ServerCertificateSha256);

        using var response = await client.GetAsync("server-info", cancellationToken);
        response.EnsureSuccessStatusCode();

        var info = await response.Content.ReadFromJsonAsync<ServerInfoResponse>(
            cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("The server returned an empty identity response.");

        if (!string.Equals(info.ServerId, configuration.ServerId, StringComparison.Ordinal))
            throw new InvalidOperationException("The configured host resolves to a different pharmacy server.");

        if (!Version.TryParse(info.ApplicationVersion, out var version))
            throw new InvalidOperationException("The Main Pharmacy Server did not report a valid application version.");

        return version;
    }

    private static string NormalizeFingerprint(string value) =>
        value.Replace(":", string.Empty)
            .Replace(" ", string.Empty)
            .Trim()
            .ToUpperInvariant();

    private sealed record PairingRequest(
        string PairingCode,
        string TerminalId,
        string Name,
        string ComputerName,
        string TerminalRole);

    private sealed record PairingResponse(
        string TerminalId,
        string TerminalSecret,
        string ServerId,
        string TenantId,
        DateTimeOffset RegisteredAt,
        string? Message = null);

    private sealed record ServerInfoResponse(
        string Service,
        string ApiVersion,
        string ApplicationVersion,
        string MinimumClientVersion,
        string ServerId,
        string ServerName,
        string HostName,
        int Port,
        string CertificateSha256);
}
