using System.IO;
using System.Windows;
using BusinessOS.Pharmacy.Application.Abstractions.Networking;
using BusinessOS.Pharmacy.Desktop.Appearance;
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

        ApplyStoredAppearance();

        if (TryHandleInstallVerification(e.Args))
        {
            Shutdown(0);
            return;
        }

        var paths = new ApplicationPaths();
        paths.EnsureCreated();

        var configurationStore = new NetworkConfigurationStore(paths);
        var networkConfiguration = await configurationStore.LoadAsync();
        var installerMode = ReadInstallerDeploymentMode();

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
                    pairingClient,
                    installerMode);
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

    private static void ApplyStoredAppearance()
    {
        var stored = new AppearanceSettingsStore().Load();
        var theme = Enum.TryParse<AppearanceTheme>(stored.Theme, true, out var parsed)
            ? parsed
            : AppearanceTheme.Classic;
        ThemeManager.Apply(theme);
    }

    private static bool TryHandleInstallVerification(IReadOnlyList<string> args)
    {
        var prefix = "--verify-install=";
        var argument = args.FirstOrDefault(x => x.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        if (argument is null) return false;

        var output = argument[prefix.Length..].Trim().Trim('"');
        var version = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        var payload = System.Text.Json.JsonSerializer.Serialize(new
        {
            product = "Darmaltoon",
            version,
            base_directory = AppContext.BaseDirectory,
            process_architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
            framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            deployment_hint = ReadInstallerDeploymentMode()?.ToString(),
        });

        var directory = Path.GetDirectoryName(Path.GetFullPath(output));
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(output, payload);
        return true;
    }

    private static DeploymentMode? ReadInstallerDeploymentMode()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "deployment-default.txt");
            if (!File.Exists(path)) return null;
            var text = File.ReadAllText(path).Trim();
            return Enum.TryParse<DeploymentMode>(text, true, out var mode) ? mode : null;
        }
        catch
        {
            return null;
        }
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
