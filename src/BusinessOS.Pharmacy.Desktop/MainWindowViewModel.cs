using System.Collections.ObjectModel;
using System.Windows;
using BusinessOS.Pharmacy.Application.Abstractions.Authentication;
using BusinessOS.Pharmacy.Application.Abstractions.Networking;
using BusinessOS.Pharmacy.Application.Abstractions.Time;
using BusinessOS.Pharmacy.Desktop.Customers;
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
        RefreshNavigation();
    }

    public event EventHandler? LogoutRequested;

    public IAsyncRelayCommand LogoutCommand { get; }
    public IAsyncRelayCommand<string> NavigateCommand { get; }
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
    public NetworkSettingsViewModel NetworkSettings { get; }

    public string ApplicationName => "Darmaltoon";
    public string ParentBrand => "BusinessOS.af";
    public string PageTitle => CurrentSectionKey switch
    {
        "pos" => Translate("Point of Sale", "فروش", "خرڅلاو"),
        "returns" => Translate("Sale Returns", "برگشت فروش", "د خرڅلاو بېرته ستنول"),
        "expenses" => Translate("Expenses & Accounting", "مصارف و حسابداری", "لګښتونه او حسابداري"),
        "closing" => Translate("Daily Closing", "بستن روزانه", "ورځنی تړل"),
        "reports" => Translate("Reports", "گزارش‌ها", "راپورونه"),
        "network" => Translate("Network & Terminals", "شبکه و ترمینال‌ها", "شبکه او ترمینلونه"),
        "medicines" => Translate("Medicines", "ادویه", "درمل"),
        "inventory" => Translate("Inventory", "موجودی", "زېرمه"),
        "purchases" => Translate("Purchases", "خریداری", "پېرود"),
        "customers" => Translate("Customers", "مشتریان", "پېرودونکي"),
        _ => Translate("Dashboard", "داشبورد", "ډشبورډ"),
    };
    public string PageSubtitle => CurrentSectionKey switch
    {
        "reports" => Translate(
            "Sales, profit, purchasing, stock and movement reporting",
            "گزارش فروش، سود، خرید، موجودی و گردش کالا",
            "د خرڅلاو، ګټې، پېرود، زېرمه او حرکتونو راپورونه"),
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
        "purchases" => Translate(
            "Suppliers, purchase orders, receiving, invoices and supplier payments",
            "تأمین‌کنندگان، سفارش خرید، دریافت، فاکتور و پرداخت",
            "عرضه کوونکي، پېرود امرونه، ترلاسه کول، بلونه او تادیات"),
        "customers" => Translate(
            "Customer records and per-sale credit limits",
            "اطلاعات مشتری و سقف اعتبار هر فروش",
            "د پېرودونکو معلومات او د هر خرڅلاو د پور حد"),
        _ => Translate(
            "Local-first pharmacy operations",
            "عملیات محلی دواخانه",
            "د درملتون محلي عملیات"),
    };
    public string OnlineText => Translate("Licensed", "فعال", "فعال");
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
        NetworkSettings.SetLanguage(value);
        RefreshLanStatusText();
        RefreshNavigation();
        RaisePageText();
        OnPropertyChanged(nameof(OnlineText));
        OnPropertyChanged(nameof(LastVerifiedText));
        OnPropertyChanged(nameof(UserDisplayName));
    }

    partial void OnCurrentSectionKeyChanged(string value) => RaisePageText();

    private void RefreshNavigation()
    {
        NavigationItems.Clear();
        AddIfAllowed("dashboard.view", "dashboard", Translate("Dashboard", "داشبورد", "ډشبورډ"), "⌂");
        AddIfAllowed("pos.sell", "pos", Translate("POS", "فروش", "خرڅلاو"), "▣");
        AddIfAllowed("returns.manage", "returns", Translate("Returns", "برگشت", "ستنېدل"), "↶");
        AddIfAllowed("accounting.manage", "expenses", Translate("Expenses", "مصارف", "لګښتونه"), "₳");
        AddIfAllowed("medicines.manage", "medicines", Translate("Medicines", "ادویه", "درمل"), "✚");
        if (_permissions.HasPermission("inventory.manage") ||
            _permissions.HasPermission("inventory.status"))
        {
            NavigationItems.Add(new(
                "inventory",
                Translate("Inventory", "موجودی", "زېرمه"),
                "▤"));
        }
        AddIfAllowed("purchases.manage", "purchases", Translate("Purchases", "خریداری", "پېرود"), "↓");
        AddIfAllowed("customers.manage", "customers", Translate("Customers", "مشتریان", "پېرودونکي"), "♙");
        AddIfAllowed("reports.view", "reports", Translate("Reports", "گزارش‌ها", "راپورونه"), "▥");

        if (_networkConfiguration.Mode == DeploymentMode.Server &&
            (_permissions.HasPermission("users.manage") ||
             _permissions.HasPermission("settings.manage")))
        {
            NavigationItems.Add(new NavigationItemViewModel(
                "network",
                Translate("Network & Terminals", "شبکه و ترمینال‌ها", "شبکه او ترمینلونه"),
                "⌁"));
        }
        AddIfAllowed("daily_closing.perform", "closing", Translate("Daily Closing", "بستن روزانه", "ورځنی تړل"), "✓");
        AddIfAllowed("users.manage", "users", Translate("Users", "کاربران", "کارنان"), "♟");
        AddIfAllowed("roles.manage", "roles", Translate("Roles", "نقش‌ها", "رولونه"), "⚿");
    }

    private void AddIfAllowed(string permission, string key, string label, string glyph)
    {
        if (_permissions.HasPermission(permission))
        {
            NavigationItems.Add(new(key, label, glyph));
        }
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

            case "network" when
                _networkConfiguration.Mode == DeploymentMode.Server &&
                (_permissions.HasPermission("users.manage") ||
                 _permissions.HasPermission("settings.manage")):
                CurrentSectionKey = "network";
                CurrentPage = NetworkSettings;
                await NetworkSettings.LoadAsync();
                RefreshLanStatusText();
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

            case "purchases" when _permissions.HasPermission("purchases.manage"):
                CurrentSectionKey = "purchases";
                CurrentPage = Purchasing;
                await Purchasing.LoadAsync();
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
