using BusinessOS.Pharmacy.Application.Abstractions.Storage;

namespace BusinessOS.Pharmacy.Licensing;

public sealed class InstallationIdentityProvider : IInstallationIdentityProvider
{
    private readonly IApplicationPaths _paths;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public InstallationIdentityProvider(IApplicationPaths paths) => _paths = paths;

    public async Task<string> GetOrCreateAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(_paths.LicensingDirectory);

            if (File.Exists(_paths.InstallationIdPath))
            {
                var existing = (await File.ReadAllTextAsync(_paths.InstallationIdPath, cancellationToken)).Trim();
                if (Guid.TryParse(existing, out var parsed))
                {
                    return parsed.ToString("D");
                }
            }

            var id = Guid.NewGuid().ToString("D");
            var temporary = _paths.InstallationIdPath + ".tmp";
            await File.WriteAllTextAsync(temporary, id, cancellationToken);
            File.Move(temporary, _paths.InstallationIdPath, overwrite: true);
            return id;
        }
        finally
        {
            _gate.Release();
        }
    }
}