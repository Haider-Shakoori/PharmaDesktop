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
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    public NetworkConfigurationStore(IApplicationPaths paths) => _paths = paths;

    public async Task<NetworkConfiguration> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_paths.NetworkConfigurationPath))
        {
            return new NetworkConfiguration();
        }

        await using var stream = new FileStream(
            _paths.NetworkConfigurationPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            16 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

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
        await _writeGate.WaitAsync(cancellationToken);
        var temporary = _paths.NetworkConfigurationPath + "." + Guid.NewGuid().ToString("N") + ".tmp";

        try
        {
            await using (var stream = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                16 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    configuration,
                    JsonOptions,
                    cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporary, _paths.NetworkConfigurationPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }

            _writeGate.Release();
        }
    }
}
