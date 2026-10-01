using BusinessOS.Pharmacy.Application.Abstractions.Backup;
using BusinessOS.Pharmacy.Application.Abstractions.Accounting;
using BusinessOS.Pharmacy.Application.Abstractions.Customers;
using BusinessOS.Pharmacy.Application.Abstractions.Dashboard;
using BusinessOS.Pharmacy.Application.Abstractions.DailyClosing;
using BusinessOS.Pharmacy.Application.Abstractions.Inventory;
using BusinessOS.Pharmacy.Application.Abstractions.Medicines;
using BusinessOS.Pharmacy.Application.Abstractions.Networking;
using BusinessOS.Pharmacy.Application.Abstractions.Persistence;
using BusinessOS.Pharmacy.Application.Abstractions.Purchasing;
using BusinessOS.Pharmacy.Application.Abstractions.Reports;
using BusinessOS.Pharmacy.Application.Abstractions.Sales;
using BusinessOS.Pharmacy.Application.Abstractions.Storage;
using BusinessOS.Pharmacy.Application.Abstractions.Sync;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BusinessOS.Pharmacy.Persistence;

public static class DependencyInjection
{
    public static IServiceCollection AddBusinessOSPersistence(this IServiceCollection services)
    {
        services.AddSingleton<SqlitePragmaInterceptor>();

        services.AddPooledDbContextFactory<PharmacyDbContext>((serviceProvider, options) =>
        {
            var paths = serviceProvider.GetRequiredService<IApplicationPaths>();
            paths.EnsureCreated();

            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = paths.DatabasePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Shared,
                Pooling = true,
                ForeignKeys = true,
                DefaultTimeout = 10,
            }.ToString();

            options.UseSqlite(connectionString);
            options.AddInterceptors(serviceProvider.GetRequiredService<SqlitePragmaInterceptor>());
        }, poolSize: 32);

        services.AddSingleton<ILocalDatabaseInitializer, LocalDatabaseInitializer>();
        services.AddSingleton<ILocalBackupService, LocalBackupService>();
        services.AddSingleton<ICloudSyncStore, CloudSyncStore>();
        services.AddSingleton<ILocalSettingsStore, LocalSettingsStore>();
        services.AddSingleton<ILocalSequenceService, LocalSequenceService>();
        services.AddSingleton<ILocalDashboardQueryService, LocalDashboardQueryService>();
        services.AddSingleton<IMedicineCatalogService, MedicineCatalogService>();
        services.AddSingleton<IMedicineCsvService, MedicineCsvService>();
        services.AddSingleton<IMedicineSeedService, MedicineSeedService>();
        services.AddSingleton<StockLedger>();
        services.AddSingleton<IInventoryService, InventoryService>();
        services.AddSingleton<IStockAllocationService, StockAllocationService>();
        services.AddSingleton<ISupplierService, SupplierService>();
        services.AddSingleton<CustomerService>();
        services.AddSingleton<ICustomerService>(sp => sp.GetRequiredService<CustomerService>());
        services.AddSingleton<ICustomerCreditPolicy>(sp => sp.GetRequiredService<CustomerService>());
        services.AddSingleton<IPurchasingService, PurchasingService>();
        services.AddSingleton<IPosService, PosService>();
        services.AddSingleton<ISaleReturnService, SaleReturnService>();
        services.AddSingleton<AccountingProvisioner>();
        services.AddSingleton<LedgerPostingService>();
        services.AddSingleton<IExpenseService, ExpenseService>();
        services.AddSingleton<IDailyClosingService, DailyClosingService>();
        services.AddSingleton<IPharmacyReportService, PharmacyReportService>();
        services.AddSingleton<ILocalLanCredentialStore, LocalLanCredentialStore>();
        services.AddSingleton<ILocalTerminalService, LocalTerminalService>();

        return services;
    }
}
