using BusinessOS.Pharmacy.Licensing;
using BusinessOS.Pharmacy.Sync;
using BusinessOS.Pharmacy.Updater;
using Xunit;

namespace BusinessOS.Pharmacy.UnitTests.Security;

public sealed class Batch23HardeningTests
{
    [Theory]
    [InlineData("https://user:secret@pharmacy.businessos.af")]
    [InlineData("https://pharmacy.businessos.af?token=secret")]
    [InlineData("https://pharmacy.businessos.af#fragment")]
    public void Licensing_rejects_credential_bearing_or_ambiguous_base_urls(string baseUrl)
    {
        var result = new LicenseApiOptionsValidator().Validate(null, new LicenseApiOptions
        {
            BaseUrl = baseUrl,
            ActivationPath = "/api/v1/license/activate",
            TimeoutSeconds = 15,
        });

        Assert.True(result.Failed);
    }

    [Theory]
    [InlineData("https://user:secret@pharmacy.businessos.af")]
    [InlineData("https://pharmacy.businessos.af?token=secret")]
    [InlineData("https://pharmacy.businessos.af#fragment")]
    [InlineData("http://pharmacy.businessos.af")]
    public void Sync_rejects_unsafe_base_urls(string baseUrl)
    {
        Assert.Throws<InvalidOperationException>(() => new CloudSyncOptions(baseUrl).Validate());
    }

    [Fact]
    public void Offline_password_verifier_rejects_tampered_cost_parameters()
    {
        var verifier = new OfflinePasswordVerifier();
        var credential = new OfflinePasswordCredential(
            Convert.ToBase64String(new byte[16]),
            Convert.ToBase64String(new byte[32]),
            OfflinePasswordVerifier.MaximumIterations + 1);

        Assert.False(verifier.Verify("password", credential));
    }

    [Fact]
    public void Update_manifest_rejects_package_url_with_embedded_credentials()
    {
        var manifest = new UpdateManifest(
            1, "stable", "1.0.1", "v1", "1.0.0", null,
            "https://user:secret@updates.businessos.af/darmaltoon.zip",
            new string('A', 64), DateTimeOffset.UtcNow, null, "AA==");

        Assert.Throws<InvalidOperationException>(() =>
            UpdateManifestSecurity.ValidateAndVerify(manifest, "unused"));
    }
}
