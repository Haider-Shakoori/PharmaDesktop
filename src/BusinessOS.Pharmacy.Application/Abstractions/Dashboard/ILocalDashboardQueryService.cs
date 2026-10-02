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
    int NearExpiryDays = 90,
    string Period = "today");

public sealed record DashboardSnapshot(
    DateOnly BusinessDate,
    decimal TodaySales,
    decimal TodayPurchases,
    int TodayPurchaseCount,
    int LowStockCount,
    int NearExpiryCount,
    int ExpiredCount,
    int TodayTransactions,
    decimal CashInDrawer,
    decimal ExpectedCash,
    decimal StockValue,
    int ActiveCustomers,
    decimal OutstandingCredit,
    int TotalMedicines,
    int TotalBatches,
    int TotalSuppliers,
    decimal MonthSales,
    decimal MonthPurchases,
    IReadOnlyList<DashboardAlertItem> Alerts,
    IReadOnlyList<DashboardLowStockItem> LowStockItems,
    IReadOnlyList<DashboardExpiryItem> ExpiryItems,
    IReadOnlyList<DashboardTransactionItem> RecentTransactions,
    IReadOnlyList<DashboardSalesPoint> SalesTimeline,
    DashboardDataAvailability Availability)
{
    public int TotalAlerts => LowStockCount + NearExpiryCount + ExpiredCount;
}

public sealed record DashboardDataAvailability(
    bool Sales,
    bool Inventory,
    bool Customers,
    bool Purchases = false,
    bool Suppliers = false,
    bool Cash = false);

public sealed record DashboardAlertItem(
    string Kind,
    string Name,
    string? MedicineCode,
    string? BatchNumber,
    string? Location,
    decimal AvailableQuantity,
    decimal? Threshold,
    DateOnly? ExpiresAt);

public sealed record DashboardLowStockItem(
    string Medicine,
    decimal CurrentStock,
    decimal MinimumStock,
    string Status);

public sealed record DashboardExpiryItem(
    string Medicine,
    string BatchNumber,
    DateOnly ExpiryDate,
    int DaysLeft,
    string Status);

public sealed record DashboardTransactionItem(
    DateTimeOffset OccurredAt,
    string Type,
    string DocumentNumber,
    string Party,
    int Items,
    decimal Total,
    string PaymentMethod,
    string Status);

public sealed record DashboardSalesPoint(
    int Hour,
    decimal Sales,
    int Invoices,
    string? Label = null);
