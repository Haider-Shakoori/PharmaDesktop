using System.Diagnostics;
using BusinessOS.Pharmacy.Application.Abstractions.Licensing;
using BusinessOS.Pharmacy.Licensing;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Options;

namespace BusinessOS.Pharmacy.Desktop.Activation;

public sealed partial class ActivationViewModel : ObservableObject
{
    private readonly ILicenseService _licenseService;
    private readonly LicenseApiOptions _options;

    [ObservableProperty]
    private string licenseKey = string.Empty;

    [ObservableProperty]
    private string statusMessage = "Enter the license key created by the Darmaltoon website.";

    [ObservableProperty]
    private bool isBusy;

    public ActivationViewModel(
        ILicenseService licenseService,
        IOptions<LicenseApiOptions> options)
    {
        _licenseService = licenseService;
        _options = options.Value;
        ActivateCommand = new AsyncRelayCommand(ActivateAsync, CanActivate);
        StartTrialCommand = new RelayCommand(OpenTrialPage);
    }

    public IAsyncRelayCommand ActivateCommand { get; }
    public IRelayCommand StartTrialCommand { get; }

    public event EventHandler? ActivationSucceeded;

    partial void OnLicenseKeyChanged(string value) => ActivateCommand.NotifyCanExecuteChanged();

    private bool CanActivate() => !IsBusy && !string.IsNullOrWhiteSpace(LicenseKey);

    private async Task ActivateAsync()
    {
        IsBusy = true;
        ActivateCommand.NotifyCanExecuteChanged();
        StatusMessage = "Verifying license with BusinessOS…";

        try
        {
            var entitlement = await _licenseService.ActivateAsync(LicenseKey);
            LicenseKey = string.Empty;
            StatusMessage = $"Activated • {entitlement.SubscriptionState} • {entitlement.PlanCode}";
            ActivationSucceeded?.Invoke(this, EventArgs.Empty);
        }
        catch (LicenseApiException exception)
        {
            StatusMessage = exception.Message;
        }
        catch (Exception exception)
        {
            StatusMessage = exception is System.Security.Cryptography.CryptographicException
                ? "The server entitlement could not be verified. Check the desktop signing-key configuration."
                : "Activation could not be completed. Please check the license and internet connection.";
        }
        finally
        {
            IsBusy = false;
            ActivateCommand.NotifyCanExecuteChanged();
        }
    }

    private void OpenTrialPage()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = _options.BaseUrl,
            UseShellExecute = true,
        });
    }
}