using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BusinessOS.Pharmacy.Application.Abstractions.Networking;
using BusinessOS.Pharmacy.Application.Abstractions.Storage;

namespace BusinessOS.Pharmacy.Infrastructure.Networking;

public sealed class WindowsNetworkSecretStore : INetworkSecretStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IApplicationPaths _paths;

    public WindowsNetworkSecretStore(IApplicationPaths paths) => _paths = paths;

    public async Task<TerminalPairingSecret?> LoadTerminalPairingAsync(
        CancellationToken cancellationToken = default)
    {
        var state = await LoadAsync(cancellationToken);
        return state?.TerminalPairing;
    }

    public async Task SaveTerminalPairingAsync(
        TerminalPairingSecret secret,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(secret);
        var state = await LoadAsync(cancellationToken) ?? new NetworkSecretsState();
        await SaveAsync(state with { TerminalPairing = secret }, cancellationToken);
    }

    public async Task ClearTerminalPairingAsync(
        CancellationToken cancellationToken = default)
    {
        var state = await LoadAsync(cancellationToken);
        if (state is null)
        {
            return;
        }

        await SaveAsync(state with { TerminalPairing = null }, cancellationToken);
    }

    public async Task<string?> LoadServerCertificatePasswordAsync(
        CancellationToken cancellationToken = default)
    {
        var state = await LoadAsync(cancellationToken);
        return state?.ServerCertificatePassword;
    }

    public async Task SaveServerCertificatePasswordAsync(
        string password,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        var state = await LoadAsync(cancellationToken) ?? new NetworkSecretsState();
        await SaveAsync(state with { ServerCertificatePassword = password }, cancellationToken);
    }

    private async Task<NetworkSecretsState?> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_paths.NetworkSecretsPath))
        {
            return null;
        }

        var protectedBytes = await File.ReadAllBytesAsync(
            _paths.NetworkSecretsPath,
            cancellationToken);
        var plainBytes = Unprotect(protectedBytes);

        try
        {
            return JsonSerializer.Deserialize<NetworkSecretsState>(plainBytes, JsonOptions)
                ?? throw new CryptographicException("Protected network secret state is invalid.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plainBytes);
        }
    }

    private async Task SaveAsync(
        NetworkSecretsState state,
        CancellationToken cancellationToken)
    {
        _paths.EnsureCreated();

        var plainBytes = JsonSerializer.SerializeToUtf8Bytes(state, JsonOptions);
        try
        {
            var protectedBytes = Protect(plainBytes);
            var temporary = _paths.NetworkSecretsPath + ".tmp";
            await File.WriteAllBytesAsync(temporary, protectedBytes, cancellationToken);
            File.Move(temporary, _paths.NetworkSecretsPath, overwrite: true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plainBytes);
        }
    }

    private static byte[] Protect(byte[] value)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Network secret storage requires Windows DPAPI.");
        }

        return ProtectedData.Protect(
            value,
            optionalEntropy: Encoding.UTF8.GetBytes("BusinessOS.Pharmacy.Network.v1"),
            DataProtectionScope.LocalMachine);
    }

    private static byte[] Unprotect(byte[] value)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Network secret storage requires Windows DPAPI.");
        }

        return ProtectedData.Unprotect(
            value,
            optionalEntropy: Encoding.UTF8.GetBytes("BusinessOS.Pharmacy.Network.v1"),
            DataProtectionScope.LocalMachine);
    }

    private sealed record NetworkSecretsState(
        TerminalPairingSecret? TerminalPairing = null,
        string? ServerCertificatePassword = null);
}
