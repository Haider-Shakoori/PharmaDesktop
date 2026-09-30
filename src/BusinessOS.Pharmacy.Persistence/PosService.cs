using BusinessOS.Pharmacy.Application.Abstractions.Authentication;
using BusinessOS.Pharmacy.Application.Abstractions.Sales;
using BusinessOS.Pharmacy.Application.Abstractions.Time;
using BusinessOS.Pharmacy.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace BusinessOS.Pharmacy.Persistence;

public sealed class PosService : IPosService
{
    private static readonly HashSet<string> PaymentMethods =
        new(StringComparer.OrdinalIgnoreCase) { "cash", "bank", "mobile", "credit" };

    private readonly IDbContextFactory<PharmacyDbContext> _contextFactory;
    private readonly IPermissionAuthorizer _permissions;
    private readonly IUserSessionService _sessions;
    private readonly IClock _clock;
    private readonly StockLedger _ledger;

    public PosService(
        IDbContextFactory<PharmacyDbContext> contextFactory,
        IPermissionAuthorizer permissions,
        IUserSessionService sessions,
        IClock clock,
        StockLedger ledger)
    {
        _contextFactory = contextFactory;
        _permissions = permissions;
        _sessions = sessions;
        _clock = clock;
        _ledger = ledger;
    }

    public async Task<PosReferenceData> GetReferenceDataAsync(
        CancellationToken cancellationToken = default)
    {
        _permissions.Demand("pos.sell");

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var locations = await context.Set<StockLocationEntity>()
            .AsNoTracking()
            .Include(x => x.Branch)
            .Where(x => x.IsActive && x.Branch.IsActive)
            .OrderByDescending(x => x.IsDefault)
            .ThenBy(x => x.Name)
            .Select(x => new PosStockLocationItem(
                x.Id,
                x.Branch.Name,
                x.Name,
                x.IsDefault))
            .ToListAsync(cancellationToken);

        var customers = await context.Set<CustomerEntity>()
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.Name)
            .Take(500)
            .Select(x => new PosCustomerItem(
                x.Id,
                x.Name,
                x.Phone,
                x.CreditLimit))
            .ToListAsync(cancellationToken);

