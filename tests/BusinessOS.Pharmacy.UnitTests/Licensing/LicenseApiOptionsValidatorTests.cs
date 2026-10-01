using BusinessOS.Pharmacy.Licensing;
using Xunit;

namespace BusinessOS.Pharmacy.UnitTests.Licensing;

public sealed class LicenseApiOptionsValidatorTests
{
    private readonly LicenseApiOptionsValidator _validator = new();

    [Fact]
    public void ValidProductionOptions_Pass()
    {
        var result = _validator.Validate(null, new LicenseApiOptions
        {
            BaseUrl = "https://pharmacy.businessos.af",
            ActivationPath = "/api/v1/license/activate",
            TimeoutSeconds = 15,
        });

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void NonHttpsRemoteEndpoint_Fails()
    {
        var result = _validator.Validate(null, new LicenseApiOptions
        {
            BaseUrl = "http://pharmacy.businessos.af",
            ActivationPath = "/api/v1/license/activate",
            TimeoutSeconds = 15,
        });

        Assert.True(result.Failed);
    }

    [Fact]
    public void UnsafeTimeout_Fails()
    {
        var result = _validator.Validate(null, new LicenseApiOptions
        {
            BaseUrl = "https://pharmacy.businessos.af",
            ActivationPath = "/api/v1/license/activate",
            TimeoutSeconds = 1,
        });

        Assert.True(result.Failed);
    }

    [Fact]
    public void InvalidRefreshPath_Fails()
    {
        var result = _validator.Validate(null, new LicenseApiOptions
        {
            BaseUrl = "https://pharmacy.businessos.af",
            ActivationPath = "/api/v1/license/activate",
            RefreshPath = "/desktop/license/refresh",
            TimeoutSeconds = 15,
        });

        Assert.True(result.Failed);
    }

    [Theory]
    [InlineData("https://user:secret@pharmacy.businessos.af")]
    [InlineData("https://pharmacy.businessos.af?tenant=other")]
    [InlineData("https://pharmacy.businessos.af#fragment")]
    public void BaseUrl_with_embedded_state_Fails(string baseUrl)
    {
        var result = _validator.Validate(null, new LicenseApiOptions
        {
            BaseUrl = baseUrl,
            ActivationPath = "/api/v1/license/activate",
            TimeoutSeconds = 15,
        });

        Assert.True(result.Failed);
    }

    [Fact]
    public void ApiPath_with_query_Fails()
    {
        var result = _validator.Validate(null, new LicenseApiOptions
        {
            BaseUrl = "https://pharmacy.businessos.af",
            ActivationPath = "/api/v1/license/activate?tenant=other",
            TimeoutSeconds = 15,
        });

        Assert.True(result.Failed);
    }

}
