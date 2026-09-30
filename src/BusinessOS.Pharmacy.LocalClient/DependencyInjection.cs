using BusinessOS.Pharmacy.Application.Abstractions.Sales;
using BusinessOS.Pharmacy.Application.Abstractions.Reports;
using BusinessOS.Pharmacy.Application.Abstractions.Purchasing;
using BusinessOS.Pharmacy.Application.Abstractions.Inventory;
using BusinessOS.Pharmacy.Application.Abstractions.DailyClosing;
using BusinessOS.Pharmacy.Application.Abstractions.Customers;
using BusinessOS.Pharmacy.Application.Abstractions.Accounting;
using BusinessOS.Pharmacy.Application.Abstractions.Authentication;
using BusinessOS.Pharmacy.Application.Abstractions.Dashboard;
using BusinessOS.Pharmacy.Application.Abstractions.Medicines;
using BusinessOS.Pharmacy.Application.Abstractions.Networking;
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
        services.AddSingleton<LanApiClient>();
        services.AddSingleton<LanConnectionMonitor>();
        services.AddSingleton<ILocalServerConnectionMonitor>(
            serviceProvider => serviceProvider.GetRequiredService<LanConnectionMonitor>());
        services.AddHostedService(
            serviceProvider => serviceProvider.GetRequiredService<LanConnectionMonitor>());

        services.AddSingleton<IUserSessionService, LanUserSessionService>();
        services.AddSingleton<IPermissionAuthorizer, LanClientPermissionAuthorizer>();

        services.AddTransient<IMedicineCatalogService, LanMedicineCatalogService>();
        services.AddTransient<IMedicineCsvService, LanMedicineCsvService>();
        services.AddSingleton<ILocalDashboardQueryService, LanDashboardQueryService>();
        services.AddTransient<IInventoryService, LanInventoryService>();
        services.AddTransient<ISupplierService, LanSupplierService>();
        services.AddTransient<IPurchasingService, LanPurchasingService>();
        services.AddTransient<ICustomerService, LanCustomerService>();
        services.AddTransient<IPosService, LanPosService>();
        services.AddTransient<ISaleReturnService, LanSaleReturnService>();
        services.AddTransient<IExpenseService, LanExpenseService>();
        services.AddTransient<IDailyClosingService, LanDailyClosingService>();
        services.AddTransient<IPharmacyReportService, LanPharmacyReportService>();

        return services;
    }
}
