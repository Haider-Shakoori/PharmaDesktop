using System.Text;
using System.Text.Json;
using BusinessOS.Pharmacy.Licensing;
using Microsoft.Extensions.Options;
using NSec.Cryptography;
using Xunit;

namespace BusinessOS.Pharmacy.UnitTests.Licensing;

public sealed class SignedLeaseVerifierTests
{
    [Fact]
    public void Verify_accepts_valid_windows_lease_and_signed_entitlements()
    {
        var algorithm = SignatureAlgorithm.Ed25519;
        using var key = Key.Create(algorithm, new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });
        var publicKey = Convert.ToBase64String(key.PublicKey.Export(KeyBlobFormat.RawPublicKey));
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            v = 1,
            purpose = "offline_lease",
            entitlement_version = 1,
            tenant_id = "tenant-1",
            subscription_id = "sub-1",
            license_id = "license-1",
            license_version = 3,
            activation_id = "activation-1",
            device_id = "device-1",
            platform = "windows",
            plan_code = "PRO",
            subscription_status = "trial",
            trial_started_at = now - 86400,
            trial_expires_at = now + (7 * 86400),
            subscription_expires_at = now + (365 * 86400),
            issued_at = now,
            expires_at = now + 3600,
            entitlements = new[] { "advanced_reports", "multi_user" }
        });

        var signature = algorithm.Sign(key, payload);
        var token = $"v1.{Base64Url(payload)}.{Base64Url(signature)}";
        var verifier = new SignedLeaseVerifier(Options.Create(new LicenseApiOptions { SigningPublicKey = publicKey }));

        var entitlement = verifier.Verify(token);

        Assert.Equal("tenant-1", entitlement.TenantId);
        Assert.Equal("device-1", entitlement.DeviceId);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(now + (7 * 86400)), entitlement.TrialExpiresAt);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(now + (365 * 86400)), entitlement.SubscriptionExpiresAt);
        Assert.True(entitlement.HasFeature("advanced_reports"));
    }

    [Fact]
    public void Verify_rejects_tampered_payload()
    {
        var algorithm = SignatureAlgorithm.Ed25519;
        using var key = Key.Create(algorithm, new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });
        var publicKey = Convert.ToBase64String(key.PublicKey.Export(KeyBlobFormat.RawPublicKey));
        var payload = Encoding.UTF8.GetBytes("{\"v\":1,\"purpose\":\"offline_lease\",\"platform\":\"windows\"}");
        var signature = algorithm.Sign(key, payload);
        payload[5] ^= 1;

        var token = $"v1.{Base64Url(payload)}.{Base64Url(signature)}";
        var verifier = new SignedLeaseVerifier(Options.Create(new LicenseApiOptions { SigningPublicKey = publicKey }));

        Assert.Throws<System.Security.Cryptography.CryptographicException>(() => verifier.Verify(token));
    }

    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}