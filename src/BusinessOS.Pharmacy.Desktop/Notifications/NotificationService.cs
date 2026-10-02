namespace BusinessOS.Pharmacy.Desktop.Notifications;

public enum NotificationKind
{
    Info,
    Success,
    Warning,
    Error,
}

public sealed record AppNotification(
    string Message,
    NotificationKind Kind,
    DateTimeOffset CreatedAt);

/// <summary>
/// App-wide popup channel. Any view model can raise an informational or error
/// message; the main shell renders and dismisses it.
/// </summary>
public sealed class NotificationService
{
    public event Action<AppNotification>? NotificationRaised;

    public void Show(string message, NotificationKind kind = NotificationKind.Info)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        NotificationRaised?.Invoke(new AppNotification(message.Trim(), kind, DateTimeOffset.Now));
    }

    public void ShowSuccess(string message) => Show(message, NotificationKind.Success);

    public void ShowWarning(string message) => Show(message, NotificationKind.Warning);

    public void ShowError(string message) => Show(message, NotificationKind.Error);
}
