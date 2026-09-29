using BusinessOS.Pharmacy.Application.Abstractions.Networking;
using BusinessOS.Pharmacy.Desktop.Activation;
using BusinessOS.Pharmacy.Desktop.Authentication;
using BusinessOS.Pharmacy.Desktop.Dashboard;
using BusinessOS.Pharmacy.Desktop.Diagnostics;
using BusinessOS.Pharmacy.Desktop.Medicines;
using BusinessOS.Pharmacy.Infrastructure;
using BusinessOS.Pharmacy.Infrastructure.Networking;
using BusinessOS.Pharmacy.Infrastructure.Storage;
using BusinessOS.Pharmacy.Licensing;
using BusinessOS.Pharmacy.LocalClient;
using BusinessOS.Pharmacy.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace BusinessOS.Pharmacy.Desktop.Hosting;

public static class DesktopHost
{
    public static IHost Build(
        ApplicationPaths? paths = null,
        NetworkConfiguration? networkConfiguration = null)
    {
        paths ??= new ApplicationPaths();
        paths.EnsureCreated();

        networkConfiguration ??= new NetworkConfigurationStore(paths)
            .LoadAsync()
            .GetAwaiter()
            .GetResult();

        Log.Logger = LoggingBootstrapper.CreateLogger(paths);

        return Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.SetBasePath(AppContext.BaseDirectory);
                configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: false);
            })
            .UseSerilog()
            .ConfigureServices((context, services) =>
            {
                services.AddBusinessOSInfrastructure(paths);

                if (networkConfiguration.Mode == DeploymentMode.Client &&
                    networkConfiguration.IsConfigured)
                {
                    services.AddBusinessOSLocalClient();
                }
                else
                {
                    services.AddBusinessOSPersistence();
                    services.AddBusinessOSLicensing(context.Configuration);
                }

                services.AddSingleton(networkConfiguration);
                services.AddSingleton<GlobalExceptionHandler>();
                services.AddSingleton<ActivationViewModel>();
                services.AddSingleton<ActivationWindow>();
                services.AddTransient<LoginViewModel>();
                services.AddTransient<LoginWindow>();
                services.AddSingleton<DashboardViewModel>();
                services.AddSingleton<MedicinesViewModel>();
                services.AddSingleton<MainWindowViewModel>();
                services.AddSingleton<MainWindow>();
                services.AddSingleton<StartupCoordinator>();
            })
            .Build();
    }
}
