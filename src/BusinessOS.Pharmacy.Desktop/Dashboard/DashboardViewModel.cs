using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using BusinessOS.Pharmacy.Application.Abstractions.Authentication;
using BusinessOS.Pharmacy.Application.Abstractions.Dashboard;
using BusinessOS.Pharmacy.Application.Abstractions.Time;
using BusinessOS.Pharmacy.Application.Abstractions.Networking;
using BusinessOS.Pharmacy.Application.Abstractions.Sync;
using BusinessOS.Pharmacy.Desktop.Localization;
using BusinessOS.Pharmacy.Licensing;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;

namespace BusinessOS.Pharmacy.Desktop.Dashboard;

public sealed partial class DashboardViewModel : ObservableObject
{
    private const int DefaultLowStockThreshold = 10;
    private const int DefaultNearExpiryDays = 90;

    private readonly ILocalDashboardQueryService _dashboardQueries;
    private readonly IActivationStore? _activationStore;
    private readonly INetworkConfigurationStore _networkConfiguration;
    private readonly IUserSessionService _sessions;
    private readonly IPermissionAuthorizer _permissions;
    private readonly IClock _clock;
    private readonly ICloudSyncService? _cloudSync;

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

    [ObservableProperty]
    private string planLabel = "Plan";

    [ObservableProperty]
    private string licenseValidityText = "License status unavailable";

    [ObservableProperty]
    private string licenseDaysRemainingText = string.Empty;

    [ObservableProperty]
    private double licenseProgressValue;

    [ObservableProperty]
    private string syncStatusText = "Cloud sync ready";

    public DashboardViewModel(
        ILocalDashboardQueryService dashboardQueries,
        INetworkConfigurationStore networkConfiguration,
        IUserSessionService sessions,
        IPermissionAuthorizer permissions,
        IClock clock,
        IServiceProvider services,
        IActivationStore? activationStore = null)
    {
        _dashboardQueries = dashboardQueries;
        _networkConfiguration = networkConfiguration;
        _activationStore = activationStore;
        _sessions = sessions;
        _permissions = permissions;
        _clock = clock;
        _cloudSync = services.GetService<ICloudSyncService>();

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
    public ObservableCollection<string> LicenseFeatures { get; } = new();

    public IReadOnlyList<DashboardLowStockItem> LowStockItems =>
        _snapshot?.LowStockItems ?? Array.Empty<DashboardLowStockItem>();

    public IReadOnlyList<DashboardExpiryItem> ExpiryItems =>
        _snapshot?.ExpiryItems ?? Array.Empty<DashboardExpiryItem>();

    public IReadOnlyList<DashboardTransactionItem> RecentTransactions =>
        _snapshot?.RecentTransactions ?? Array.Empty<DashboardTransactionItem>();

    public IReadOnlyList<DashboardSalesPoint> SalesTimeline =>
        _snapshot?.SalesTimeline ?? Array.Empty<DashboardSalesPoint>();

    public PointCollection SalesChartPoints { get; private set; } = new();
    public PointCollection SalesAreaPoints { get; private set; } = new();

    public string WelcomeText => Translate(
        $"Welcome back, {SignedInUser}  |  {BusinessDateText}",
        $"خوش آمدید، {SignedInUser}  |  {BusinessDateText}",
        $"ښه راغلاست، {SignedInUser}  |  {BusinessDateText}");

    public string SalesTotalText => FormatMoney(_snapshot?.TodaySales ?? 0m);
    public string SalesInvoiceCountText => (_snapshot?.TodayTransactions ?? 0).ToString("N0", CultureInfo.InvariantCulture);
    public string AverageInvoiceText => FormatMoney(
        (_snapshot?.TodayTransactions ?? 0) == 0
            ? 0m
            : (_snapshot?.TodaySales ?? 0m) / _snapshot!.TodayTransactions);

    public string OverviewMedicinesText => (_snapshot?.TotalMedicines ?? 0).ToString("N0", CultureInfo.InvariantCulture);
    public string OverviewBatchesText => (_snapshot?.TotalBatches ?? 0).ToString("N0", CultureInfo.InvariantCulture);
    public string OverviewSuppliersText => (_snapshot?.TotalSuppliers ?? 0).ToString("N0", CultureInfo.InvariantCulture);
    public string OverviewCustomersText => (_snapshot?.ActiveCustomers ?? 0).ToString("N0", CultureInfo.InvariantCulture);
    public string OverviewMonthSalesText => FormatMoney(_snapshot?.MonthSales ?? 0m);
    public string OverviewMonthPurchasesText => FormatMoney(_snapshot?.MonthPurchases ?? 0m);
    public string OverviewStockValueText => FormatMoney(_snapshot?.StockValue ?? 0m);
    public string OverviewCreditDueText => FormatMoney(_snapshot?.OutstandingCredit ?? 0m);

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

            var activation = _activationStore is null
                ? null
                : await _activationStore.LoadAsync();
            var tenant = activation?.Tenant;
            var network = await _networkConfiguration.LoadAsync();

            PharmacyName = string.IsNullOrWhiteSpace(tenant?.Name)
                ? (network.Mode == DeploymentMode.Client ? "Darmaltoon" : "Darmaltoon")
                : tenant.Name;
            PharmacyCode = !string.IsNullOrWhiteSpace(tenant?.Slug)
                ? tenant.Slug
                : activation?.Entitlement.TenantId
                  ?? network.TenantId
                  ?? "—";
            SubscriptionLabel = network.Mode == DeploymentMode.Client
                ? Translate("Main Server", "سرور اصلی", "اصلي سرور")
                : HumanizeHealth(activation?.SubscriptionHealth);
            SignedInUser = _sessions.Current?.Name ?? "—";
            _currency = string.IsNullOrWhiteSpace(tenant?.Currency) ? "AFN" : tenant.Currency;
            ApplyLicensePresentation(activation, network.Mode);

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
            RaiseDashboardDataProperties();
            return;
        }

