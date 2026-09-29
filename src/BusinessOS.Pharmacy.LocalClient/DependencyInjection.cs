using BusinessOS.Pharmacy.Application.Abstractions.Authentication;
using BusinessOS.Pharmacy.Application.Abstractions.Dashboard;
using BusinessOS.Pharmacy.Application.Abstractions.Medicines;
using Microsoft.Extensions.DependencyInjection;

namespace BusinessOS.Pharmacy.LocalClient;

public static class DependencyInjection
{
    public static IServiceCollection AddBusinessOSLocalClient(
        this IServiceCollection services)
    {
        services.AddSingleton<LanClientSessionState>();
        services.AddSingleton<PinnedLocalServerTransport>();
        services.AddSingleton<LanTerminalPairingClient>();
        services.AddSingleton<LanApiRequestFactory>();

        services.AddSingleton<IUserSessionService, LanUserSessionService>();
        services.AddSingleton<IPermissionAuthorizer, LanClientPermissionAuthorizer>();

        services.AddTransient<IMedicineCatalogService, LanMedicineCatalogService>();
        services.AddTransient<IMedicineCsvService, LanMedicineCsvService>();
        services.AddSingleton<ILocalDashboardQueryService, LanDashboardQueryService>();

        return services;
    }
}
