using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BusinessOS.Pharmacy.Licensing;

public static class DependencyInjection
{
    public const string HttpClientName = "BusinessOS.Pharmacy.Licensing";

    public static IServiceCollection AddBusinessOSLicensing(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton<IValidateOptions<LicenseApiOptions>, LicenseApiOptionsValidator>();

        services.AddOptions<LicenseApiOptions>()
            .Bind(configuration.GetSection(LicenseApiOptions.SectionName))
            .ValidateOnStart();

        services.AddTransient<ILicenseActivationClient, LicenseActivationClient>();

        services.AddHttpClient(HttpClientName, (serviceProvider, client) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<LicenseApiOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl, UriKind.Absolute);
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("BusinessOS-Pharmacy-Desktop/1.0");
        });

        return services;
    }
}
