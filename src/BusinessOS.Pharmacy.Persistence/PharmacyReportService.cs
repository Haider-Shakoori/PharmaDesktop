using System.Globalization;
using System.Text;
using BusinessOS.Pharmacy.Application.Abstractions.Authentication;
using BusinessOS.Pharmacy.Application.Abstractions.Reports;
using BusinessOS.Pharmacy.Application.Abstractions.Time;
using BusinessOS.Pharmacy.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace BusinessOS.Pharmacy.Persistence;

public sealed class PharmacyReportService : IPharmacyReportService
{
    private static readonly TimeSpan KabulOffset = TimeSpan.FromMinutes(270);
    private static readonly string[] ExportTypes = ["sales", "returns", "purchases", "movements"];

    private readonly IDbContextFactory<PharmacyDbContext> _contextFactory;
    private readonly IPermissionAuthorizer _permissions;
    private readonly IClock _clock;

    public PharmacyReportService(
        IDbContextFactory<PharmacyDbContext> contextFactory,
        IPermissionAuthorizer permissions,
        IClock clock)
    {
        _contextFactory = contextFactory;
        _permissions = permissions;
        _clock = clock;
    }

    public ReportRange ResolveRange(DateOnly? from = null, DateOnly? to = null)
    {
        var end = to ?? StockLedger.BusinessDate(_clock.UtcNow);
        var start = from ?? new DateOnly(end.Year, end.Month, 1);

        return start <= end
            ? new ReportRange(start, end)
            : new ReportRange(end, start);
    }

    public async Task<PharmacyReportWorkspace> GetAsync(
        ReportRange range,
        CancellationToken cancellationToken = default)
    {
        _permissions.Demand("reports.view");
        range = ResolveRange(range.From, range.To);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var sales = await context.Set<SaleEntity>()
            .AsNoTracking()
            .Include(x => x.Customer)
            .Include(x => x.StockLocation)
            .Where(x =>
                x.Status == "completed" &&
                x.BusinessDate >= range.From &&
                x.BusinessDate <= range.To)
            .OrderByDescending(x => x.BusinessDate)
            .ThenByDescending(x => x.Id)
            .ToListAsync(cancellationToken);

        var saleIds = sales.Select(x => x.Id).ToList();

        var saleLines = await context.Set<SaleLineEntity>()
            .AsNoTracking()
            .Where(x => saleIds.Contains(x.SaleId))
            .ToListAsync(cancellationToken);

        var returns = await context.Set<SaleReturnEntity>()
            .AsNoTracking()
            .Include(x => x.Sale)
            .Where(x =>
                x.Status == "completed" &&
                x.BusinessDate >= range.From &&
                x.BusinessDate <= range.To)
            .OrderByDescending(x => x.BusinessDate)
            .ThenByDescending(x => x.Id)
            .ToListAsync(cancellationToken);

        var returnIds = returns.Select(x => x.Id).ToList();
        var returnAllocations = await context.Set<SaleReturnAllocationEntity>()
            .AsNoTracking()
            .Include(x => x.SaleReturnLine)
            .Include(x => x.SaleBatchAllocation)
            .Where(x => returnIds.Contains(x.SaleReturnLine.SaleReturnId))
            .ToListAsync(cancellationToken);

        var payments = await context.Set<SalePaymentEntity>()
            .AsNoTracking()
            .Where(x => saleIds.Contains(x.SaleId))
            .ToListAsync(cancellationToken);

        var refunds = await context.Set<SaleReturnRefundEntity>()
            .AsNoTracking()
            .Where(x => returnIds.Contains(x.SaleReturnId))
            .ToListAsync(cancellationToken);

        var purchases = await context.Set<PurchaseInvoiceEntity>()
            .AsNoTracking()
            .Include(x => x.Supplier)
            .Where(x => x.InvoiceDate >= range.From && x.InvoiceDate <= range.To)
            .OrderByDescending(x => x.InvoiceDate)
            .ThenByDescending(x => x.Id)
            .ToListAsync(cancellationToken);

        var allReceivables = await context.Set<SaleEntity>()
            .AsNoTracking()
            .Where(x => x.Status == "completed" && x.DueTotal > 0m)
            .Select(x => x.DueTotal)
            .ToListAsync(cancellationToken);

        var allPayables = await context.Set<PurchaseInvoiceEntity>()
            .AsNoTracking()
            .Where(x => x.BalanceDue > 0m)
            .Select(x => x.BalanceDue)
            .ToListAsync(cancellationToken);

        var batches = await context.Set<ProductBatchEntity>()
            .AsNoTracking()
            .Include(x => x.Medicine)
            .Include(x => x.StockLocation)
            .Where(x => x.AvailableQuantity > 0m)
            .ToListAsync(cancellationToken);

        var medicines = await context.Set<MedicineEntity>()
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.BrandName)
            .ToListAsync(cancellationToken);

