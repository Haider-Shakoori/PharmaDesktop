using System.Globalization;
using BusinessOS.Pharmacy.Application.Abstractions.Sync;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BusinessOS.Pharmacy.Desktop.Updates;

public sealed partial class SyncConflictItemViewModel : ObservableObject
{
    public SyncConflictItemViewModel(CloudSyncConflictItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        IdempotencyKey = item.IdempotencyKey;
        EventType = item.EventType;
        ErrorCode = item.ErrorCode ?? "rejected";
        ErrorMessage = item.ErrorMessage ?? "BusinessOS cloud rejected this synchronization event.";
        Reference = BuildReference(item);
        RaisedText = item.CreatedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
        CanRetry = item.CanRetry;
    }

    public string IdempotencyKey { get; }
    public string EventType { get; }
    public string ErrorCode { get; }
    public string ErrorMessage { get; }
    public string Reference { get; }
    public string RaisedText { get; }
    public bool CanRetry { get; }

    private static string BuildReference(CloudSyncConflictItem item)
    {
        var parts = new List<string>(3);
        if (!string.IsNullOrWhiteSpace(item.LocalId))
            parts.Add($"Record {item.LocalId}");
        if (!string.IsNullOrWhiteSpace(item.BusinessDate))
            parts.Add($"Business date {item.BusinessDate}");
        if (!string.IsNullOrWhiteSpace(item.ActorUserId))
            parts.Add($"Cashier {item.ActorUserId}");

        return parts.Count == 0
            ? "—"
            : string.Join(" · ", parts);
    }
}