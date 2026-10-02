using System.Windows;
using BusinessOS.Pharmacy.Application.Abstractions.Licensing;
using BusinessOS.Pharmacy.Application.Abstractions.Medicines;
using BusinessOS.Pharmacy.Application.Abstractions.Networking;
using BusinessOS.Pharmacy.Application.Abstractions.Persistence;
using BusinessOS.Pharmacy.Desktop.Activation;
using BusinessOS.Pharmacy.Desktop.Authentication;
using BusinessOS.Pharmacy.Desktop.Dashboard;
using BusinessOS.Pharmacy.Domain.Licensing;
using BusinessOS.Pharmacy.Licensing;
using Microsoft.Extensions.DependencyInjection;

namespace BusinessOS.Pharmacy.Desktop.Hosting;

public sealed class StartupCoordinator
{
    private readonly IServiceProvider _services;
    private readonly NetworkConfiguration _networkConfiguration;
    private readonly MainWindow _mainWindow;
    private readonly MainWindowViewModel _mainViewModel;
    private readonly DashboardViewModel _dashboard;
    private bool _handlingLogout;

    public StartupCoordinator(
        IServiceProvider services,
        NetworkConfiguration networkConfiguration,
        MainWindow mainWindow,
        MainWindowViewModel mainViewModel,
        DashboardViewModel dashboard)
    {
        _services = services;
        _networkConfiguration = networkConfiguration;
        _mainWindow = mainWindow;
        _mainViewModel = mainViewModel;
        _dashboard = dashboard;
        _mainViewModel.LogoutRequested += OnLogoutRequested;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_networkConfiguration.Mode == DeploymentMode.Client)
        {
            await StartClientAsync(cancellationToken);
            return;
        }

