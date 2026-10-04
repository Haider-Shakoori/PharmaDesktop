namespace BusinessOS.Pharmacy.Desktop.Notifications;

public enum NotificationKind
{
    Info,
    Success,
    Warning,
    Error,
}

public enum NotificationCategory
{
    Operations,
    Inventory,
    Sync,
    Connectivity,
    System,
    Security,
}

public sealed record AppNotification(
    string Id,
    string Message,
    NotificationKind Kind,
    DateTimeOffset CreatedAt,
    NotificationCategory Category = NotificationCategory.Operations,
    string? Title = null,
    string? Reference = null,
    bool IsRead = false)
{
    public AppNotification(
        string message,
        NotificationKind kind,
        DateTimeOffset createdAt)
        : this(
            Guid.NewGuid().ToString("N"),
            message,
            kind,
            createdAt)
    {
    }
}

/// <summary>
/// App-wide notification channel. Every event is persisted to local history.
/// Workstation settings only control whether a transient toast is shown.
/// </summary>
public sealed class NotificationService
{
    private readonly NotificationHistoryStore _history;
    private readonly NotificationSettingsStore _settings;

    public NotificationService(
        NotificationHistoryStore history,
        NotificationSettingsStore settings)
    {
        _history = history;
        _settings = settings;
    }

    public event Action<AppNotification>? NotificationRaised;
    public event Action? HistoryChanged;

    public NotificationSettings GetSettings() => _settings.Load();

    public IReadOnlyList<AppNotification> GetHistory() =>
        _history.Load();

    public void Show(
        string message,
        NotificationKind kind = NotificationKind.Info,
        NotificationCategory category = NotificationCategory.Operations,
        string? title = null,
        string? reference = null,
        bool forceToast = false)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var notification = new AppNotification(
            Guid.NewGuid().ToString("N"),
            message.Trim(),
            kind,
            DateTimeOffset.Now,
            category,
            string.IsNullOrWhiteSpace(title) ? null : title.Trim(),
            string.IsNullOrWhiteSpace(reference) ? null : reference.Trim());

        _history.Add(notification);
        HistoryChanged?.Invoke();

        if (forceToast || ShouldShowToast(notification, _settings.Load()))
        {
            NotificationRaised?.Invoke(notification);
        }
    }

    public void ShowSuccess(
        string message,
        NotificationCategory category = NotificationCategory.Operations,
        string? title = null,
        string? reference = null) =>
        Show(
            message,
            NotificationKind.Success,
            category,
            title,
            reference);

    public void ShowWarning(
        string message,
        NotificationCategory category = NotificationCategory.Operations,
        string? title = null,
        string? reference = null) =>
        Show(
            message,
            NotificationKind.Warning,
            category,
            title,
            reference);

    public void ShowError(
        string message,
        NotificationCategory category = NotificationCategory.Operations,
        string? title = null,
        string? reference = null) =>
        Show(
            message,
            NotificationKind.Error,
            category,
            title,
            reference);

    public void ShowSync(
        string message,
        NotificationKind kind = NotificationKind.Info,
        string? title = null) =>
        Show(
            message,
            kind,
            NotificationCategory.Sync,
            title);

    public void ShowConnectivity(
        string message,
        NotificationKind kind = NotificationKind.Info,
        string? title = null) =>
        Show(
            message,
            kind,
            NotificationCategory.Connectivity,
            title);

    public void MarkAllRead()
    {
        _history.MarkAllRead();
        HistoryChanged?.Invoke();
    }

    public void ClearHistory()
    {
        _history.Clear();
        HistoryChanged?.Invoke();
    }

    private static bool ShouldShowToast(
        AppNotification notification,
        NotificationSettings settings)
    {
        if (!settings.EnableToasts)
        {
            return false;
        }

        if (notification.Category == NotificationCategory.Sync &&
            !settings.SyncToasts)
        {
            return false;
        }

        if (notification.Category == NotificationCategory.Connectivity &&
            !settings.ConnectivityToasts)
        {
            return false;
        }

        return notification.Kind switch
        {
            NotificationKind.Success => settings.SuccessToasts,
            NotificationKind.Warning => settings.WarningToasts,
            NotificationKind.Error => settings.ErrorToasts,
            _ => settings.InformationToasts,
        };
    }
}