        Stats.Clear();
        Stats.Add(new DashboardStatViewModel(
            Translate("Total Sales Today", "فروش امروز", "د نن ورځې خرڅلاو"),
            FormatMoney(_snapshot.TodaySales),
            Translate(
                $"{_snapshot.TodayTransactions:N0} invoices",
                $"{_snapshot.TodayTransactions:N0} فاکتور",
                $"{_snapshot.TodayTransactions:N0} بلونه"),
            "reports",
            "#059669",
            "#ECFDF5"));

        Stats.Add(new DashboardStatViewModel(
            Translate("Purchases Today", "خرید امروز", "د نن ورځې پېرود"),
            FormatMoney(_snapshot.TodayPurchases),
            Translate(
                $"{_snapshot.TodayPurchaseCount:N0} purchases",
                $"{_snapshot.TodayPurchaseCount:N0} خرید",
                $"{_snapshot.TodayPurchaseCount:N0} پېرود"),
            "purchases",
            "#0284C7",
            "#F0F9FF"));

        Stats.Add(new DashboardStatViewModel(
            Translate("Low Stock Items", "اقلام کم موجود", "کم زېرمه توکي"),
            _snapshot.LowStockCount.ToString("N0", CultureInfo.InvariantCulture),
            Translate("Items below threshold", "اقلام زیر حد", "توکي تر حد لاندې"),
            "inventory",
            "#F59E0B",
            "#FFF7ED"));

        Stats.Add(new DashboardStatViewModel(
            Translate("Expiring Soon", "نزدیک به انقضا", "ژر ختمېدونکي"),
            _snapshot.NearExpiryCount.ToString("N0", CultureInfo.InvariantCulture),
            Translate("Within 3 months", "در ۳ ماه آینده", "په ۳ میاشتو کې"),
            "batches",
            "#F43F5E",
            "#FFF1F2"));

        Stats.Add(new DashboardStatViewModel(
            Translate("Cash in Drawer", "نقد صندوق", "په صندوق کې نغدې"),
            FormatMoney(_snapshot.CashInDrawer),
            Translate(
                $"Expected: {FormatMoney(_snapshot.ExpectedCash)}",
                $"مورد انتظار: {FormatMoney(_snapshot.ExpectedCash)}",
                $"تمه: {FormatMoney(_snapshot.ExpectedCash)}"),
            "expenses",
            "#6D28D9",
            "#F5F3FF"));

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

        BuildSalesChart();
        RebuildQuickActions();

        var availableModules = new[]
        {
            _snapshot.Availability.Sales,
            _snapshot.Availability.Inventory,
            _snapshot.Availability.Customers,
            _snapshot.Availability.Purchases,
            _snapshot.Availability.Suppliers,
            _snapshot.Availability.Cash,
        }.Count(value => value);

