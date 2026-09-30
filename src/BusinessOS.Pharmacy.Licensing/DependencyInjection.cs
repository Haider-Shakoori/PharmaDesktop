using BusinessOS.Pharmacy.Application.Abstractions.Authentication;
using BusinessOS.Pharmacy.Application.Abstractions.Licensing;
using BusinessOS.Pharmacy.Application.Abstractions.Sync;
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
        services.AddTransient<IDesktopSessionClient, DesktopSessionClient>();

        services.AddSingleton<ISignedLeaseVerifier, SignedLeaseVerifier>();
        services.AddSingleton<ISignedDesktopSessionVerifier, SignedDesktopSessionVerifier>();
        services.AddSingleton<IInstallationIdentityProvider, InstallationIdentityProvider>();
        services.AddSingleton<IActivationStore, WindowsActivationStore>();
        services.AddSingleton<IUserSessionStore, WindowsUserSessionStore>();
        services.AddSingleton<IOfflinePasswordVerifier, OfflinePasswordVerifier>();

        services.AddSingleton<ILicenseService, LicenseService>();
        services.AddSingleton<ICloudSyncSessionProvider, DesktopCloudSyncSessionProvider>();
        services.AddSingleton<IUserSessionService, PharmacyUserSessionService>();
        services.AddSingleton<IPermissionAuthorizer, PermissionAuthorizer>();

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
