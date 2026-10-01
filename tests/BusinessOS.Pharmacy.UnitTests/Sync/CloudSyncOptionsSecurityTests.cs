using BusinessOS.Pharmacy.Sync;
using Xunit;

namespace BusinessOS.Pharmacy.UnitTests.Sync;

public sealed class CloudSyncOptionsSecurityTests
{
    [Fact]
    public void Valid_https_configuration_passes()
    {
        new CloudSyncOptions("https://pharmacy.businessos.af").Validate();
    }

    [Theory]
    [InlineData("http://pharmacy.businessos.af")]
    [InlineData("https://user:secret@pharmacy.businessos.af")]
    [InlineData("https://pharmacy.businessos.af?tenant=other")]
    [InlineData("https://pharmacy.businessos.af#fragment")]
    public void Unsafe_base_url_is_rejected(string baseUrl)
    {
        Assert.Throws<InvalidOperationException>(
            () => new CloudSyncOptions(baseUrl).Validate());
    }

    [Theory]
    [InlineData("//evil.example/api/v1/sync")]
    [InlineData("/api/v1/sync?tenant=other")]
    [InlineData("/api/v1/sync#fragment")]
    [InlineData("/not-api/sync")]
    public void Unsafe_api_path_is_rejected(string path)
    {
        Assert.Throws<InvalidOperationException>(
            () => new CloudSyncOptions(
                "https://pharmacy.businessos.af",
                PushPath: path).Validate());
    }
}