        await StartDatabaseOwnerAsync(cancellationToken);
    }

    private async Task StartClientAsync(CancellationToken cancellationToken)
    {
        if (!_networkConfiguration.IsConfigured ||
            string.IsNullOrWhiteSpace(_networkConfiguration.ServerHost) ||
            string.IsNullOrWhiteSpace(_networkConfiguration.ServerId) ||
            string.IsNullOrWhiteSpace(_networkConfiguration.TerminalId))
        {
            MessageBox.Show(
                "This Client Terminal has not been paired with a Main Pharmacy Server. Open the deployment setup and pair this computer first.",
                "Darmaltoon — Client Terminal Setup",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            System.Windows.Application.Current.Shutdown();
            return;
        }

        if (!ShowLogin())
        {
            System.Windows.Application.Current.Shutdown();
            return;
        }

        _mainViewModel.ApplyCurrentUser();
        await _dashboard.LoadAsync();
        _mainWindow.Show();
    }

    private async Task StartDatabaseOwnerAsync(CancellationToken cancellationToken)
    {
        var licenseService = _services.GetRequiredService<ILicenseService>();
        var localDatabase = _services.GetRequiredService<ILocalDatabaseInitializer>();
        var medicineSeeds = _services.GetRequiredService<IMedicineSeedService>();
        var activationWindow = _services.GetRequiredService<ActivationWindow>();
        var activationViewModel = _services.GetRequiredService<ActivationViewModel>();

        EntitlementSnapshot? entitlement = null;

        try
        {
            entitlement = await licenseService.GetCachedEntitlementAsync(cancellationToken);
        }
        catch (ClockRollbackDetectedException)
        {
            activationViewModel.StatusMessage =
                "Windows clock rollback was detected. Connect to the internet and verify the subscription.";
        }
        catch
        {
            activationViewModel.StatusMessage =
                "The saved activation could not be verified. Please activate or verify the subscription online.";
        }

        if (entitlement is null)
        {
            // Preserve an already assigned activation across upgrades and source/debug runs.
            // A cached offline lease may have expired even though this installation is still
            // assigned on the licensing server. Refresh it automatically before asking the
            // pharmacy user to enter the license key again.
            try
            {
                entitlement = await licenseService.RefreshAsync(cancellationToken);
            }
            catch (InvalidOperationException exception)
                when (exception.Message.Contains(
                    "has not been activated",
                    StringComparison.OrdinalIgnoreCase))
            {
                // No saved activation exists on this PC; fall through to normal activation.
            }
            catch (Exception exception)
            {
                if (string.IsNullOrWhiteSpace(activationViewModel.StatusMessage))
                {
                    activationViewModel.StatusMessage =
                        $"The existing activation could not be refreshed automatically. " +
                        $"Connect this PC to the internet and try again.\n\n{exception.Message}";
                }
            }
        }

        if (entitlement is null)
        {
            if (activationWindow.ShowDialog() != true)
            {
                System.Windows.Application.Current.Shutdown();
                return;
            }

            entitlement = await licenseService.GetCachedEntitlementAsync(cancellationToken);
            if (entitlement is null)
            {
                System.Windows.Application.Current.Shutdown();
                return;
            }
        }

        try
        {
            await localDatabase.InitializeAsync(entitlement.TenantId, cancellationToken);

            try
            {
                await medicineSeeds.SeedDefaultsOnceAsync(cancellationToken);
            }
            catch (Exception exception)
            {
                MessageBox.Show(
                    $"Darmaltoon could not load the optional starter medicine data. You can still add medicines manually or import CSV.\n\n{exception.Message}",
                    "Darmaltoon — Starter Data",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        catch (LocalDatabaseTenantMismatchException)
        {
            MessageBox.Show(
                "This PC already contains local data for another pharmacy. Darmaltoon will not overwrite or mix tenant data. Use the correct pharmacy activation or restore/reset the local database through the supported maintenance workflow.",
                "Darmaltoon — Local Database Protection",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            System.Windows.Application.Current.Shutdown();
            return;
        }

        if (_networkConfiguration.Mode == DeploymentMode.Server)
        {
            var terminals = _services.GetRequiredService<ILocalTerminalService>();
            await terminals.GetOrCreateServerIdentityAsync(
                entitlement.TenantId,
                _networkConfiguration.ServerName,
                cancellationToken);

            try
            {
                var controller = _services.GetRequiredService<ILocalServerServiceController>();
                _ = await controller.EnsurePrivateFirewallRuleAsync(
                    _networkConfiguration.ServerPort,
                    cancellationToken);
                _ = await controller.EnsurePrivateDiscoveryFirewallRuleAsync(
                    _networkConfiguration.DiscoveryPort,
                    cancellationToken);
                var serviceStatus = await controller.StartAsync(cancellationToken);

                if (serviceStatus.IsWindows && serviceStatus.IsInstalled && !serviceStatus.IsRunning)
                {
                    MessageBox.Show(
                        serviceStatus.Message,
                        "Darmaltoon — Main Server Service",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }
            catch (Exception exception)
            {
                MessageBox.Show(
                    $"Darmaltoon is ready, but the Main Pharmacy Server Windows Service could not be started automatically. Open Network & Terminals after login to review it.\n\n{exception.Message}",
                    "Darmaltoon — Main Server Service",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        if (!ShowLogin())
        {
            System.Windows.Application.Current.Shutdown();
            return;
        }

        _mainViewModel.ApplyCurrentUser();
        await _dashboard.LoadAsync();
        _mainWindow.Show();
    }

    private bool ShowLogin()
    {
        var loginWindow = _services.GetRequiredService<LoginWindow>();
        return loginWindow.ShowDialog() == true;
    }

    private async void OnLogoutRequested(object? sender, EventArgs e)
    {
        if (_handlingLogout)
        {
            return;
        }

        _handlingLogout = true;
        try
        {
            _mainWindow.Hide();

            if (!ShowLogin())
            {
                System.Windows.Application.Current.Shutdown();
                return;
            }

            _mainViewModel.ApplyCurrentUser();
            await _dashboard.LoadAsync();
            _mainWindow.Show();
        }
        finally
        {
            _handlingLogout = false;
        }
    }
}
