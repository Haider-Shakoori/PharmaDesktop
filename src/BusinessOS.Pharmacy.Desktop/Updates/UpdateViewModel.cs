using System.Collections.ObjectModel;
using BusinessOS.Pharmacy.Application.Abstractions.Backup;
using BusinessOS.Pharmacy.Application.Abstractions.Licensing;
using BusinessOS.Pharmacy.Application.Abstractions.Networking;
using BusinessOS.Pharmacy.Application.Abstractions.Storage;
using BusinessOS.Pharmacy.Application.Abstractions.Sync;
using BusinessOS.Pharmacy.Desktop.Localization;
using BusinessOS.Pharmacy.Infrastructure.Networking;
using BusinessOS.Pharmacy.LocalClient;
using BusinessOS.Pharmacy.Updater;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;

namespace BusinessOS.Pharmacy.Desktop.Updates;

public sealed partial class UpdateViewModel : ObservableObject
{
    private readonly IServiceProvider _services;
    private readonly NetworkConfiguration _network;
    private readonly UpdateService _updater;
    private UiLanguage _language = UiLanguageCatalog.All[0];
    private UpdateCheckResult? _check;
    private PreparedUpdate? _prepared;
    private bool _syncSubscribed;
    private string? _lastHistoryKey;

    [ObservableProperty] private string currentVersion = string.Empty;
    [ObservableProperty] private string availableVersion = "—";
    [ObservableProperty] private string statusMessage = string.Empty;
    [ObservableProperty] private string releaseNotes = string.Empty;
    [ObservableProperty] private string compatibilityText = string.Empty;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string conflictSummary = string.Empty;
    [ObservableProperty] private bool hasConflicts;
    [ObservableProperty] private string syncStatusText = "Cloud synchronization has not run yet.";

    public UpdateViewModel(
        IServiceProvider services,
        NetworkConfiguration network,
        UpdateService updater)
    {
        _services = services;
        _network = network;
        _updater = updater;

        CurrentVersion = GetCurrentVersion().ToString();
        CompatibilityText = ModeText();

        CheckCommand = new AsyncRelayCommand(CheckAsync, () => !IsBusy);
        DownloadCommand = new AsyncRelayCommand(
            DownloadAsync,
            () => !IsBusy && _check?.Manifest is not null &&
                  _check.Availability is UpdateAvailability.Available or UpdateAvailability.Required);
        InstallCommand = new AsyncRelayCommand(
            InstallAsync,
            () => !IsBusy && _prepared is not null);
        SyncNowCommand = new AsyncRelayCommand(SyncNowAsync, () => !IsBusy);
        RefreshConflictsCommand = new AsyncRelayCommand(
            RefreshConflictsAsync,
            () => !IsBusy);
        RetryConflictCommand = new AsyncRelayCommand<string>(
            RetryConflictAsync,
            _ => !IsBusy);
        ForceSyncConflictsCommand = new AsyncRelayCommand(
            ForceSyncConflictsAsync,
            () => !IsBusy && HasConflicts);
        DismissConflictCommand = new AsyncRelayCommand<string>(
            DismissConflictAsync,
            _ => !IsBusy);
    }

    public ObservableCollection<SyncConflictItemViewModel> Conflicts { get; } = new();
    public ObservableCollection<string> SyncHistory { get; } = new();
    public IAsyncRelayCommand SyncNowCommand { get; }
    public string SyncStatusTitle => T("Cloud sync status", "وضعیت همگام‌سازی ابری", "د کلاوډ همغږۍ حالت");
    public string SyncNowLabel => T("Sync now", "همگام‌سازی اکنون", "اوس همغږي کړئ");
    public string SyncHistoryTitle => T("Recent sync attempts (this session)", "تلاش‌های اخیر همگام‌سازی (این نشست)", "د همغږۍ وروستۍ هڅې (دا ناسته)");
    public string SyncHistoryEmpty => T("No synchronization attempts in this session.", "در این نشست همگام‌سازی انجام نشده است.", "په دې ناسته کې د همغږۍ هڅه نشته.");

    public IAsyncRelayCommand CheckCommand { get; }
    public IAsyncRelayCommand DownloadCommand { get; }
    public IAsyncRelayCommand InstallCommand { get; }
    public IAsyncRelayCommand RefreshConflictsCommand { get; }
    public IAsyncRelayCommand<string> RetryConflictCommand { get; }
    public IAsyncRelayCommand ForceSyncConflictsCommand { get; }
    public IAsyncRelayCommand<string> DismissConflictCommand { get; }

