using System.Collections.ObjectModel;
using BusinessOS.Pharmacy.Application.Abstractions.Dashboard;
using BusinessOS.Pharmacy.Desktop.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BusinessOS.Pharmacy.Desktop.Notifications;

public sealed partial class NotificationCenterViewModel : ObservableObject
{
    private const int DefaultLowStockThreshold = 10;
    private const int DefaultNearExpiryDays = 90;

    private readonly ILocalDashboardQueryService _dashboardQueries;
    private readonly NotificationService _notifications;
    private readonly NotificationSettingsStore _settings;
    private readonly NotificationPurchaseExportService _export;
    private UiLanguage _language = UiLanguageCatalog.All[0];

    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private string statusMessage = string.Empty;

    public NotificationCenterViewModel(
        ILocalDashboardQueryService dashboardQueries,
        NotificationService notifications,
        NotificationSettingsStore settings,
        NotificationPurchaseExportService export)
    {
        _dashboardQueries = dashboardQueries;
        _notifications = notifications;
        _settings = settings;
        _export = export;

        RefreshCommand = new AsyncRelayCommand(LoadAsync, () => !IsLoading);
        ExportExcelCommand = new RelayCommand(ExportExcel, () => PurchaseItemCount > 0);
        PrintCommand = new RelayCommand(PrintPurchaseList, () => PurchaseItemCount > 0);
        MarkAllReadCommand = new RelayCommand(MarkAllRead, () => UnreadCount > 0);
        ClearHistoryCommand = new RelayCommand(ClearHistory, () => AllNotifications.Count > 0);

        _notifications.HistoryChanged += OnHistoryChanged;
        RebuildHistory();
    }

    public ObservableCollection<StockPurchaseAlertViewModel> OutOfStockItems { get; } = [];
    public ObservableCollection<StockPurchaseAlertViewModel> LowStockItems { get; } = [];
    public ObservableCollection<NotificationCenterItemViewModel> OperationsItems { get; } = [];
    public ObservableCollection<NotificationCenterItemViewModel> SyncSystemItems { get; } = [];
    public ObservableCollection<NotificationCenterItemViewModel> WarningErrorItems { get; } = [];
    public ObservableCollection<NotificationCenterItemViewModel> AllNotifications { get; } = [];

    public IAsyncRelayCommand RefreshCommand { get; }
    public IRelayCommand ExportExcelCommand { get; }
    public IRelayCommand PrintCommand { get; }
    public IRelayCommand MarkAllReadCommand { get; }
    public IRelayCommand ClearHistoryCommand { get; }

    public string Eyebrow => T("PHARMACY ATTENTION CENTER", "مرکز توجه دواخانه", "د درملتون د پاملرنې مرکز");
    public string Title => T("Notifications", "اعلان‌ها", "خبرتیاوې");
    public string Subtitle => T(
        "Review stock purchasing alerts, operational messages, synchronization events, warnings and errors from one place.",
        "هشدارهای خرید موجودی، پیام‌های عملیاتی، همگام‌سازی، هشدارها و خطاها را در یک محل بررسی کنید.",
        "د زېرمتون د پېرود خبرتیاوې، عملیاتي پیغامونه، همغږي، خبرداری او تېروتنې په یوه ځای کې وګورئ.");

    public string StockTabLabel => T("Stock & Purchase", "موجودی و خرید", "زېرمه او پېرود");
    public string OperationsTabLabel => T("Operations", "عملیات", "عملیات");
    public string SyncTabLabel => T("Sync & System", "همگام‌سازی و سیستم", "همغږي او سیسټم");
    public string WarningTabLabel => T("Warnings & Errors", "هشدارها و خطاها", "خبرداری او تېروتنې");
    public string AllTabLabel => T("All Notifications", "همه اعلان‌ها", "ټولې خبرتیاوې");
    public string ExportLabel => T("Export to Excel", "خروجی Excel", "Excel ته صادرول");
    public string PrintLabel => T("Print purchase list", "چاپ لیست خرید", "د پېرود لست چاپ");
    public string RefreshLabel => T("Refresh", "تازه‌سازی", "تازه کول");
    public string MarkAllReadLabel => T("Mark all read", "همه خوانده شد", "ټول لوستل شوي");
    public string ClearHistoryLabel => T("Clear history", "پاک کردن تاریخچه", "تاریخچه پاکول");

    public int OutOfStockCount => OutOfStockItems.Count;
    public int LowStockCount => LowStockItems.Count;
    public int PurchaseItemCount => OutOfStockItems.Count + LowStockItems.Count;
    public int UnreadCount => AllNotifications.Count(x => !x.IsRead);
    public int SyncSystemCount => SyncSystemItems.Count;
    public bool HasPurchaseItems => PurchaseItemCount > 0;

