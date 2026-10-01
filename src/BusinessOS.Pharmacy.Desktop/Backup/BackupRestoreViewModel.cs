using System.Collections.ObjectModel;
using BusinessOS.Pharmacy.Application.Abstractions.Backup;
using BusinessOS.Pharmacy.Application.Abstractions.Licensing;
using BusinessOS.Pharmacy.Application.Abstractions.Networking;
using BusinessOS.Pharmacy.Desktop.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;

namespace BusinessOS.Pharmacy.Desktop.Backup;

public sealed partial class BackupRestoreViewModel : ObservableObject
{
    private readonly IServiceProvider _services;
    private readonly NetworkConfiguration _network;
    private readonly ILocalServerServiceController _serverController;
    private UiLanguage _language = UiLanguageCatalog.All[0];

    [ObservableProperty] private LocalBackupSnapshot? selectedBackup;
    [ObservableProperty] private string statusMessage = string.Empty;
    [ObservableProperty] private string restoreConfirmation = string.Empty;
    [ObservableProperty] private bool isBusy;

    public BackupRestoreViewModel(IServiceProvider services, NetworkConfiguration network, ILocalServerServiceController serverController)
    {
        _services = services;
        _network = network;
        _serverController = serverController;
        RefreshCommand = new AsyncRelayCommand(LoadAsync, () => !IsBusy);
        CreateBackupCommand = new AsyncRelayCommand(CreateBackupAsync, () => !IsBusy && CanManageHere);
        VerifyCommand = new AsyncRelayCommand(VerifyAsync, () => !IsBusy && CanManageHere && SelectedBackup is not null);
        RestoreCommand = new AsyncRelayCommand(RestoreAsync, () => !IsBusy && CanManageHere && SelectedBackup is not null && RestoreConfirmation == "RESTORE");
    }

    public ObservableCollection<LocalBackupSnapshot> Backups { get; } = new();
    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand CreateBackupCommand { get; }
    public IAsyncRelayCommand VerifyCommand { get; }
    public IAsyncRelayCommand RestoreCommand { get; }
    public bool CanManageHere => _network.Mode != DeploymentMode.Client;
    public string Title => T("Backup & Restore", "پشتیبان‌گیری و بازیابی", "بیک اپ او بېرته راګرځول");
    public string Subtitle => T("Verified local backups of the authoritative pharmacy database", "نسخه‌های تأییدشده پایگاه داده اصلی دواخانه", "د اصلي درملتون ډیټابیس تایید شوي بیک اپونه");
    public string WarningText => T("Restore replaces the authoritative local database. Darmaltoon first creates a pre-restore safety backup and blocks cross-tenant or damaged backups.", "بازیابی پایگاه داده اصلی را جایگزین می‌کند. دارملتون ابتدا نسخه ایمنی می‌سازد و نسخه خراب یا مربوط به داروخانه دیگر را رد می‌کند.", "بېرته راګرځول اصلي ډیټابیس بدلوي. درملتون لومړی خوندي بیک اپ جوړوي او خراب یا د بل درملتون بیک اپ ردوي.");

