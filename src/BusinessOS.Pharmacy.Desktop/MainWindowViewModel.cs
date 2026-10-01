using System.Collections.ObjectModel;
using System.Windows;
using BusinessOS.Pharmacy.Application.Abstractions.Authentication;
using BusinessOS.Pharmacy.Application.Abstractions.Networking;
using BusinessOS.Pharmacy.Application.Abstractions.Time;
using BusinessOS.Pharmacy.Desktop.Customers;
using BusinessOS.Pharmacy.Desktop.Backup;
using BusinessOS.Pharmacy.Desktop.Dashboard;
using BusinessOS.Pharmacy.Desktop.DailyClosing;
using BusinessOS.Pharmacy.Desktop.Localization;
using BusinessOS.Pharmacy.Desktop.Inventory;
using BusinessOS.Pharmacy.Desktop.Expenses;
using BusinessOS.Pharmacy.Desktop.Medicines;
using BusinessOS.Pharmacy.Desktop.Navigation;
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

    public MainWindowViewModel(
        IClock clock,
        IUserSessionService sessions,
        IPermissionAuthorizer permissions,
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
        NetworkConfiguration networkConfiguration,
        IServiceProvider services)
    {
        _clock = clock;
        _sessions = sessions;
        _permissions = permissions;
        _networkConfiguration = networkConfiguration;
        _connectionMonitor = services.GetService<ILocalServerConnectionMonitor>();
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
        currentPage = Dashboard;

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
        Dashboard.NavigationRequested += OnDashboardNavigationRequested;

        if (_connectionMonitor is not null)
        {
            _connectionMonitor.StatusChanged += OnLanStatusChanged;
        }

        RefreshLanStatusText();

        LogoutCommand = new AsyncRelayCommand(LogoutAsync);
        NavigateCommand = new AsyncRelayCommand<string>(NavigateAsync);
        GlobalSearchCommand = new AsyncRelayCommand(GlobalSearchAsync);
        RefreshNavigation();
    }

    public event EventHandler? LogoutRequested;

    public IAsyncRelayCommand LogoutCommand { get; }
    public IAsyncRelayCommand<string> NavigateCommand { get; }
    public IAsyncRelayCommand GlobalSearchCommand { get; }
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

    public string ApplicationName => "BusinessOS Pharmacy";
    public string ProductName => "Darmaltoon";
    public string ParentBrand => "BusinessOS.af";
    public string PageTitle => CurrentSectionKey switch
    {
        "pos" => Translate("Point of Sale", "فروش", "خرڅلاو"),
        "returns" => Translate("Sale Returns", "برگشت فروش", "د خرڅلاو بېرته ستنول"),
        "expenses" => Translate("Expenses & Accounting", "مصارف و حسابداری", "لګښتونه او حسابداري"),
        "closing" => Translate("Daily Closing", "بستن روزانه", "ورځنی تړل"),
        "reports" => Translate("Reports", "گزارش‌ها", "راپورونه"),
        "backup" => Translate("Backup & Restore", "پشتیبان‌گیری و بازیابی", "بیک اپ او بېرته راګرځول"),
        "updates" => Translate("Application Updates", "به‌روزرسانی برنامه", "د اپلېکېشن تازه کول"),
        "network" => Translate("Network & Terminals", "شبکه و ترمینال‌ها", "شبکه او ترمینلونه"),
        "medicines" => Translate("Medicines", "ادویه", "درمل"),
        "inventory" => Translate("Inventory", "موجودی", "زېرمه"),
        "batches" => Translate("Batches", "بچ‌ها", "بېچونه"),
        "purchases" => Translate("Purchases", "خریداری", "پېرود"),
        "suppliers" => Translate("Suppliers", "تأمین‌کنندگان", "عرضه کوونکي"),
        "customers" => Translate("Customers", "مشتریان", "پېرودونکي"),
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
            "د بېچ، ختمېدو او زېرمتون حرکتونو کنټرول"),
        "batches" => Translate(
            "Medicine batches, quantities and expiry control",
            "بچ‌های دوا، مقدار و کنترل انقضا",
            "د درملو بېچونه، مقدار او د ختمېدو کنټرول"),
        "purchases" => Translate(
            "Purchase orders, receiving, invoices and supplier payments",
            "سفارش خرید، دریافت، فاکتور و پرداخت تأمین‌کننده",
            "پېرود امرونه، ترلاسه کول، بلونه او د عرضه کوونکي تادیات"),
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
        _ => Translate(
            "Local-first pharmacy operations",
            "عملیات محلی دواخانه",
            "د درملتون محلي عملیات"),
    };
    public string OnlineText => Translate("Online", "آنلاین", "آنلاین");
    public string LicenseText => Translate("Licensed", "مجوز فعال", "جواز فعال");
    public string LastVerifiedText => $"{Translate("Ready", "آماده", "چمتو")} • {_clock.UtcNow:yyyy-MM-dd HH:mm} UTC";
    public string UserDisplayName => _sessions.Current?.Name ?? Translate("No user", "بدون کاربر", "کارن نشته");
    public string UserRoleText => _sessions.Current is null
        ? string.Empty
        : string.Join(", ", _sessions.Current.Roles);
    public int PermissionCount => _sessions.Current?.Permissions.Count ?? 0;
    public ReadOnlyCollection<UiLanguage> Languages => UiLanguageCatalog.All;
    public ObservableCollection<NavigationItemViewModel> NavigationItems { get; } = new();

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
        CurrentSectionKey = "dashboard";
        CurrentPage = Dashboard;
        RefreshNavigation();
    }

    partial void OnSelectedLanguageChanged(UiLanguage value)
    {
        LayoutDirection = value.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
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
        RaisePageText();
        RefreshNavigation();
    }

    private void RefreshNavigation()
    {
        NavigationItems.Clear();

        // Primary pharmacy navigation follows the reference dashboard order.
        AddIfAllowed("dashboard.view", "dashboard", Translate("Dashboard", "داشبورد", "ډشبورډ"), "⌂");
        AddIfAllowed("pos.sell", "pos", Translate("POS (New Sale)", "فروش (جدید)", "خرڅلاو (نوی)"), "▣");
        AddIfAllowed("medicines.manage", "medicines", Translate("Medicines", "ادویه", "درمل"), "✚");

        if (_permissions.HasPermission("inventory.manage") ||
            _permissions.HasPermission("inventory.status"))
        {
            AddNavigationItem(
                "inventory",
                Translate("Inventory", "موجودی", "زېرمه"),
                "▤");
            AddNavigationItem(
                "batches",
                Translate("Batches", "بچ‌ها", "بېچونه"),
                "◫");
        }

        AddIfAllowed("purchases.manage", "purchases", Translate("Purchases", "خریداری", "پېرود"), "↓");
        AddIfAllowed("purchases.manage", "suppliers", Translate("Suppliers", "تأمین‌کنندگان", "عرضه کوونکي"), "♜");
        AddIfAllowed("customers.manage", "customers", Translate("Customers", "مشتریان", "پېرودونکي"), "♙");
        AddIfAllowed("accounting.manage", "expenses", Translate("Expenses", "مصارف", "لګښتونه"), "₳");
        AddIfAllowed("daily_closing.perform", "closing", Translate("Daily Closing", "بستن روزانه", "ورځنی تړل"), "✓");
        AddIfAllowed("reports.view", "reports", Translate("Reports", "گزارش‌ها", "راپورونه"), "▥");
        AddIfAllowed("users.manage", "users", Translate("Users", "کاربران", "کارنان"), "♟");

        if (_networkConfiguration.Mode != DeploymentMode.Client &&
            _permissions.HasPermission("settings.manage"))
        {
            AddNavigationItem(
                "backup",
                Translate("Backup", "پشتیبان‌گیری", "بیک اپ"),
                "◫");
        }

        AddIfAllowed("settings.manage", "settings", Translate("Settings", "تنظیمات", "امستنې"), "⚙");

        // Keep the advanced native modules available without displacing the
        // reference dashboard options above.
        AddIfAllowed("returns.manage", "returns", Translate("Returns", "برگشت", "ستنېدل"), "↶");
        AddIfAllowed("roles.manage", "roles", Translate("Roles & Permissions", "نقش‌ها و مجوزها", "رولونه او اجازې"), "⚿");
        AddIfAllowed("settings.manage", "updates", Translate("Sync & Updates", "همگام‌سازی و به‌روزرسانی", "همغږي او تازه کول"), "⇧");

        if (_networkConfiguration.Mode == DeploymentMode.Server &&
            (_permissions.HasPermission("users.manage") ||
             _permissions.HasPermission("settings.manage")))
        {
            AddNavigationItem(
                "network",
                Translate("Network & Terminals", "شبکه و ترمینال‌ها", "شبکه او ترمینلونه"),
                "⌁");
        }
    }

    private void AddIfAllowed(string permission, string key, string label, string glyph)
    {
        if (_permissions.HasPermission(permission))
        {
            AddNavigationItem(key, label, glyph);
        }
    }

    private void AddNavigationItem(string key, string label, string glyph)
    {
        NavigationItems.Add(new NavigationItemViewModel(
            key,
            label,
            glyph,
            string.Equals(key, CurrentSectionKey, StringComparison.OrdinalIgnoreCase)));
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
                CurrentSectionKey = "inventory";
                CurrentPage = Inventory;
                await Inventory.LoadAsync();
                break;

            case "batches" when
                _permissions.HasPermission("inventory.manage") ||
                _permissions.HasPermission("inventory.status"):
                CurrentSectionKey = "batches";
                CurrentPage = Inventory;
                await Inventory.LoadAsync();
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