        return new PosReferenceData(locations, customers);
    }

    public async Task<IReadOnlyList<PosProductSearchItem>> SearchProductsAsync(
        PosProductSearchFilter filter,
        CancellationToken cancellationToken = default)
    {
        _permissions.Demand("pos.sell");

        ArgumentNullException.ThrowIfNull(filter);
        ArgumentException.ThrowIfNullOrWhiteSpace(filter.StockLocationId);

        var queryText = filter.Query?.Trim() ?? string.Empty;
        if (queryText.Length == 0)
        {
            return [];
        }

        if (queryText.Length > 120)
        {
            throw new ArgumentOutOfRangeException(nameof(filter.Query));
        }

        var take = Math.Clamp(filter.Take, 1, 100);
        var businessDate = StockLedger.BusinessDate(_clock.UtcNow);
        var pattern = $"%{queryText}%";

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var locationExists = await context.Set<StockLocationEntity>()
            .AsNoTracking()
            .AnyAsync(
                x => x.Id == filter.StockLocationId && x.IsActive,
                cancellationToken);

        if (!locationExists)
        {
            throw new InvalidOperationException("Stock location was not found or is inactive.");
        }

        var batches = await context.Set<ProductBatchEntity>()
            .AsNoTracking()
            .Include(x => x.Medicine)
            .Where(x =>
                x.StockLocationId == filter.StockLocationId &&
                x.Status == "active" &&
                x.AvailableQuantity > 0m &&
                x.SalePrice != null &&
                (x.ExpiresAt == null || x.ExpiresAt >= businessDate) &&
                x.Medicine.IsActive &&
                (
                    x.Medicine.Barcode == queryText ||
                    EF.Functions.Like(x.Medicine.MedicineCode, pattern) ||
                    EF.Functions.Like(x.Medicine.BrandName, pattern) ||
                    (x.Medicine.GenericName != null &&
                        EF.Functions.Like(x.Medicine.GenericName, pattern)) ||
                    (x.Medicine.Strength != null &&
                        EF.Functions.Like(x.Medicine.Strength, pattern))
                ))
            .OrderBy(x => x.Medicine.BrandName)
            .ThenBy(x => x.ExpiresAt == null)
            .ThenBy(x => x.ExpiresAt)
            .ThenBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

        return batches
            .GroupBy(x => x.MedicineId)
            .Take(take)
            .Select(group =>
            {
                var ordered = group
                    .OrderBy(x => x.ExpiresAt == null)
                    .ThenBy(x => x.ExpiresAt)
                    .ThenBy(x => x.CreatedAt)
                    .ToList();
                var medicine = ordered[0].Medicine;
                var prices = ordered.Select(x => x.SalePrice!.Value).ToList();

                return new PosProductSearchItem(
                    medicine.Id,
                    medicine.MedicineCode,
                    medicine.Barcode,
                    medicine.BrandName,
                    medicine.GenericName,
                    medicine.Strength,
                    medicine.SaleUnit,
                    StockLedger.Scale(ordered.Sum(x => x.AvailableQuantity)),
                    ordered[0].SalePrice,
                    prices.Min(),
                    prices.Max(),
                    medicine.PrescriptionRequired,
                    ordered.Select(x => new PosBatchPriceItem(
                        x.Id,
                        x.BatchNumber,
                        x.AvailableQuantity,
                        x.SalePrice!.Value,
                        x.ExpiresAt)).ToList());
            })
            .ToList();
    }

    public async Task<SaleDetail> CheckoutAsync(
        PosCheckoutRequest request,
        CancellationToken cancellationToken = default)
    {
        _permissions.Demand("pos.sell");
        ValidateCheckout(request);

        var session = _sessions.Current
            ?? throw new InvalidOperationException("A pharmacy user must be signed in.");
        var actorId = session.UserId;
        var now = _clock.UtcNow;
        var businessDate = StockLedger.BusinessDate(now);
        var idempotencyKey = request.IdempotencyKey.Trim();

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await InventoryWriteTransaction.BeginAsync(
            context,
            cancellationToken);

        var existing = await QuerySale(context)
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.IdempotencyKey == idempotencyKey,
                cancellationToken);

        if (existing is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return ToDetail(existing, session);
        }

        var location = await context.Set<StockLocationEntity>()
            .Include(x => x.Branch)
            .SingleOrDefaultAsync(
                x => x.Id == request.StockLocationId && x.IsActive && x.Branch.IsActive,
                cancellationToken)
            ?? throw new InvalidOperationException("Stock location was not found or is inactive.");

        CustomerEntity? customer = null;
        if (!string.IsNullOrWhiteSpace(request.CustomerId))
        {
            customer = await context.Set<CustomerEntity>()
                .SingleOrDefaultAsync(
                    x => x.Id == request.CustomerId && x.IsActive,
                    cancellationToken)
                ?? throw new InvalidOperationException("Customer was not found or is inactive.");
        }

        var medicineIds = request.Lines.Select(x => x.MedicineId).Distinct().ToList();
        var medicines = await context.Set<MedicineEntity>()
            .Where(x => medicineIds.Contains(x.Id) && x.IsActive)
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        if (medicines.Count != medicineIds.Count)
        {
            throw new InvalidOperationException("One or more medicines were not found or are inactive.");
        }

        if (request.Lines.Any(x => medicines[x.MedicineId].PrescriptionRequired) &&
            string.IsNullOrWhiteSpace(request.PrescriptionReference))
        {
            throw new InvalidOperationException(
                "A prescription reference is required for prescription-only medicines.");
        }

        var sale = new SaleEntity
        {
            Id = Guid.CreateVersion7().ToString(),
            SaleNumber = CreateDocumentNumber("POS", businessDate),
            StockLocationId = location.Id,
            CustomerId = customer?.Id,
            PrescriptionReference = NormalizeOptional(request.PrescriptionReference, 120),
            PrescriberName = NormalizeOptional(request.PrescriberName, 160),
            PrescriptionDate = request.PrescriptionDate,
            BusinessDate = businessDate,
            Status = "processing",
            Currency = "AFN",
            PaymentStatus = "unpaid",
            IdempotencyKey = idempotencyKey,
            Notes = NormalizeOptional(request.Notes, 1000),
            CreatedBy = actorId,
            CreatedAt = now,
            UpdatedAt = now,
        };
        context.Add(sale);

        decimal subtotal = 0m;
        decimal discountTotal = 0m;

        for (var index = 0; index < request.Lines.Count; index++)
        {
            var input = request.Lines[index];
            var medicine = medicines[input.MedicineId];
            var quantity = ScaleQuantity(input.Quantity);
            var discount = ScaleMoney(input.DiscountAmount);

            if (input.OverridePrice && !_permissions.HasPermission("pos.price_override"))
            {
                throw new UnauthorizedAccessException(
                    "You do not have permission to override the sale price.");
            }

            if (input.OverridePrice && input.UnitPrice is null)
            {
                throw new ArgumentException("Enter the override price.");
            }

            if (discount > 0m && !_permissions.HasPermission("pos.discount"))
            {
                throw new UnauthorizedAccessException(
                    "You do not have permission to apply discounts.");
            }

            var overridePrice = input.OverridePrice
                ? ScaleMoney(input.UnitPrice!.Value)
                : (decimal?)null;

            var line = new SaleLineEntity
            {
                Id = Guid.CreateVersion7().ToString(),
                SaleId = sale.Id,
                MedicineId = medicine.Id,
                Description = BuildDescription(medicine),
                SaleUnit = medicine.SaleUnit,
                Quantity = quantity,
                UnitPrice = 0m,
                DiscountAmount = 0m,
                TaxAmount = 0m,
                LineTotal = 0m,
                CostTotal = 0m,
                PrescriptionRequired = medicine.PrescriptionRequired,
                CreatedAt = now,
                UpdatedAt = now,
            };
            sale.Lines.Add(line);

            var candidates = await context.Set<ProductBatchEntity>()
                .Where(x =>
                    x.MedicineId == medicine.Id &&
                    x.StockLocationId == location.Id &&
                    x.Status == "active" &&
                    x.AvailableQuantity > 0m &&
                    x.SalePrice != null &&
                    (x.ExpiresAt == null || x.ExpiresAt >= businessDate))
                .OrderBy(x => x.ExpiresAt == null)
                .ThenBy(x => x.ExpiresAt)
                .ThenBy(x => x.CreatedAt)
                .ToListAsync(cancellationToken);

            var remaining = quantity;
            decimal costTotal = 0m;
            decimal lineSubtotal = 0m;

            foreach (var batch in candidates)
            {
                if (remaining == 0m)
                {
                    break;
                }

                var allocatedQuantity = ScaleQuantity(
                    batch.AvailableQuantity < remaining
                        ? batch.AvailableQuantity
                        : remaining);

                var chargedUnitPrice = overridePrice ?? batch.SalePrice!.Value;
                chargedUnitPrice = ScaleMoney(chargedUnitPrice);
                var allocationTotal = ScaleMoney(allocatedQuantity * chargedUnitPrice);

                var movement = await _ledger.RecordAsync(
                    context,
                    batch,
                    -allocatedQuantity,
                    "sale",
                    "sale",
                    sale.Id,
                    $"sale:{sale.Id}:line:{line.Id}:{batch.Id}",
                    actorId,
                    line.Id,
                    $"POS {sale.SaleNumber}",
                    batch.PurchaseCost,
                    null,
                    cancellationToken);

                line.Allocations.Add(new SaleBatchAllocationEntity
                {
                    Id = Guid.CreateVersion7().ToString(),
                    SaleLineId = line.Id,
                    ProductBatchId = batch.Id,
                    StockMovementId = movement.Id,
                    Quantity = allocatedQuantity,
                    UnitCost = ScaleMoney(batch.PurchaseCost),
                    UnitPrice = chargedUnitPrice,
                    LineTotal = allocationTotal,
                    CreatedAt = now,
                    UpdatedAt = now,
                });

                costTotal += allocatedQuantity * batch.PurchaseCost;
                lineSubtotal += allocationTotal;
                remaining = ScaleQuantity(remaining - allocatedQuantity);
            }

            if (remaining != 0m)
            {
                throw new InvalidOperationException(
                    $"Insufficient eligible stock for {medicine.BrandName}. Expired and non-active batches are excluded.");
            }

            lineSubtotal = ScaleMoney(lineSubtotal);
            costTotal = ScaleMoney(costTotal);

            if (discount > lineSubtotal)
            {
                throw new ArgumentException("Discount cannot exceed the line subtotal.");
            }

            var lineTotal = ScaleMoney(lineSubtotal - discount);
            var weightedUnitPrice = ScaleMoney(lineSubtotal / quantity);

            line.UnitPrice = weightedUnitPrice;
            line.DiscountAmount = discount;
            line.LineTotal = lineTotal;
            line.CostTotal = costTotal;
            line.UpdatedAt = now;

            subtotal += lineSubtotal;
            discountTotal += discount;
        }

        subtotal = ScaleMoney(subtotal);
        discountTotal = ScaleMoney(discountTotal);
        var grandTotal = ScaleMoney(subtotal - discountTotal);

        decimal paidInput = 0m;
        decimal credit = 0m;
        var hasCash = false;

        foreach (var payment in request.Payments)
        {
            var method = payment.Method.Trim().ToLowerInvariant();
            var amount = ScaleMoney(payment.Amount);

            if (!PaymentMethods.Contains(method))
            {
                throw new ArgumentException($"Unsupported payment method: {payment.Method}.");
            }

            if (method == "credit")
            {
                if (customer is null)
                {
                    throw new InvalidOperationException("A customer is required for credit sales.");
                }

                credit += amount;
            }
            else
            {
                paidInput += amount;
                hasCash |= method == "cash";
            }

            sale.Payments.Add(new SalePaymentEntity
            {
                Id = Guid.CreateVersion7().ToString(),
                SaleId = sale.Id,
                PaymentNumber = CreateRandomDocumentNumber("PAY"),
                Method = method,
                Amount = amount,
                Currency = "AFN",
                Reference = NormalizeOptional(payment.Reference, 160),
                PaidAt = now,
                CreatedBy = actorId,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }

        credit = ScaleMoney(credit);
        paidInput = ScaleMoney(paidInput);
        var settled = ScaleMoney(paidInput + credit);

        if (settled < grandTotal)
        {
            throw new InvalidOperationException("Payments and credit do not cover the sale total.");
        }

        if (credit > 0m)
        {
            if (customer is null)
            {
                throw new InvalidOperationException("A customer is required for credit sales.");
            }

            if (credit > ScaleMoney(customer.CreditLimit))
            {
                throw new InvalidOperationException(
                    "Credit amount exceeds this customer credit limit.");
            }

            if (settled > grandTotal)
            {
                throw new InvalidOperationException("Credit sales cannot exceed the sale total.");
            }
        }

        var change = ScaleMoney(settled - grandTotal);
        if (change > 0m && !hasCash)
        {
            throw new InvalidOperationException(
                "Overpayment can only be returned as cash change.");
        }

        var paidTotal = ScaleMoney(grandTotal - credit);
        sale.Subtotal = subtotal;
        sale.DiscountTotal = discountTotal;
        sale.TaxTotal = 0m;
        sale.GrandTotal = grandTotal;
        sale.PaidTotal = paidTotal;
        sale.DueTotal = credit;
        sale.ChangeTotal = change;
        sale.PaymentStatus = credit == 0m
            ? "paid"
            : paidTotal == 0m
                ? "credit"
                : "partial";
        sale.Status = "completed";
        sale.CompletedAt = now;
        sale.UpdatedAt = now;

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await using var readContext = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var completed = await QuerySale(readContext)
            .AsNoTracking()
            .SingleAsync(x => x.Id == sale.Id, cancellationToken);

        return ToDetail(completed, session);
    }

    public async Task<IReadOnlyList<SaleListItem>> SearchSalesAsync(
        SaleSearchFilter filter,
        CancellationToken cancellationToken = default)
    {
        _permissions.Demand("pos.sell");
        ArgumentNullException.ThrowIfNull(filter);

        var take = Math.Clamp(filter.Take, 1, 1000);
        var search = NormalizeOptional(filter.Search, 180);
        var paymentStatus = NormalizeOptional(filter.PaymentStatus, 32)?.ToLowerInvariant();

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var query = context.Set<SaleEntity>()
            .AsNoTracking()
            .Include(x => x.Customer)
            .Include(x => x.StockLocation)
            .Where(x => x.Status == "completed")
            .AsQueryable();

        if (filter.From is not null)
        {
            query = query.Where(x => x.BusinessDate >= filter.From.Value);
        }

        if (filter.To is not null)
        {
            query = query.Where(x => x.BusinessDate <= filter.To.Value);
        }

        if (paymentStatus is not null)
        {
            query = query.Where(x => x.PaymentStatus == paymentStatus);
        }

        if (search is not null)
        {
            var pattern = $"%{search}%";
            query = query.Where(x =>
                EF.Functions.Like(x.SaleNumber, pattern) ||
                (x.Customer != null &&
                    (EF.Functions.Like(x.Customer.Name, pattern) ||
                     (x.Customer.Phone != null &&
                      EF.Functions.Like(x.Customer.Phone, pattern)))));
        }

        return await query
            .OrderByDescending(x => x.CompletedAt)
            .ThenByDescending(x => x.CreatedAt)
            .Take(take)
            .Select(x => new SaleListItem(
                x.Id,
                x.SaleNumber,
                x.BusinessDate,
                x.CompletedAt,
                x.Customer == null ? null : x.Customer.Name,
                x.StockLocation.Name,
                x.PaymentStatus,
                x.GrandTotal,
                x.PaidTotal,
                x.DueTotal,
                x.ChangeTotal))
            .ToListAsync(cancellationToken);
    }

    public async Task<SaleDetail?> GetSaleAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        _permissions.Demand("pos.sell");
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var sale = await QuerySale(context)
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        return sale is null
            ? null
            : ToDetail(sale, _sessions.Current);
    }

    private static IQueryable<SaleEntity> QuerySale(PharmacyDbContext context) =>
        context.Set<SaleEntity>()
            .Include(x => x.StockLocation)
            .Include(x => x.Customer)
            .Include(x => x.Lines)
                .ThenInclude(x => x.Medicine)
            .Include(x => x.Lines)
                .ThenInclude(x => x.Allocations)
                    .ThenInclude(x => x.ProductBatch)
            .Include(x => x.Payments);

    private static SaleDetail ToDetail(
        SaleEntity sale,
        UserSessionSnapshot? session)
    {
        var cashierName = session?.UserId == sale.CreatedBy
            ? session.Name
            : sale.CreatedBy;

        var lines = sale.Lines
            .OrderBy(x => x.Description)
            .Select(x => new SaleLineItem(
                x.Id,
                x.MedicineId,
                x.Description,
                x.SaleUnit,
                x.Quantity,
                x.UnitPrice,
                x.DiscountAmount,
                x.TaxAmount,
                x.LineTotal,
                x.CostTotal,
                x.PrescriptionRequired,
                x.Allocations
                    .OrderBy(a => a.ProductBatch.ExpiresAt == null)
                    .ThenBy(a => a.ProductBatch.ExpiresAt)
                    .Select(a => new SaleBatchAllocationItem(
                        a.Id,
                        a.ProductBatchId,
                        a.ProductBatch.BatchNumber,
                        a.ProductBatch.ExpiresAt,
                        a.Quantity,
                        a.UnitCost,
                        a.UnitPrice,
                        a.LineTotal))
                    .ToList()))
            .ToList();

        var payments = sale.Payments
            .OrderBy(x => x.PaidAt)
            .ThenBy(x => x.PaymentNumber)
            .Select(x => new SalePaymentItem(
                x.Id,
                x.PaymentNumber,
                x.Method,
                x.Amount,
                x.Currency,
                x.Reference,
                x.PaidAt))
            .ToList();

        return new SaleDetail(
            new SaleListItem(
                sale.Id,
                sale.SaleNumber,
                sale.BusinessDate,
                sale.CompletedAt,
                sale.Customer?.Name,
                sale.StockLocation.Name,
                sale.PaymentStatus,
                sale.GrandTotal,
                sale.PaidTotal,
                sale.DueTotal,
                sale.ChangeTotal),
            sale.Currency,
            sale.Subtotal,
            sale.DiscountTotal,
            sale.TaxTotal,
            cashierName,
            sale.Customer?.Phone,
            sale.PrescriptionReference,
            sale.PrescriberName,
            sale.PrescriptionDate,
            sale.Notes,
            lines,
            payments);
    }

    private static void ValidateCheckout(PosCheckoutRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.StockLocationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.IdempotencyKey);

        if (request.IdempotencyKey.Trim().Length > 191)
        {
            throw new ArgumentOutOfRangeException(nameof(request.IdempotencyKey));
        }

        if (request.Lines.Count is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(request.Lines));
        }

        if (request.Payments.Count is < 1 or > 10)
        {
            throw new ArgumentOutOfRangeException(nameof(request.Payments));
        }

        if (request.Notes?.Trim().Length > 1000 ||
            request.PrescriptionReference?.Trim().Length > 120 ||
            request.PrescriberName?.Trim().Length > 160)
        {
            throw new ArgumentOutOfRangeException(nameof(request));
        }

        foreach (var line in request.Lines)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(line.MedicineId);

            if (line.Quantity <= 0m || line.Quantity > 99_999_999m)
            {
                throw new ArgumentOutOfRangeException(nameof(line.Quantity));
            }

            if (line.UnitPrice is < 0m or > 999_999_999_999m)
            {
                throw new ArgumentOutOfRangeException(nameof(line.UnitPrice));
            }

            if (line.DiscountAmount < 0m || line.DiscountAmount > 999_999_999_999m)
            {
                throw new ArgumentOutOfRangeException(nameof(line.DiscountAmount));
            }
        }

        foreach (var payment in request.Payments)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(payment.Method);

            if (payment.Amount <= 0m || payment.Amount > 999_999_999_999m)
            {
                throw new ArgumentOutOfRangeException(nameof(payment.Amount));
            }

            if (payment.Reference?.Trim().Length > 160)
            {
                throw new ArgumentOutOfRangeException(nameof(payment.Reference));
            }
        }
    }

    private static decimal ScaleQuantity(decimal value) =>
        decimal.Round(value, 4, MidpointRounding.AwayFromZero);

    private static decimal ScaleMoney(decimal value) =>
        decimal.Round(value, 4, MidpointRounding.AwayFromZero);

    private static string BuildDescription(MedicineEntity medicine) =>
        string.Join(
            " ",
            new[] { medicine.BrandName, medicine.Strength }
                .Where(x => !string.IsNullOrWhiteSpace(x)));

    private static string CreateDocumentNumber(string prefix, DateOnly businessDate) =>
        $"{prefix}-{businessDate:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..10].ToUpperInvariant()}";

    private static string CreateRandomDocumentNumber(string prefix) =>
        $"{prefix}-{Guid.NewGuid().ToString("N")[..18].ToUpperInvariant()}";

    private static string? NormalizeOptional(string? value, int maxLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (normalized?.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        return normalized;
    }
}
