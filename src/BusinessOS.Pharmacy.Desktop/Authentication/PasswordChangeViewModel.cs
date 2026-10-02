using BusinessOS.Pharmacy.Application.Abstractions.Authentication;
using BusinessOS.Pharmacy.Application.Abstractions.Networking;
using BusinessOS.Pharmacy.Desktop.Localization;
using BusinessOS.Pharmacy.Licensing;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;

namespace BusinessOS.Pharmacy.Desktop.Authentication;

/// <summary>
/// Updates the offline sign-in credential cached on this pharmacy server after
/// verifying the current password. Online password changes remain cloud-managed.
/// </summary>
public sealed partial class PasswordChangeViewModel : ObservableObject
{
    private const int MinimumPasswordLength = 8;

    private readonly IUserSessionService _sessions;
    private readonly IServiceProvider _services;
    private UiLanguage _language = UiLanguageCatalog.All[0];

    [ObservableProperty] private string currentPassword = string.Empty;
    [ObservableProperty] private string newPassword = string.Empty;
    [ObservableProperty] private string confirmPassword = string.Empty;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string statusMessage = string.Empty;
    [ObservableProperty] private bool isError;

    public PasswordChangeViewModel(
        IUserSessionService sessions,
        IServiceProvider services)
    {
        _sessions = sessions;
        _services = services;
        ChangePasswordCommand = new AsyncRelayCommand(ChangePasswordAsync, () => !IsBusy);
    }

    public IAsyncRelayCommand ChangePasswordCommand { get; }

    public string Title => Translate("Change Password", "تغییر رمز عبور", "پټنوم بدلول");
    public string Subtitle => Translate(
        "Update the offline sign-in password used on this pharmacy server",
        "رمز عبور ورود آفلاین این سرور دواخانه را به‌روزرسانی کنید",
        "د دې درملتون سرور د آفلاین ننوتلو پټنوم تازه کړئ");
    public string CurrentPasswordLabel => Translate("Current password", "رمز عبور فعلی", "اوسنی پټنوم");
    public string NewPasswordLabel => Translate("New password", "رمز عبور جدید", "نوی پټنوم");
    public string ConfirmPasswordLabel => Translate("Confirm new password", "تأیید رمز عبور جدید", "نوی پټنوم تایید کړئ");
    public string ChangePasswordButtonText => Translate("Update password", "به‌روزرسانی رمز عبور", "پټنوم تازه کړئ");
    public string RequirementsText => Translate(
        $"At least {MinimumPasswordLength} characters. The new password must differ from the current one.",
        $"حداقل {MinimumPasswordLength} کاراکتر. رمز جدید باید با رمز فعلی متفاوت باشد.",
        $"لږ تر لږه {MinimumPasswordLength} توري. نوی پټنوم باید د اوسني څخه توپیر ولري.");