        var fromInstant = new DateTimeOffset(
            range.From.ToDateTime(TimeOnly.MinValue),
            KabulOffset).ToUniversalTime();
        var toExclusive = new DateTimeOffset(
            range.To.AddDays(1).ToDateTime(TimeOnly.MinValue),
            KabulOffset).ToUniversalTime();

        var movementRows = await context.Set<StockMovementEntity>()
            .AsNoTracking()
            .Include(x => x.ProductBatch)
                .ThenInclude(x => x.Medicine)
            .Include(x => x.ProductBatch)
                .ThenInclude(x => x.StockLocation)
            .ToListAsync(cancellationToken);

        var movements = movementRows
            .Where(x => x.OccurredAt >= fromInstant && x.OccurredAt < toExclusive)
            .OrderByDescending(x => x.OccurredAt)
            .Take(1000)
            .ToList();

        var salesTotal = Money(sales.Sum(x => x.GrandTotal));
        var returnsTotal = Money(returns.Sum(x => x.RefundTotal));
        var discounts = Money(sales.Sum(x => x.DiscountTotal));
        var cogs = Money(saleLines.Sum(x => x.CostTotal));
        var returnedCost = Money(returnAllocations.Sum(
            x => x.Quantity * x.SaleBatchAllocation.UnitCost));
        var netRevenue = Money(salesTotal - returnsTotal);
        var netCogs = Money(cogs - returnedCost);
        var collections = Money(
            payments.Where(x => x.Method != "credit").Sum(x => x.Amount) -
            refunds.Where(x => x.Method != "credit").Sum(x => x.Amount));
        var purchaseTotal = Money(purchases.Sum(x => x.GrandTotal));
        var receivables = Money(allReceivables.Sum());
        var payables = Money(allPayables.Sum());
        var stockValue = Money(batches.Sum(x => x.AvailableQuantity * x.PurchaseCost));

        var summary = new ReportSummary(
            salesTotal,
            returnsTotal,
            netRevenue,
            discounts,
            Money(netRevenue - netCogs),
            collections,
            purchaseTotal,
            receivables,
            payables,
            stockValue);

        var saleItems = sales
            .Select(x => new ReportSaleItem(
                x.Id,
                x.SaleNumber,
                x.BusinessDate,
                x.CompletedAt,
                x.Customer?.Name,
                x.StockLocation.Name,
                x.GrandTotal,
                x.DiscountTotal,
                x.PaidTotal,
                x.DueTotal,
                x.PaymentStatus))
            .Take(250)
            .ToList();

        var returnItems = returns
            .Select(x => new ReportReturnItem(
                x.Id,
                x.ReturnNumber,
                x.Sale.SaleNumber,
                x.BusinessDate,
                x.CompletedAt,
                x.RefundTotal,
                x.Reason))
            .Take(250)
            .ToList();

        var purchaseItems = purchases
            .Select(x => new ReportPurchaseItem(
                x.Id,
                x.InvoiceNumber,
                x.Supplier.Name,
                x.InvoiceDate,
                x.GrandTotal,
                x.PaidTotal,
                x.BalanceDue,
                x.Status))
            .Take(250)
            .ToList();

        var movementItems = movements
            .Select(x => new ReportMovementItem(
                x.Id,
                x.OccurredAt,
                x.ProductBatch.Medicine.BrandName,
                x.ProductBatch.BatchNumber,
                x.ProductBatch.StockLocation.Name,
                x.MovementType,
                x.QuantityDelta,
                x.BalanceAfter,
                x.SourceType,
                x.SourceId))
            .Take(500)
            .ToList();

        var today = StockLedger.BusinessDate(_clock.UtcNow);
        var nearExpiryEnd = today.AddDays(90);
        var nearExpiry = batches
            .Where(x =>
                x.ExpiresAt is not null &&
                x.ExpiresAt.Value >= today &&
                x.ExpiresAt.Value <= nearExpiryEnd)
            .OrderBy(x => x.ExpiresAt)
            .Take(100)
            .Select(x => new ReportNearExpiryItem(
                x.Id,
                x.Medicine.BrandName,
                x.Medicine.Strength,
                x.BatchNumber,
                x.StockLocation.Name,
                x.AvailableQuantity,
                x.ExpiresAt!.Value,
                x.ExpiresAt.Value.DayNumber - today.DayNumber))
            .ToList();

