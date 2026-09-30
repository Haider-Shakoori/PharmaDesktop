using System.Collections.ObjectModel;
using System.Globalization;
using BusinessOS.Pharmacy.Application.Abstractions.Authentication;
using BusinessOS.Pharmacy.Application.Abstractions.Dashboard;
using BusinessOS.Pharmacy.Application.Abstractions.Time;
using BusinessOS.Pharmacy.Desktop.Localization;
using BusinessOS.Pharmacy.Licensing;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BusinessOS.Pharmacy.Desktop.Dashboard;

public sealed partial class DashboardViewModel : ObservableObject
{
    private const int DefaultLowStockThreshold = 10;
    private const int DefaultNearExpiryDays = 90;

    private readonly ILocalDashboardQueryService _dashboardQueries;
    private readonly IActivationStore _activationStore;
    private readonly IUserSessionService _sessions;
    private readonly IPermissionAuthorizer _permissions;
    private readonly IClock _clock;

    private DashboardSnapshot? _snapshot;
    private UiLanguage _language = UiLanguageCatalog.All[0];
    private string _currency = "AFN";

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private string pharmacyName = "Darmaltoon";

    [ObservableProperty]
    private string pharmacyCode = "—";

    [ObservableProperty]
    private string subscriptionLabel = "Licensed";

    [ObservableProperty]
    private string businessDateText = "—";

    [ObservableProperty]
    private string statusText = "Loading local dashboard…";

    [ObservableProperty]
    private bool hasAlerts;

    [ObservableProperty]
    private string attentionTitle = "Attention required";

    [ObservableProperty]
    private string attentionSummary = string.Empty;

    [ObservableProperty]
    private string signedInUser = string.Empty;

    public DashboardViewModel(
        ILocalDashboardQueryService dashboardQueries,
        IActivationStore activationStore,
        IUserSessionService sessions,
        IPermissionAuthorizer permissions,
        IClock clock)
    {
        _dashboardQueries = dashboardQueries;
        _activationStore = activationStore;
        _sessions = sessions;
        _permissions = permissions;
        _clock = clock;

        RefreshCommand = new AsyncRelayCommand(LoadAsync, () => !IsLoading);
        NavigateCommand = new RelayCommand<string>(key =>
        {
            if (!string.IsNullOrWhiteSpace(key))
            {
                NavigationRequested?.Invoke(key);
            }
        });
    }

    public IAsyncRelayCommand RefreshCommand { get; }
    public IRelayCommand<string> NavigateCommand { get; }

    public event Action<string>? NavigationRequested;

    public ObservableCollection<DashboardStatViewModel> Stats { get; } = new();
    public ObservableCollection<DashboardQuickActionViewModel> QuickActions { get; } = new();
    public ObservableCollection<DashboardAlertViewModel> Alerts { get; } = new();

    public string QuickActionsTitle => Translate("Quick actions", "اقدامات سریع", "چټک کارونه");
    public string RunPharmacyTitle => Translate("Run the pharmacy", "مدیریت دواخانه", "درملتون اداره کړئ");
    public string RefreshLabel => Translate("Refresh", "تازه‌سازی", "تازه کول");
    public string PharmacyCodeLabel => Translate("Pharmacy code", "کد دواخانه", "د درملتون کوډ");
    public string LocalDataLabel => Translate("Local data", "داده محلی", "محلي معلومات");
    public string NoAlertsText => Translate(
        "No inventory alerts in the local database.",
        "هیچ هشدار موجودی در پایگاه‌داده محلی نیست.",
        "په محلي ډیټابیس کې د زېرمتون خبرتیا نشته.");

    public void SetLanguage(UiLanguage language)
    {
        _language = language;
        RebuildPresentation();
        RaiseLocalizedProperties();
    }

    public async Task LoadAsync()
    {
        if (IsLoading)
        {
            return;
        }

        IsLoading = true;
        RefreshCommand.NotifyCanExecuteChanged();

        try
        {
            if (!_permissions.HasPermission("dashboard.view"))
            {
                StatusText = Translate(
                    "Your role does not grant dashboard access.",
                    "نقش شما اجازه دسترسی به داشبورد را ندارد.",
                    "ستاسو رول ډشبورډ ته لاسرسی نه لري.");
                Stats.Clear();
                QuickActions.Clear();
                Alerts.Clear();
                HasAlerts = false;
                return;
            }

            var activation = await _activationStore.LoadAsync();
            var tenant = activation?.Tenant;

            PharmacyName = string.IsNullOrWhiteSpace(tenant?.Name)
                ? "Darmaltoon"
                : tenant.Name;
            PharmacyCode = !string.IsNullOrWhiteSpace(tenant?.Slug)
                ? tenant.Slug
                : activation?.Entitlement.TenantId ?? "—";
            SubscriptionLabel = HumanizeHealth(activation?.SubscriptionHealth);
            SignedInUser = _sessions.Current?.Name ?? "—";
            _currency = string.IsNullOrWhiteSpace(tenant?.Currency) ? "AFN" : tenant.Currency;

            var businessDate = ResolveBusinessDate(tenant?.Timezone);
            _snapshot = await _dashboardQueries.GetSnapshotAsync(
                new DashboardQueryOptions(
                    businessDate,
                    DefaultLowStockThreshold,
                    DefaultNearExpiryDays));

            BusinessDateText = businessDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            RebuildPresentation();
        }
        catch (Exception)
        {
            StatusText = Translate(
                "The local dashboard could not be loaded. Operational data remains protected in the local database.",
                "داشبورد محلی بارگذاری نشد. داده‌های عملیاتی در پایگاه‌داده محلی محفوظ است.",
                "محلي ډشبورډ پورته نه شو. عملیاتي معلومات په محلي ډیټابیس کې خوندي دي.");
        }
        finally
        {
            IsLoading = false;
            RefreshCommand.NotifyCanExecuteChanged();
        }
    }

