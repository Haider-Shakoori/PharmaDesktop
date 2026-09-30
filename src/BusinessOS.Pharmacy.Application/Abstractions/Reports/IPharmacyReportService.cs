namespace BusinessOS.Pharmacy.Application.Abstractions.Reports;

public interface IPharmacyReportService
{
    ReportRange ResolveRange(DateOnly? from = null, DateOnly? to = null);
    Task<PharmacyReportWorkspace> GetAsync(ReportRange range, CancellationToken cancellationToken = default);
    Task<ReportCsv> BuildCsvAsync(string type, ReportRange range, CancellationToken cancellationToken = default);
}

public sealed record ReportRange(DateOnly From, DateOnly To);

public sealed record ReportSummary(
    decimal Sales,
    decimal Returns,
    decimal NetSales,
    decimal Discounts,
    decimal GrossProfit,
    decimal Collections,
    decimal Purchases,
    decimal Receivables,
    decimal Payables,
    decimal StockValue);

public sealed record ReportSaleItem(
    string Id,
    string SaleNumber,
    DateOnly BusinessDate,
    DateTimeOffset? CompletedAt,
    string? CustomerName,
    string StockLocationName,
    decimal GrandTotal,
    decimal DiscountTotal,
    decimal PaidTotal,
    decimal DueTotal,
    string PaymentStatus);

public sealed record ReportReturnItem(
    string Id,
    string ReturnNumber,
    string SaleNumber,
    DateOnly BusinessDate,
    DateTimeOffset? CompletedAt,
    decimal RefundTotal,
    string Reason);

public sealed record ReportPurchaseItem(
    string Id,
    string InvoiceNumber,
    string SupplierName,
    DateOnly InvoiceDate,
    decimal GrandTotal,
    decimal PaidTotal,
    decimal BalanceDue,
    string Status);

public sealed record ReportMovementItem(
    string Id,
    DateTimeOffset OccurredAt,
    string MedicineName,
    string? BatchNumber,
    string StockLocationName,
    string MovementType,
    decimal QuantityDelta,
    decimal BalanceAfter,
    string SourceType,
    string SourceId);

public sealed record ReportNearExpiryItem(
    string ProductBatchId,
    string MedicineName,
    string? Strength,
    string? BatchNumber,
    string StockLocationName,
    decimal AvailableQuantity,
    DateOnly ExpiresAt,
    int DaysRemaining);

public sealed record ReportLowStockItem(
    string MedicineId,
    string MedicineCode,
    string BrandName,
    string? Strength,
    decimal AvailableStock,
    decimal ReorderLevel);

public sealed record PharmacyReportWorkspace(
    ReportRange Range,
    ReportSummary Summary,
    IReadOnlyList<ReportSaleItem> Sales,
    IReadOnlyList<ReportReturnItem> Returns,
    IReadOnlyList<ReportPurchaseItem> Purchases,
    IReadOnlyList<ReportMovementItem> Movements,
    IReadOnlyList<ReportNearExpiryItem> NearExpiry,
    IReadOnlyList<ReportLowStockItem> LowStock);

public sealed record ReportCsv(string FileName, string Content);
