using System.Collections.ObjectModel;
using System.Windows;
using BusinessOS.Pharmacy.Application.Abstractions.Authentication;
using BusinessOS.Pharmacy.Application.Abstractions.Time;
using BusinessOS.Pharmacy.Desktop.Customers;
using BusinessOS.Pharmacy.Desktop.Dashboard;
using BusinessOS.Pharmacy.Desktop.Localization;
using BusinessOS.Pharmacy.Desktop.Inventory;
using BusinessOS.Pharmacy.Desktop.Expenses;
using BusinessOS.Pharmacy.Desktop.Medicines;
using BusinessOS.Pharmacy.Desktop.Navigation;
using BusinessOS.Pharmacy.Desktop.Purchasing;
using BusinessOS.Pharmacy.Desktop.Pos;
using BusinessOS.Pharmacy.Desktop.Returns;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BusinessOS.Pharmacy.Desktop;

public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly IClock _clock;
    private readonly IUserSessionService _sessions;
    private readonly IPermissionAuthorizer _permissions;

    [ObservableProperty]
    private UiLanguage selectedLanguage = UiLanguageCatalog.All[0];

    [ObservableProperty]
    private FlowDirection layoutDirection = FlowDirection.LeftToRight;

    [ObservableProperty]
    private object currentPage;

    [ObservableProperty]
    private string currentSectionKey = "dashboard";

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
        ExpensesViewModel expenses)
    {
        _clock = clock;
        _sessions = sessions;
        _permissions = permissions;
        Dashboard = dashboard;
        Customers = customers;
        Medicines = medicines;
        Inventory = inventory;
        Purchasing = purchasing;
        Pos = pos;
        Returns = returns;
        Expenses = expenses;
        currentPage = Dashboard;

        Dashboard.SetLanguage(SelectedLanguage);
        Customers.SetLanguage(SelectedLanguage);
        Medicines.SetLanguage(SelectedLanguage);
        Inventory.SetLanguage(SelectedLanguage);
        Purchasing.SetLanguage(SelectedLanguage);
        Pos.SetLanguage(SelectedLanguage);
        Returns.SetLanguage(SelectedLanguage);
        Expenses.SetLanguage(SelectedLanguage);
        Dashboard.NavigationRequested += OnDashboardNavigationRequested;

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

    public string ApplicationName => "Darmaltoon";
    public string ParentBrand => "BusinessOS.af";
    public string PageTitle => CurrentSectionKey switch
    {
        "pos" => Translate("Point of Sale", "فروش", "خرڅلاو"),
        "returns" => Translate("Sale Returns", "برگشت فروش", "د خرڅلاو بېرته ستنول"),
        "expenses" => Translate("Expenses & Accounting", "مصارف و حسابداری", "لګښتونه او حسابداري"),
        "medicines" => Translate("Medicines", "ادویه", "درمل"),
        "inventory" => Translate("Inventory", "موجودی", "زېرمه"),
        "purchases" => Translate("Purchases", "خریداری", "پېرود"),
        "customers" => Translate("Customers", "مشتریان", "پېرودونکي"),
        _ => Translate("Dashboard", "داشبورد", "ډشبورډ"),
    };
    public string PageSubtitle => CurrentSectionKey switch
    {
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
