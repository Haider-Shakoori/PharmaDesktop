using System.Security.Cryptography;
using System.Text.Json;
using BusinessOS.Pharmacy.Domain.Authentication;
using Microsoft.Extensions.Options;
using NSec.Cryptography;

namespace BusinessOS.Pharmacy.Licensing;

public interface ISignedDesktopSessionVerifier
{
    UserSessionSnapshot Verify(string token);
}

public sealed class SignedDesktopSessionVerifier : ISignedDesktopSessionVerifier
{
    private readonly LicenseApiOptions _options;

    public SignedDesktopSessionVerifier(IOptions<LicenseApiOptions> options) => _options = options.Value;

    public UserSessionSnapshot Verify(string token)
    {
        if (string.IsNullOrWhiteSpace(_options.SigningPublicKey))
        {
            throw new CryptographicException("The desktop release is missing the BusinessOS license signing public key.");
        }

        var parts = token.Split('.');
        if (parts.Length != 3 || parts[0] != "v1")
        {
            throw new CryptographicException("The desktop user session format is invalid.");
        }

        var payloadBytes = DecodeBase64Url(parts[1]);
        var signature = DecodeBase64Url(parts[2]);
        var publicKeyBytes = Convert.FromBase64String(_options.SigningPublicKey);
        var algorithm = SignatureAlgorithm.Ed25519;

        if (publicKeyBytes.Length != algorithm.PublicKeySize ||
            signature.Length != algorithm.SignatureSize)
        {
            throw new CryptographicException("The desktop user session signature is invalid.");
        }

        var publicKey = PublicKey.Import(algorithm, publicKeyBytes, KeyBlobFormat.RawPublicKey);
        if (!algorithm.Verify(publicKey, payloadBytes, signature))
        {
            throw new CryptographicException("The desktop user session signature is invalid.");
        }

        using var document = JsonDocument.Parse(payloadBytes);
        var root = document.RootElement;

        if (ReadInt(root, "v") != 1 ||
            ReadString(root, "purpose") != "desktop_access")
        {
            throw new CryptographicException("The desktop user session claims are invalid.");
        }

        var issuedAt = DateTimeOffset.FromUnixTimeSeconds(ReadLong(root, "issued_at"));
        var expiresAt = DateTimeOffset.FromUnixTimeSeconds(ReadLong(root, "expires_at"));
        if (expiresAt <= issuedAt)
        {
            throw new CryptographicException("The desktop user session lifetime is invalid.");
        }

        return new UserSessionSnapshot(
            ReadIdentifier(root, "user_id"),
            ReadIdentifier(root, "tenant_id"),
            ReadIdentifier(root, "activation_id"),
            ReadString(root, "device_id"),
            string.Empty,
            string.Empty,
            ReadStringSet(root, "roles"),
            ReadStringSet(root, "permissions"),
            issuedAt,
            expiresAt);
    }

    private static byte[] DecodeBase64Url(string value)
    {
        value = value.Replace('-', '+').Replace('_', '/');
        value += (value.Length % 4) switch { 2 => "==", 3 => "=", _ => string.Empty };
        return Convert.FromBase64String(value);
    }

    private static string ReadIdentifier(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value))
        {
            throw new CryptographicException($"Desktop session claim '{name}' is missing.");
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString()!,
            JsonValueKind.Number => value.GetRawText(),
            _ => throw new CryptographicException($"Desktop session claim '{name}' is invalid.")
        };
    }

    private static string ReadString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : throw new CryptographicException($"Desktop session claim '{name}' is missing.");

    private static long ReadLong(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.TryGetInt64(out var result)
            ? result
            : throw new CryptographicException($"Desktop session claim '{name}' is invalid.");

    private static int ReadInt(JsonElement root, string name) => checked((int)ReadLong(root, name));

    private static IReadOnlySet<string> ReadStringSet(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            throw new CryptographicException($"Desktop session claim '{name}' is invalid.");
        }

        return value.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString()!)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
