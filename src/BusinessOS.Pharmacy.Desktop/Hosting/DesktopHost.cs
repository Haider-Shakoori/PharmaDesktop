using System.Net.Http;
using BusinessOS.Pharmacy.Application.Abstractions.Networking;
using BusinessOS.Pharmacy.Desktop.Activation;
using BusinessOS.Pharmacy.Desktop.Authentication;
using BusinessOS.Pharmacy.Desktop.Backup;
using BusinessOS.Pharmacy.Desktop.Dashboard;
using BusinessOS.Pharmacy.Desktop.Diagnostics;
using BusinessOS.Pharmacy.Desktop.Inventory;
using BusinessOS.Pharmacy.Desktop.Purchasing;
using BusinessOS.Pharmacy.Desktop.Customers;
using BusinessOS.Pharmacy.Desktop.Pos;
using BusinessOS.Pharmacy.Desktop.Returns;
using BusinessOS.Pharmacy.Desktop.Expenses;
using BusinessOS.Pharmacy.Desktop.DailyClosing;
using BusinessOS.Pharmacy.Desktop.Reports;
using BusinessOS.Pharmacy.Desktop.Medicines;
using BusinessOS.Pharmacy.Desktop.Networking;
using BusinessOS.Pharmacy.Desktop.Updates;
using BusinessOS.Pharmacy.Infrastructure;
using BusinessOS.Pharmacy.Infrastructure.Networking;
using BusinessOS.Pharmacy.Infrastructure.Storage;
using BusinessOS.Pharmacy.Licensing;
using BusinessOS.Pharmacy.LocalClient;
using BusinessOS.Pharmacy.Persistence;
using BusinessOS.Pharmacy.Updater;
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

                var updaterSection = context.Configuration.GetSection("BusinessOS:Updater");
                var updaterChannel = Enum.TryParse<UpdateChannel>(updaterSection["Channel"], true, out var parsedChannel)
                    ? parsedChannel
                    : UpdateChannel.Stable;
                var updaterOptions = new UpdateOptions(
                    updaterSection["ManifestUrl"] ?? string.Empty,
                    updaterSection["SigningPublicKeyPem"] ?? string.Empty,
                    int.TryParse(updaterSection["TimeoutSeconds"], out var updateTimeout) ? updateTimeout : 30,
                    updaterChannel,
                    long.TryParse(updaterSection["MaximumPackageBytes"], out var maxPackageBytes) ? maxPackageBytes : 536_870_912);

                services.AddSingleton(updaterOptions);
                services.AddSingleton(_ => new UpdateService(
                    new HttpClient { Timeout = TimeSpan.FromSeconds(Math.Clamp(updaterOptions.TimeoutSeconds, 5, 300)) },
                    updaterOptions,
                    paths.UpdatesDirectory));
                services.AddSingleton(networkConfiguration);
                services.AddSingleton<GlobalExceptionHandler>();
                services.AddSingleton<ActivationViewModel>();
                services.AddSingleton<ActivationWindow>();
                services.AddTransient<LoginViewModel>();
                services.AddTransient<LoginWindow>();
                services.AddSingleton<DashboardViewModel>();
                services.AddSingleton<MedicinesViewModel>();
                services.AddSingleton<InventoryViewModel>();
                services.AddSingleton<PurchasingViewModel>();
                services.AddSingleton<CustomersViewModel>();
                services.AddSingleton<PosViewModel>();
                services.AddSingleton<ReturnsViewModel>();
                services.AddSingleton<ExpensesViewModel>();
                services.AddSingleton<DailyClosingViewModel>();
                services.AddSingleton<ReportsViewModel>();
                services.AddSingleton<BackupRestoreViewModel>();
                services.AddSingleton<UpdateViewModel>();
                services.AddSingleton<NetworkSettingsViewModel>();
                services.AddSingleton<MainWindowViewModel>();
                services.AddSingleton<MainWindow>();
                services.AddSingleton<StartupCoordinator>();
            })
            .Build();
    }
}
