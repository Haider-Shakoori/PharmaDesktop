using System.Diagnostics;
using BusinessOS.Pharmacy.Application.Abstractions.Authentication;
using BusinessOS.Pharmacy.Licensing;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BusinessOS.Pharmacy.Desktop.Authentication;

public sealed partial class LoginViewModel : ObservableObject
{
    private const string AccountWebsiteUrl = "https://darmaltoon.com";

    private readonly IUserSessionService _sessions;

    [ObservableProperty]
    private string email = string.Empty;

    [ObservableProperty]
    private bool allowOfflineSignIn = true;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private bool isError;

    [ObservableProperty]
    private string statusMessage = "Sign in with your pharmacy staff account.";

    public LoginViewModel(IUserSessionService sessions)
    {
        _sessions = sessions;
        ForgotPasswordCommand = new RelayCommand(OpenForgotPasswordPage);
    }

    public event EventHandler? LoginSucceeded;

    public IRelayCommand ForgotPasswordCommand { get; }

    public bool IsNotBusy => !IsBusy;
    public string SignInButtonText => IsBusy ? "Signing in…" : "Sign In";

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(IsNotBusy));
        OnPropertyChanged(nameof(SignInButtonText));
    }

    public async Task SignInAsync(string password)
    {
        if (IsBusy)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(password))
        {
            SetStatus("Email and password are required.", isError: true);
            return;
        }

        IsBusy = true;
        SetStatus("Signing in…");

        try
        {
            var user = await _sessions.LoginAsync(
                Email,
                password,
                AllowOfflineSignIn);

            SetStatus($"Welcome, {user.Name}.");
            LoginSucceeded?.Invoke(this, EventArgs.Empty);
        }
        catch (ClockRollbackDetectedException)
        {
            SetStatus(
                "Windows clock rollback was detected. Connect to the internet and verify the license first.",
                isError: true);
        }
        catch (LicenseApiException exception)
        {
            SetStatus(exception.Message, isError: true);
        }
        catch
        {
            SetStatus(
                "Sign-in could not be completed. Check the credentials, license, and connection.",
                isError: true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void OpenForgotPasswordPage()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = AccountWebsiteUrl,
                UseShellExecute = true,
            });

            SetStatus(
                "darmaltoon.com opened in your browser. Reset your password there, then sign in with the new password.");
        }
        catch
        {
            SetStatus(
                $"Open {AccountWebsiteUrl} in your browser to reset your password. Contact your pharmacy administrator if you need help.",
                isError: true);
        }
    }

    private void SetStatus(string message, bool isError = false)
    {
        IsError = isError;
        StatusMessage = message;
    }
}
