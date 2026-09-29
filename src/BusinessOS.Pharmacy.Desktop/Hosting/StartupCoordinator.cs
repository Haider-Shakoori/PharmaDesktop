using System.Windows;
using BusinessOS.Pharmacy.Application.Abstractions.Licensing;
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
    private readonly ILicenseService _licenseService;
    private readonly ILocalDatabaseInitializer _localDatabase;
    private readonly ActivationWindow _activationWindow;
    private readonly ActivationViewModel _activationViewModel;
    private readonly IServiceProvider _services;
    private readonly MainWindow _mainWindow;
    private readonly MainWindowViewModel _mainViewModel;
    private readonly DashboardViewModel _dashboard;
    private bool _handlingLogout;

    public StartupCoordinator(
        ILicenseService licenseService,
        ILocalDatabaseInitializer localDatabase,
        ActivationWindow activationWindow,
        ActivationViewModel activationViewModel,
        IServiceProvider services,
        MainWindow mainWindow,
        MainWindowViewModel mainViewModel,
        DashboardViewModel dashboard)
    {
        _licenseService = licenseService;
        _localDatabase = localDatabase;
        _activationWindow = activationWindow;
        _activationViewModel = activationViewModel;
        _services = services;
        _mainWindow = mainWindow;
        _mainViewModel = mainViewModel;
        _dashboard = dashboard;
        _mainViewModel.LogoutRequested += OnLogoutRequested;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        EntitlementSnapshot? entitlement = null;

        try
        {
            entitlement = await _licenseService.GetCachedEntitlementAsync(cancellationToken);
        }
        catch (ClockRollbackDetectedException)
        {
            _activationViewModel.StatusMessage =
                "Windows clock rollback was detected. Connect to the internet and verify the subscription.";
        }
        catch
        {
            _activationViewModel.StatusMessage =
                "The saved activation could not be verified. Please activate or verify the subscription online.";
        }

        if (entitlement is null)
        {
            if (_activationWindow.ShowDialog() != true)
            {
                System.Windows.Application.Current.Shutdown();
                return;
            }

            entitlement = await _licenseService.GetCachedEntitlementAsync(cancellationToken);
            if (entitlement is null)
            {
                System.Windows.Application.Current.Shutdown();
                return;
            }
        }

        try
        {
            await _localDatabase.InitializeAsync(entitlement.TenantId, cancellationToken);
        }
        catch (LocalDatabaseTenantMismatchException)
        {
            MessageBox.Show(
                "This PC already contains local data for another pharmacy. BusinessOS Pharmacy will not overwrite or mix tenant data. Use the correct pharmacy activation or restore/reset the local database through the supported maintenance workflow.",
                "BusinessOS Pharmacy — Local Database Protection",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

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
