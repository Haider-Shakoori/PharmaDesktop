namespace BusinessOS.Pharmacy.Application.Abstractions.Dashboard;

public interface ILocalDashboardQueryService
{
    Task<DashboardSnapshot> GetSnapshotAsync(
        DashboardQueryOptions options,
        CancellationToken cancellationToken = default);
}

public sealed record DashboardQueryOptions(
    DateOnly BusinessDate,
    int LowStockThreshold = 10,
    int NearExpiryDays = 90);

public sealed record DashboardSnapshot(
    DateOnly BusinessDate,
    decimal TodaySales,
    int LowStockCount,
    int NearExpiryCount,
    int ExpiredCount,
    int TodayTransactions,
    decimal StockValue,
    int ActiveCustomers,
    decimal OutstandingCredit,
    IReadOnlyList<DashboardAlertItem> Alerts,
    DashboardDataAvailability Availability)
{
    public int TotalAlerts => LowStockCount + NearExpiryCount + ExpiredCount;
}

public sealed record DashboardDataAvailability(
    bool Sales,
    bool Inventory,
    bool Customers);

public sealed record DashboardAlertItem(
    string Kind,
    string Name,
    string? MedicineCode,
    string? BatchNumber,
    string? Location,
    decimal AvailableQuantity,
    decimal? Threshold,
    DateOnly? ExpiresAt);
