namespace BusinessOS.Pharmacy.Application.Abstractions.DailyClosing;

public interface IDailyClosingService
{
    DateOnly BusinessDate();
    Task<DailyClosingReferenceData> GetReferenceDataAsync(CancellationToken cancellationToken = default);
    Task<DailyClosingWorkspace> GetWorkspaceAsync(string stockLocationId, DateOnly? businessDate = null, CancellationToken cancellationToken = default);
    Task<CashierShiftItem> OpenShiftAsync(string stockLocationId, decimal openingCash, CancellationToken cancellationToken = default);
    Task<CashierShiftItem> CloseShiftAsync(string shiftId, decimal countedCash, string? notes, CancellationToken cancellationToken = default);
    Task<DailyClosingItem> FinalizeAsync(string stockLocationId, decimal? countedCash, string? notes, CancellationToken cancellationToken = default);
    Task<DailyClosingItem> ApproveAsync(string closingId, CancellationToken cancellationToken = default);
    Task<DailyClosingItem> ReopenAsync(string closingId, string reason, CancellationToken cancellationToken = default);
    Task<bool> SalesBlockedAsync(string stockLocationId, DateOnly? businessDate = null, CancellationToken cancellationToken = default);
}

public sealed record DailyClosingLocationItem(string Id, string Name, string BranchName, bool IsDefault);
public sealed record DailyClosingReferenceData(IReadOnlyList<DailyClosingLocationItem> Locations);
public sealed record DailyClosingSnapshot(DateOnly BusinessDate, decimal GrossSales, decimal DiscountTotal, decimal ReturnsTotal, decimal CashCollected, decimal BankCollected, decimal MobileCollected, decimal CreditSales, decimal OpeningCash, decimal ExpectedCash);
public sealed record CashierShiftItem(string Id, string StockLocationId, string UserId, DateOnly BusinessDate, string Status, decimal OpeningCash, decimal? ExpectedCash, decimal? CountedCash, decimal? Variance, DateTimeOffset OpenedAt, DateTimeOffset? ClosedAt, string? ClosingNotes);
public sealed record DailyClosingEventItem(string Id, string EventType, string? ActorId, string? Reason, string? SnapshotJson, DateTimeOffset OccurredAt);
public sealed record DailyClosingItem(string Id, string StockLocationId, DateOnly BusinessDate, string Status, DailyClosingSnapshot Snapshot, decimal? CountedCash, decimal? Variance, string? FinalizedBy, string? ApprovedBy, string? ReopenedBy, DateTimeOffset? FinalizedAt, DateTimeOffset? ApprovedAt, DateTimeOffset? ReopenedAt, string? ClosingNotes, string? ReopenReason, IReadOnlyList<DailyClosingEventItem> Events);
public sealed record DailyClosingWorkspace(DailyClosingSnapshot Snapshot, DailyClosingItem? Closing, CashierShiftItem? MyOpenShift, IReadOnlyList<CashierShiftItem> Shifts);