    public string ConflictsTitle => T(
        "Cloud synchronization conflicts",
        "تعارض‌های همگام‌سازی ابری",
        "د کلود سینک نړۍوالۍConflicts");
    public string ConflictsEmptyText => T(
        "No cloud synchronization conflicts require review.",
        "هیچ تعارضی برای همگام‌سازی ابری وجود ندارد.",
        "د کلود سینک کوم نړۍوال نه دي.");
    public string ConflictsHelpText => T(
        "Retrying re-sends the event to BusinessOS cloud. The local sale is never deleted or rewritten.",
        "تلاش دوباره رویداد را به فضای ابری BusinessOS ارسال می‌کند. فروش محلی هرگز حذف یا بازنویسی نمی‌شود.",
        "بیا هڅه په کلود ته ولېږي. پلارې نه شي پاک یا بیا لیکل کېږي.");
    public string RetryLabel => T("Retry", "تلاش دوباره", "بیا هڅه");
    public string DismissLabel => T("Dismiss", "نادیده گرفتن", "پرېږده");
    public string RefreshConflictsLabel => T("Refresh conflicts", "به‌روزرسانی تعارض‌ها", "Conflicts نوې کړئ");
    public string ForceSyncConflictsLabel => T("Force sync conflicts", "همگام‌سازی اجباری تعارض‌ها", "د شخړو جبري همغږي");

