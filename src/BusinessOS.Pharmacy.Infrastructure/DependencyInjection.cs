using BusinessOS.Pharmacy.Application.Abstractions.Networking;
using BusinessOS.Pharmacy.Application.Abstractions.Storage;
using BusinessOS.Pharmacy.Application.Abstractions.Time;
using BusinessOS.Pharmacy.Infrastructure.Networking;
using BusinessOS.Pharmacy.Infrastructure.Storage;
using BusinessOS.Pharmacy.Infrastructure.Time;
using Microsoft.Extensions.DependencyInjection;

namespace BusinessOS.Pharmacy.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddBusinessOSInfrastructure(
        this IServiceCollection services,
        ApplicationPaths? applicationPaths = null)
    {
        applicationPaths ??= new ApplicationPaths();
        applicationPaths.EnsureCreated();

        services.AddSingleton(applicationPaths);
        services.AddSingleton<IApplicationPaths>(applicationPaths);
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<INetworkConfigurationStore, NetworkConfigurationStore>();
        services.AddSingleton<INetworkSecretStore, WindowsNetworkSecretStore>();
        services.AddSingleton<ILocalServerDiscovery, UdpLocalServerDiscovery>();
        services.AddSingleton<ILocalServerServiceController, WindowsLocalServerServiceController>();

        return services;
    }
}