    private void RebuildPresentation()
    {
        if (_snapshot is null)
        {
            RebuildQuickActions();
            return;
        }

        Stats.Clear();
        Stats.Add(new DashboardStatViewModel(
            Translate("Today's sales", "فروش امروز", "د نن ورځې خرڅلاو"),
            FormatMoney(_snapshot.TodaySales),
            LocalNote(_snapshot.Availability.Sales),
            "؋"));
        Stats.Add(new DashboardStatViewModel(
            Translate("Low stock", "کمبود موجودی", "کم زېرمه"),
            _snapshot.LowStockCount.ToString(CultureInfo.InvariantCulture),
            LocalNote(_snapshot.Availability.Inventory),
            "↓"));
        Stats.Add(new DashboardStatViewModel(
            Translate("Expiring soon", "نزدیک به انقضا", "ژر ختمېدونکي"),
            _snapshot.NearExpiryCount.ToString(CultureInfo.InvariantCulture),
            LocalNote(_snapshot.Availability.Inventory),
            "◷"));
        Stats.Add(new DashboardStatViewModel(
            Translate("Alerts", "هشدارها", "خبرتیاوې"),
            _snapshot.TotalAlerts.ToString(CultureInfo.InvariantCulture),
            LocalNote(_snapshot.Availability.Inventory),
            "!"));
        Stats.Add(new DashboardStatViewModel(
            Translate("Today's transactions", "تراکنش‌های امروز", "د نن راکړې ورکړې"),
            _snapshot.TodayTransactions.ToString(CultureInfo.InvariantCulture),
            LocalNote(_snapshot.Availability.Sales),
            "#"));
        Stats.Add(new DashboardStatViewModel(
            Translate("Stock value", "ارزش موجودی", "د زېرمتون ارزښت"),
            FormatMoney(_snapshot.StockValue),
            LocalNote(_snapshot.Availability.Inventory),
            "▤"));
        Stats.Add(new DashboardStatViewModel(
            Translate("Customers", "مشتریان", "پېرودونکي"),
            _snapshot.ActiveCustomers.ToString(CultureInfo.InvariantCulture),
            LocalNote(_snapshot.Availability.Customers),
            "♙"));
        Stats.Add(new DashboardStatViewModel(
            Translate("Credit due", "اعتبار قابل دریافت", "پور پاتې"),
            FormatMoney(_snapshot.OutstandingCredit),
            LocalNote(_snapshot.Availability.Sales),
            "◈"));

        HasAlerts = _snapshot.TotalAlerts > 0;
        AttentionTitle = Translate("Attention required", "نیاز به توجه", "پاملرنه اړینه ده");
        AttentionSummary = Translate(
            $"{_snapshot.LowStockCount} low stock · {_snapshot.NearExpiryCount} near expiry · {_snapshot.ExpiredCount} expired",
            $"{_snapshot.LowStockCount} کمبود موجودی · {_snapshot.NearExpiryCount} نزدیک انقضا · {_snapshot.ExpiredCount} منقضی",
            $"{_snapshot.LowStockCount} کم زېرمه · {_snapshot.NearExpiryCount} ژر ختمېدونکي · {_snapshot.ExpiredCount} ختم شوي");

        Alerts.Clear();
        foreach (var alert in _snapshot.Alerts.Take(8))
        {
            Alerts.Add(ToAlertViewModel(alert));
        }

        RebuildQuickActions();

        var availableModules = new[]
        {
            _snapshot.Availability.Sales,
            _snapshot.Availability.Inventory,
            _snapshot.Availability.Customers,
        }.Count(value => value);

        StatusText = availableModules == 0
            ? Translate(
                "Local database ready. Dashboard cards will populate as operational module data is added.",
                "پایگاه‌داده محلی آماده است. کارت‌های داشبورد با اضافه‌شدن داده‌های عملیاتی تکمیل می‌شوند.",
                "محلي ډیټابیس چمتو دی. د عملیاتي معلوماتو په زیاتېدو سره ډشبورډ ډکېږي.")
            : Translate(
                $"Local dashboard loaded · {availableModules}/3 data groups available",
                $"داشبورد محلی بارگذاری شد · {availableModules}/3 گروه داده موجود است",
                $"محلي ډشبورډ پورته شو · {availableModules}/3 د معلوماتو ډلې شته");
    }