    public void SetLanguage(UiLanguage language)
    {
        _language = language;
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(CurrentPasswordLabel));
        OnPropertyChanged(nameof(NewPasswordLabel));
        OnPropertyChanged(nameof(ConfirmPasswordLabel));
        OnPropertyChanged(nameof(ChangePasswordButtonText));
        OnPropertyChanged(nameof(RequirementsText));
    }

    private async Task ChangePasswordAsync()
    {
        IsError = false;
        StatusMessage = string.Empty;

        if (NewPassword.Length < MinimumPasswordLength)
        {
            Fail(Translate(
                $"The new password must be at least {MinimumPasswordLength} characters.",
                $"رمز عبور جدید باید حداقل {MinimumPasswordLength} کاراکتر باشد.",
                $"نوی پټنوم باید لږ تر لږه {MinimumPasswordLength} توري وي."));
            return;
        }

        if (!string.Equals(NewPassword, ConfirmPassword, StringComparison.Ordinal))
        {
            Fail(Translate(
                "The new password and its confirmation do not match.",
                "رمز عبور جدید و تأیید آن مطابقت ندارند.",
                "نوی پټنوم او تایید یې سره سمون نه لري."));
            return;
        }

        if (string.Equals(NewPassword, CurrentPassword, StringComparison.Ordinal))
        {
            Fail(Translate(
                "The new password must be different from the current password.",
                "رمز عبور جدید باید با رمز فعلی متفاوت باشد.",
                "نوی پټنوم باید د اوسني څخه توپیر ولري."));
            return;
        }

        IsBusy = true;
        ChangePasswordCommand.NotifyCanExecuteChanged();

        try
        {
            var session = _sessions.Current;
            if (session is null)
            {
                Fail(Translate("No active sign-in session was found.", "هیچ نشست فعالی یافت نشد.", "هیڅ فعاله ناسته ونه موندل شوه."));
                return;
            }

            var store = _services.GetService<ILocalLanCredentialStore>();
            if (store is null)
            {
                Fail(Translate(
                    "Password changes are managed on the Main Pharmacy Server. Open this page on the server terminal.",
                    "تغییر رمز عبور از طریق سرور اصلی دواخانه مدیریت می‌شود. این صفحه را روی ترمینال سرور باز کنید.",
                    "د پټنوم بدلون د درملتون اصلي سرور له لارې مدیریت کیږي. دا پاڼه د سرور ټرمینل کې پرانیزئ."));
                return;
            }

            var cached = await store.FindUserByEmailAsync(session.Email);
            if (cached is null)
            {
                Fail(Translate(
                    "No cached offline credentials were found for this user. Connect to the internet, sign in again and retry.",
                    "هیچ اعتبارنامه آفلاین برای این کاربر یافت نشد. به اینترنت وصل شوید، دوباره وارد شوید و تلاش کنید.",
                    "د دې کارن لپاره هیڅ آفلاین اعتبار ونه موندل شو. انټرنیټ سره وصل شئ، بیا ننوځئ او هڅه وکړئ."));
                return;
            }

            var verifier = _services.GetService<IOfflinePasswordVerifier>();
            if (verifier is null)
            {
                Fail(Translate(
                    "Password verification is unavailable on this terminal. Open this page on the Main Pharmacy Server.",
                    "تأیید رمز عبور در این ترمینال در دسترس نیست. این صفحه را روی سرور اصلی دواخانه باز کنید.",
                    "په دې ټرمینل کې د پټنوم تایید نشته. دا پاڼه د درملتون اصلي سرور کې پرانیزئ."));
                return;
            }

            var credential = new OfflinePasswordCredential(
                cached.PasswordSaltBase64,
                cached.PasswordHashBase64,
                cached.PasswordIterations);

            if (!verifier.Verify(CurrentPassword, credential))
            {
                Fail(Translate("The current password is incorrect.", "رمز عبور فعلی نادرست است.", "اوسنی پټنوم سم نه دی."));
                return;
            }

            var updated = verifier.Create(NewPassword);
            await store.UpsertUserAsync(
                cached with
                {
                    PasswordSaltBase64 = updated.SaltBase64,
                    PasswordHashBase64 = updated.HashBase64,
                    PasswordIterations = updated.Iterations,
                });

            CurrentPassword = string.Empty;
            NewPassword = string.Empty;
            ConfirmPassword = string.Empty;
            StatusMessage = Translate(
                "Password updated. Use the new password for the next offline sign-in on this server. Changing the online password still requires the cloud account.",
                "رمز عبور به‌روزرسانی شد. برای ورود آفلاین بعدی از رمز جدید استفاده کنید. تغییر رمز آنلاین همچنان از طریق حساب ابری انجام می‌شود.",
                "پټنوم تازه شو. د راتلونکي آفلاین ننوتلو لپاره نوی پټنوم وکاروئ. آنلاین پټنوم بدلون لاهم د کلاوډ حساب له لارې دی.");
        }
        catch (Exception exception)
        {
            Fail(exception.Message);
        }
        finally
        {
            IsBusy = false;
            ChangePasswordCommand.NotifyCanExecuteChanged();
        }
    }

    private void Fail(string message)
    {
        IsError = true;
        StatusMessage = message;
    }

    private string Translate(string english, string dari, string pashto) =>
        _language.Code switch
        {
            "fa" => dari,
            "ps" => pashto,
            _ => english,
        };
}
