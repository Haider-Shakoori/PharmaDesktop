using BusinessOS.Pharmacy.Application.Abstractions.Authentication;
using BusinessOS.Pharmacy.Licensing;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BusinessOS.Pharmacy.Desktop.Authentication;

public sealed partial class LoginViewModel : ObservableObject
{
    private readonly IUserSessionService _sessions;

    [ObservableProperty]
    private string email = string.Empty;

    [ObservableProperty]
    private bool allowOfflineSignIn = true;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string statusMessage = "Sign in with your pharmacy staff account.";

    public LoginViewModel(IUserSessionService sessions) => _sessions = sessions;

    public event EventHandler? LoginSucceeded;

    public async Task SignInAsync(string password)
    {
        if (IsBusy)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(password))
        {
            StatusMessage = "Email and password are required.";
            return;
        }

        IsBusy = true;
        StatusMessage = "Signing in…";

        try
        {
            var user = await _sessions.LoginAsync(
                Email,
                password,
                AllowOfflineSignIn);

            StatusMessage = $"Welcome, {user.Name}.";
            LoginSucceeded?.Invoke(this, EventArgs.Empty);
        }
        catch (ClockRollbackDetectedException)
        {
            StatusMessage = "Windows clock rollback was detected. Connect to the internet and verify the license first.";
        }
        catch (LicenseApiException exception)
        {
            StatusMessage = exception.Message;
        }
        catch
        {
            StatusMessage = "Sign-in could not be completed. Check the credentials, license, and connection.";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
