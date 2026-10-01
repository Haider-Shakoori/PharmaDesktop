using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BusinessOS.Pharmacy.Application.Abstractions.Networking;
using BusinessOS.Pharmacy.Application.Abstractions.Storage;

namespace BusinessOS.Pharmacy.Infrastructure.Networking;

public sealed class WindowsNetworkSecretStore : INetworkSecretStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("BusinessOS.Pharmacy.Network.v1");
    private readonly IApplicationPaths _paths;
    private readonly SemaphoreSlim _writeGate = new(1, 1);

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
        ValidatePairing(secret);
        await MutateAsync(
            state => state with { TerminalPairing = secret },
            cancellationToken);
    }

    public Task ClearTerminalPairingAsync(
        CancellationToken cancellationToken = default) =>
        MutateAsync(
            state => state with { TerminalPairing = null },
            cancellationToken);

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
        if (password.Length > 512)
        {
            throw new ArgumentOutOfRangeException(nameof(password));
        }

        await MutateAsync(
            state => state with { ServerCertificatePassword = password },
            cancellationToken);
    }

    private async Task MutateAsync(
        Func<NetworkSecretsState, NetworkSecretsState> mutation,
        CancellationToken cancellationToken)
    {
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            var current = await LoadAsync(cancellationToken) ?? new NetworkSecretsState();
            await SaveAsync(mutation(current), cancellationToken);
        }
        finally
        {
            _writeGate.Release();
        }
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
        if (protectedBytes.Length is 0 or > 256 * 1024)
        {
            throw new CryptographicException("Protected network secret state has an invalid size.");
        }

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
        byte[]? protectedBytes = null;
        var temporary = _paths.NetworkSecretsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";

        try
        {
            protectedBytes = Protect(plainBytes);
            await using (var stream = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                16 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(protectedBytes, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporary, _paths.NetworkSecretsPath, overwrite: true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plainBytes);
            if (protectedBytes is not null)
            {
                CryptographicOperations.ZeroMemory(protectedBytes);
            }

            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static void ValidatePairing(TerminalPairingSecret secret)
    {
        ArgumentNullException.ThrowIfNull(secret);

        if (string.IsNullOrWhiteSpace(secret.TerminalId) || secret.TerminalId.Length > 128 ||
            string.IsNullOrWhiteSpace(secret.ServerId) || secret.ServerId.Length > 128 ||
            string.IsNullOrWhiteSpace(secret.TerminalSecret) || secret.TerminalSecret.Length is < 32 or > 512)
        {
            throw new ArgumentException("Terminal pairing credential is invalid.", nameof(secret));
        }

        var fingerprint = secret.ServerCertificateSha256
            .Replace(":", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Trim();

        if (fingerprint.Length != 64 || fingerprint.Any(x => !Uri.IsHexDigit(x)))
        {
            throw new ArgumentException("Terminal pairing certificate fingerprint is invalid.", nameof(secret));
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
            optionalEntropy: Entropy,
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
            optionalEntropy: Entropy,
            DataProtectionScope.LocalMachine);
    }

    private sealed record NetworkSecretsState(
        TerminalPairingSecret? TerminalPairing = null,
        string? ServerCertificatePassword = null);
}
