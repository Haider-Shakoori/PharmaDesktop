using System.Security.Cryptography;
using System.Text.Json;
using BusinessOS.Pharmacy.Application.Abstractions.Storage;

namespace BusinessOS.Pharmacy.Licensing;

public sealed class WindowsUserSessionStore : IUserSessionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IApplicationPaths _paths;

    public WindowsUserSessionStore(IApplicationPaths paths) => _paths = paths;

    public async Task<DesktopSessionState?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_paths.UserSessionStatePath))
        {
            return null;
        }

        var protectedBytes = await File.ReadAllBytesAsync(_paths.UserSessionStatePath, cancellationToken);
        var plainBytes = Unprotect(protectedBytes);
        try
        {
            return JsonSerializer.Deserialize<DesktopSessionState>(plainBytes, JsonOptions)
                ?? throw new CryptographicException("The protected pharmacy user session is invalid.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plainBytes);
        }
    }

    public async Task SaveAsync(DesktopSessionState state, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_paths.LicensingDirectory);
        var plainBytes = JsonSerializer.SerializeToUtf8Bytes(state, JsonOptions);
        try
        {
            var protectedBytes = Protect(plainBytes);
            var temporary = _paths.UserSessionStatePath + ".tmp";
            await File.WriteAllBytesAsync(temporary, protectedBytes, cancellationToken);
            File.Move(temporary, _paths.UserSessionStatePath, overwrite: true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plainBytes);
        }
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        if (File.Exists(_paths.UserSessionStatePath))
        {
            File.Delete(_paths.UserSessionStatePath);
        }

        return Task.CompletedTask;
    }

    private static byte[] Protect(byte[] value)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Protected pharmacy user sessions require Windows DPAPI.");
        }

        return ProtectedData.Protect(value, optionalEntropy: null, DataProtectionScope.CurrentUser);
    }

    private static byte[] Unprotect(byte[] value)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Protected pharmacy user sessions require Windows DPAPI.");
        }

        return ProtectedData.Unprotect(value, optionalEntropy: null, DataProtectionScope.CurrentUser);
    }
}
