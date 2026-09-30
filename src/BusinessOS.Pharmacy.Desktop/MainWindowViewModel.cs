using System.Collections.ObjectModel;
using System.Windows;
using BusinessOS.Pharmacy.Application.Abstractions.Authentication;
using BusinessOS.Pharmacy.Application.Abstractions.Time;
using BusinessOS.Pharmacy.Desktop.Dashboard;
using BusinessOS.Pharmacy.Desktop.Localization;
using BusinessOS.Pharmacy.Desktop.Medicines;
using BusinessOS.Pharmacy.Desktop.Navigation;
using BusinessOS.Pharmacy.Desktop.Networking;
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
        MedicinesViewModel medicines,
        NetworkSettingsViewModel networkSettings)
    {
        _clock = clock;
        _sessions = sessions;
        _permissions = permissions;
        Dashboard = dashboard;
        Medicines = medicines;
        NetworkSettings = networkSettings;
        currentPage = Dashboard;

        Dashboard.SetLanguage(SelectedLanguage);
        Medicines.SetLanguage(SelectedLanguage);
        NetworkSettings.SetLanguage(SelectedLanguage);
        Dashboard.NavigationRequested += OnDashboardNavigationRequested;

        LogoutCommand = new AsyncRelayCommand(LogoutAsync);
        NavigateCommand = new AsyncRelayCommand<string>(NavigateAsync);
        RefreshNavigation();
    }

    public event EventHandler? LogoutRequested;

    public IAsyncRelayCommand LogoutCommand { get; }
    public IAsyncRelayCommand<string> NavigateCommand { get; }
    public DashboardViewModel Dashboard { get; }
    public MedicinesViewModel Medicines { get; }
    public NetworkSettingsViewModel NetworkSettings { get; }

    public string ApplicationName => "Darmaltoon";
    public string ParentBrand => "BusinessOS.af";
    public string PageTitle => CurrentSectionKey switch
    {
        "medicines" => Translate("Medicines", "ادویه", "درمل"),
        "network" => Translate("Network & Terminals", "شبکه و ترمینال‌ها", "شبکه او ترمینلونه"),
        _ => Translate("Dashboard", "داشبورد", "ډشبورډ")
    };

    public string PageSubtitle => CurrentSectionKey switch
    {
        "medicines" => Translate(
            "Medicine master data, categories and CSV registration",
            "اطلاعات اصلی ادویه، دسته‌بندی و ثبت CSV",
            "د درملو اصلي معلومات، کټګورۍ او CSV ثبت"),
        "network" => Translate(
            "Local server, paired terminals and network diagnostics",
            "سرور محلی، ترمینال‌های جفت‌شده و عیب‌یابی شبکه",
            "محلي سرور، نښلول شوي ترمینلونه او د شبکې تشخیص"),
        _ => Translate(
            "Local-first pharmacy operations",
            "عملیات محلی دواخانه",
            "د درملتون محلي عملیات")
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
        Medicines.SetLanguage(SelectedLanguage);
        NetworkSettings.SetLanguage(SelectedLanguage);
        CurrentSectionKey = "dashboard";
        CurrentPage = Dashboard;
        RefreshNavigation();
    }

    partial void OnSelectedLanguageChanged(UiLanguage value)
    {
        LayoutDirection = value.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        Dashboard.SetLanguage(value);
        Medicines.SetLanguage(value);
        NetworkSettings.SetLanguage(value);
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
        AddIfAllowed("medicines.manage", "medicines", Translate("Medicines", "ادویه", "درمل"), "✚");
        AddIfAllowed("inventory.manage", "inventory", Translate("Inventory", "موجودی", "زېرمه"), "▤");
        AddIfAllowed("purchases.manage", "purchases", Translate("Purchases", "خریداری", "پېرود"), "↓");
        AddIfAllowed("customers.manage", "customers", Translate("Customers", "مشتریان", "پېرودونکي"), "♙");
        AddIfAllowed("reports.view", "reports", Translate("Reports", "گزارش‌ها", "راپورونه"), "▥");
        AddIfAllowed("daily_closing.perform", "closing", Translate("Daily Closing", "بستن روزانه", "ورځنی تړل"), "✓");
        AddIfAllowed("users.manage", "users", Translate("Users", "کاربران", "کارنان"), "♟");
        AddIfAllowed("roles.manage", "roles", Translate("Roles", "نقش‌ها", "رولونه"), "⚿");

        if (_permissions.HasPermission("settings.manage"))
        {
            NavigationItems.Add(new(
                "network",
                Translate("Network & Terminals", "شبکه و ترمینال‌ها", "شبکه او ترمینلونه"),
                "⌁"));
        }
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

            case "medicines" when _permissions.HasPermission("medicines.manage"):
                CurrentSectionKey = "medicines";
                CurrentPage = Medicines;
                await Medicines.LoadAsync();
                break;

            case "network" when _permissions.HasPermission("settings.manage"):
                CurrentSectionKey = "network";
                CurrentPage = NetworkSettings;
                await NetworkSettings.LoadAsync();
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
