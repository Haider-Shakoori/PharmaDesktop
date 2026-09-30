using System.Text.Json;
using BusinessOS.Pharmacy.Application.Abstractions.Networking;
using BusinessOS.Pharmacy.Application.Abstractions.Storage;

namespace BusinessOS.Pharmacy.Infrastructure.Networking;

public sealed class NetworkConfigurationStore : INetworkConfigurationStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly IApplicationPaths _paths;

    public NetworkConfigurationStore(IApplicationPaths paths) => _paths = paths;

    public async Task<NetworkConfiguration> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_paths.NetworkConfigurationPath))
        {
            return new NetworkConfiguration();
        }

        await using var stream = File.OpenRead(_paths.NetworkConfigurationPath);
        var configuration = await JsonSerializer.DeserializeAsync<NetworkConfiguration>(
            stream,
            JsonOptions,
            cancellationToken);

        configuration ??= new NetworkConfiguration();
        configuration.Validate();
        return configuration;
    }

    public async Task SaveAsync(
        NetworkConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        configuration.Validate();

        _paths.EnsureCreated();

        var temporary = _paths.NetworkConfigurationPath + ".tmp";
        await using (var stream = File.Create(temporary))
        {
            await JsonSerializer.SerializeAsync(
                stream,
                configuration,
                JsonOptions,
                cancellationToken);
        }

        File.Move(temporary, _paths.NetworkConfigurationPath, overwrite: true);
    }
}
