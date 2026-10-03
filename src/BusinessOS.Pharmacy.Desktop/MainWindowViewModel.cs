using System.Collections.ObjectModel;
using System.Windows;
using BusinessOS.Pharmacy.Application.Abstractions.Authentication;
using BusinessOS.Pharmacy.Application.Abstractions.Networking;
using BusinessOS.Pharmacy.Application.Abstractions.Time;
using BusinessOS.Pharmacy.Desktop.Authentication;
using BusinessOS.Pharmacy.Desktop.Administration;
using BusinessOS.Pharmacy.Desktop.Barcode;
using BusinessOS.Pharmacy.Desktop.Customers;
using BusinessOS.Pharmacy.Desktop.Backup;
using BusinessOS.Pharmacy.Desktop.Dashboard;
using BusinessOS.Pharmacy.Desktop.DailyClosing;
using BusinessOS.Pharmacy.Desktop.Localization;
using BusinessOS.Pharmacy.Desktop.Inventory;
using BusinessOS.Pharmacy.Desktop.Expenses;
using BusinessOS.Pharmacy.Desktop.Medicines;
using BusinessOS.Pharmacy.Desktop.Navigation;
using BusinessOS.Pharmacy.Desktop.Notifications;
using BusinessOS.Pharmacy.Desktop.Purchasing;
using BusinessOS.Pharmacy.Desktop.Pos;
using BusinessOS.Pharmacy.Desktop.Returns;
using BusinessOS.Pharmacy.Desktop.Reports;
using BusinessOS.Pharmacy.Desktop.Networking;
using BusinessOS.Pharmacy.Desktop.Updates;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;

namespace BusinessOS.Pharmacy.Desktop;

