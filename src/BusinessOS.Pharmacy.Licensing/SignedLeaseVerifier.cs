using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BusinessOS.Pharmacy.Domain.Licensing;
using Microsoft.Extensions.Options;

namespace BusinessOS.Pharmacy.Licensing;

public interface ISignedLeaseVerifier
{
    EntitlementSnapshot Verify(string token);
}

public sealed class SignedLeaseVerifier : ISignedLeaseVerifier
{
    private readonly LicenseApiOptions _options;

    public SignedLeaseVerifier(IOptions<LicenseApiOptions> options) => _options = options.Value;

    public EntitlementSnapshot Verify(string token)
    {
        var parts = token.Split('.');
        if (parts.Length != 3 || parts[0] != "v1")
        {
            throw new CryptographicException("The license entitlement format is invalid.");
        }

        var payloadBytes = DecodeBase64Url(parts[1]);
        var signature = DecodeBase64Url(parts[2]);
        var publicKey = Convert.FromBase64String(_options.SigningPublicKey);

        if (publicKey.Length != 32 || signature.Length != 64 ||
            !Ed25519.Verify(signature, payloadBytes, publicKey))
        {
            throw new CryptographicException("The license entitlement signature is invalid.");
        }

        using var document = JsonDocument.Parse(payloadBytes);
        var root = document.RootElement;

        if (ReadInt(root, "v") != 1 ||
            ReadString(root, "purpose") != "offline_lease" ||
            ReadString(root, "platform") != "windows")
        {
            throw new CryptographicException("The signed entitlement claims are invalid.");
        }

        var issuedAt = FromUnix(ReadLong(root, "issued_at"));
        var expiresAt = FromUnix(ReadLong(root, "expires_at"));
        if (expiresAt <= issuedAt)
        {
            throw new CryptographicException("The signed entitlement lifetime is invalid.");
        }

        var features = root.TryGetProperty("entitlements", out var featureElement) &&
                       featureElement.ValueKind == JsonValueKind.Array
            ? featureElement.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString()!)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        return new EntitlementSnapshot(
            ReadString(root, "tenant_id"),
            ReadString(root, "subscription_id"),
            ReadString(root, "license_id"),
            ReadInt(root, "license_version"),
            ReadString(root, "activation_id"),
            ReadString(root, "device_id"),
            ReadString(root, "plan_code"),
            ParseSubscriptionState(ReadString(root, "subscription_status")),
            issuedAt,
            expiresAt,
            features);
    }

    private static byte[] DecodeBase64Url(string value)
    {
        value = value.Replace('-', '+').Replace('_', '/');
        value += value.Length % 4 switch { 2 => "==", 3 => "=", _ => string.Empty };
        return Convert.FromBase64String(value);
    }

    private static string ReadString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : throw new CryptographicException($"Signed entitlement claim '{name}' is missing.");

    private static long ReadLong(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.TryGetInt64(out var result)
            ? result
            : throw new CryptographicException($"Signed entitlement claim '{name}' is invalid.");

    private static int ReadInt(JsonElement root, string name) =>
        checked((int)ReadLong(root, name));

    private static DateTimeOffset FromUnix(long value) => DateTimeOffset.FromUnixTimeSeconds(value);

    private static SubscriptionState ParseSubscriptionState(string value) =>
        value.ToLowerInvariant() switch
        {
            "trial" => SubscriptionState.Trial,
            "active" => SubscriptionState.Active,
            "grace_period" or "graceperiod" => SubscriptionState.GracePeriod,
            "expired" => SubscriptionState.Expired,
            "suspended" => SubscriptionState.Suspended,
            "cancelled" or "canceled" => SubscriptionState.Cancelled,
            "pending" => SubscriptionState.Pending,
            _ => throw new CryptographicException("The subscription state in the signed entitlement is invalid.")
        };
}