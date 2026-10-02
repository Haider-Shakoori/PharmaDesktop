using System.IO;
using System.Collections.ObjectModel;
using BusinessOS.Pharmacy.Application.Abstractions.Licensing;
using BusinessOS.Pharmacy.Application.Abstractions.Networking;
using BusinessOS.Pharmacy.Application.Abstractions.Storage;
using BusinessOS.Pharmacy.Desktop.Localization;
using BusinessOS.Pharmacy.LocalClient;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;

namespace BusinessOS.Pharmacy.Desktop.Networking;

public sealed partial class NetworkSettingsViewModel : ObservableObject
{
    private readonly IServiceProvider _services;
    private readonly INetworkConfigurationStore _configurationStore;
    private readonly ILocalServerDiscovery _discovery;
    private readonly ILocalServerServiceController _serviceController;
    private readonly Profile.UserProfileStore _profileStore;
    private readonly Notifications.NotificationService _notifications;
    private readonly Printing.ReceiptSettingsStore _receiptSettings;
    private readonly Pos.PosSettingsStore _posSettings;

    private UiLanguage _language = UiLanguageCatalog.All[0];

    [ObservableProperty] private DeploymentMode currentMode;
    [ObservableProperty] private DeploymentMode requestedMode;
    [ObservableProperty] private string serverName = string.Empty;
    [ObservableProperty] private string serverHost = string.Empty;
    [ObservableProperty] private int serverPort = NetworkConfiguration.DefaultServerPort;
    [ObservableProperty] private string serverId = string.Empty;
    [ObservableProperty] private string certificateFingerprint = string.Empty;
    [ObservableProperty] private string terminalId = string.Empty;
    [ObservableProperty] private string statusMessage = string.Empty;
    [ObservableProperty] private string serviceStatus = string.Empty;
    [ObservableProperty] private string connectionStatus = string.Empty;
    [ObservableProperty] private string networkProfileStatus = string.Empty;
    [ObservableProperty] private string firewallStatus = string.Empty;
    [ObservableProperty] private string diagnosticsReport = string.Empty;
    [ObservableProperty] private string pairingCode = string.Empty;
    [ObservableProperty] private string pairingExpiry = string.Empty;
    [ObservableProperty] private string profileFirstName = string.Empty;
    [ObservableProperty] private string profileLastName = string.Empty;
    [ObservableProperty] private string profileImagePath = string.Empty;
    [ObservableProperty] private bool receiptShowBatchDetails = true;
    [ObservableProperty] private bool receiptShowCashier = true;
    [ObservableProperty] private bool receiptShowCustomer = true;
    [ObservableProperty] private bool receiptShowPayments = true;
    [ObservableProperty] private bool receiptShowFooter = true;
    [ObservableProperty] private string receiptFooterText = "Thank you for your purchase";
    [ObservableProperty] private bool posShowTopSellers = true;
    [ObservableProperty] private int posTopSellerCount = 10;
    [ObservableProperty] private int posTopSellerDays = 30;
    [ObservableProperty] private RegisteredTerminal? selectedTerminal;
    [ObservableProperty] private string renameTerminalTo = string.Empty;
    [ObservableProperty] private bool isBusy;

    public NetworkSettingsViewModel(
        IServiceProvider services,
        INetworkConfigurationStore configurationStore,
        ILocalServerDiscovery discovery,
        ILocalServerServiceController serviceController,
        Profile.UserProfileStore profileStore,
        Notifications.NotificationService notifications,
        Printing.ReceiptSettingsStore receiptSettings,
        Pos.PosSettingsStore posSettings)
    {
        _services = services;
        _configurationStore = configurationStore;
        _discovery = discovery;
        _serviceController = serviceController;
        _profileStore = profileStore;
        _notifications = notifications;
        _receiptSettings = receiptSettings;
        _posSettings = posSettings;

        UploadProfileImageCommand = new RelayCommand(UploadProfileImage);
        SaveProfileCommand = new RelayCommand(SaveProfile);
        SaveReceiptSettingsCommand = new RelayCommand(SaveReceiptSettings);
        SavePosSettingsCommand = new RelayCommand(SavePosSettings);
        RefreshTopSellersCommand = new AsyncRelayCommand(RefreshTopSellersAsync, () => PosShowTopSellers && !IsBusy);

        RefreshCommand = new AsyncRelayCommand(LoadAsync, () => !IsBusy);
        CreatePairingCodeCommand = new AsyncRelayCommand(CreatePairingCodeAsync, () => !IsBusy && CurrentMode == DeploymentMode.Server);
        RevokeTerminalCommand = new AsyncRelayCommand(RevokeTerminalAsync, () => !IsBusy && SelectedTerminal is not null);
        RenameTerminalCommand = new AsyncRelayCommand(RenameTerminalAsync, () => !IsBusy && SelectedTerminal is not null);
        StartServerCommand = new AsyncRelayCommand(StartServerAsync, () => !IsBusy && CurrentMode == DeploymentMode.Server);
        ConfigureFirewallCommand = new AsyncRelayCommand(ConfigureFirewallAsync, () => !IsBusy && CurrentMode == DeploymentMode.Server);
        TestConnectionCommand = new AsyncRelayCommand(TestConnectionAsync, () => !IsBusy && CurrentMode == DeploymentMode.Client);
        RediscoverCommand = new AsyncRelayCommand(RediscoverAsync, () => !IsBusy && CurrentMode == DeploymentMode.Client);
        ApplyModeCommand = new AsyncRelayCommand(ApplyModeAsync, () => !IsBusy);
        SaveConnectionCommand = new AsyncRelayCommand(SaveConnectionAsync, () => !IsBusy);
        RunDiagnosticsCommand = new AsyncRelayCommand(RunDiagnosticsAsync, () => !IsBusy);
    }

