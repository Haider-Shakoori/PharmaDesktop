using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using BusinessOS.Pharmacy.Application.Abstractions.Networking;

namespace BusinessOS.Pharmacy.LocalClient;

public sealed class PinnedLocalServerTransport : IDisposable
{
    private readonly INetworkConfigurationStore _configurationStore;
    private readonly INetworkSecretStore _secretStore;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private HttpClient? _client;
    private string? _clientKey;

    public PinnedLocalServerTransport(
        INetworkConfigurationStore configurationStore,
        INetworkSecretStore secretStore)
    {
        _configurationStore = configurationStore;
        _secretStore = secretStore;
    }

    public async Task<HttpClient> GetPairedClientAsync(
        CancellationToken cancellationToken = default)
    {
        var configuration = await _configurationStore.LoadAsync(cancellationToken);
        configuration.Validate();

        if (configuration.Mode != DeploymentMode.Client || !configuration.IsConfigured)
        {
            throw new InvalidOperationException("This computer is not configured as a paired Client Terminal.");
        }

        var pairing = await _secretStore.LoadTerminalPairingAsync(cancellationToken)
            ?? throw new InvalidOperationException("The protected terminal pairing credential is missing.");

        if (!string.Equals(pairing.TerminalId, configuration.TerminalId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(pairing.ServerId, configuration.ServerId, StringComparison.Ordinal) ||
            !string.Equals(pairing.ServerCertificateSha256, configuration.ServerCertificateSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The protected terminal credential does not match the configured pharmacy server identity.");
        }

        var key = string.Join(
            "|",
            configuration.ServerHost,
            configuration.ServerPort,
            configuration.ServerCertificateSha256,
            pairing.TerminalId);

        if (_client is not null && string.Equals(_clientKey, key, StringComparison.Ordinal))
        {
            return _client;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_client is not null && string.Equals(_clientKey, key, StringComparison.Ordinal))
            {
                return _client;
            }

            _client?.Dispose();
            _client = CreatePinnedClient(
                configuration.ServerHost!,
                configuration.ServerPort,
                configuration.ServerCertificateSha256!);

            _client.DefaultRequestHeaders.Remove("X-BusinessOS-Terminal-Id");
            _client.DefaultRequestHeaders.Remove("X-BusinessOS-Terminal-Secret");
            _client.DefaultRequestHeaders.Add("X-BusinessOS-Terminal-Id", pairing.TerminalId);
            _client.DefaultRequestHeaders.Add("X-BusinessOS-Terminal-Secret", pairing.TerminalSecret);
            _clientKey = key;

            return _client;
        }
        finally
        {
            _gate.Release();
        }
    }

    public static HttpClient CreatePinnedClient(
        string host,
        int port,
        string expectedCertificateSha256)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedCertificateSha256);

        var normalizedExpected = NormalizeFingerprint(expectedCertificateSha256);

        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, certificate, _, _) =>
                CertificateMatches(certificate, normalizedExpected),
        };

        return new HttpClient(handler, disposeHandler: true)
        {
            BaseAddress = new Uri($"https://{host}:{port}/api/local/v1/"),
            Timeout = TimeSpan.FromSeconds(8),
        };
    }

    private static bool CertificateMatches(
        X509Certificate2? certificate,
        string normalizedExpected)
    {
        if (certificate is null)
        {
            return false;
        }

        var actual = Convert.ToHexString(
            certificate.GetCertHash(HashAlgorithmName.SHA256));

        return CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(actual),
            Convert.FromHexString(normalizedExpected));
    }

    private static string NormalizeFingerprint(string value)
    {
        value = value.Replace(":", string.Empty).Replace(" ", string.Empty).Trim();

        if (value.Length != 64 || value.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new InvalidOperationException("The configured server certificate fingerprint is invalid.");
        }

        return value.ToUpperInvariant();
    }

    public void Dispose()
    {
        _client?.Dispose();
        _gate.Dispose();
    }
}