    public string Title => T("Application Updates", "به‌روزرسانی برنامه", "د اپلېکېشن تازه کول");
    public string Subtitle => T(
        "Signed, version-aware Darmaltoon releases",
        "نسخه‌های امضاشده و کنترل‌شده دارملتون",
        "د درملتون لاسلیک شوي او نسخه-خبر تازه معلومات");
    public string CheckLabel => T("Check for Updates", "بررسی به‌روزرسانی", "تازه معلومات وګورئ");
    public string DownloadLabel => T("Download & Verify", "دانلود و تأیید", "ډاونلوډ او تایید");
    public string InstallLabel => T("Install Update", "نصب به‌روزرسانی", "تازه کول نصب کړئ");
    public void SetLanguage(UiLanguage language)
    {
        _language = language;
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(CheckLabel));
        OnPropertyChanged(nameof(DownloadLabel));
        OnPropertyChanged(nameof(InstallLabel));
        OnPropertyChanged(nameof(SyncStatusTitle));
        OnPropertyChanged(nameof(SyncNowLabel));
        OnPropertyChanged(nameof(SyncHistoryTitle));
        OnPropertyChanged(nameof(SyncHistoryEmpty));
        CompatibilityText = ModeText();
    }

    public Task LoadAsync()
    {
        CurrentVersion = GetCurrentVersion().ToString();
        CompatibilityText = ModeText();
        if (string.IsNullOrWhiteSpace(StatusMessage))
        {
            StatusMessage = T(
                "Updates are accepted only when the release manifest signature and package checksum are valid.",
                "به‌روزرسانی فقط با امضای معتبر و بررسی صحت بسته پذیرفته می‌شود.",
                "تازه کول یوازې د سم لاسلیک او د بسته د سم checksum سره منل کېږي.");
        }

        var sync = _services.GetService<ICloudSyncService>();
        if (sync is not null)
        {
            if (!_syncSubscribed)
            {
                sync.ResultUpdated += OnSyncResultUpdated;
                _syncSubscribed = true;
            }
            ApplySyncResult(sync.LastResult);
        }
        else
        {
            SyncStatusText = T("Cloud sync is unavailable for this mode.",
                "همگام‌سازی ابری برای این حالت در دسترس نیست.",
                "په دې حالت کې کلاوډ همغږي نشته.");
        }

        return RefreshConflictsAsync();
    }

    private async Task SyncNowAsync()
    {
        var sync = _services.GetService<ICloudSyncService>();
        if (sync is null)
        {
            SyncStatusText = T("Cloud sync is unavailable for this mode.",
                "همگام‌سازی ابری برای این حالت در دسترس نیست.",
                "په دې حالت کې کلاوډ همغږي نشته.");
            return;
        }

        await BusyAsync(async () =>
        {
            var result = await sync.SyncOnceAsync();
            ApplySyncResult(result);
        });
        await RefreshConflictsAsync();
    }

    private void OnSyncResultUpdated(CloudSyncRunResult result)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            _ = dispatcher.BeginInvoke(() => ApplySyncResult(result));
            return;
        }
        ApplySyncResult(result);
    }

    private void ApplySyncResult(CloudSyncRunResult result)
    {
        var attempted = result.AttemptedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "—";
        SyncStatusText = $"{result.State} · {result.Message} · Last attempt: {attempted}"
            + $" · Sent: {result.Pushed} · Received: {result.Pulled} · Conflicts: {result.Conflicts}";

        if (result.State == CloudSyncRunState.NeverRun) return;
        var key = $"{result.AttemptedAt:O}|{result.State}|{result.Pushed}|{result.Pulled}|{result.Conflicts}";
        if (key == _lastHistoryKey) return;
        _lastHistoryKey = key;
        SyncHistory.Insert(0, $"{attempted} · {result.State} · ↑{result.Pushed} ↓{result.Pulled} · {result.Message}");
        while (SyncHistory.Count > 20) SyncHistory.RemoveAt(SyncHistory.Count - 1);
    }

    private async Task RefreshConflictsAsync()
    {
        var sync = _services.GetService<ICloudSyncService>();
        if (sync is null)
        {
            ApplyConflicts(
                CloudSyncConflictReview.Unavailable(
                    "Cloud synchronization is disabled for this terminal mode."),
                keepSummary: true);
            return;
        }

        await BusyAsync(async () =>
        {
            try
            {
                ApplyConflicts(
                    await sync.GetConflictReviewAsync(),
                    keepSummary: false);
            }
            catch (Exception exception)
            {
                ConflictSummary = exception.Message;
                HasConflicts = false;
                Conflicts.Clear();
            }
        });
    }

    private async Task RetryConflictAsync(string? idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            return;

        var sync = _services.GetService<ICloudSyncService>();
        if (sync is null)
            return;

        await BusyAsync(async () =>
        {
            try
            {
                ApplyConflicts(await sync.RetryConflictAsync(idempotencyKey), keepSummary: false);
            }
            catch (Exception exception)
            {
                ConflictSummary = exception.Message;
            }
        });
    }

    private async Task DismissConflictAsync(string? idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            return;

        var sync = _services.GetService<ICloudSyncService>();
        if (sync is null)
            return;

        await BusyAsync(async () =>
        {
            try
            {
                ApplyConflicts(await sync.DismissConflictAsync(idempotencyKey), keepSummary: false);
            }
            catch (Exception exception)
            {
                ConflictSummary = exception.Message;
            }
        });
    }

    private async Task ForceSyncConflictsAsync()
    {
        var sync = _services.GetService<ICloudSyncService>();
        if (sync is null)
            return;

        await BusyAsync(async () =>
        {
            try
            {
                var review = await sync.GetConflictReviewAsync();
                var retryable = review.Conflicts
                    .Where(x => x.CanRetry)
                    .Select(x => x.IdempotencyKey)
                    .ToList();

                foreach (var key in retryable)
                    review = await sync.RetryConflictAsync(key);

                var result = await sync.SyncOnceAsync();
                ApplySyncResult(result);
                ApplyConflicts(
                    await sync.GetConflictReviewAsync(),
                    keepSummary: false);
            }
            catch (Exception exception)
            {
                ConflictSummary = exception.Message;
            }
        });
    }

    private void ApplyConflicts(
        CloudSyncConflictReview review,
        bool keepSummary)
    {
        if (!keepSummary)
            ConflictSummary = review.Message;

        Conflicts.Clear();
        foreach (var conflict in review.Conflicts)
            Conflicts.Add(new SyncConflictItemViewModel(conflict));

        HasConflicts = Conflicts.Count > 0;
        NotifyCommands();
    }

    private async Task CheckAsync()
    {
        await BusyAsync(async () =>
        {
            Version? serverVersion = null;
            if (_network.Mode == DeploymentMode.Client)
            {
                serverVersion = await _services
                    .GetRequiredService<LanTerminalPairingClient>()
                    .GetServerApplicationVersionAsync();
            }
            _check = await _updater.CheckAsync(
                GetCurrentVersion(),
                _network.Mode.ToString(),
                serverVersion);

            AvailableVersion = _check.AvailableVersion?.ToString() ?? "—";
            ReleaseNotes = _check.Manifest?.ReleaseNotes ?? string.Empty;
            StatusMessage = _check.Message;
            NotifyCommands();
        });
    }

    private async Task DownloadAsync()
    {
        if (_check?.Manifest is null)
            return;

        await BusyAsync(async () =>
        {
            _prepared = await _updater.DownloadAndStageAsync(_check.Manifest);
            StatusMessage = T(
                $"Darmaltoon {_prepared.Manifest.Version} downloaded and verified. Ready to install.",
                $"نسخه {_prepared.Manifest.Version} دانلود و تأیید شد. آماده نصب است.",
                $"نسخه {_prepared.Manifest.Version} ډاونلوډ او تایید شوه. نصب ته چمتو ده.");
            NotifyCommands();
        });
    }

    private async Task InstallAsync()
    {
        if (_prepared is null)
            return;

        await BusyAsync(async () =>
        {
            string? preUpdateBackupPath = null;
            if (_network.Mode != DeploymentMode.Client)
            {
                var backup = _services.GetRequiredService<ILocalBackupService>();
                var licensing = _services.GetRequiredService<ILicenseService>();
                var entitlement = await licensing.GetCachedEntitlementAsync()
                    ?? throw new InvalidOperationException(
                        "A valid pharmacy activation is required before application update.");

                var safety = await backup.CreateAsync(entitlement.TenantId);
                preUpdateBackupPath = safety.FullPath;
            }

            var paths = _services.GetRequiredService<IApplicationPaths>();
            var installDirectory = AppContext.BaseDirectory;
            var executable = Environment.ProcessPath;
            var serviceName = _network.Mode == DeploymentMode.Server
                ? WindowsLocalServerServiceController.ServiceName
                : null;

            var plan = await _updater.WriteApplyPlanAsync(
                _prepared,
                installDirectory,
                paths.RootDirectory,
                _network.Mode.ToString(),
                Environment.ProcessId,
                executable,
                serviceName,
                preUpdateBackupPath);

            _ = _updater.LaunchApplyAgent(plan);
            StatusMessage = T(
                "Verified update is starting. Darmaltoon will close and reopen after installation.",
                "به‌روزرسانی تأییدشده در حال شروع است. دارملتون پس از نصب دوباره باز می‌شود.",
                "تایید شوی تازه کول پیلېږي. درملتون به د نصب وروسته بیا پرانیستل شي.");

            System.Windows.Application.Current.Shutdown();
        });
    }
    private async Task BusyAsync(Func<Task> action)
    {
        if (IsBusy) return;
        IsBusy = true;
        NotifyCommands();
        try { await action(); }
        catch (Exception exception) { StatusMessage = exception.Message; }
        finally
        {
            IsBusy = false;
            NotifyCommands();
        }
    }

    private void NotifyCommands()
    {
        SyncNowCommand.NotifyCanExecuteChanged();
        CheckCommand.NotifyCanExecuteChanged();
        DownloadCommand.NotifyCanExecuteChanged();
        InstallCommand.NotifyCanExecuteChanged();
        RefreshConflictsCommand.NotifyCanExecuteChanged();
        RetryConflictCommand.NotifyCanExecuteChanged();
        ForceSyncConflictsCommand.NotifyCanExecuteChanged();
        DismissConflictCommand.NotifyCanExecuteChanged();
    }

    private string ModeText() => _network.Mode switch
    {
        DeploymentMode.Server => T(
            "Main Server mode: a database safety backup is created before installation and the LAN service is stopped only during file replacement.",
            "حالت سرور اصلی: قبل از نصب نسخه ایمنی پایگاه داده ساخته می‌شود و سرویس شبکه فقط هنگام جایگزینی فایل‌ها متوقف می‌شود.",
            "اصلي سرور: د نصب مخکې خوندي بیک اپ جوړېږي او د LAN خدمت یوازې د فایلونو د بدلولو پر مهال درېږي."),
        DeploymentMode.Client => T(
            "Client Terminal mode: no local pharmacy database is replaced. The update is blocked unless the paired Main Server is compatible.",
            "حالت ترمینال: پایگاه داده محلی جایگزین نمی‌شود و نسخه فقط با سرور اصلی سازگار نصب می‌شود.",
            "کلاینټ ترمینل: محلي ډیټابیس نه بدلېږي او تازه کول یوازې له سازګار اصلي سرور سره نصب کېږي."),
        _ => T(
            "Standalone mode: a database safety backup is created before application files are replaced.",
            "حالت مستقل: قبل از جایگزینی فایل‌های برنامه نسخه ایمنی پایگاه داده ساخته می‌شود.",
            "خپلواک حالت: د اپلېکېشن فایلونو تر بدلولو مخکې د ډیټابیس خوندي بیک اپ جوړېږي."),
    };

    private static Version GetCurrentVersion() =>
        typeof(UpdateViewModel).Assembly.GetName().Version ?? new Version(1, 0, 0);

    private string T(string en, string fa, string ps) =>
        _language.Code switch { "fa" => fa, "ps" => ps, _ => en };
}