    public ObservableCollection<RegisteredTerminal> Terminals { get; } = new();
    public IReadOnlyList<DeploymentMode> Modes { get; } =
        [DeploymentMode.Standalone, DeploymentMode.Server, DeploymentMode.Client];

    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand CreatePairingCodeCommand { get; }
    public IAsyncRelayCommand RevokeTerminalCommand { get; }
    public IAsyncRelayCommand RenameTerminalCommand { get; }
    public IAsyncRelayCommand StartServerCommand { get; }
    public IAsyncRelayCommand ConfigureFirewallCommand { get; }
    public IAsyncRelayCommand TestConnectionCommand { get; }
    public IAsyncRelayCommand RediscoverCommand { get; }
    public IAsyncRelayCommand ApplyModeCommand { get; }
    public IAsyncRelayCommand SaveConnectionCommand { get; }
    public IAsyncRelayCommand RunDiagnosticsCommand { get; }
    public IRelayCommand UploadProfileImageCommand { get; }
    public IRelayCommand SaveProfileCommand { get; }
    public IRelayCommand SaveReceiptSettingsCommand { get; }
    public IRelayCommand SavePosSettingsCommand { get; }
    public IAsyncRelayCommand RefreshTopSellersCommand { get; }

    public string ReceiptTitle => T("Receipt printing", "چاپ فاکتور", "د بل چاپ");
    public string ReceiptSubtitle => T(
        "Choose what appears on the printed customer receipt",
        "انتخاب مواردی که روی فاکتور چاپی مشتری نمایش داده می‌شود",
        "هغه څه وټاکئ چې د پېرودونکي په چاپي بل کې ښکاري");
    public string ReceiptShowBatchLabel => T("Show batch and expiry details", "نمایش جزئیات بچ و انقضا", "د بېچ او تاریخ تېر جزئیات ښودل");
    public string ReceiptShowCashierLabel => T("Show cashier name", "نمایش نام صندوقدار", "د کاشر نوم ښودل");
    public string ReceiptShowCustomerLabel => T("Show customer name", "نمایش نام مشتری", "د پېرودونکي نوم ښودل");
    public string ReceiptShowPaymentsLabel => T("Show payment details", "نمایش جزئیات پرداخت", "د تادیې جزئیات ښودل");
    public string ReceiptShowFooterLabel => T("Show footer message", "نمایش پیام پایانی", "د پای پیغام ښودل");
    public string ReceiptFooterTextLabel => T("Footer message", "پیام پایانی", "د پای پیغام");
    public string SaveReceiptLabel => T("Save receipt options", "ذخیره تنظیمات فاکتور", "د بل تنظیمات خوندي کړئ");

    public void LoadReceiptSettings()
    {
        var settings = _receiptSettings.Load();
        ReceiptShowBatchDetails = settings.ShowBatchDetails;
        ReceiptShowCashier = settings.ShowCashier;
        ReceiptShowCustomer = settings.ShowCustomer;
        ReceiptShowPayments = settings.ShowPayments;
        ReceiptShowFooter = settings.ShowFooter;
        ReceiptFooterText = settings.FooterText;
    }

    private void SaveReceiptSettings()
    {
        try
        {
            _receiptSettings.Save(new Printing.ReceiptSettings(
                ReceiptShowBatchDetails,
                ReceiptShowCashier,
                ReceiptShowCustomer,
                ReceiptShowPayments,
                ReceiptShowFooter,
                ReceiptFooterText));

            _notifications.ShowSuccess(T(
                "Receipt options saved. New prints use these settings.",
                "تنظیمات فاکتور ذخیره شد. چاپ‌های بعدی از این تنظیمات استفاده می‌کنند.",
                "د بل تنظیمات خوندي شول. راتلونکي چاپونه دا تنظیمات کاروي."));
        }
        catch (Exception exception)
        {
            _notifications.ShowError(T(
                $"Receipt options could not be saved: {exception.Message}",
                $"تنظیمات فاکتور ذخیره نشد: {exception.Message}",
                $"د بل تنظیمات خوندي نه شول: {exception.Message}"));
        }
    }

