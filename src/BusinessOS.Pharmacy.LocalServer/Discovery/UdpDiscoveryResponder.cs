using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using BusinessOS.Pharmacy.Application.Abstractions.Networking;
using BusinessOS.Pharmacy.Infrastructure.Networking;
using BusinessOS.Pharmacy.LocalServer.Runtime;

namespace BusinessOS.Pharmacy.LocalServer.Discovery;

public sealed class UdpDiscoveryResponder(
    LocalServerRuntimeState runtime,
    ILogger<UdpDiscoveryResponder> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var state = runtime.Require();

        if (!state.Configuration.DiscoveryEnabled)
        {
            logger.LogInformation("LAN discovery is disabled.");
            return;
        }

        using var udp = new UdpClient(AddressFamily.InterNetwork);
        udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        udp.Client.Bind(new IPEndPoint(IPAddress.Any, state.Configuration.DiscoveryPort));

        logger.LogInformation(
            "LAN discovery listening on UDP port {DiscoveryPort}.",
            state.Configuration.DiscoveryPort);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var message = await udp.ReceiveAsync(stoppingToken);
                var text = Encoding.UTF8.GetString(message.Buffer);

                if (!string.Equals(
                        text,
                        UdpLocalServerDiscovery.DiscoveryRequest,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                var advertisement = new LocalServerDiscoveryAdvertisement(
                    "BusinessOS.Pharmacy.LocalServer",
                    "v1",
                    state.Identity.ServerId,
                    state.Identity.ServerName,
                    Environment.MachineName,
                    state.Configuration.ServerPort,
                    state.CertificateSha256);

                var payload = JsonSerializer.SerializeToUtf8Bytes(advertisement, JsonOptions);
                await udp.SendAsync(payload, message.RemoteEndPoint, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (SocketException exception)
            {
                logger.LogWarning(exception, "LAN discovery socket error.");
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "LAN discovery responder failed to process a request.");
            }
        }
    }
}
