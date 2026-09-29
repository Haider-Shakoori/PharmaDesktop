using BusinessOS.Pharmacy.Application.Abstractions.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BusinessOS.Pharmacy.Persistence;

public static class DependencyInjection
{
    public static IServiceCollection AddBusinessOSPersistence(this IServiceCollection services)
    {
        services.AddDbContextFactory<PharmacyDbContext>((serviceProvider, options) =>
        {
            var paths = serviceProvider.GetRequiredService<IApplicationPaths>();
            options.UseSqlite($"Data Source={paths.DatabasePath};Cache=Shared");
        });

        return services;
    }
}