    public string PosTitle => T("Point of sale", "فروش", "خرڅلاو");
    public string PosSubtitle => T(
        "Control the quick-add top sellers strip in the POS workspace",
        "نوار پرفروش‌ترین‌ها برای افزودن سریع در صفحه فروش",
        "په خرڅلاو کې د ژر اضافولو لپاره د ډېر پلورېدونکو درملو په ونډه کنټرول کړئ");
    public string PosShowTopSellersLabel => T(
        "Show top selling medicines in POS",
        "نمایش پرفروش‌ترین دواها در فروش",
        "د خرڅلاو په پاڼه کې ډېر پلورېدونکي درمل ښودل");
    public string PosShowTopSellersHint => T(
        "One click adds the medicine to the cart",
        "با یک کلیک دوا به سبد اضافه می‌شود",
        "په یوې کلیک سره درمل په ټوکرۍ کې زیاتېږي");
    public string PosTopSellerCountLabel => T("How many to show", "تعداد نمایش", "څومره ښودل شي");
    public string PosTopSellerDaysLabel => T("Sales period", "بازه فروش", "د پلور مدت");
    public string PosTopSellerDaysText => T("days", "روز", "ورځې");
    public string SavePosLabel => T("Save point of sale options", "ذخیره تنظیمات فروش", "د خرڅلاو تنظیمات خوندي کړئ");
    public string PosPreviewTitle => T("Current top sellers", "پرفروش‌های فعلی", "اوسني ډېر پلورېدونکي درمل");
    public string PosPreviewRefreshLabel => T("Refresh preview", "به‌روزرسانی پیش‌نمایش", "پیش‌نمایش نوې کړئ");
    public string PosPreviewEmpty => T(
        "No completed sales in the selected period.",
        "در بازه انتخابی فروش تکمیل‌شده‌ای وجود ندارد.",
        "په انتخاب شوې م دوره کې بشپړ خرڅلاو نشته.");
    public string PosPreviewSoldFormat => T(
        "sold in period",
        "فروش در بازه",
        "په موره کې پلور شوی");

    public IReadOnlyList<int> PosTopSellerCountOptions { get; } = [5, 10, 20, 30];
    public IReadOnlyList<int> PosTopSellerDaysOptions { get; } = [7, 15, 30, 90, 180, 365];

    public ObservableCollection<string> PosTopSellerPreview { get; } = new();

    partial void OnPosShowTopSellersChanged(bool value) =>
        RefreshTopSellersCommand.NotifyCanExecuteChanged();

    public void LoadPosSettings()
    {
        var settings = _posSettings.Load();
        PosShowTopSellers = settings.ShowTopSellers;
        PosTopSellerCount = settings.TopSellerCount;
        PosTopSellerDays = settings.TopSellerDays;
        _ = RefreshTopSellersAsync();
    }

    private void SavePosSettings()
    {
        try
        {
            _posSettings.Save(new Pos.PosSettings(PosShowTopSellers, PosTopSellerCount, PosTopSellerDays));

            _notifications.ShowSuccess(T(
                "Point of sale options saved.",
                "تنظیمات فروش ذخیره شد.",
                "د خرڅلاو تنظیمات خوندي شول."));
        }
        catch (Exception exception)
        {
            _notifications.ShowError(T(
                $"Point of sale options could not be saved: {exception.Message}",
                $"تنظیمات فروش ذخیره نشد: {exception.Message}",
                $"د خرڅلاو تنظیمات خوندي نه شول: {exception.Message}"));
        }

        _ = RefreshTopSellersAsync();
    }

    private async Task RefreshTopSellersAsync()
    {
        PosTopSellerPreview.Clear();

        if (!PosShowTopSellers)
        {
            return;
        }

        try
        {
            var pos = _services.GetRequiredService<Application.Abstractions.Sales.IPosService>();
            var references = await pos.GetReferenceDataAsync();
            var location = references.StockLocations.FirstOrDefault(x => x.IsDefault)
                ?? references.StockLocations.FirstOrDefault();

            if (location is null)
            {
                return;
            }

            var items = await pos.GetTopProductsAsync(location.Id, Math.Min(PosTopSellerCount, 10), PosTopSellerDays);
            foreach (var item in items)
            {
                PosTopSellerPreview.Add(
                    $"{item.BrandName}{(string.IsNullOrWhiteSpace(item.Strength) ? string.Empty : " " + item.Strength)} · " +
                    $"{item.QuantitySold:0.##} {item.SaleUnit}");
            }
        }
        catch (Exception exception)
        {
            PosTopSellerPreview.Add(T(
                $"Could not load top sellers: {exception.Message}",
                $"بارگذاری پرفروش‌ها ممکن نشد: {exception.Message}",
                $"د ډېر پلورېدونکو درملو بارولو ممکن نه شو: {exception.Message}"));
        }
    }

    public bool HasProfileImage =>
        !string.IsNullOrWhiteSpace(ProfileImagePath) && File.Exists(ProfileImagePath);

    public string ProfileTitle => T("User profile", "پروفایل کاربر", "د کارن پروفایل");
    public string ProfileSubtitle => T(
        "Name and photo shown in the application header on this PC",
        "نام و عکس نمایش‌داده‌شده در سربرگ برنامه روی این رایانه",
        "هغه نوم او عکس چې په دې کمپیوټر کې د اپلیکیشن په سر کې ښودل کیږي");
    public string FirstNameLabel => T("First name", "نام", "نوم");
    public string LastNameLabel => T("Last name", "تخلص", "تخلص");
    public string UploadPhotoLabel => T("Upload photo", "بارگذاری عکس", "عکس پورته کړئ");
    public string SaveProfileLabel => T("Save profile", "ذخیره پروفایل", "پروفایل خوندي کړئ");