    public void SetLanguage(UiLanguage language)
    {
        _language = language;
        RaiseLabels();
    }

    public async Task LoadAsync()
    {
        if (IsLoading)
        {
            return;
        }

        IsLoading = true;
        NotifyCommands();

        try
        {
            RebuildHistory();

            OutOfStockItems.Clear();
            LowStockItems.Clear();

            if (_settings.Load().StockAlerts)
            {
                var snapshot = await _dashboardQueries.GetSnapshotAsync(
                    new DashboardQueryOptions(
                        DateOnly.FromDateTime(DateTime.Today),
                        DefaultLowStockThreshold,
                        DefaultNearExpiryDays,
                        "today"));

                foreach (var item in snapshot.LowStockItems
                             .OrderBy(x => x.CurrentStock)
                             .ThenBy(x => x.Medicine))
                {
                    var suggested = Math.Max(
                        0m,
                        item.MinimumStock - item.CurrentStock);

                    var alert = new StockPurchaseAlertViewModel(
                        item.Medicine,
                        item.CurrentStock <= 0m
                            ? T("Out of stock", "تمام‌شده", "خلاص شوی")
                            : T("Low stock", "کم‌موجود", "کم زېرمه"),
                        item.CurrentStock,
                        item.MinimumStock,
                        suggested);

                    if (item.CurrentStock <= 0m)
                    {
                        OutOfStockItems.Add(alert);
                    }
                    else
                    {
                        LowStockItems.Add(alert);
                    }
                }
            }

            StatusMessage = PurchaseItemCount == 0
                ? T(
                    "No medicines currently require purchasing based on the configured stock threshold.",
                    "در حال حاضر هیچ دوا بر اساس حد موجودی نیاز به خرید ندارد.",
                    "اوس مهال د ټاکل شوي زېرمتون حد له مخې هېڅ درمل پېرود ته اړتیا نه لري.")
                : T(
                    $"{PurchaseItemCount:N0} medicine(s) currently require purchasing attention.",
                    $"{PurchaseItemCount:N0} دوا در حال حاضر نیاز به توجه خرید دارد.",
                    $"{PurchaseItemCount:N0} درمل اوس د پېرود پاملرنې ته اړتیا لري.");
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
            _notifications.ShowError(
                T(
                    $"Notification Center could not refresh stock alerts: {exception.Message}",
                    $"هشدارهای موجودی تازه نشد: {exception.Message}",
                    $"د زېرمتون خبرتیاوې تازه نه شوې: {exception.Message}"),
                NotificationCategory.System);
        }
        finally
        {
            IsLoading = false;
            RaiseCounts();
            NotifyCommands();
        }
    }

    private void ExportExcel()
    {
        try
        {
            var path = _export.ExportToExcel(BuildPurchaseList());
            if (path is null)
            {
                return;
            }

            StatusMessage = T(
                $"Purchase list exported to {path}.",
                $"لیست خرید در {path} ذخیره شد.",
                $"د پېرود لست په {path} کې صادر شو.");

            _notifications.ShowSuccess(
                T(
                    "Stock purchase list exported to Excel.",
                    "لیست خرید موجودی به Excel صادر شد.",
                    "د زېرمتون د پېرود لست Excel ته صادر شو."),
                NotificationCategory.Inventory);
        }
        catch (Exception exception)
        {
            _notifications.ShowError(
                T(
                    $"Excel export failed: {exception.Message}",
                    $"خروجی Excel ناموفق بود: {exception.Message}",
                    $"Excel صادرول ناکام شول: {exception.Message}"),
                NotificationCategory.Inventory);
        }
    }

    private void PrintPurchaseList()
    {
        try
        {
            if (_export.Print(BuildPurchaseList()))
            {
                StatusMessage = T(
                    "Purchase list sent to the selected printer.",
                    "لیست خرید به چاپگر انتخاب‌شده ارسال شد.",
                    "د پېرود لست ټاکل شوي چاپګر ته ولېږل شو.");

                _notifications.ShowSuccess(
                    T(
                        "Stock purchase list sent to printer.",
                        "لیست خرید موجودی برای چاپ ارسال شد.",
                        "د زېرمتون د پېرود لست چاپګر ته ولېږل شو."),
                    NotificationCategory.Inventory);
            }
        }
        catch (Exception exception)
        {
            _notifications.ShowError(
                T(
                    $"Printing failed: {exception.Message}",
                    $"چاپ ناموفق بود: {exception.Message}",
                    $"چاپ ناکام شو: {exception.Message}"),
                NotificationCategory.Inventory);
        }
    }

    private IReadOnlyList<StockPurchaseAlert> BuildPurchaseList() =>
        OutOfStockItems
            .Concat(LowStockItems)
            .Select(x => new StockPurchaseAlert(
                x.Medicine,
                x.Status,
                x.CurrentStock,
                x.MinimumStock,
                x.SuggestedPurchase))
            .ToList();

