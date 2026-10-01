using BusinessOS.Pharmacy.Sync;
using Xunit;

namespace BusinessOS.Pharmacy.UnitTests.Sync;

public sealed class CloudSyncOptionsSecurityTests
{
    [Fact]
    public void Clean_https_options_are_accepted()
    {
        new CloudSyncOptions("https://pharmacy.businessos.af").Validate();
    }

    [Theory]
    [InlineData("http://pharmacy.businessos.af")]
    [InlineData("https://user:secret@pharmacy.businessos.af")]
    [InlineData("https://pharmacy.businessos.af?token=secret")]
    [InlineData("https://pharmacy.businessos.af#fragment")]
    public void Unsafe_base_url_is_rejected(string baseUrl)
    {
        Assert.Throws<InvalidOperationException>(() =>
            new CloudSyncOptions(baseUrl).Validate());
    }

    [Theory]
    [InlineData("//attacker.example/api/v1/desktop/sync/push")]
    [InlineData("https://attacker.example/api/v1/desktop/sync/push")]
    [InlineData("/api/v1/desktop/sync/push?redirect=https://attacker.example")]
    [InlineData("/api/v1/desktop/sync/push#fragment")]
    [InlineData("/not-api/sync/push")]
    public void Unsafe_api_path_is_rejected(string pushPath)
    {
        Assert.Throws<InvalidOperationException>(() =>
            new CloudSyncOptions(
                "https://pharmacy.businessos.af",
                PushPath: pushPath).Validate());
    }
}
