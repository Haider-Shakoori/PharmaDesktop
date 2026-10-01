using System.Net;
using System.Net.Security;
using System.Security.Authentication;
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
        var connection = await GetPairedConnectionAsync(cancellationToken);
        return connection.Client;
    }

    public async Task<(HttpClient Client, TerminalPairingSecret Pairing)> GetPairedConnectionAsync(
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
            configuration.ServerCertificateSha256);

        if (_client is not null && string.Equals(_clientKey, key, StringComparison.Ordinal))
        {
            return (_client, pairing);
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_client is null || !string.Equals(_clientKey, key, StringComparison.Ordinal))
            {
                _client?.Dispose();
                _client = CreatePinnedClient(
                    configuration.ServerHost!,
                    configuration.ServerPort,
                    configuration.ServerCertificateSha256!);
                _clientKey = key;
            }

            return (_client, pairing);
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

        if (port is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(port));

        host = host.Trim();
        if (Uri.CheckHostName(host) == UriHostNameType.Unknown)
            throw new InvalidOperationException("The configured Main Pharmacy Server host is invalid.");

        var expectedHash = Convert.FromHexString(
            NormalizeFingerprint(expectedCertificateSha256));

        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression =
                DecompressionMethods.GZip |
                DecompressionMethods.Deflate |
                DecompressionMethods.Brotli,
            ConnectTimeout = TimeSpan.FromSeconds(3),
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
            MaxConnectionsPerServer = 32,
            SslOptions = new SslClientAuthenticationOptions
            {
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                RemoteCertificateValidationCallback = (_, certificate, _, _) =>
                    CertificateMatches(certificate, expectedHash),
            },
        };

        var baseAddress = new UriBuilder(
            Uri.UriSchemeHttps,
            host,
            port,
            "api/local/v1/").Uri;

        return new HttpClient(handler, disposeHandler: true)
        {
            BaseAddress = baseAddress,
            Timeout = TimeSpan.FromSeconds(8),
        };
    }

    private static bool CertificateMatches(
        X509Certificate? certificate,
        byte[] expectedHash)
    {
        if (certificate is null)
        {
            return false;
        }

        var actual = certificate.GetCertHash(HashAlgorithmName.SHA256);
        try
        {
            return CryptographicOperations.FixedTimeEquals(actual, expectedHash);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(actual);
        }
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
