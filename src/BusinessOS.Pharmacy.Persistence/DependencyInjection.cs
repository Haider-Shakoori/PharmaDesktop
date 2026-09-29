using BusinessOS.Pharmacy.Application.Abstractions.Dashboard;
using BusinessOS.Pharmacy.Application.Abstractions.Medicines;
using BusinessOS.Pharmacy.Application.Abstractions.Persistence;
using BusinessOS.Pharmacy.Application.Abstractions.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BusinessOS.Pharmacy.Persistence;

public static class DependencyInjection
{
    public static IServiceCollection AddBusinessOSPersistence(this IServiceCollection services)
    {
        services.AddSingleton<SqlitePragmaInterceptor>();

        services.AddDbContextFactory<PharmacyDbContext>((serviceProvider, options) =>
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
                DefaultTimeout = 5,
            }.ToString();

            options.UseSqlite(connectionString);
            options.AddInterceptors(serviceProvider.GetRequiredService<SqlitePragmaInterceptor>());
        });

        services.AddSingleton<ILocalDatabaseInitializer, LocalDatabaseInitializer>();
        services.AddSingleton<ILocalSettingsStore, LocalSettingsStore>();
        services.AddSingleton<ILocalSequenceService, LocalSequenceService>();
        services.AddSingleton<ILocalDashboardQueryService, LocalDashboardQueryService>();
        services.AddSingleton<IMedicineCatalogService, MedicineCatalogService>();
        services.AddSingleton<IMedicineCsvService, MedicineCsvService>();
        services.AddSingleton<IMedicineSeedService, MedicineSeedService>();

        return services;
    }
}
