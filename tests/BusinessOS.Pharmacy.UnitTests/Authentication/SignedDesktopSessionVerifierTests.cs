using System.Text.Json;
using BusinessOS.Pharmacy.Licensing;
using Microsoft.Extensions.Options;
using NSec.Cryptography;
using Xunit;

namespace BusinessOS.Pharmacy.UnitTests.Authentication;

public sealed class SignedDesktopSessionVerifierTests
{
    [Fact]
    public void Verify_accepts_signed_identity_roles_and_permissions()
    {
        var algorithm = SignatureAlgorithm.Ed25519;
        using var key = Key.Create(algorithm, new KeyCreationParameters
        {
            ExportPolicy = KeyExportPolicies.AllowPlaintextExport,
        });

        var publicKey = Convert.ToBase64String(
            key.PublicKey.Export(KeyBlobFormat.RawPublicKey));
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            v = 1,
            purpose = "desktop_access",
            tenant_id = "tenant-1",
            activation_id = "activation-1",
            device_id = "11111111-1111-4111-8111-111111111111",
            user_id = 42,
            user_name = "Cashier One",
            user_email = "cashier@example.test",
            roles = new[] { "cashier" },
            permissions = new[] { "dashboard.view", "pos.sell" },
            issued_at = now,
            expires_at = now + 3600,
        });

        var signature = algorithm.Sign(key, payload);
        var token = $"v1.{Base64Url(payload)}.{Base64Url(signature)}";
        var verifier = new SignedDesktopSessionVerifier(
            Options.Create(new LicenseApiOptions { SigningPublicKey = publicKey }));

        var session = verifier.Verify(token);

        Assert.Equal("42", session.UserId);
        Assert.Equal("Cashier One", session.Name);
        Assert.Equal("cashier@example.test", session.Email);
        Assert.Contains("cashier", session.Roles);
        Assert.Contains("pos.sell", session.Permissions);
    }

    [Fact]
    public void Verify_rejects_tampered_session()
    {
        var algorithm = SignatureAlgorithm.Ed25519;
        using var key = Key.Create(algorithm, new KeyCreationParameters
        {
            ExportPolicy = KeyExportPolicies.AllowPlaintextExport,
        });

        var publicKey = Convert.ToBase64String(
            key.PublicKey.Export(KeyBlobFormat.RawPublicKey));
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            v = 1,
            purpose = "desktop_access",
            tenant_id = "tenant-1",
            activation_id = "activation-1",
            device_id = "11111111-1111-4111-8111-111111111111",
            user_id = 42,
            user_name = "Cashier One",
            user_email = "cashier@example.test",
            roles = new[] { "cashier" },
            permissions = new[] { "pos.sell" },
            issued_at = now,
            expires_at = now + 3600,
        });

        var signature = algorithm.Sign(key, payload);
        payload[^2] ^= 1;

        var token = $"v1.{Base64Url(payload)}.{Base64Url(signature)}";
        var verifier = new SignedDesktopSessionVerifier(
            Options.Create(new LicenseApiOptions { SigningPublicKey = publicKey }));

        Assert.Throws<System.Security.Cryptography.CryptographicException>(
            () => verifier.Verify(token));
    }

    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