public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly IClock _clock;
    private readonly IUserSessionService _sessions;
    private readonly NetworkConfiguration _networkConfiguration;
    private readonly IPermissionAuthorizer _permissions;
    private readonly ILocalServerConnectionMonitor? _connectionMonitor;
    private readonly NotificationService _notifications;
    private readonly Profile.UserProfileStore _profileStore;
    private Profile.UserProfile? _profile;

    [ObservableProperty]
    private UiLanguage selectedLanguage = UiLanguageCatalog.All[0];

    [ObservableProperty]
    private FlowDirection layoutDirection = FlowDirection.LeftToRight;

    [ObservableProperty]
    private object currentPage;

    [ObservableProperty]
    private string currentSectionKey = "dashboard";

    [ObservableProperty]
    private string lanStatusText = string.Empty;

    [ObservableProperty]
    private string globalSearchText = string.Empty;

    [ObservableProperty]
    private bool sidebarCollapsed;

    [ObservableProperty]
    private bool isGlassTheme;

    public bool IsClassicTheme => !IsGlassTheme;

    partial void OnIsGlassThemeChanged(bool value) =>
        OnPropertyChanged(nameof(IsClassicTheme));

    public MainWindowViewModel(
        IClock clock,
        IUserSessionService sessions,
        IPermissionAuthorizer permissions,
        AccessManagementViewModel accessManagement,
        DashboardViewModel dashboard,
        CustomersViewModel customers,
        MedicinesViewModel medicines,
        InventoryViewModel inventory,
        PurchasingViewModel purchasing,
        PosViewModel pos,
        ReturnsViewModel returns,
        ExpensesViewModel expenses,
        DailyClosingViewModel dailyClosing,
        ReportsViewModel reports,
        BackupRestoreViewModel backupRestore,
        UpdateViewModel updates,
        NetworkSettingsViewModel networkSettings,
        PasswordChangeViewModel passwordChange,
        BarcodePrintViewModel barcodePrint,
        NetworkConfiguration networkConfiguration,
        NotificationService notifications,
        Profile.UserProfileStore profileStore,
        IServiceProvider services)
    {
        _clock = clock;
        _sessions = sessions;
        _permissions = permissions;
        _networkConfiguration = networkConfiguration;
        _notifications = notifications;
        _profileStore = profileStore;
        _connectionMonitor = services.GetService<ILocalServerConnectionMonitor>();
        _notifications.NotificationRaised += OnNotificationRaised;
        _profileStore.ProfileChanged += OnProfileChanged;

        IsGlassTheme = Appearance.ThemeManager.Current == Appearance.AppearanceTheme.Glass;
        Appearance.ThemeManager.ThemeChanged += OnThemeChanged;
        _profile = _profileStore.Load();
        AccessManagement = accessManagement;
        Dashboard = dashboard;
        Customers = customers;
        Medicines = medicines;
        Inventory = inventory;
        Purchasing = purchasing;
        Pos = pos;
        Returns = returns;
        Expenses = expenses;
        DailyClosing = dailyClosing;
        Reports = reports;
        BackupRestore = backupRestore;
        Updates = updates;
        NetworkSettings = networkSettings;
        PasswordChange = passwordChange;
        BarcodePrint = barcodePrint;
        currentPage = Dashboard;

        AccessManagement.SetLanguage(SelectedLanguage);
        AccessManagement.SetLanguage(SelectedLanguage);
        Dashboard.SetLanguage(SelectedLanguage);
        Customers.SetLanguage(SelectedLanguage);
        Medicines.SetLanguage(SelectedLanguage);
        Inventory.SetLanguage(SelectedLanguage);
        Purchasing.SetLanguage(SelectedLanguage);
        Pos.SetLanguage(SelectedLanguage);
        Returns.SetLanguage(SelectedLanguage);
        Expenses.SetLanguage(SelectedLanguage);
        DailyClosing.SetLanguage(SelectedLanguage);
        Reports.SetLanguage(SelectedLanguage);
        NetworkSettings.SetLanguage(SelectedLanguage);
        PasswordChange.SetLanguage(SelectedLanguage);
        BarcodePrint.SetLanguage(SelectedLanguage);
        Dashboard.NavigationRequested += OnDashboardNavigationRequested;
        Pos.ReturnSaleRequested += OnPosReturnSaleRequested;

        if (_connectionMonitor is not null)
        {
            _connectionMonitor.StatusChanged += OnLanStatusChanged;
        }

        RefreshLanStatusText();

        LogoutCommand = new AsyncRelayCommand(LogoutAsync);
        NavigateCommand = new AsyncRelayCommand<string>(NavigateAsync);
        GlobalSearchCommand = new AsyncRelayCommand(GlobalSearchAsync);
        ToggleSidebarCommand = new RelayCommand(ToggleSidebar);
        ToggleFullScreenCommand = new RelayCommand(ToggleFullScreen);
        RefreshNavigation();
    }

    public event EventHandler? LogoutRequested;

    public IAsyncRelayCommand LogoutCommand { get; }
    public IAsyncRelayCommand<string> NavigateCommand { get; }
    public IAsyncRelayCommand GlobalSearchCommand { get; }
    public IRelayCommand ToggleSidebarCommand { get; }
    public IRelayCommand ToggleFullScreenCommand { get; }
    public bool IsPosMode => string.Equals(CurrentSectionKey, "pos", StringComparison.OrdinalIgnoreCase);
    public double SidebarWidth => IsPosMode ? 0d : SidebarCollapsed ? 70d : 224d;
    public AccessManagementViewModel AccessManagement { get; }
    public DashboardViewModel Dashboard { get; }
    public CustomersViewModel Customers { get; }
    public MedicinesViewModel Medicines { get; }
    public InventoryViewModel Inventory { get; }
    public PurchasingViewModel Purchasing { get; }
    public PosViewModel Pos { get; }
    public ReturnsViewModel Returns { get; }
    public ExpensesViewModel Expenses { get; }
    public DailyClosingViewModel DailyClosing { get; }
    public ReportsViewModel Reports { get; }
    public BackupRestoreViewModel BackupRestore { get; }
    public UpdateViewModel Updates { get; }
    public NetworkSettingsViewModel NetworkSettings { get; }
    public PasswordChangeViewModel PasswordChange { get; }
    public BarcodePrintViewModel BarcodePrint { get; }

    public string ApplicationName => "Darmaltoon";
    public string ProductName => "Darmaltoon";
    public string ParentBrand => "BusinessOS";
    public string PageTitle => CurrentSectionKey switch
    {
        "pos" => Translate("Point of Sale", "فروش", "خرڅلاو"),
        "returns" => Translate("Sale Returns", "برگشت فروش", "د خرڅلاو بېرته ستنول"),
        "expenses" => Translate("Expenses & Accounting", "مصارف و حسابداری", "لګښتونه او حسابداري"),
        "closing" => Translate("Daily Closing", "بستن حساب روزانه", "ورځنی حساب"),
        "reports" => Translate("Reports", "گزارش‌ها", "راپورونه"),
        "backup" => Translate("Backup & Restore", "پشتیبان‌گیری و بازیابی", "بیک اپ او بېرته راګرځول"),
        "updates" => Translate("Application Updates", "به‌روزرسانی برنامه", "د اپلیکیشن تازه کول"),
        "network" => Translate("Network & Terminals", "شبکه و ترمینال‌ها", "شبکه او ټرمینلونه"),
        "medicines" => Translate("Medicines", "ادویه", "درمل"),
        "inventory" => Translate("Inventory", "موجودی", "زېرمه"),
        "batches" => Translate("Batches", "بچ‌ها", "بېچونه"),
        "purchases" => Translate("Supplier", "تأمین‌کننده", "عرضه کوونکی"),
        "suppliers" => Translate("Suppliers", "تأمین‌کنندگان", "عرضه کوونکي"),
        "customers" => Translate("Customers", "مشتریان", "پېرودونکي"),
        "users" => Translate("Users", "کاربران", "کارنان"),
        "roles" => Translate("Roles & Permissions", "نقش‌ها و مجوزها", "رولونه او اجازې"),
        "settings" => Translate("Settings", "تنظیمات", "امستنې"),
        _ => Translate("Dashboard", "داشبورد", "ډشبورډ"),
    };
    public string PageSubtitle => CurrentSectionKey switch
    {
        "reports" => Translate(
            "Sales, profit, purchasing, stock and movement reporting",
            "گزارش فروش، سود، خرید، موجودی و گردش کالا",
            "د خرڅلاو، ګټې، پېرود، زېرمه او حرکتونو راپورونه"),
        "backup" => Translate(
            "Verified backup and safe restore of the authoritative pharmacy database",
            "پشتیبان‌گیری تأییدشده و بازیابی امن پایگاه داده اصلی دواخانه",
            "د اصلي درملتون ډیټابیس تایید شوی بیک اپ او خوندي بېرته راګرځول"),
        "updates" => Translate(
            "Signed releases with checksum verification and deployment-mode compatibility checks",
            "نسخه‌های امضاشده با بررسی صحت و سازگاری حالت نصب",
            "لاسلیک شوي نسخې د checksum او نصب حالت د سازګارۍ له تایید سره"),
        "network" => Translate(
            "LAN server, client terminals and connection diagnostics",
            "سرور شبکه، ترمینال‌های مشتری و عیب‌یابی اتصال",
            "د LAN سرور، مراجع ترمینلونه او د نښلونې تشخیص"),
        "closing" => Translate(
            "Cashier shifts, cash reconciliation and auditable day finalization",
            "شیفت صندوق، تطبیق نقد و نهایی‌سازی قابل حسابرسی",
            "د کشر شفټونه، نغدي تصفیه او د پلټنې وړ ورځنی تړل"),
        "expenses" => Translate(
            "Balanced expense journals with auditable reversals",
            "ثبت متوازن مصارف با معکوس‌سازی قابل حسابرسی",
            "متوازن لګښت ژورنالونه او د پلټنې وړ معکوسونه"),
        "returns" => Translate(
            "Allocation-aware returns, refunds and safe stock restocking",
            "برگشت مبتنی بر تخصیص، بازپرداخت و بازگردانی امن موجودی",
            "د تخصیص پر بنسټ ستنېدل، بېرته تادیه او خوندي زېرمه"),
        "pos" => Translate(
            "Fast local sales with FEFO stock allocation and mixed payments",
            "فروش سریع محلی با تخصیص FEFO و پرداخت ترکیبی",
            "چټک محلي خرڅلاو د FEFO زېرمه او ګډو تادیاتو سره"),
        "medicines" => Translate(
            "Medicine master data, categories and CSV registration",
            "اطلاعات اصلی ادویه، دسته‌بندی و ثبت CSV",
            "د درملو اصلي معلومات، کټګورۍ او CSV ثبت"),
        "inventory" => Translate(
            "Batch-aware stock, expiry and movement control",
            "کنترل موجودی، بچ، انقضا و گردش کالا",
            "د بېچ، تاریخ تېر او زېرمتون حرکتونو کنټرول"),
        "batches" => Translate(
            "Medicine batches, quantities and expiry control",
            "بچ‌های دوا، مقدار و کنترل انقضا",
            "د درملو بېچونه، مقدار او د تاریخ تېر کنټرول"),
        "purchases" => Translate(
            "Supplier records, purchase orders, receiving, invoices and payments",
            "اطلاعات تأمین‌کننده، سفارش خرید، دریافت، فاکتور و پرداخت",
            "د عرضه کوونکي معلومات، پېرود امرونه، ترلاسه کول، بلونه او تادیات"),
        "suppliers" => Translate(
            "Supplier records and purchasing relationships",
            "اطلاعات تأمین‌کنندگان و روابط خرید",
            "د عرضه کوونکو معلومات او د پېرود اړیکې"),
        "settings" => Translate(
            "Application, deployment and local network settings",
            "تنظیمات برنامه، حالت نصب و شبکه محلی",
            "د اپلېکېشن، نصب حالت او محلي شبکې امستنې"),
        "customers" => Translate(
            "Customer records and per-sale credit limits",
            "اطلاعات مشتری و سقف اعتبار هر فروش",
            "د پېرودونکو معلومات او د هر خرڅلاو د پور حد"),
        "users" => Translate(
            "Manage pharmacy staff accounts and role assignments",
            "مدیریت حساب کارمندان دواخانه و نقش‌های آنان",
            "د درملتون د کارکوونکو حسابونه او رولونه اداره کړئ"),
        "roles" => Translate(
            "Manage roles and permissions for pharmacy staff",
            "مدیریت نقش‌ها و مجوزهای کارمندان دواخانه",
            "د درملتون د کارکوونکو رولونه او اجازې اداره کړئ"),
        _ => Translate(
            "Local-first pharmacy operations",
            "عملیات محلی دواخانه",
            "د درملتون محلي عملیات"),
    };
    public string OnlineText => Translate("Online", "آنلاین", "آنلاین");
    public string LicenseText => Translate("Licensed", "مجوز فعال", "جواز فعال");
    public string LastVerifiedText => $"{Translate("Ready", "آماده", "چمتو")} • {_clock.UtcNow:yyyy-MM-dd HH:mm} UTC";
    public string UserDisplayName =>
        !string.IsNullOrWhiteSpace(_profile?.DisplayName)
            ? _profile!.DisplayName
            : _sessions.Current?.Name ?? Translate("No user", "بدون کاربر", "کارن نشته");

    public string ProfileImageSource =>
        _profile?.HasImage == true ? _profile.ImagePath! : string.Empty;

    public bool HasProfileImage => _profile?.HasImage == true;

    private void OnThemeChanged(Appearance.AppearanceTheme theme) =>
        IsGlassTheme = theme == Appearance.AppearanceTheme.Glass;

    private void OnProfileChanged(Profile.UserProfile profile)
    {
        _profile = profile;
        OnPropertyChanged(nameof(UserDisplayName));
        OnPropertyChanged(nameof(ProfileImageSource));
        OnPropertyChanged(nameof(HasProfileImage));
    }
    public string UserRoleText => _sessions.Current is null
        ? string.Empty
        : string.Join(", ", _sessions.Current.Roles);
    public int PermissionCount => _sessions.Current?.Permissions.Count ?? 0;
    public ReadOnlyCollection<UiLanguage> Languages => UiLanguageCatalog.All;
    public ObservableCollection<NavigationItemViewModel> NavigationItems { get; } = new();
    public ObservableCollection<NotificationViewModel> Notifications { get; } = new();

    private void OnNotificationRaised(AppNotification notification)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return;
        }

        _ = dispatcher.InvokeAsync(async () =>
        {
            var item = new NotificationViewModel(notification, RemoveNotification);
            Notifications.Add(item);

            while (Notifications.Count > 4)
            {
                Notifications.RemoveAt(0);
            }

            await Task.Delay(TimeSpan.FromSeconds(9));
            RemoveNotification(item);
        });
    }

    private void RemoveNotification(NotificationViewModel item)
    {
        if (Notifications.Contains(item))
        {
            Notifications.Remove(item);
        }
    }

    public void ApplyCurrentUser()
    {
        OnPropertyChanged(nameof(UserDisplayName));
        OnPropertyChanged(nameof(UserRoleText));
        OnPropertyChanged(nameof(PermissionCount));

        Dashboard.SetLanguage(SelectedLanguage);
        Customers.SetLanguage(SelectedLanguage);
        Medicines.SetLanguage(SelectedLanguage);
        Inventory.SetLanguage(SelectedLanguage);
        Purchasing.SetLanguage(SelectedLanguage);
        Pos.SetLanguage(SelectedLanguage);
        Returns.SetLanguage(SelectedLanguage);
        Expenses.SetLanguage(SelectedLanguage);
        DailyClosing.SetLanguage(SelectedLanguage);
        Reports.SetLanguage(SelectedLanguage);
        BackupRestore.SetLanguage(SelectedLanguage);
        Updates.SetLanguage(SelectedLanguage);
        NetworkSettings.SetLanguage(SelectedLanguage);
        PasswordChange.SetLanguage(SelectedLanguage);
        BarcodePrint.SetLanguage(SelectedLanguage);
        CurrentSectionKey = "dashboard";
        CurrentPage = Dashboard;
        RefreshNavigation();
    }

    partial void OnSelectedLanguageChanged(UiLanguage value)
    {
        LayoutDirection = value.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        AccessManagement.SetLanguage(value);
        Dashboard.SetLanguage(value);
        Customers.SetLanguage(value);
        Medicines.SetLanguage(value);
        Inventory.SetLanguage(value);
        Purchasing.SetLanguage(value);
        Pos.SetLanguage(value);
        Returns.SetLanguage(value);
        Expenses.SetLanguage(value);
        DailyClosing.SetLanguage(value);
        Reports.SetLanguage(value);
        BackupRestore.SetLanguage(value);
        Updates.SetLanguage(value);
        NetworkSettings.SetLanguage(value);
        PasswordChange.SetLanguage(value);
        BarcodePrint.SetLanguage(value);
        RefreshLanStatusText();
        RefreshNavigation();
        RaisePageText();
        OnPropertyChanged(nameof(OnlineText));
        OnPropertyChanged(nameof(LicenseText));
        OnPropertyChanged(nameof(LastVerifiedText));
        OnPropertyChanged(nameof(UserDisplayName));
    }

    partial void OnCurrentSectionKeyChanged(string value)
    {
        OnPropertyChanged(nameof(IsPosMode));
        OnPropertyChanged(nameof(SidebarWidth));
        RaisePageText();
        RefreshNavigation();
    }

    private void RefreshNavigation()
    {
        NavigationItems.Clear();

        var operations = Translate("Operations", "عملیات", "عملیات");
        var stock = Translate("Stock", "موجودی", "زېرمه");
        var purchasing = Translate("Purchasing", "خریداری", "پېرود");
        var finance = Translate("Finance", "مالی", "مالي");
        var administration = Translate("Administration", "مدیریت", "اداره");
        var system = Translate("System", "سیستم", "سیسټم");

        AddIfAllowed("dashboard.view", "dashboard", Translate("Dashboard", "داشبورد", "ډشبورډ"), operations);
        AddIfAllowed("pos.sell", "pos", Translate("POS (New Sale)", "فروش جدید", "نوی خرڅلاو"), operations);
        AddIfAllowed("medicines.manage", "medicines", Translate("Medicines", "ادویه", "درمل"), stock);

        if (_permissions.HasPermission("inventory.manage") ||
            _permissions.HasPermission("inventory.status"))
        {
            AddNavigationItem("inventory", Translate("Inventory", "موجودی", "زېرمه"), stock);
            AddNavigationItem("batches", Translate("Batches", "بچ‌ها", "بېچونه"), stock);
            AddNavigationItem("barcode", Translate("Barcode Printing", "چاپ بارکد", "بارکوډ چاپ"), stock);
        }

        AddIfAllowed("customers.manage", "customers", Translate("Customers", "مشتریان", "پېرودونکي"), operations);
        AddIfAllowed("purchases.manage", "purchases", Translate("Supplier", "تأمین‌کننده", "عرضه کوونکی"), purchasing);
        AddIfAllowed("accounting.manage", "expenses", Translate("Expenses", "مصارف", "لګښتونه"), finance);
        AddIfAllowed("daily_closing.perform", "closing", Translate("Daily Closing", "بستن حساب روزانه", "ورځنی حساب"), finance);
        AddIfAllowed("reports.view", "reports", Translate("Reports", "گزارش‌ها", "راپورونه"), finance);
        AddIfAllowed("returns.manage", "returns", Translate("Returns", "برگشت", "بېرته ستنول"), purchasing);
        AddIfAllowed("users.manage", "users", Translate("Users", "کاربران", "کارنان"), administration);
        AddIfAllowed("roles.manage", "roles", Translate("Roles & Permissions", "نقش‌ها و مجوزها", "رولونه او اجازې"), administration);

        if (_networkConfiguration.Mode != DeploymentMode.Client &&
            _permissions.HasPermission("settings.manage"))
        {
            AddNavigationItem("backup", Translate("Backup", "پشتیبان‌گیری", "بیک اپ"), administration);
        }

        AddIfAllowed("settings.manage", "updates", Translate("Sync & Updates", "همگام‌سازی و به‌روزرسانی", "همغږي او تازه کول"), system);

        if (_networkConfiguration.Mode == DeploymentMode.Server &&
            (_permissions.HasPermission("users.manage") ||
             _permissions.HasPermission("settings.manage")))
        {
            AddNavigationItem(
                "network",
                Translate("Network & Terminals", "شبکه و ترمینال‌ها", "شبکه او ټرمینلونه"),
                system);
        }

        AddIfAllowed("settings.manage", "settings", Translate("Settings", "تنظیمات", "امستنې"), administration);
    }

    private void AddIfAllowed(string permission, string key, string label, string group)
    {
        if (_permissions.HasPermission(permission))
        {
            AddNavigationItem(key, label, group);
        }
    }

    private void AddNavigationItem(string key, string label, string group)
    {
        NavigationItems.Add(new NavigationItemViewModel(
            key,
            label,
            group,
            string.Equals(key, CurrentSectionKey, StringComparison.OrdinalIgnoreCase)));
    }

    partial void OnSidebarCollapsedChanged(bool value)
    {
        OnPropertyChanged(nameof(SidebarWidth));
    }

    private void ToggleSidebar() => SidebarCollapsed = !SidebarCollapsed;

    private WindowState _windowStateBeforeFullScreen = WindowState.Maximized;
    private WindowStyle _windowStyleBeforeFullScreen = WindowStyle.SingleBorderWindow;
    private ResizeMode _resizeModeBeforeFullScreen = ResizeMode.CanResize;

    public bool IsFullScreen { get; private set; }

    private void ToggleFullScreen()
    {
        if (System.Windows.Application.Current?.MainWindow is not { } window)
        {
            return;
        }

        if (IsFullScreen)
        {
            window.WindowStyle = _windowStyleBeforeFullScreen;
            window.ResizeMode = _resizeModeBeforeFullScreen;
            window.WindowState = _windowStateBeforeFullScreen;
            IsFullScreen = false;
        }
        else
        {
            _windowStateBeforeFullScreen = window.WindowState;
            _windowStyleBeforeFullScreen = window.WindowStyle;
            _resizeModeBeforeFullScreen = window.ResizeMode;
            window.WindowState = WindowState.Normal;
            window.WindowStyle = WindowStyle.None;
            window.ResizeMode = ResizeMode.NoResize;
            window.WindowState = WindowState.Maximized;
            IsFullScreen = true;
        }

        OnPropertyChanged(nameof(IsFullScreen));
    }

    private async Task NavigateAsync(string? key)
    {
        switch (key)
        {
            case "dashboard" when _permissions.HasPermission("dashboard.view"):
                CurrentSectionKey = "dashboard";
                CurrentPage = Dashboard;
                await Dashboard.LoadAsync();
                break;

            case "pos" when _permissions.HasPermission("pos.sell"):
                CurrentSectionKey = "pos";
                CurrentPage = Pos;
                await Pos.LoadAsync();
                break;

            case "returns" when _permissions.HasPermission("returns.manage"):
                CurrentSectionKey = "returns";
                CurrentPage = Returns;
                await Returns.LoadAsync();
                break;

            case "expenses" when _permissions.HasPermission("accounting.manage"):
                CurrentSectionKey = "expenses";
                CurrentPage = Expenses;
                await Expenses.LoadAsync();
                break;

            case "reports" when _permissions.HasPermission("reports.view"):
                CurrentSectionKey = "reports";
                CurrentPage = Reports;
                await Reports.LoadAsync();
                break;

            case "backup" when _networkConfiguration.Mode != DeploymentMode.Client && _permissions.HasPermission("settings.manage"):
                CurrentSectionKey = "backup";
                CurrentPage = BackupRestore;
                await BackupRestore.LoadAsync();
                break;

            case "updates" when _permissions.HasPermission("settings.manage"):
                CurrentSectionKey = "updates";
                CurrentPage = Updates;
                await Updates.LoadAsync();
                break;

            case "network" when
                _networkConfiguration.Mode == DeploymentMode.Server &&
                (_permissions.HasPermission("users.manage") ||
                 _permissions.HasPermission("settings.manage")):
                CurrentSectionKey = "network";
                CurrentPage = NetworkSettings;
                await NetworkSettings.LoadAsync();
                RefreshLanStatusText();
                break;

            case "sync" when _permissions.HasPermission("dashboard.view"):
                await Dashboard.SyncNowAsync();
                break;

            case "closing" when _permissions.HasPermission("daily_closing.perform"):
                CurrentSectionKey = "closing";
                CurrentPage = DailyClosing;
                await DailyClosing.LoadAsync();
                break;

            case "medicines" when _permissions.HasPermission("medicines.manage"):
                CurrentSectionKey = "medicines";
                CurrentPage = Medicines;
                await Medicines.LoadAsync();
                break;

            case "inventory" when
                _permissions.HasPermission("inventory.manage") ||
                _permissions.HasPermission("inventory.status"):
                Inventory.SetMode(false);
                CurrentSectionKey = "inventory";
                CurrentPage = Inventory;
                await Inventory.LoadAsync();
                break;

            case "batches" when
                _permissions.HasPermission("inventory.manage") ||
                _permissions.HasPermission("inventory.status"):
                Inventory.SetMode(true);
                CurrentSectionKey = "batches";
                CurrentPage = Inventory;
                await Inventory.LoadAsync();
                break;

            case "barcode" when _permissions.HasPermission("medicines.manage"):
                CurrentSectionKey = "barcode";
                CurrentPage = BarcodePrint;
                await BarcodePrint.LoadAsync();
                await BarcodePrint.LoadPharmacyNameAsync();
                break;

            case "purchases" when _permissions.HasPermission("purchases.manage"):
                CurrentSectionKey = "purchases";
                CurrentPage = Purchasing;
                await Purchasing.LoadAsync();
                break;

            case "suppliers" when _permissions.HasPermission("purchases.manage"):
                CurrentSectionKey = "suppliers";
                CurrentPage = Purchasing;
                await Purchasing.LoadAsync();
                break;

            case "users" when _permissions.HasPermission("users.manage"):
                AccessManagement.SetSection("users");
                CurrentSectionKey = "users";
                CurrentPage = AccessManagement;
                await AccessManagement.LoadAsync();
                break;

            case "roles" when _permissions.HasPermission("roles.manage"):
                AccessManagement.SetSection("roles");
                CurrentSectionKey = "roles";
                CurrentPage = AccessManagement;
                await AccessManagement.LoadAsync();
                break;

            case "settings" when _permissions.HasPermission("settings.manage"):
                CurrentSectionKey = "settings";
                CurrentPage = NetworkSettings;
                await NetworkSettings.LoadAsync();
                RefreshLanStatusText();
                break;

            case "customers" when _permissions.HasPermission("customers.manage"):
                CurrentSectionKey = "customers";
                CurrentPage = Customers;
                await Customers.LoadAsync();
                break;

        }
    }

    private async void OnDashboardNavigationRequested(string key)
    {
        await NavigateAsync(key);
    }

    private async void OnPosReturnSaleRequested(string saleId)
    {
        if (!_permissions.HasPermission("returns.manage"))
            return;

        CurrentSectionKey = "returns";
        CurrentPage = Returns;
        await Returns.LoadAsync();
        await Returns.LoadSaleAsync(saleId);
    }

    private async Task GlobalSearchAsync()
    {
        var term = GlobalSearchText.Trim();
        if (term.Length == 0)
        {
            return;
        }

        if (_permissions.HasPermission("medicines.manage"))
        {
            Medicines.SearchText = term;
            CurrentSectionKey = "medicines";
            CurrentPage = Medicines;
            await Medicines.LoadAsync();

            if (Medicines.Medicines.Count > 0)
            {
                return;
            }
        }

        if (_permissions.HasPermission("customers.manage"))
        {
            Customers.SearchText = term;
            CurrentSectionKey = "customers";
            CurrentPage = Customers;
            await Customers.LoadAsync();

            if (Customers.Customers.Count > 0)
            {
                return;
            }
        }

        if (_permissions.HasPermission("purchases.manage"))
        {
            Purchasing.SupplierSearchText = term;
            CurrentSectionKey = "purchases";
            CurrentPage = Purchasing;
            await Purchasing.LoadAsync();
        }
    }

    private async Task LogoutAsync()
    {
        await _sessions.LogoutAsync();
        ApplyCurrentUser();
        LogoutRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnLanStatusChanged(
        object? sender,
        LocalServerConnectionStatus status)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;

        if (dispatcher is null || dispatcher.CheckAccess())
        {
            LanStatusText = FormatLanStatus(status);
            return;
        }

        _ = dispatcher.InvokeAsync(() =>
            LanStatusText = FormatLanStatus(status));
    }

    private void RefreshLanStatusText()
    {
        LanStatusText = _networkConfiguration.Mode switch
        {
            DeploymentMode.Client when _connectionMonitor is not null =>
                FormatLanStatus(_connectionMonitor.Current),
            DeploymentMode.Server =>
                Translate("Main Server", "سرور اصلی", "اصلي سرور"),
            _ =>
                Translate("Standalone", "مستقل", "خپلواک"),
        };
    }

    private string FormatLanStatus(LocalServerConnectionStatus status)
    {
        if (status.IsConnected)
        {
            var latency = status.Latency?.TotalMilliseconds;
            return latency is null
                ? Translate("Main Server connected", "سرور اصلی متصل", "اصلي سرور وصل")
                : Translate(
                    $"Main Server · {latency:0} ms",
                    $"سرور اصلی · {latency:0} ms",
                    $"اصلي سرور · {latency:0} ms");
        }

        return Translate(
            "Server reconnecting…",
            "اتصال مجدد به سرور…",
            "سرور ته بیا نښلول…");
    }

    private void RaisePageText()
    {
        OnPropertyChanged(nameof(PageTitle));
        OnPropertyChanged(nameof(PageSubtitle));
    }

    private string Translate(string english, string dari, string pashto) =>
        SelectedLanguage.Code switch
        {
            "fa" => dari,
            "ps" => pashto,
            _ => english
        };
}
