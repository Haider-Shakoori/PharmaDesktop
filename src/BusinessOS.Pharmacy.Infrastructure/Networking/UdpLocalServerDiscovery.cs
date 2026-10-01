using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using BusinessOS.Pharmacy.Application.Abstractions.Networking;

namespace BusinessOS.Pharmacy.Infrastructure.Networking;

public sealed class UdpLocalServerDiscovery : ILocalServerDiscovery
{
    public const string DiscoveryRequest = "BUSINESSOS_PHARMACY_DISCOVER_V1";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly INetworkConfigurationStore _configurationStore;

    public UdpLocalServerDiscovery(INetworkConfigurationStore configurationStore)
    {
        _configurationStore = configurationStore;
    }

    public async Task<IReadOnlyList<LocalServerDiscoveryAdvertisement>> DiscoverAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        var configuration = await _configurationStore.LoadAsync(cancellationToken);
        using var client = new UdpClient(AddressFamily.InterNetwork);
        client.EnableBroadcast = true;
        client.Client.ReceiveTimeout = Math.Max(1, (int)timeout.TotalMilliseconds);

        var request = Encoding.UTF8.GetBytes(DiscoveryRequest);
        await client.SendAsync(
            request,
            new IPEndPoint(IPAddress.Broadcast, configuration.DiscoveryPort),
            cancellationToken);

        var results = new ConcurrentDictionary<string, LocalServerDiscoveryAdvertisement>(
            StringComparer.OrdinalIgnoreCase);
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        while (!timeoutSource.IsCancellationRequested)
        {
            try
            {
                var response = await client.ReceiveAsync(timeoutSource.Token);
                var advertisement = JsonSerializer.Deserialize<LocalServerDiscoveryAdvertisement>(
                    response.Buffer,
                    JsonOptions);

                if (advertisement is null ||
                    !string.Equals(
                        advertisement.Service,
                        "BusinessOS.Pharmacy.LocalServer",
                        StringComparison.Ordinal) ||
                    string.IsNullOrWhiteSpace(advertisement.ServerId) ||
                    string.IsNullOrWhiteSpace(advertisement.CertificateSha256))
                {
                    continue;
                }

                results[advertisement.ServerId] = advertisement;
            }
            catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested)
            {
                break;
            }
            catch (JsonException)
            {
                // Ignore malformed or unrelated LAN broadcasts.
            }
            catch (SocketException)
            {
                break;
            }
        }

        return results.Values
            .OrderBy(x => x.ServerName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