        StatusText = availableModules == 0
            ? Translate(
                "Local database ready. Dashboard cards will populate as operational module data is added.",
                "پایگاه‌داده محلی آماده است. کارت‌های داشبورد با اضافه‌شدن داده‌های عملیاتی تکمیل می‌شوند.",
                "محلي ډیټابیس چمتو دی. د عملیاتي معلوماتو په زیاتېدو سره ډشبورډ ډکېږي.")
            : Translate(
                $"Live local dashboard loaded · {availableModules}/6 data groups available",
                $"داشبورد زنده محلی بارگذاری شد · {availableModules}/6 گروه داده موجود است",
                $"ژوندی محلي ډشبورډ پورته شو · {availableModules}/6 د معلوماتو ډلې شته");

        RaiseDashboardDataProperties();
    }

    private void RebuildQuickActions()
    {
        QuickActions.Clear();

        AddAction("pos", Translate("New Sale (POS)", "فروش جدید", "نوی خرڅلاو"), "pos.sell", true, "#0B82F6", "F2");
        AddAction("purchases", Translate("New Purchase", "خرید جدید", "نوی پېرود"), "purchases.manage", true, "#059669", "F3");
        AddAction("closing", Translate("Daily Closing", "بستن روزانه", "ورځنی تړل"), "daily_closing.perform", true, "#F97316", "F4");
        AddAction("backup", Translate("Backup Now", "پشتیبان‌گیری", "اوس بیک اپ"), "settings.manage", true, "#7C3AED", "F5");
        AddAction("sync", Translate("Sync Now", "همگام‌سازی", "اوس همغږي"), "dashboard.view", _cloudSync is not null, "#0EA5E9", "F6");
        AddAction("medicines", Translate("Medicines", "ادویه", "درمل"), "medicines.manage", true, "#0F8A83", "F7");
        AddAction("inventory", Translate("Inventory", "موجودی", "زېرمه"), "inventory.manage", true, "#475569", "F8");
    }

    private void AddAction(
        string key,
        string label,
        string permission,
        bool isAvailable,
        string accent,
        string shortcut)
    {
        if (_permissions.HasPermission(permission))
        {
            QuickActions.Add(new DashboardQuickActionViewModel(
                key,
                label,
                permission,
                isAvailable,
                accent,
                shortcut));
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

    public async Task SyncNowAsync()
    {
        if (_cloudSync is null)
        {
            SyncStatusText = Translate(
                "Cloud sync is handled by the Main Pharmacy Server in this deployment mode.",
                "همگام‌سازی ابری در این حالت توسط سرور اصلی دواخانه انجام می‌شود.",
                "په دې حالت کې کلاوډ همغږي د اصلي درملتون سرور ترسره کوي.");
            return;
        }

        try
        {
            SyncStatusText = Translate("Synchronizing…", "در حال همگام‌سازی…", "همغږي روانه ده…");
            var result = await _cloudSync.SyncOnceAsync();
            SyncStatusText = result.Message;
            StatusText = result.Message;
        }
        catch (Exception)
        {
            SyncStatusText = Translate(
                "Sync could not complete. Local pharmacy work remains available.",
                "همگام‌سازی کامل نشد. کار محلی دواخانه همچنان در دسترس است.",
                "همغږي بشپړه نه شوه. محلي درملتون کار لا هم شته.");
        }
    }

    private void ApplyLicensePresentation(ActivationState? activation, DeploymentMode mode)
    {
        LicenseFeatures.Clear();

        if (mode == DeploymentMode.Client)
        {
            PlanLabel = Translate("Main Server", "سرور اصلی", "اصلي سرور");
            LicenseValidityText = Translate(
                "License is managed by the Main Pharmacy Server",
                "مجوز توسط سرور اصلی دواخانه مدیریت می‌شود",
                "جواز د اصلي درملتون سرور لخوا اداره کېږي");
            LicenseDaysRemainingText = string.Empty;
            LicenseProgressValue = 100;
        }
        else if (activation is null)
        {
            PlanLabel = Translate("License", "مجوز", "جواز");
            LicenseValidityText = Translate("Activation required", "فعال‌سازی لازم است", "فعالول اړین دي");
            LicenseDaysRemainingText = string.Empty;
            LicenseProgressValue = 0;
        }
        else
        {
            PlanLabel = HumanizeFeature(
                activation.Plan?.Code
                ?? activation.Entitlement.PlanCode
                ?? "Plan");

            var issued = activation.Entitlement.IssuedAt;
            var expires = activation.Entitlement.ExpiresAt;
            var now = _clock.UtcNow;
            var totalSeconds = Math.Max(1, (expires - issued).TotalSeconds);
            var remainingSeconds = Math.Clamp((expires - now).TotalSeconds, 0, totalSeconds);
            LicenseProgressValue = remainingSeconds / totalSeconds * 100d;

            LicenseValidityText = Translate(
                $"Valid until {expires:dd MMMM yyyy}",
                $"معتبر تا {expires:dd MMMM yyyy}",
                $"تر {expires:dd MMMM yyyy} پورې معتبر");

            var days = Math.Max(0, (int)Math.Ceiling((expires - now).TotalDays));
            LicenseDaysRemainingText = Translate(
                $"{days:N0} days remaining",
                $"{days:N0} روز باقی مانده",
                $"{days:N0} ورځې پاتې");
        }

        var featureNames = activation?.Entitlement.Features
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(HumanizeFeature)
            .Take(4)
            .ToList()
            ?? [];

        foreach (var feature in featureNames)
        {
            LicenseFeatures.Add(feature);
        }

        var fallback = new[]
        {
            Translate("Secure offline license", "مجوز امن آفلاین", "خوندي افلاین جواز"),
            Translate("Local-first operation", "عملیات محلی", "محلي لومړنی کار"),
            Translate("Automatic entitlement refresh", "تازه‌سازی خودکار مجوز", "اتومات جواز تازه کول"),
            Translate("Cloud synchronization", "همگام‌سازی ابری", "کلاوډ همغږي"),
        };

        foreach (var item in fallback)
        {
            if (LicenseFeatures.Count >= 4)
            {
                break;
            }

            if (!LicenseFeatures.Contains(item))
            {
                LicenseFeatures.Add(item);
            }
        }

        SyncStatusText = _cloudSync is null
            ? Translate("Sync via Main Server", "همگام‌سازی از سرور اصلی", "همغږي د اصلي سرور له لارې")
            : _cloudSync.LastResult.Message;
    }

    private void BuildSalesChart()
    {
        const double left = 24d;
        const double right = 696d;
        const double top = 18d;
        const double bottom = 148d;

        var source = _snapshot?.SalesTimeline ?? Array.Empty<DashboardSalesPoint>();
        var max = source.Count == 0 ? 1m : Math.Max(1m, source.Max(x => x.Sales));
        var step = source.Count <= 1 ? 0d : (right - left) / (source.Count - 1);

        var line = new PointCollection();
        var area = new PointCollection { new(left, bottom) };

        for (var index = 0; index < source.Count; index++)
        {
            var item = source[index];
            var x = left + (index * step);
            var ratio = (double)(item.Sales / max);
            var y = bottom - ((bottom - top) * ratio);
            var point = new Point(x, y);
            line.Add(point);
            area.Add(point);
        }

        if (source.Count > 0)
        {
            area.Add(new(right, bottom));
        }

        SalesChartPoints = line;
        SalesAreaPoints = area;
        OnPropertyChanged(nameof(SalesChartPoints));
        OnPropertyChanged(nameof(SalesAreaPoints));
    }

    private void RaiseDashboardDataProperties()
    {
        OnPropertyChanged(nameof(WelcomeText));
        OnPropertyChanged(nameof(LowStockItems));
        OnPropertyChanged(nameof(ExpiryItems));
        OnPropertyChanged(nameof(RecentTransactions));
        OnPropertyChanged(nameof(SalesTimeline));
        OnPropertyChanged(nameof(SalesTotalText));
        OnPropertyChanged(nameof(SalesInvoiceCountText));
        OnPropertyChanged(nameof(AverageInvoiceText));
        OnPropertyChanged(nameof(OverviewMedicinesText));
        OnPropertyChanged(nameof(OverviewBatchesText));
        OnPropertyChanged(nameof(OverviewSuppliersText));
        OnPropertyChanged(nameof(OverviewCustomersText));
        OnPropertyChanged(nameof(OverviewMonthSalesText));
        OnPropertyChanged(nameof(OverviewMonthPurchasesText));
        OnPropertyChanged(nameof(OverviewStockValueText));
        OnPropertyChanged(nameof(OverviewCreditDueText));
    }

    private static string HumanizeFeature(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Plan";
        }

        var normalized = value
            .Replace('_', ' ')
            .Replace('-', ' ')
            .Trim()
            .ToLowerInvariant();

        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(normalized);
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