    private void MarkAllRead()
    {
        _notifications.MarkAllRead();
        StatusMessage = T(
            "All notification history marked as read.",
            "همه اعلان‌ها خوانده‌شده علامت‌گذاری شد.",
            "ټولې خبرتیاوې لوستل شوې وټاکل شوې.");
    }

    private void ClearHistory()
    {
        _notifications.ClearHistory();
        StatusMessage = T(
            "Notification history cleared. Current stock alerts remain available.",
            "تاریخچه اعلان‌ها پاک شد. هشدارهای فعلی موجودی باقی می‌ماند.",
            "د خبرتیاوو تاریخچه پاکه شوه. د زېرمتون اوسني خبرتیاوې پاتې دي.");
    }

    private void OnHistoryChanged()
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;

        if (dispatcher is null)
        {
            RebuildHistory();
            return;
        }

        _ = dispatcher.InvokeAsync(RebuildHistory);
    }

    private void RebuildHistory()
    {
        AllNotifications.Clear();
        OperationsItems.Clear();
        SyncSystemItems.Clear();
        WarningErrorItems.Clear();

        foreach (var item in _notifications.GetHistory()
                     .OrderByDescending(x => x.CreatedAt))
        {
            var viewModel = new NotificationCenterItemViewModel(item);
            AllNotifications.Add(viewModel);

            if (item.Category is NotificationCategory.Sync
                or NotificationCategory.Connectivity
                or NotificationCategory.System
                or NotificationCategory.Security)
            {
                SyncSystemItems.Add(viewModel);
            }

            if (item.Category is NotificationCategory.Operations
                or NotificationCategory.Inventory)
            {
                OperationsItems.Add(viewModel);
            }

            if (item.Kind is NotificationKind.Warning
                or NotificationKind.Error)
            {
                WarningErrorItems.Add(viewModel);
            }
        }

        RaiseCounts();
        NotifyCommands();
    }

    private void RaiseCounts()
    {
        OnPropertyChanged(nameof(OutOfStockCount));
        OnPropertyChanged(nameof(LowStockCount));
        OnPropertyChanged(nameof(PurchaseItemCount));
        OnPropertyChanged(nameof(UnreadCount));
        OnPropertyChanged(nameof(SyncSystemCount));
        OnPropertyChanged(nameof(HasPurchaseItems));
    }

    private void NotifyCommands()
    {
        RefreshCommand.NotifyCanExecuteChanged();
        ExportExcelCommand.NotifyCanExecuteChanged();
        PrintCommand.NotifyCanExecuteChanged();
        MarkAllReadCommand.NotifyCanExecuteChanged();
        ClearHistoryCommand.NotifyCanExecuteChanged();
    }

    private void RaiseLabels()
    {
        OnPropertyChanged(nameof(Eyebrow));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(StockTabLabel));
        OnPropertyChanged(nameof(OperationsTabLabel));
        OnPropertyChanged(nameof(SyncTabLabel));
        OnPropertyChanged(nameof(WarningTabLabel));
        OnPropertyChanged(nameof(AllTabLabel));
        OnPropertyChanged(nameof(ExportLabel));
        OnPropertyChanged(nameof(PrintLabel));
        OnPropertyChanged(nameof(RefreshLabel));
        OnPropertyChanged(nameof(MarkAllReadLabel));
        OnPropertyChanged(nameof(ClearHistoryLabel));
    }

    private string T(string english, string dari, string pashto) =>
        _language.Code switch
        {
            "fa" => dari,
            "ps" => pashto,
            _ => english,
        };
}

public sealed record StockPurchaseAlertViewModel(
    string Medicine,
    string Status,
    decimal CurrentStock,
    decimal MinimumStock,
    decimal SuggestedPurchase);

public sealed class NotificationCenterItemViewModel
{
    public NotificationCenterItemViewModel(AppNotification notification)
    {
        Id = notification.Id;
        Message = notification.Message;
        Kind = notification.Kind;
        Category = notification.Category;
        CreatedAt = notification.CreatedAt;
        Reference = notification.Reference ?? string.Empty;
        IsRead = notification.IsRead;

        Title = !string.IsNullOrWhiteSpace(notification.Title)
            ? notification.Title!
            : notification.Kind switch
            {
                NotificationKind.Success => "Success",
                NotificationKind.Warning => "Attention",
                NotificationKind.Error => "Error",
                _ => "Information",
            };
    }

    public string Id { get; }
    public string Title { get; }
    public string Message { get; }
    public NotificationKind Kind { get; }
    public NotificationCategory Category { get; }
    public DateTimeOffset CreatedAt { get; }
    public string Reference { get; }
    public bool IsRead { get; }
}
