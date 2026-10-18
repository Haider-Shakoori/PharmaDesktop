using BusinessOS.Pharmacy.Application.Abstractions.Sync;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BusinessOS.Pharmacy.Desktop.Notifications;

/// <summary>
/// Translates cloud synchronization state changes into app notifications
/// without making the sync project depend on desktop UI concerns.
/// </summary>
public sealed class CloudSyncNotificationBridge(
    IServiceProvider services,
    NotificationService notifications) : IHostedService
{
    private ICloudSyncService? _sync;
    private CloudSyncRunState _lastState = CloudSyncRunState.NeverRun;
    private string? _lastImportantMessage;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _sync = services.GetService<ICloudSyncService>();

        if (_sync is not null)
        {
            _sync.ResultUpdated += OnResultUpdated;
            _lastState = _sync.LastResult.State;
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (_sync is not null)
        {
            _sync.ResultUpdated -= OnResultUpdated;
        }

        return Task.CompletedTask;
    }

    private void OnResultUpdated(CloudSyncRunResult result)
    {
        var priorState = _lastState;
        _lastState = result.State;

        switch (result.State)
        {
            case CloudSyncRunState.Synced:
                if (priorState is CloudSyncRunState.Offline
                    or CloudSyncRunState.Failed
                    or CloudSyncRunState.LicenseRejected
                    or CloudSyncRunState.DisabledByPlatform)
                {
                    notifications.ShowConnectivity(
                        "Cloud connection restored. Automatic synchronization has resumed.",
                        NotificationKind.Success,
                        "Connection restored");
                }

                // Routine successful background synchronization is intentionally
                // silent. The worker runs frequently and success toasts become
                // distracting during normal operation. We still notify when a
                // broken connection is restored above, while failures, conflicts,
                // authorization issues and platform pauses remain actionable.
                _lastImportantMessage = null;
                break;

            case CloudSyncRunState.Offline:
                PublishOnce(
                    result.Message,
                    () => notifications.ShowConnectivity(
                        "Cloud connection is unavailable. You can continue working locally; pending changes will retry automatically.",
                        NotificationKind.Warning,
                        "Working offline"));
                break;

            case CloudSyncRunState.Conflicts:
                PublishOnce(
                    result.Message,
                    () => notifications.ShowSync(
                        $"{result.Conflicts:N0} synchronization conflict(s) require review. Local pharmacy data remains protected.",
                        NotificationKind.Warning,
                        "Sync needs attention"));
                break;

            case CloudSyncRunState.LicenseRejected:
                PublishOnce(
                    result.Message,
                    () => notifications.Show(
                        result.Message,
                        NotificationKind.Error,
                        NotificationCategory.Security,
                        "Cloud authorization blocked"));
                break;

            case CloudSyncRunState.Failed:
                PublishOnce(
                    result.Message,
                    () => notifications.ShowSync(
                        result.Message,
                        NotificationKind.Error,
                        "Synchronization failed"));
                break;

            case CloudSyncRunState.WaitingForSignIn:
                if (priorState != CloudSyncRunState.WaitingForSignIn)
                {
                    notifications.ShowSync(
                        result.Message,
                        NotificationKind.Info,
                        "Cloud sync waiting");
                }
                break;

            case CloudSyncRunState.DisabledByPlatform:
                PublishOnce(
                    result.Message,
                    () => notifications.ShowSync(
                        result.Message,
                        NotificationKind.Info,
                        "Cloud sync paused by platform"));
                break;

            case CloudSyncRunState.DisabledForClientTerminal:
                // Client terminals intentionally synchronize through the LAN
                // main server, so this state is informational rather than an
                // actionable desktop notification.
                break;

            case CloudSyncRunState.NeverRun:
            default:
                break;
        }
    }

    private void PublishOnce(string key, Action publish)
    {
        if (string.Equals(
                _lastImportantMessage,
                key,
                StringComparison.Ordinal))
        {
            return;
        }

        _lastImportantMessage = key;
        publish();
    }
}
