using BusinessOS.Pharmacy.Application.Abstractions.Licensing;
using BusinessOS.Pharmacy.Desktop.Activation;
using BusinessOS.Pharmacy.Desktop.Authentication;
using BusinessOS.Pharmacy.Licensing;
using Microsoft.Extensions.DependencyInjection;

namespace BusinessOS.Pharmacy.Desktop.Hosting;

public sealed class StartupCoordinator
{
    private readonly ILicenseService _licenseService;
    private readonly ActivationWindow _activationWindow;
    private readonly ActivationViewModel _activationViewModel;
    private readonly IServiceProvider _services;
    private readonly MainWindow _mainWindow;
    private readonly MainWindowViewModel _mainViewModel;
    private bool _handlingLogout;

    public StartupCoordinator(
        ILicenseService licenseService,
        ActivationWindow activationWindow,
        ActivationViewModel activationViewModel,
        IServiceProvider services,
        MainWindow mainWindow,
        MainWindowViewModel mainViewModel)
    {
        _licenseService = licenseService;
        _activationWindow = activationWindow;
        _activationViewModel = activationViewModel;
        _services = services;
        _mainWindow = mainWindow;
        _mainViewModel = mainViewModel;
        _mainViewModel.LogoutRequested += OnLogoutRequested;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        var showActivation = false;

        try
        {
            showActivation = await _licenseService.GetCachedEntitlementAsync(cancellationToken) is null;
        }
        catch (ClockRollbackDetectedException)
        {
            _activationViewModel.StatusMessage =
                "Windows clock rollback was detected. Connect to the internet and verify the subscription.";
            showActivation = true;
        }
        catch
        {
            _activationViewModel.StatusMessage =
                "The saved activation could not be verified. Please activate or verify the subscription online.";
            showActivation = true;
        }

        if (showActivation && _activationWindow.ShowDialog() != true)
        {
            System.Windows.Application.Current.Shutdown();
            return;
        }

        if (!ShowLogin())
        {
            System.Windows.Application.Current.Shutdown();
            return;
        }

        _mainViewModel.ApplyCurrentUser();
        _mainWindow.Show();
    }

    private bool ShowLogin()
    {
        var loginWindow = _services.GetRequiredService<LoginWindow>();
        return loginWindow.ShowDialog() == true;
    }

    private void OnLogoutRequested(object? sender, EventArgs e)
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
            _mainWindow.Show();
        }
        finally
        {
            _handlingLogout = false;
        }
    }
}