    public void SetLanguage(UiLanguage language)
    {
        _language = language;
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(WarningText));
    }

    public async Task LoadAsync()
    {
        await BusyAsync(async () =>
        {
            Backups.Clear();
            if (!CanManageHere)
            {
                StatusMessage = T("Backup and restore must be performed on the Main Pharmacy Server.", "پشتیبان‌گیری و بازیابی باید روی سرور اصلی انجام شود.", "بیک اپ او بېرته راګرځول باید په اصلي سرور کې ترسره شي.");
                return;
            }

            var service = _services.GetRequiredService<ILocalBackupService>();
            foreach (var backup in await service.ListAsync()) Backups.Add(backup);
            StatusMessage = T($"{Backups.Count} verified backup(s) available.", $"{Backups.Count} نسخه تأییدشده موجود است.", $"{Backups.Count} تایید شوي بیک اپونه شته.");
        });
    }

    private async Task CreateBackupAsync()
    {
        await BusyAsync(async () =>
        {
            var tenant = await TenantIdAsync();
            var created = await _services.GetRequiredService<ILocalBackupService>().CreateAsync(tenant);
            await ReloadCoreAsync();
            SelectedBackup = Backups.FirstOrDefault(x => x.FullPath == created.FullPath);
            StatusMessage = T($"Backup created: {created.FileName}", $"نسخه پشتیبان ساخته شد: {created.FileName}", $"بیک اپ جوړ شو: {created.FileName}");
        });
    }

    private async Task VerifyAsync()
    {
        if (SelectedBackup is null) return;
        await BusyAsync(async () =>
        {
            var result = await _services.GetRequiredService<ILocalBackupService>().VerifyAsync(SelectedBackup.FullPath, await TenantIdAsync());
            StatusMessage = result.Message;
        });
    }

    private async Task RestoreAsync()
    {
        if (SelectedBackup is null || RestoreConfirmation != "RESTORE") return;
        await BusyAsync(async () =>
        {
            var wasRunning = false;
            if (_network.Mode == DeploymentMode.Server)
            {
                var status = await _serverController.GetStatusAsync();
                wasRunning = status.IsInstalled && status.IsRunning;
                if (wasRunning)
                {
                    status = await _serverController.StopAsync();
                    if (status.IsRunning) throw new InvalidOperationException(status.Message);
                }
            }

            try
            {
                var result = await _services.GetRequiredService<ILocalBackupService>().RestoreAsync(SelectedBackup.FullPath, await TenantIdAsync());
                RestoreConfirmation = string.Empty;
                await ReloadCoreAsync();
                StatusMessage = result.SafetyBackup is null
                    ? T("Restore completed and verified.", "بازیابی و تأیید کامل شد.", "بېرته راګرځول او تایید بشپړ شو.")
                    : T($"Restore completed. Safety backup: {result.SafetyBackup.FileName}", $"بازیابی کامل شد. نسخه ایمنی: {result.SafetyBackup.FileName}", $"بېرته راګرځول بشپړ شو. خوندي بیک اپ: {result.SafetyBackup.FileName}");
            }
            finally
            {
                if (wasRunning)
                {
                    var status = await _serverController.StartAsync();
                    if (!status.IsRunning) StatusMessage += Environment.NewLine + status.Message;
                }
            }
        });
    }

    private async Task ReloadCoreAsync()
    {
        Backups.Clear();
        foreach (var backup in await _services.GetRequiredService<ILocalBackupService>().ListAsync()) Backups.Add(backup);
    }

    private async Task<string> TenantIdAsync()
    {
        var entitlement = await _services.GetRequiredService<ILicenseService>().GetCachedEntitlementAsync();
        return entitlement?.TenantId ?? throw new InvalidOperationException("A valid pharmacy activation is required before backup or restore.");
    }

    partial void OnSelectedBackupChanged(LocalBackupSnapshot? value) { VerifyCommand.NotifyCanExecuteChanged(); RestoreCommand.NotifyCanExecuteChanged(); }
    partial void OnRestoreConfirmationChanged(string value) => RestoreCommand.NotifyCanExecuteChanged();

    private async Task BusyAsync(Func<Task> action)
    {
        if (IsBusy) return;
        IsBusy = true; Notify();
        try { await action(); }
        catch (Exception exception) { StatusMessage = exception.Message; }
        finally { IsBusy = false; Notify(); }
    }

    private void Notify()
    {
        RefreshCommand.NotifyCanExecuteChanged();
        CreateBackupCommand.NotifyCanExecuteChanged();
        VerifyCommand.NotifyCanExecuteChanged();
        RestoreCommand.NotifyCanExecuteChanged();
    }

    private string T(string en, string fa, string ps) => _language.Code switch { "fa" => fa, "ps" => ps, _ => en };
}
