using System.Security.Cryptography;
using System.Text.Json;
using BusinessOS.Pharmacy.Application.Abstractions.Storage;

namespace BusinessOS.Pharmacy.Licensing;

public sealed class WindowsActivationStore : IActivationStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IApplicationPaths _paths;

    public WindowsActivationStore(IApplicationPaths paths) => _paths = paths;

    public async Task<ActivationState?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_paths.ActivationStatePath))
        {
            return null;
        }

        var protectedBytes = await File.ReadAllBytesAsync(_paths.ActivationStatePath, cancellationToken);
        var plainBytes = Unprotect(protectedBytes);
        return JsonSerializer.Deserialize<ActivationState>(plainBytes, JsonOptions)
            ?? throw new CryptographicException("The protected activation state is invalid.");
    }

    public async Task SaveAsync(ActivationState state, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_paths.LicensingDirectory);
        var plainBytes = JsonSerializer.SerializeToUtf8Bytes(state, JsonOptions);
        var protectedBytes = Protect(plainBytes);
        var temporary = _paths.ActivationStatePath + ".tmp";
        await File.WriteAllBytesAsync(temporary, protectedBytes, cancellationToken);
        File.Move(temporary, _paths.ActivationStatePath, overwrite: true);
        CryptographicOperations.ZeroMemory(plainBytes);
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        if (File.Exists(_paths.ActivationStatePath))
        {
            File.Delete(_paths.ActivationStatePath);
        }

        return Task.CompletedTask;
    }

    private static byte[] Protect(byte[] value)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Protected activation storage requires Windows DPAPI.");
        }

        return ProtectedData.Protect(value, optionalEntropy: null, DataProtectionScope.LocalMachine);
    }

    private static byte[] Unprotect(byte[] value)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Protected activation storage requires Windows DPAPI.");
        }

        return ProtectedData.Unprotect(value, optionalEntropy: null, DataProtectionScope.LocalMachine);
    }
}