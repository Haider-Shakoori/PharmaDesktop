using BusinessOS.Pharmacy.Application.Abstractions.Licensing;
using BusinessOS.Pharmacy.Desktop.Activation;
using BusinessOS.Pharmacy.Licensing;

namespace BusinessOS.Pharmacy.Desktop.Hosting;

public sealed class StartupCoordinator
{
    private readonly ILicenseService _licenseService;
    private readonly ActivationWindow _activationWindow;
    private readonly ActivationViewModel _activationViewModel;
    private readonly MainWindow _mainWindow;

    public StartupCoordinator(
        ILicenseService licenseService,
        ActivationWindow activationWindow,
        ActivationViewModel activationViewModel,
        MainWindow mainWindow)
    {
        _licenseService = licenseService;
        _activationWindow = activationWindow;
        _activationViewModel = activationViewModel;
        _mainWindow = mainWindow;
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

        _mainWindow.Show();
    }
}