        var stockByMedicine = batches
            .GroupBy(x => x.MedicineId)
            .ToDictionary(
                x => x.Key,
                x => Quantity(x.Sum(y => y.AvailableQuantity)));

        var lowStock = medicines
            .Select(x => new ReportLowStockItem(
                x.Id,
                x.MedicineCode,
                x.BrandName,
                x.Strength,
                stockByMedicine.GetValueOrDefault(x.Id),
                x.ReorderLevel))
            .Where(x => x.AvailableStock <= x.ReorderLevel)
            .Take(100)
            .ToList();

        return new PharmacyReportWorkspace(
            range,
            summary,
            saleItems,
            returnItems,
            purchaseItems,
            movementItems,
            nearExpiry,
            lowStock);
    }

    public async Task<ReportCsv> BuildCsvAsync(
        string type,
        ReportRange range,
        CancellationToken cancellationToken = default)
    {
        _permissions.Demand("reports.view");
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        type = type.Trim().ToLowerInvariant();

        if (!ExportTypes.Contains(type))
        {
            throw new ArgumentException("Report export type must be sales, returns, purchases, or movements.");
        }

        var report = await GetAsync(range, cancellationToken);
        var rows = new List<IReadOnlyList<object?>>();

        IReadOnlyList<string> headers;
        switch (type)
        {
            case "sales":
                headers = ["Sale Number", "Business Date", "Total", "Discount", "Paid", "Due", "Payment Status"];
                rows.AddRange(report.Sales
                    .OrderBy(x => x.BusinessDate)
                    .ThenBy(x => x.SaleNumber)
                    .Select(x => (IReadOnlyList<object?>)
                    [
                        x.SaleNumber, x.BusinessDate, x.GrandTotal, x.DiscountTotal,
                        x.PaidTotal, x.DueTotal, x.PaymentStatus
                    ]));
                break;
            case "returns":
                headers = ["Return Number", "Business Date", "Refund Total", "Reason"];
                rows.AddRange(report.Returns
                    .OrderBy(x => x.BusinessDate)
                    .ThenBy(x => x.ReturnNumber)
                    .Select(x => (IReadOnlyList<object?>)
                    [
                        x.ReturnNumber, x.BusinessDate, x.RefundTotal, x.Reason
                    ]));
                break;
            case "purchases":
                headers = ["Invoice Number", "Supplier", "Invoice Date", "Total", "Paid", "Balance Due"];
                rows.AddRange(report.Purchases
                    .OrderBy(x => x.InvoiceDate)
                    .ThenBy(x => x.InvoiceNumber)
                    .Select(x => (IReadOnlyList<object?>)
                    [
                        x.InvoiceNumber, x.SupplierName, x.InvoiceDate, x.GrandTotal,
                        x.PaidTotal, x.BalanceDue
                    ]));
                break;
            default:
                headers = ["Occurred At", "Medicine", "Batch", "Movement Type", "Quantity", "Balance After", "Source Type", "Source ID"];
                rows.AddRange(report.Movements
                    .OrderBy(x => x.OccurredAt)
                    .Select(x => (IReadOnlyList<object?>)
                    [
                        x.OccurredAt, x.MedicineName, x.BatchNumber, x.MovementType,
                        x.QuantityDelta, x.BalanceAfter, x.SourceType, x.SourceId
                    ]));
                break;
        }

        var csv = new StringBuilder();
        csv.AppendLine(string.Join(',', headers.Select(EscapeCsv)));
        foreach (var row in rows)
        {
            csv.AppendLine(string.Join(',', row.Select(FormatCsvValue).Select(EscapeCsv)));
        }

        return new ReportCsv(
            $"{type}-{report.Range.From:yyyy-MM-dd}-to-{report.Range.To:yyyy-MM-dd}.csv",
            csv.ToString());
    }

    private static decimal Money(decimal value) =>
        decimal.Round(value, 4, MidpointRounding.AwayFromZero);

    private static decimal Quantity(decimal value) =>
        decimal.Round(value, 4, MidpointRounding.AwayFromZero);

    private static string FormatCsvValue(object? value) =>
        value switch
        {
            null => string.Empty,
            DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateTimeOffset dateTime => dateTime.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture),
            decimal number => number.ToString("0.0000", CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
        };

    private static string EscapeCsv(string value)
    {
        if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\n') && !value.Contains('\r'))
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"")}\"";
    }
}
