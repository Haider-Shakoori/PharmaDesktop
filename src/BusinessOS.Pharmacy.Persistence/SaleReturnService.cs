using BusinessOS.Pharmacy.Application.Abstractions.Authentication;
using BusinessOS.Pharmacy.Application.Abstractions.Sales;
using BusinessOS.Pharmacy.Application.Abstractions.DailyClosing;
using BusinessOS.Pharmacy.Application.Abstractions.Time;
using BusinessOS.Pharmacy.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace BusinessOS.Pharmacy.Persistence;

public sealed class SaleReturnService : ISaleReturnService
{
    private static readonly HashSet<string> RefundMethods = new(StringComparer.OrdinalIgnoreCase) { "cash", "bank", "mobile", "credit" };
    private readonly IDbContextFactory<PharmacyDbContext> _contextFactory;
    private readonly IPermissionAuthorizer _permissions;
    private readonly IUserSessionService _sessions;
    private readonly IClock _clock;
    private readonly StockLedger _ledger;
    private readonly IDailyClosingService _dailyClosing;

    public SaleReturnService(IDbContextFactory<PharmacyDbContext> contextFactory, IPermissionAuthorizer permissions, IUserSessionService sessions, IClock clock, StockLedger ledger)
    {
        _contextFactory = contextFactory;
        _permissions = permissions;
        _sessions = sessions;
        _clock = clock;
        _ledger = ledger;
        _dailyClosing = dailyClosing;
    }

    public async Task<IReadOnlyList<SaleListItem>> SearchReturnableSalesAsync(SaleSearchFilter filter, CancellationToken cancellationToken = default)
    {
        _permissions.Demand("returns.manage");
        ArgumentNullException.ThrowIfNull(filter);
        var take = Math.Clamp(filter.Take, 1, 1000);
        var search = NormalizeOptional(filter.Search, 180);
        var paymentStatus = NormalizeOptional(filter.PaymentStatus, 32)?.ToLowerInvariant();

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var query = context.Set<SaleEntity>().AsNoTracking().Include(x => x.Customer).Include(x => x.StockLocation).Where(x => x.Status == "completed").AsQueryable();
        if (filter.From is not null) query = query.Where(x => x.BusinessDate >= filter.From.Value);
        if (filter.To is not null) query = query.Where(x => x.BusinessDate <= filter.To.Value);
        if (paymentStatus is not null) query = query.Where(x => x.PaymentStatus == paymentStatus);
        if (search is not null)
        {
            var pattern = $"%{search}%";
            query = query.Where(x => EF.Functions.Like(x.SaleNumber, pattern) || (x.Customer != null && (EF.Functions.Like(x.Customer.Name, pattern) || (x.Customer.Phone != null && EF.Functions.Like(x.Customer.Phone, pattern)))));
        }

        return await query.OrderByDescending(x => x.CompletedAt).Take(take)
            .Select(x => new SaleListItem(x.Id, x.SaleNumber, x.BusinessDate, x.CompletedAt, x.Customer == null ? null : x.Customer.Name, x.StockLocation.Name, x.PaymentStatus, x.GrandTotal, x.PaidTotal, x.DueTotal, x.ChangeTotal))
            .ToListAsync(cancellationToken);
    }

    public async Task<ReturnableSaleDetail?> GetReturnableSaleAsync(string saleId, CancellationToken cancellationToken = default)
    {
        _permissions.Demand("returns.manage");
        ArgumentException.ThrowIfNullOrWhiteSpace(saleId);
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var sale = await context.Set<SaleEntity>().AsNoTracking().Include(x => x.Customer).Include(x => x.StockLocation).Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == saleId && x.Status == "completed", cancellationToken);
        if (sale is null) return null;

