using CommunityToolkit.Mvvm.Input;

namespace BusinessOS.Pharmacy.Desktop.Notifications;

public sealed class NotificationViewModel
{
    public NotificationViewModel(
        AppNotification notification,
        Action<NotificationViewModel> dismiss)
    {
        Message = notification.Message;
        Kind = notification.Kind;
        Category = notification.Category;
        CreatedAt = notification.CreatedAt;
        Reference = notification.Reference ?? string.Empty;
        Title = !string.IsNullOrWhiteSpace(notification.Title)
            ? notification.Title!
            : notification.Kind switch
            {
                NotificationKind.Success => "Success",
                NotificationKind.Warning => "Attention",
                NotificationKind.Error => "Error",
                _ => "Information",
            };

        DismissCommand = new RelayCommand(() => dismiss(this));
    }

    public string Message { get; }
    public NotificationKind Kind { get; }
    public NotificationCategory Category { get; }
    public DateTimeOffset CreatedAt { get; }
    public string Reference { get; }
    public string Title { get; }
    public IRelayCommand DismissCommand { get; }

    public bool IsError => Kind == NotificationKind.Error;
    public bool IsSuccess => Kind == NotificationKind.Success;
    public bool IsWarning => Kind == NotificationKind.Warning;
    public bool IsInfo => Kind == NotificationKind.Info;
}
