using System.Windows;
using BusinessOS.Pharmacy.Application.Abstractions.Networking;
using BusinessOS.Pharmacy.Desktop.Diagnostics;
using BusinessOS.Pharmacy.Desktop.Hosting;
using BusinessOS.Pharmacy.Desktop.Networking;
using BusinessOS.Pharmacy.Infrastructure.Networking;
using BusinessOS.Pharmacy.Infrastructure.Storage;
using BusinessOS.Pharmacy.LocalClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace BusinessOS.Pharmacy.Desktop;

public partial class App : System.Windows.Application
{
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var paths = new ApplicationPaths();
        paths.EnsureCreated();

        var configurationStore = new NetworkConfigurationStore(paths);
        var networkConfiguration = await configurationStore.LoadAsync();

        if (!networkConfiguration.IsConfigured)
        {
            var hasLegacyPharmacyState =
                File.Exists(paths.ActivationStatePath) ||
                File.Exists(paths.DatabasePath);

            if (hasLegacyPharmacyState)
            {
                networkConfiguration = networkConfiguration with
                {
                    Mode = DeploymentMode.Standalone,
                    ServerName = "Standalone Pharmacy",
                    DiscoveryEnabled = false,
                    IsConfigured = true,
                };

                await configurationStore.SaveAsync(networkConfiguration);
            }
            else
            {
                var secretStore = new WindowsNetworkSecretStore(paths);
                var discovery = new UdpLocalServerDiscovery(configurationStore);
                var pairingClient = new LanTerminalPairingClient(
                    configurationStore,
                    secretStore);

                var setupViewModel = new DeploymentSetupViewModel(
                    configurationStore,
                    discovery,
                    pairingClient);
                var setupWindow = new DeploymentSetupWindow(setupViewModel);

                if (setupWindow.ShowDialog() != true)
                {
                    Shutdown();
                    return;
                }

                networkConfiguration = await configurationStore.LoadAsync();
                networkConfiguration.Validate();
            }
        }

        _host = DesktopHost.Build(paths, networkConfiguration);
        await _host.StartAsync();

        _host.Services
            .GetRequiredService<GlobalExceptionHandler>()
            .Attach(this);

        await _host.Services
            .GetRequiredService<StartupCoordinator>()
            .StartAsync();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            await _host.StopAsync(TimeSpan.FromSeconds(5));
            _host.Dispose();
        }

        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