    partial void OnProfileImagePathChanged(string value) =>
        OnPropertyChanged(nameof(HasProfileImage));

    public void LoadProfile()
    {
        var profile = _profileStore.Load();
        ProfileFirstName = profile.FirstName;
        ProfileLastName = profile.LastName;
        ProfileImagePath = profile.ImagePath ?? string.Empty;
    }

    private void UploadProfileImage()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = T("Choose a profile photo", "انتخاب عکس پروفایل", "د پروفایل عکس وټاکئ"),
            Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp",
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            ProfileImagePath = _profileStore.CopyImage(dialog.FileName);
            _notifications.ShowSuccess(T(
                "Profile photo updated. Save the profile to apply it.",
                "عکس پروفایل به‌روزرسانی شد. برای اعمال، پروفایل را ذخیره کنید.",
                "د پروفایل عکس تازه شو. د پلي کولو لپاره پروفایل خوندي کړئ."));
        }
        catch (Exception exception)
        {
            _notifications.ShowError(T(
                $"Profile photo could not be saved: {exception.Message}",
                $"عکس پروفایل ذخیره نشد: {exception.Message}",
                $"د پروفایل عکس خوندي نه شو: {exception.Message}"));
        }
    }

    private void SaveProfile()
    {
        try
        {
            _profileStore.Save(new Profile.UserProfile(
                ProfileFirstName.Trim(),
                ProfileLastName.Trim(),
                string.IsNullOrWhiteSpace(ProfileImagePath) ? null : ProfileImagePath));
            _notifications.ShowSuccess(T(
                "User profile saved.",
                "پروفایل کاربر ذخیره شد.",
                "د کارن پروفایل خوندي شو."));
        }
        catch (Exception exception)
        {
            _notifications.ShowError(T(
                $"Profile could not be saved: {exception.Message}",
                $"پروفایل ذخیره نشد: {exception.Message}",
                $"پروفایل خوندي نه شو: {exception.Message}"));
        }
    }

    public string Title => T("Network & Terminals", "شبکه و ترمینال‌ها", "شبکه او ترمینلونه");
    public string Subtitle => T("Local pharmacy server, terminals and diagnostics", "سرور محلی دواخانه، ترمینال‌ها و عیب‌یابی", "د درملتون محلي سرور، ترمینلونه او تشخیص");
    public string DeploymentModeLabel => T("Deployment mode", "حالت نصب", "د نصب حالت");
    public string ServerNameLabel => T("Server name", "نام سرور", "د سرور نوم");
    public string ServerAddressLabel => T("Server address", "آدرس سرور", "د سرور پته");
    public string PortLabel => T("Port", "پورت", "پورټ");
    public string ServerIdLabel => T("Server UUID", "شناسه سرور", "د سرور پېژند");
    public string CertificateLabel => T("Certificate fingerprint", "اثر انگشت گواهی", "د سند ګوتنښه");
    public string ServiceLabel => T("Local Server service", "سرویس سرور محلی", "د محلي سرور خدمت");
    public string ConnectionLabel => T("Connection", "اتصال", "اړیکه");
    public string TerminalsLabel => T("Registered terminals", "ترمینال‌های ثبت‌شده", "ثبت شوي ترمینلونه");
    public string PairingLabel => T("New terminal pairing", "اتصال ترمینال جدید", "د نوي ترمینل نښلول");
    public string CreatePairingLabel => T("Create 5-minute pairing code", "ایجاد کد اتصال ۵ دقیقه‌ای", "د ۵ دقیقو نښلون کوډ جوړول");
    public string StartServerLabel => T("Start Server", "راه‌اندازی سرور", "سرور پیل کړئ");
    public string FirewallLabel => T("Configure Private Firewall", "تنظیم فایروال خصوصی", "شخصي فایروال تنظیمول");
    public string TestConnectionLabel => T("Test Connection", "آزمایش اتصال", "اړیکه وازمویئ");
    public string RediscoverLabel => T("Find Server Again", "یافتن دوباره سرور", "سرور بیا ومومئ");
    public string ApplyModeLabel => T("Apply Mode", "اعمال حالت", "حالت پلي کړئ");
    public string SaveConnectionLabel => T("Save Address / Port", "ذخیره آدرس / پورت", "پته / پورټ خوندي کړئ");
    public string RenameLabel => T("Rename", "تغییر نام", "نوم بدلول");
    public string RevokeLabel => T("Revoke Terminal", "لغو ترمینال", "ترمینل لغوه کړئ");
    public string RefreshLabel => T("Refresh", "تازه‌سازی", "تازه کول");
    public string RunDiagnosticsLabel => T("Run Diagnostics", "اجرای عیب‌یابی", "تشخیص وچلوئ");
    public string NetworkProfileLabel => T("Windows network profile", "پروفایل شبکه ویندوز", "د وینډوز شبکې پروفایل");
    public string FirewallStatusLabel => T("Firewall status", "وضعیت فایروال", "د فایروال حالت");

    public void SetLanguage(UiLanguage language)
    {
        _language = language;
        RaiseLocalizedProperties();
    }

    public async Task LoadAsync()
    {
        await BusyAsync(async () =>
        {
            LoadProfile();
            LoadReceiptSettings();
            LoadPosSettings();

            var configuration = await _configurationStore.LoadAsync();
            ApplyConfiguration(configuration);

            var service = await _serviceController.GetStatusAsync();
            ServiceStatus = service.Message;

            var profile = await _serviceController.GetNetworkProfileStatusAsync();
            NetworkProfileStatus = profile.Message;

            if (configuration.Mode == DeploymentMode.Server)
            {
                var apiFirewall = await _serviceController
                    .GetPrivateFirewallRuleStatusAsync(configuration.ServerPort);
                var discoveryFirewall = await _serviceController
                    .GetPrivateDiscoveryFirewallRuleStatusAsync(configuration.DiscoveryPort);

                FirewallStatus = string.Join(
                    Environment.NewLine,
                    apiFirewall.Message,
                    discoveryFirewall.Message);
            }
            else
            {
                FirewallStatus = T(
                    "Not applicable in this deployment mode.",
                    "در این حالت نصب قابل تطبیق نیست.",
                    "په دې نصب حالت کې نه پلي کېږي.");
            }

            Terminals.Clear();

            if (configuration.Mode == DeploymentMode.Server)
            {
                var terminalService = _services.GetRequiredService<ILocalTerminalService>();
                foreach (var terminal in await terminalService.ListAsync())
                {
                    Terminals.Add(terminal);
                }

                ConnectionStatus = T(
                    $"{Terminals.Count(x => x.IsActive)} active terminal(s)",
                    $"{Terminals.Count(x => x.IsActive)} ترمینال فعال",
                    $"{Terminals.Count(x => x.IsActive)} فعال ترمینل");
            }
            else if (configuration.Mode == DeploymentMode.Client)
            {
                await TestConnectionCoreAsync();
            }
            else
            {
                ConnectionStatus = T(
                    "Standalone mode — no LAN server required.",
                    "حالت مستقل — سرور شبکه لازم نیست.",
                    "خپلواک حالت — د شبکې سرور ته اړتیا نشته.");
            }

            StatusMessage = T(
                "Network configuration loaded.",
                "تنظیمات شبکه بارگذاری شد.",
                "د شبکې تنظیمات پورته شول.");
        });
    }

    private async Task CreatePairingCodeAsync()
    {
        await BusyAsync(async () =>
        {
            EnsureServerMode();
            var service = _services.GetRequiredService<ILocalTerminalService>();
            var pairing = await service.CreatePairingCodeAsync(TimeSpan.FromMinutes(5));

            PairingCode = pairing.Code;
            PairingExpiry = pairing.ExpiresAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

            StatusMessage = T(
                "Give this short-lived code only to the terminal you are pairing.",
                "این کد کوتاه‌مدت را فقط به ترمینالی بدهید که متصل می‌کنید.",
                "دا لنډمهاله کوډ یوازې هغه ترمینل ته ورکړئ چې نښلوئ.");
        });
    }

    private async Task RevokeTerminalAsync()
    {
        if (SelectedTerminal is null)
        {
            return;
        }

        await BusyAsync(async () =>
        {
            EnsureServerMode();
            var service = _services.GetRequiredService<ILocalTerminalService>();
            await service.RevokeAsync(SelectedTerminal.TerminalId);
            StatusMessage = T("Terminal revoked.", "ترمینال لغو شد.", "ترمینل لغوه شو.");
            await LoadServerTerminalsCoreAsync(service);
        });
    }

    private async Task RenameTerminalAsync()
    {
        if (SelectedTerminal is null || string.IsNullOrWhiteSpace(RenameTerminalTo))
        {
            return;
        }

        await BusyAsync(async () =>
        {
            EnsureServerMode();
            var service = _services.GetRequiredService<ILocalTerminalService>();
            await service.RenameAsync(SelectedTerminal.TerminalId, RenameTerminalTo);
            RenameTerminalTo = string.Empty;
            StatusMessage = T("Terminal renamed.", "نام ترمینال تغییر کرد.", "د ترمینل نوم بدل شو.");
            await LoadServerTerminalsCoreAsync(service);
        });
    }

    private async Task StartServerAsync()
    {
        await BusyAsync(async () =>
        {
            EnsureServerMode();
            var status = await _serviceController.StartAsync();
            ServiceStatus = status.Message;
            StatusMessage = status.Message;
        });
    }

    private async Task ConfigureFirewallAsync()
    {
        await BusyAsync(async () =>
        {
            EnsureServerMode();

            var configuration = await _configurationStore.LoadAsync();
            var apiResult = await _serviceController
                .EnsurePrivateFirewallRuleAsync(ServerPort);
            var discoveryResult = await _serviceController
                .EnsurePrivateDiscoveryFirewallRuleAsync(configuration.DiscoveryPort);

            FirewallStatus = string.Join(
                Environment.NewLine,
                apiResult.Message,
                discoveryResult.Message);

            StatusMessage = apiResult.Success && discoveryResult.Success
                ? T(
                    "Private LAN firewall rules are configured for the API and automatic discovery.",
                    "قوانین فایروال شبکه خصوصی برای API و کشف خودکار تنظیم شد.",
                    "د شخصي شبکې فایروال قواعد د API او اتومات موندنې لپاره تنظیم شول.")
                : FirewallStatus;
        });
    }

    private async Task TestConnectionAsync()
    {
        await BusyAsync(TestConnectionCoreAsync);
    }

    private async Task TestConnectionCoreAsync()
    {
        if (CurrentMode != DeploymentMode.Client)
        {
            return;
        }

        var monitor = _services.GetService<ILocalServerConnectionMonitor>();
        var status = monitor is not null
            ? await monitor.CheckNowAsync()
            : await _services
                .GetRequiredService<LanTerminalPairingClient>()
                .TestConnectionAsync();

        ConnectionStatus = status.IsConnected
            ? T(
                $"Main Server connected · {status.Latency?.TotalMilliseconds:0} ms",
                $"سرور اصلی متصل · {status.Latency?.TotalMilliseconds:0} میلی‌ثانیه",
                $"اصلي سرور وصل · {status.Latency?.TotalMilliseconds:0} ms")
            : T(
                $"Pharmacy Server unavailable · {status.Message}",
                $"سرور دواخانه در دسترس نیست · {status.Message}",
                $"د درملتون سرور نشته · {status.Message}");
    }

    private async Task RediscoverAsync()
    {
        await BusyAsync(async () =>
        {
            if (CurrentMode != DeploymentMode.Client)
            {
                return;
            }

            var current = await _configurationStore.LoadAsync();
            var servers = await _discovery.DiscoverAsync(TimeSpan.FromSeconds(3));

            var match = servers.FirstOrDefault(x =>
                string.Equals(x.ServerId, current.ServerId, StringComparison.Ordinal) &&
                string.Equals(
                    NormalizeFingerprint(x.CertificateSha256),
                    NormalizeFingerprint(current.ServerCertificateSha256 ?? string.Empty),
                    StringComparison.Ordinal));

            if (match is null)
            {
                throw new InvalidOperationException(
                    T(
                        "The paired Main Pharmacy Server was not found. Darmaltoon will not trust a different server automatically.",
                        "سرور اصلی جفت‌شده یافت نشد. دارملتون به‌صورت خودکار به سرور دیگری اعتماد نمی‌کند.",
                        "جوړ شوی اصلي سرور ونه موندل شو. درملتون بل سرور په اوتومات ډول نه مني."));
            }

            var updated = current with
            {
                ServerHost = match.HostName,
                ServerPort = match.Port,
            };

            await _configurationStore.SaveAsync(updated);
            ApplyConfiguration(updated);
            StatusMessage = T(
                $"Server rediscovered at {match.HostName}:{match.Port}.",
                $"سرور دوباره در {match.HostName}:{match.Port} یافت شد.",
                $"سرور بیا په {match.HostName}:{match.Port} وموندل شو.");
        });
    }

    private async Task ApplyModeAsync()
    {
        await BusyAsync(async () =>
        {
            var current = await _configurationStore.LoadAsync();

            if (RequestedMode == current.Mode)
            {
                StatusMessage = T("Deployment mode is unchanged.", "حالت نصب تغییری نکرد.", "د نصب حالت بدل نه شو.");
                return;
            }

            var safeOwnershipChange =
                (current.Mode == DeploymentMode.Standalone && RequestedMode == DeploymentMode.Server) ||
                (current.Mode == DeploymentMode.Server && RequestedMode == DeploymentMode.Standalone);

            if (!safeOwnershipChange)
            {
                throw new InvalidOperationException(
                    T(
                        "This mode change would change data ownership. Use the explicit migration/recovery workflow; Darmaltoon will not copy, delete or promote client data automatically.",
                        "این تغییر حالت مالکیت داده را تغییر می‌دهد. از روند مهاجرت/بازیابی استفاده کنید؛ دارملتون داده را خودکار حذف یا ارتقا نمی‌دهد.",
                        "دا بدلون د معلوماتو مالکیت بدلوي. د مهاجرت/بیا رغونې بهیر وکاروئ؛ درملتون معلومات په اوتومات ډول نه ړنګوي او نه یې لوړوي."));
            }

            var updated = current with
            {
                Mode = RequestedMode,
                ServerName = string.IsNullOrWhiteSpace(ServerName)
                    ? current.ServerName
                    : ServerName.Trim(),
                ServerPort = ServerPort,
                DiscoveryEnabled = RequestedMode == DeploymentMode.Server,
                IsConfigured = true,
            };

            await _configurationStore.SaveAsync(updated);
            ApplyConfiguration(updated);

            StatusMessage = T(
                "Mode saved safely. Restart Darmaltoon so service composition can switch modes. Existing pharmacy data was not changed.",
                "حالت با امنیت ذخیره شد. دارملتون را دوباره راه‌اندازی کنید. داده‌های موجود تغییر نکرد.",
                "حالت خوندي شو. درملتون بیا پیل کړئ. موجود معلومات بدل نه شول.");
        });
    }

    private async Task SaveConnectionAsync()
    {
        await BusyAsync(async () =>
        {
            var current = await _configurationStore.LoadAsync();

            if (current.Mode == DeploymentMode.Client)
            {
                var updated = current with
                {
                    ServerHost = ServerHost.Trim(),
                    ServerPort = ServerPort,
                };

                updated.Validate();
                await _configurationStore.SaveAsync(updated);
                ApplyConfiguration(updated);
                await TestConnectionCoreAsync();
                return;
            }

            if (current.Mode == DeploymentMode.Server)
            {
                var updated = current with
                {
                    ServerName = ServerName.Trim(),
                    ServerPort = ServerPort,
                };

                updated.Validate();
                await _configurationStore.SaveAsync(updated);
                ApplyConfiguration(updated);
                StatusMessage = T(
                    "Server settings saved. Restart the Local Server service after changing the port.",
                    "تنظیمات سرور ذخیره شد. پس از تغییر پورت سرویس را دوباره راه‌اندازی کنید.",
                    "د سرور تنظیمات خوندي شول. د پورټ له بدلون وروسته خدمت بیا پیل کړئ.");
            }
        });
    }

    private async Task LoadServerTerminalsCoreAsync(ILocalTerminalService service)
    {
        Terminals.Clear();
        foreach (var terminal in await service.ListAsync())
        {
            Terminals.Add(terminal);
        }

        SelectedTerminal = null;
    }

    private void ApplyConfiguration(NetworkConfiguration configuration)
    {
        CurrentMode = configuration.Mode;
        RequestedMode = configuration.Mode;
        ServerName = configuration.ServerName;
        ServerHost = configuration.ServerHost ?? Environment.MachineName;
        ServerPort = configuration.ServerPort;
        ServerId = configuration.ServerId ?? "—";
        CertificateFingerprint = configuration.ServerCertificateSha256 ?? "—";
        TerminalId = configuration.TerminalId ?? "—";
        NotifyCommandStates();
    }

    private void EnsureServerMode()
    {
        if (CurrentMode != DeploymentMode.Server)
        {
            throw new InvalidOperationException("This action is available only on the Main Pharmacy Server.");
        }
    }


    private async Task RunDiagnosticsAsync()
    {
        await BusyAsync(async () =>
        {
            var configuration = await _configurationStore.LoadAsync();
            var service = await _serviceController.GetStatusAsync();
            var profile = await _serviceController.GetNetworkProfileStatusAsync();
            FirewallConfigurationResult apiFirewall;
            FirewallConfigurationResult discoveryFirewall;

            if (configuration.Mode == DeploymentMode.Server)
            {
                apiFirewall = await _serviceController
                    .GetPrivateFirewallRuleStatusAsync(configuration.ServerPort);
                discoveryFirewall = await _serviceController
                    .GetPrivateDiscoveryFirewallRuleStatusAsync(configuration.DiscoveryPort);
            }
            else
            {
                apiFirewall = new FirewallConfigurationResult(
                    true,
                    "Not applicable; this computer is not the Main Pharmacy Server.");
                discoveryFirewall = apiFirewall;
            }

            var paths = _services.GetRequiredService<IApplicationPaths>();
            paths.EnsureCreated();

            var databaseStatus = configuration.Mode == DeploymentMode.Client
                ? "Authoritative database: Main Pharmacy Server (no local authoritative client database)."
                : File.Exists(paths.DatabasePath)
                    ? $"Authoritative database: available locally ({new FileInfo(paths.DatabasePath).Length:N0} bytes)."
                    : "Authoritative database: not created yet.";

            string diskStatus;
            try
            {
                var root = Path.GetPathRoot(paths.RootDirectory);
                var drive = !string.IsNullOrWhiteSpace(root)
                    ? new DriveInfo(root)
                    : null;

                diskStatus = drive is null || !drive.IsReady
                    ? "Disk space: unavailable."
                    : $"Disk free: {drive.AvailableFreeSpace / 1024d / 1024d / 1024d:N1} GB.";
            }
            catch
            {
                diskStatus = "Disk space: unavailable.";
            }

            string licenseStatus;
            if (configuration.Mode == DeploymentMode.Client)
            {
                licenseStatus =
                    "License: inherited from the paired Main Pharmacy Server; this client does not create a separate trial.";
            }
            else
            {
                var licensing = _services.GetService<ILicenseService>();
                if (licensing is null)
                {
                    licenseStatus = "License: service unavailable.";
                }
                else
                {
                    try
                    {
                        var entitlement = await licensing.GetCachedEntitlementAsync();
                        licenseStatus = entitlement is null
                            ? "License: no valid cached server/standalone entitlement."
                            : $"License: tenant {entitlement.TenantId}; offline lease expires {entitlement.ExpiresAt:yyyy-MM-dd HH:mm zzz}.";
                    }
                    catch (Exception exception)
                    {
                        licenseStatus = $"License: verification error — {exception.Message}";
                    }
                }
            }

            if (configuration.Mode == DeploymentMode.Client)
            {
                await TestConnectionCoreAsync();
            }

            NetworkProfileStatus = profile.Message;
            FirewallStatus = configuration.Mode == DeploymentMode.Server
                ? string.Join(
                    Environment.NewLine,
                    apiFirewall.Message,
                    discoveryFirewall.Message)
                : apiFirewall.Message;

            DiagnosticsReport = string.Join(
                Environment.NewLine,
                $"Deployment Mode: {configuration.Mode}",
                $"Server Service: {service.Message}",
                $"LAN Connection: {ConnectionStatus}",
                $"Network Profile: {profile.Message}",
                $"API Firewall: {apiFirewall.Message}",
                $"Discovery Firewall: {discoveryFirewall.Message}",
                $"Address/Port: {(configuration.ServerHost ?? Environment.MachineName)}:{configuration.ServerPort}",
                databaseStatus,
                diskStatus,
                licenseStatus,
                "Cloud sync and LAN connectivity are independent. A cloud outage must not be treated as a LAN outage.");

            StatusMessage = T(
                "Diagnostics completed.",
                "عیب‌یابی تکمیل شد.",
                "تشخیص بشپړ شو.");
        });
    }

    private async Task BusyAsync(Func<Task> action)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        NotifyCommandStates();

        try
        {
            await action();
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
        }
        finally
        {
            IsBusy = false;
            NotifyCommandStates();
        }
    }

    private void NotifyCommandStates()
    {
        RefreshCommand.NotifyCanExecuteChanged();
        CreatePairingCodeCommand.NotifyCanExecuteChanged();
        RevokeTerminalCommand.NotifyCanExecuteChanged();
        RenameTerminalCommand.NotifyCanExecuteChanged();
        StartServerCommand.NotifyCanExecuteChanged();
        ConfigureFirewallCommand.NotifyCanExecuteChanged();
        TestConnectionCommand.NotifyCanExecuteChanged();
        RediscoverCommand.NotifyCanExecuteChanged();
        ApplyModeCommand.NotifyCanExecuteChanged();
        SaveConnectionCommand.NotifyCanExecuteChanged();
        RunDiagnosticsCommand.NotifyCanExecuteChanged();
    }

    partial void OnCurrentModeChanged(DeploymentMode value) => NotifyCommandStates();
    partial void OnSelectedTerminalChanged(RegisteredTerminal? value)
    {
        RenameTerminalTo = value?.Name ?? string.Empty;
        NotifyCommandStates();
    }

    private static string NormalizeFingerprint(string value) =>
        value.Replace(":", string.Empty)
            .Replace(" ", string.Empty)
            .Trim()
            .ToUpperInvariant();

    private string T(string english, string dari, string pashto) =>
        _language.Code switch
        {
            "fa" => dari,
            "ps" => pashto,
            _ => english,
        };

    private void RaiseLocalizedProperties()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(DeploymentModeLabel));
        OnPropertyChanged(nameof(ServerNameLabel));
        OnPropertyChanged(nameof(ServerAddressLabel));
        OnPropertyChanged(nameof(PortLabel));
        OnPropertyChanged(nameof(ServerIdLabel));
        OnPropertyChanged(nameof(CertificateLabel));
        OnPropertyChanged(nameof(ServiceLabel));
        OnPropertyChanged(nameof(ConnectionLabel));
        OnPropertyChanged(nameof(TerminalsLabel));
        OnPropertyChanged(nameof(PairingLabel));
        OnPropertyChanged(nameof(CreatePairingLabel));
        OnPropertyChanged(nameof(StartServerLabel));
        OnPropertyChanged(nameof(FirewallLabel));
        OnPropertyChanged(nameof(TestConnectionLabel));
        OnPropertyChanged(nameof(RediscoverLabel));
        OnPropertyChanged(nameof(ApplyModeLabel));
        OnPropertyChanged(nameof(SaveConnectionLabel));
        OnPropertyChanged(nameof(RenameLabel));
        OnPropertyChanged(nameof(RevokeLabel));
        OnPropertyChanged(nameof(RefreshLabel));
        OnPropertyChanged(nameof(RunDiagnosticsLabel));
        OnPropertyChanged(nameof(NetworkProfileLabel));
        OnPropertyChanged(nameof(FirewallStatusLabel));
        OnPropertyChanged(nameof(ProfileTitle));
        OnPropertyChanged(nameof(ProfileSubtitle));
        OnPropertyChanged(nameof(FirstNameLabel));
        OnPropertyChanged(nameof(LastNameLabel));
        OnPropertyChanged(nameof(UploadPhotoLabel));
        OnPropertyChanged(nameof(SaveProfileLabel));
        OnPropertyChanged(nameof(ReceiptTitle));
        OnPropertyChanged(nameof(ReceiptSubtitle));
        OnPropertyChanged(nameof(ReceiptShowBatchLabel));
        OnPropertyChanged(nameof(ReceiptShowCashierLabel));
        OnPropertyChanged(nameof(ReceiptShowCustomerLabel));
        OnPropertyChanged(nameof(ReceiptShowPaymentsLabel));
        OnPropertyChanged(nameof(ReceiptShowFooterLabel));
        OnPropertyChanged(nameof(ReceiptFooterTextLabel));
        OnPropertyChanged(nameof(SaveReceiptLabel));
    }
}
