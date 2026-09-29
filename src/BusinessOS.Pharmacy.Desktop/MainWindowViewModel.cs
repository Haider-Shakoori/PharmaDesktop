using System.Collections.ObjectModel;
using System.Windows;
using BusinessOS.Pharmacy.Application.Abstractions.Time;
using BusinessOS.Pharmacy.Desktop.Localization;
using BusinessOS.Pharmacy.Desktop.Navigation;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BusinessOS.Pharmacy.Desktop;

public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly IClock _clock;

    [ObservableProperty]
    private UiLanguage selectedLanguage = UiLanguageCatalog.All[0];

    [ObservableProperty]
    private FlowDirection layoutDirection = FlowDirection.LeftToRight;

    public MainWindowViewModel(IClock clock)
    {
        _clock = clock;
        RefreshNavigation();
    }

    public string ApplicationName => "BusinessOS Pharmacy";
    public string ParentBrand => "BusinessOS.af";
    public string PageTitle => Translate("Dashboard", "داشبورد", "ډشبورډ");
    public string PageSubtitle => Translate("Native Windows pharmacy workspace", "محیط کاری ویندوز برای دواخانه", "د وینډوز درملتون کاري چاپېریال");
    public string OnlineText => Translate("Online", "آنلاین", "آنلاین");
    public string LastVerifiedText => $"{Translate("Ready", "آماده", "چمتو")} • {_clock.UtcNow:yyyy-MM-dd HH:mm} UTC";
    public ReadOnlyCollection<UiLanguage> Languages => UiLanguageCatalog.All;
    public ObservableCollection<NavigationItemViewModel> NavigationItems { get; } = new();

    partial void OnSelectedLanguageChanged(UiLanguage value)
    {
        LayoutDirection = value.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        RefreshNavigation();
        OnPropertyChanged(nameof(PageTitle));
        OnPropertyChanged(nameof(PageSubtitle));
        OnPropertyChanged(nameof(OnlineText));
        OnPropertyChanged(nameof(LastVerifiedText));
    }

    private void RefreshNavigation()
    {
        NavigationItems.Clear();
        NavigationItems.Add(new("dashboard", Translate("Dashboard", "داشبورد", "ډشبورډ"), "⌂"));
        NavigationItems.Add(new("pos", Translate("POS", "فروش", "خرڅلاو"), "▣"));
        NavigationItems.Add(new("medicines", Translate("Medicines", "ادویه", "درمل"), "✚"));
        NavigationItems.Add(new("inventory", Translate("Inventory", "موجودی", "زېرمه"), "▤"));
        NavigationItems.Add(new("purchases", Translate("Purchases", "خریداری", "پېرود"), "↓"));
        NavigationItems.Add(new("customers", Translate("Customers", "مشتریان", "پېرودونکي"), "♙"));
        NavigationItems.Add(new("reports", Translate("Reports", "گزارش‌ها", "راپورونه"), "▥"));
        NavigationItems.Add(new("closing", Translate("Daily Closing", "بستن روزانه", "ورځنی تړل"), "✓"));
    }

    private string Translate(string english, string dari, string pashto) =>
        SelectedLanguage.Code switch
        {
            "fa" => dari,
            "ps" => pashto,
            _ => english
        };
}