    private void RebuildQuickActions()
    {
        QuickActions.Clear();

        AddAction("pos", Translate("Open POS", "باز کردن فروش", "خرڅلاو پرانیزئ"), "▣", "pos.sell", false);
        AddAction("medicines", Translate("Add medicine", "افزودن دوا", "درمل زیات کړئ"), "✚", "medicines.manage", true);
        AddAction("customers", Translate("Customers", "مشتریان", "پېرودونکي"), "♙", "customers.manage", false);
        AddAction("purchases", Translate("New purchase", "خرید جدید", "نوی پېرود"), "↓", "purchases.manage", true);
        AddAction("inventory", Translate("Inventory", "موجودی", "زېرمه"), "▤", "inventory.manage", true);
        AddAction("reports", Translate("Reports", "گزارش‌ها", "راپورونه"), "▥", "reports.view", false);
    }

    private void AddAction(
        string key,
        string label,
        string glyph,
        string permission,
        bool isAvailable)
    {
        if (_permissions.HasPermission(permission))
        {
            QuickActions.Add(new DashboardQuickActionViewModel(
                key,
                label,
                glyph,
                permission,
                isAvailable));
        }
    }

    private DashboardAlertViewModel ToAlertViewModel(DashboardAlertItem alert)
    {
        return alert.Kind switch
        {
            "low_stock" => new DashboardAlertViewModel(
                alert.Kind,
                alert.Name,
                Translate(
                    $"Available {alert.AvailableQuantity:0.####} · threshold {alert.Threshold:0.####}",
                    $"موجود {alert.AvailableQuantity:0.####} · حد {alert.Threshold:0.####}",
                    $"شته {alert.AvailableQuantity:0.####} · حد {alert.Threshold:0.####}"),
                "warning"),
            "expired" => new DashboardAlertViewModel(
                alert.Kind,
                alert.Name,
                Translate(
                    $"Batch {alert.BatchNumber ?? "—"} · expired {alert.ExpiresAt:yyyy-MM-dd}",
                    $"بچ {alert.BatchNumber ?? "—"} · منقضی {alert.ExpiresAt:yyyy-MM-dd}",
                    $"بېچ {alert.BatchNumber ?? "—"} · ختم {alert.ExpiresAt:yyyy-MM-dd}"),
                "danger"),
            _ => new DashboardAlertViewModel(
                alert.Kind,
                alert.Name,
                Translate(
                    $"Batch {alert.BatchNumber ?? "—"} · expires {alert.ExpiresAt:yyyy-MM-dd}",
                    $"بچ {alert.BatchNumber ?? "—"} · انقضا {alert.ExpiresAt:yyyy-MM-dd}",
                    $"بېچ {alert.BatchNumber ?? "—"} · ختمېږي {alert.ExpiresAt:yyyy-MM-dd}"),
                "attention"),
        };
    }

    private string LocalNote(bool available) => available
        ? Translate("Local database", "پایگاه‌داده محلی", "محلي ډیټابیس")
        : Translate("Awaiting module data", "در انتظار داده ماژول", "د ماډیول معلوماتو ته منتظر");

    private string FormatMoney(decimal value) =>
        $"{_currency} {value:N2}";

    private DateOnly ResolveBusinessDate(string? timezoneId)
    {
        var utcNow = _clock.UtcNow;

        foreach (var candidate in TimezoneCandidates(timezoneId))
        {
            try
            {
                var timezone = TimeZoneInfo.FindSystemTimeZoneById(candidate);
                var local = TimeZoneInfo.ConvertTime(utcNow, timezone);
                return DateOnly.FromDateTime(local.DateTime);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        if (string.Equals(timezoneId, "Asia/Kabul", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(timezoneId))
        {
            return DateOnly.FromDateTime(utcNow.ToOffset(TimeSpan.FromMinutes(270)).DateTime);
        }

        return DateOnly.FromDateTime(utcNow.UtcDateTime);
    }

    private static IEnumerable<string> TimezoneCandidates(string? timezoneId)
    {
        if (!string.IsNullOrWhiteSpace(timezoneId))
        {
            yield return timezoneId;
        }

        if (string.Equals(timezoneId, "Asia/Kabul", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(timezoneId))
        {
            yield return "Afghanistan Standard Time";
            yield return "Asia/Kabul";
        }
    }

    private string HumanizeHealth(string? health)
    {
        if (string.IsNullOrWhiteSpace(health))
        {
            return Translate("Licensed", "فعال", "فعال");
        }

        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(
            health.Replace('_', ' ').ToLowerInvariant());
    }

    private string Translate(string english, string dari, string pashto) =>
        _language.Code switch
        {
            "fa" => dari,
            "ps" => pashto,
            _ => english,
        };

    private void RaiseLocalizedProperties()
    {
        OnPropertyChanged(nameof(QuickActionsTitle));
        OnPropertyChanged(nameof(RunPharmacyTitle));
        OnPropertyChanged(nameof(RefreshLabel));
        OnPropertyChanged(nameof(PharmacyCodeLabel));
        OnPropertyChanged(nameof(LocalDataLabel));
        OnPropertyChanged(nameof(NoAlertsText));
    }
}