        var lineIds = sale.Lines.Select(x => x.Id).ToList();
        var rows = await context.Set<SaleReturnLineEntity>().AsNoTracking()
            .Where(x => lineIds.Contains(x.SaleLineId) && x.SaleReturn.Status == "completed")
            .Select(x => new { x.SaleLineId, x.Quantity }).ToListAsync(cancellationToken);
        var returned = rows.GroupBy(x => x.SaleLineId).ToDictionary(x => x.Key, x => ScaleQuantity(x.Sum(y => y.Quantity)));
        var lines = sale.Lines.OrderBy(x => x.Description).Select(x =>
        {
            var done = returned.GetValueOrDefault(x.Id);
            var remaining = ScaleQuantity(Math.Max(0m, x.Quantity - done));
            var net = x.Quantity == 0m ? 0m : ScaleMoney(decimal.Round(x.LineTotal / x.Quantity, 8, MidpointRounding.AwayFromZero));
            return new ReturnableSaleLineItem(x.Id, x.MedicineId, x.Description, x.Quantity, done, remaining, net);
        }).ToList();
        return new ReturnableSaleDetail(ToSaleListItem(sale), lines);
    }

    public async Task<SaleReturnDetail> ProcessAsync(ProcessSaleReturnRequest request, CancellationToken cancellationToken = default)
    {
        _permissions.Demand("returns.manage");
        ValidateRequest(request);
        var actorId = _sessions.Current?.UserId ?? throw new InvalidOperationException("A pharmacy user must be signed in.");
        var now = _clock.UtcNow;
        var businessDate = StockLedger.BusinessDate(now);
        var idempotencyKey = request.IdempotencyKey.Trim();

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await InventoryWriteTransaction.BeginAsync(context, cancellationToken);
        var existing = await QueryReturn(context).AsNoTracking().SingleOrDefaultAsync(x => x.IdempotencyKey == idempotencyKey, cancellationToken);
        if (existing is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return ToDetail(existing);
        }

        var sale = await context.Set<SaleEntity>().Include(x => x.StockLocation).SingleOrDefaultAsync(x => x.Id == request.SaleId, cancellationToken)
            ?? throw new InvalidOperationException("Sale was not found.");
        if (sale.Status != "completed") throw new InvalidOperationException("Only completed sales can be returned.");
        if (await _dailyClosing.SalesBlockedAsync(sale.StockLocationId, businessDate, cancellationToken))
            throw new InvalidOperationException("This business day is finalized. Reopen Daily Closing before posting a return.");

        var saleReturn = new SaleReturnEntity
        {
            Id = Guid.CreateVersion7().ToString(), ReturnNumber = CreateReturnNumber(), SaleId = sale.Id, StockLocationId = sale.StockLocationId,
            BusinessDate = businessDate, Status = "processing", IdempotencyKey = idempotencyKey, Reason = request.Reason.Trim(), CreatedBy = actorId,
            CompletedAt = now, CreatedAt = now, UpdatedAt = now,
        };
        context.Add(saleReturn);
        decimal refundTotal = 0m;

        foreach (var input in request.Lines)
        {
            var quantity = ScaleQuantity(input.Quantity);
            if (quantity == 0m) continue;

            var line = await context.Set<SaleLineEntity>().Include(x => x.Medicine).Include(x => x.Allocations).ThenInclude(x => x.ProductBatch)
                .SingleOrDefaultAsync(x => x.Id == input.SaleLineId && x.SaleId == sale.Id, cancellationToken)
                ?? throw new InvalidOperationException("Sale line was not found on this sale.");
            var returnedRows = await context.Set<SaleReturnLineEntity>().Where(x => x.SaleLineId == line.Id && x.SaleReturn.Status == "completed").Select(x => x.Quantity).ToListAsync(cancellationToken);
            var alreadyReturned = ScaleQuantity(returnedRows.Sum());
            var remainingReturnable = ScaleQuantity(line.Quantity - alreadyReturned);
            if (quantity > remainingReturnable) throw new InvalidOperationException($"Return quantity exceeds the remaining sold quantity for {line.Description}.");

            var unitNet = decimal.Round(line.LineTotal / line.Quantity, 8, MidpointRounding.AwayFromZero);
            var refundAmount = ScaleMoney(unitNet * quantity);
            var returnLine = new SaleReturnLineEntity
            {
                Id = Guid.CreateVersion7().ToString(), SaleReturnId = saleReturn.Id, SaleLineId = line.Id, MedicineId = line.MedicineId,
                Quantity = quantity, RefundAmount = refundAmount, Disposition = "restock", CreatedAt = now, UpdatedAt = now,
            };
            saleReturn.Lines.Add(returnLine);

            var remaining = quantity;
            var restockedAll = true;
            foreach (var allocation in line.Allocations.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id))
            {
                if (remaining == 0m) break;
                var prior = await context.Set<SaleReturnAllocationEntity>().Where(x => x.SaleBatchAllocationId == allocation.Id).Select(x => x.Quantity).ToListAsync(cancellationToken);
                var available = ScaleQuantity(allocation.Quantity - ScaleQuantity(prior.Sum()));
                if (available <= 0m) continue;
                var take = ScaleQuantity(available < remaining ? available : remaining);
                var batch = allocation.ProductBatch;
                var eligible = (batch.Status == "active" || batch.Status == "depleted") && !StockLedger.IsExpired(batch, businessDate);
                StockMovementEntity? movement = null;
                if (eligible)
                {
                    movement = await _ledger.RecordAsync(context, batch, take, "sale_return", "sale_return", saleReturn.Id,
                        $"return:{saleReturn.Id}:allocation:{allocation.Id}", actorId, returnLine.Id,
                        $"Return {saleReturn.ReturnNumber} for {sale.SaleNumber}", allocation.UnitCost, null, cancellationToken);
                }
                else restockedAll = false;

                returnLine.Allocations.Add(new SaleReturnAllocationEntity
                {
                    Id = Guid.CreateVersion7().ToString(), SaleReturnLineId = returnLine.Id, SaleBatchAllocationId = allocation.Id,
                    ProductBatchId = batch.Id, StockMovementId = movement?.Id, Quantity = take, Restocked = movement is not null,
                    CreatedAt = now, UpdatedAt = now,
                });
                remaining = ScaleQuantity(remaining - take);
            }

            if (remaining != 0m) throw new InvalidOperationException("Original batch allocation could not cover this return quantity.");
            if (!restockedAll) returnLine.Disposition = "not_restocked";
            refundTotal += refundAmount;
        }

        refundTotal = ScaleMoney(refundTotal);
        if (refundTotal == 0m) throw new InvalidOperationException("Select at least one item quantity to return.");

        decimal submittedRefund = 0m;
        foreach (var refund in request.Refunds)
        {
            var method = refund.Method.Trim().ToLowerInvariant();
            var amount = ScaleMoney(refund.Amount);
            if (!RefundMethods.Contains(method)) throw new ArgumentException($"Unsupported refund method: {refund.Method}.");
            submittedRefund += amount;
            saleReturn.Refunds.Add(new SaleReturnRefundEntity
            {
                Id = Guid.CreateVersion7().ToString(), SaleReturnId = saleReturn.Id, Method = method, Amount = amount, Currency = "AFN",
                Reference = NormalizeOptional(refund.Reference, 160), CreatedAt = now, UpdatedAt = now,
            });
        }

        if (ScaleMoney(submittedRefund) != refundTotal) throw new InvalidOperationException("Refund settlement must equal the calculated return amount.");
        saleReturn.Status = "completed";
        saleReturn.RefundTotal = refundTotal;
        saleReturn.UpdatedAt = now;
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await using var readContext = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var completed = await QueryReturn(readContext).AsNoTracking().SingleAsync(x => x.Id == saleReturn.Id, cancellationToken);
        return ToDetail(completed);
    }

    public async Task<IReadOnlyList<SaleReturnListItem>> SearchReturnsAsync(SaleReturnSearchFilter filter, CancellationToken cancellationToken = default)
    {
        _permissions.Demand("returns.manage");
        ArgumentNullException.ThrowIfNull(filter);
        var take = Math.Clamp(filter.Take, 1, 1000);
        var search = NormalizeOptional(filter.Search, 180);
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var query = context.Set<SaleReturnEntity>().AsNoTracking().Include(x => x.Sale).Include(x => x.StockLocation).AsQueryable();
        if (filter.From is not null) query = query.Where(x => x.BusinessDate >= filter.From.Value);
        if (filter.To is not null) query = query.Where(x => x.BusinessDate <= filter.To.Value);
        if (search is not null)
        {
            var pattern = $"%{search}%";
            query = query.Where(x => EF.Functions.Like(x.ReturnNumber, pattern) || EF.Functions.Like(x.Sale.SaleNumber, pattern));
        }
        return await query.OrderByDescending(x => x.BusinessDate).ThenByDescending(x => x.Id).Take(take)
            .Select(x => new SaleReturnListItem(x.Id, x.ReturnNumber, x.SaleId, x.Sale.SaleNumber, x.BusinessDate, x.StockLocation.Name, x.Status, x.RefundTotal, x.Reason, x.CompletedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<SaleReturnDetail?> GetReturnAsync(string returnId, CancellationToken cancellationToken = default)
    {
        _permissions.Demand("returns.manage");
        ArgumentException.ThrowIfNullOrWhiteSpace(returnId);
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var result = await QueryReturn(context).AsNoTracking().SingleOrDefaultAsync(x => x.Id == returnId, cancellationToken);
        return result is null ? null : ToDetail(result);
    }

    private static IQueryable<SaleReturnEntity> QueryReturn(PharmacyDbContext context) =>
        context.Set<SaleReturnEntity>().Include(x => x.Sale).Include(x => x.StockLocation)
            .Include(x => x.Lines).ThenInclude(x => x.SaleLine)
            .Include(x => x.Lines).ThenInclude(x => x.Allocations).ThenInclude(x => x.ProductBatch)
            .Include(x => x.Refunds);

    private static SaleReturnDetail ToDetail(SaleReturnEntity value)
    {
        var list = new SaleReturnListItem(value.Id, value.ReturnNumber, value.SaleId, value.Sale.SaleNumber, value.BusinessDate, value.StockLocation.Name, value.Status, value.RefundTotal, value.Reason, value.CompletedAt);
        var lines = value.Lines.OrderBy(x => x.SaleLine.Description).Select(x => new SaleReturnLineItem(
            x.Id, x.SaleLineId, x.MedicineId, x.SaleLine.Description, x.Quantity, x.RefundAmount, x.Disposition,
            x.Allocations.OrderBy(a => a.CreatedAt).Select(a => new SaleReturnAllocationItem(a.Id, a.SaleBatchAllocationId, a.ProductBatchId, a.ProductBatch.BatchNumber, a.Quantity, a.Restocked, a.StockMovementId)).ToList())).ToList();
        var refunds = value.Refunds.OrderBy(x => x.CreatedAt).Select(x => new SaleReturnRefundItem(x.Id, x.Method, x.Amount, x.Currency, x.Reference)).ToList();
        return new SaleReturnDetail(list, lines, refunds);
    }

    private static SaleListItem ToSaleListItem(SaleEntity sale) => new(sale.Id, sale.SaleNumber, sale.BusinessDate, sale.CompletedAt, sale.Customer?.Name, sale.StockLocation.Name, sale.PaymentStatus, sale.GrandTotal, sale.PaidTotal, sale.DueTotal, sale.ChangeTotal);

    private static void ValidateRequest(ProcessSaleReturnRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SaleId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.IdempotencyKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Reason);
        if (request.IdempotencyKey.Trim().Length > 191) throw new ArgumentOutOfRangeException(nameof(request.IdempotencyKey));
        if (request.Reason.Trim().Length > 1000) throw new ArgumentOutOfRangeException(nameof(request.Reason));
        if (request.Lines.Count == 0) throw new ArgumentOutOfRangeException(nameof(request.Lines));
        if (request.Refunds.Count == 0) throw new ArgumentOutOfRangeException(nameof(request.Refunds));
        if (request.Lines.Select(x => x.SaleLineId).Distinct(StringComparer.Ordinal).Count() != request.Lines.Count)
            throw new ArgumentException("A sale line can appear only once in a return request.", nameof(request.Lines));
        foreach (var line in request.Lines)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(line.SaleLineId);
            if (line.Quantity < 0m || line.Quantity > 99_999_999m) throw new ArgumentOutOfRangeException(nameof(line.Quantity));
        }
        foreach (var refund in request.Refunds)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(refund.Method);
            if (refund.Amount <= 0m || refund.Amount > 999_999_999_999m) throw new ArgumentOutOfRangeException(nameof(refund.Amount));
            if (refund.Reference?.Trim().Length > 160) throw new ArgumentOutOfRangeException(nameof(refund.Reference));
        }
    }

    private static decimal ScaleQuantity(decimal value) => decimal.Round(value, 4, MidpointRounding.AwayFromZero);
    private static decimal ScaleMoney(decimal value) => decimal.Round(value, 4, MidpointRounding.AwayFromZero);
    private static string CreateReturnNumber() => $"RET-{Guid.NewGuid().ToString("N")[..20].ToUpperInvariant()}";
    private static string? NormalizeOptional(string? value, int maxLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (normalized?.Length > maxLength) throw new ArgumentOutOfRangeException(nameof(value));
        return normalized;
    }
}
