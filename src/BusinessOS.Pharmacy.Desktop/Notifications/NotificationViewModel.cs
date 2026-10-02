using CommunityToolkit.Mvvm.Input;

namespace BusinessOS.Pharmacy.Desktop.Notifications;

public sealed class NotificationViewModel
{
    public NotificationViewModel(AppNotification notification, Action<NotificationViewModel> dismiss)
    {
        Message = notification.Message;
        Kind = notification.Kind;
        CreatedAt = notification.CreatedAt;
        DismissCommand = new RelayCommand(() => dismiss(this));
    }

    public string Message { get; }
    public NotificationKind Kind { get; }
    public DateTimeOffset CreatedAt { get; }
    public IRelayCommand DismissCommand { get; }

    public string Title => Kind switch
    {
        NotificationKind.Success => "Success",
        NotificationKind.Warning => "Attention",
        NotificationKind.Error => "Error",
        _ => "Information",
    };

    public bool IsError => Kind == NotificationKind.Error;
    public bool IsSuccess => Kind == NotificationKind.Success;
    public bool IsWarning => Kind == NotificationKind.Warning;
    public bool IsInfo => Kind == NotificationKind.Info;
}
