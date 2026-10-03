using System.Net;
using System.Net.Http.Headers;
using System.Security.Authentication;
using BusinessOS.Pharmacy.Application.Abstractions.Administration;
using BusinessOS.Pharmacy.Application.Abstractions.Authentication;
using BusinessOS.Pharmacy.Application.Abstractions.Licensing;
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
        services.AddSingleton<IUserSessionService, PharmacyUserSessionService>();
        services.AddSingleton<IPermissionAuthorizer, PermissionAuthorizer>();
        services.AddTransient<IAccessManagementService, DesktopAccessManagementService>();

        services.AddHttpClient(HttpClientName, (serviceProvider, client) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<LicenseApiOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl, UriKind.Absolute);
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("BusinessOS-Pharmacy-Desktop/1.0");
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));
        })
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression =
                DecompressionMethods.GZip |
                DecompressionMethods.Deflate |
                DecompressionMethods.Brotli,
            ConnectTimeout = TimeSpan.FromSeconds(5),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1),
            MaxConnectionsPerServer = 8,
            SslOptions = new()
            {
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            },
        });

        return services;
    }
}
