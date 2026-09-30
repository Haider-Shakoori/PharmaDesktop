using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using BusinessOS.Pharmacy.Application.Abstractions.Authentication;
using BusinessOS.Pharmacy.Application.Abstractions.Purchasing;
using BusinessOS.Pharmacy.Application.Abstractions.Time;
using BusinessOS.Pharmacy.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace BusinessOS.Pharmacy.Persistence;

public sealed class PurchasingService : IPurchasingService
{
    private static readonly HashSet<string> PaymentMethods =
        new(StringComparer.OrdinalIgnoreCase) { "cash", "bank", "hawala", "other" };

    private static readonly HashSet<string> ReceivableStatuses =
        new(StringComparer.OrdinalIgnoreCase) { "approved", "partially_received" };

    private readonly IDbContextFactory<PharmacyDbContext> _contextFactory;
    private readonly IPermissionAuthorizer _permissions;
    private readonly IUserSessionService _sessions;
    private readonly IClock _clock;
    private readonly StockLedger _ledger;

    public PurchasingService(
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

    public async Task<PurchaseReferenceData> GetReferenceDataAsync(
        CancellationToken cancellationToken = default)
    {
        _permissions.Demand("purchases.manage");

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var suppliers = await context.Set<SupplierEntity>()
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.Name)
            .Select(x => new PurchaseSupplierReferenceItem(
                x.Id,
                x.Code,
                x.Name,
                x.PaymentTermsDays))
            .ToListAsync(cancellationToken);

        var medicines = await context.Set<MedicineEntity>()
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.BrandName)
            .ThenBy(x => x.MedicineCode)
            .Select(x => new PurchaseMedicineReferenceItem(
                x.Id,
                x.MedicineCode,
                x.BrandName,
                x.GenericName,
                x.Strength,
                x.PurchaseUnit,
                x.BatchTrackingRequired,
                x.ExpiryTrackingRequired))
            .ToListAsync(cancellationToken);

        var locations = await context.Set<StockLocationEntity>()
            .AsNoTracking()
            .Include(x => x.Branch)
            .Where(x => x.IsActive && x.Branch.IsActive)
            .OrderByDescending(x => x.IsDefault)
            .ThenBy(x => x.Name)
            .Select(x => new PurchaseStockLocationReferenceItem(
                x.Id,
                x.Branch.Name,
                x.Name,
                x.IsDefault))
            .ToListAsync(cancellationToken);

        return new PurchaseReferenceData(suppliers, medicines, locations);
    }

    public async Task<IReadOnlyList<PurchaseOrderListItem>> SearchOrdersAsync(
        PurchaseOrderSearchFilter filter,
        CancellationToken cancellationToken = default)
    {
        _permissions.Demand("purchases.manage");

        var take = Math.Clamp(filter.Take, 1, 1000);
        var search = NormalizeOptional(filter.Search, 255);
        var status = NormalizeOptional(filter.Status, 32);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var query = context.Set<PurchaseOrderEntity>()
            .AsNoTracking()
            .Include(x => x.Supplier)
            .AsQueryable();

        if (search is not null)
        {
            var pattern = $"%{search}%";
            query = query.Where(x =>
                EF.Functions.Like(x.Number, pattern) ||
                EF.Functions.Like(x.Supplier.Name, pattern));
        }

        if (status is not null)
        {
            query = query.Where(x => x.Status == status);
        }

        return await query
            .OrderByDescending(x => x.OrderDate)
            .ThenByDescending(x => x.Id)
            .Take(take)
            .Select(x => new PurchaseOrderListItem(
                x.Id,
                x.Number,
                x.SupplierId,
                x.Supplier.Name,
                x.Status,
                x.OrderDate,
                x.ExpectedDate,
                x.Currency,
                x.Subtotal,
                x.DiscountTotal,
                x.LandedCostTotal,
                x.GrandTotal))
            .ToListAsync(cancellationToken);
    }

    public async Task<PurchaseOrderDetail?> GetOrderAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        _permissions.Demand("purchases.manage");
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var order = await context.Set<PurchaseOrderEntity>()
            .AsNoTracking()
            .Include(x => x.Supplier)
            .Include(x => x.Lines)
                .ThenInclude(x => x.Medicine)
            .Include(x => x.Receipts)
                .ThenInclude(x => x.StockLocation)
            .Include(x => x.Receipts)
                .ThenInclude(x => x.Lines)
                    .ThenInclude(x => x.Medicine)
            .Include(x => x.Invoices)
                .ThenInclude(x => x.Payments)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (order is null)
        {
            return null;
        }

        var lines = order.Lines
            .OrderBy(x => x.Description)
            .Select(x => new PurchaseOrderLineItem(
                x.Id,
                x.MedicineId,
                x.Medicine.MedicineCode,
                x.Description,
                x.OrderedQuantity,
                x.ReceivedQuantity,
                x.UnitCost,
                x.DiscountAmount,
                x.LandedCostAllocated,
                x.LineTotal,
                x.Medicine.BatchTrackingRequired,
                x.Medicine.ExpiryTrackingRequired))
            .ToList();

        var receipts = order.Receipts
            .OrderByDescending(x => x.ReceivedAt)
            .Select(x => new GoodsReceiptItem(
                x.Id,
                x.ReceiptNumber,
                x.Status,
                x.ReceivedAt,
                x.StockLocationId,
                x.StockLocation?.Name,
                x.InventoryPostedAt,
                x.Notes,
                x.Lines
                    .OrderBy(y => y.Medicine.BrandName)
                    .Select(y => new GoodsReceiptLineItem(
                        y.Id,
                        y.PurchaseOrderLineId,
                        y.MedicineId,
                        y.Medicine.BrandName,
                        y.ReceivedQuantity,
                        y.BonusQuantity,
                        y.BatchNumber,
                        y.ManufacturedAt,
                        y.ExpiresAt,
                        y.UnitCost,
                        y.SalePrice))
                    .ToList()))
            .ToList();

        var invoices = order.Invoices
            .OrderByDescending(x => x.InvoiceDate)
            .Select(x => new PurchaseInvoiceItem(
                x.Id,
                x.InvoiceNumber,
                x.SupplierInvoiceNumber,
                x.InvoiceDate,
                x.DueDate,
                x.Currency,
                x.Status,
                x.GrandTotal,
                x.PaidTotal,
                x.BalanceDue,
                x.GoodsReceiptId,
                x.Notes,
                x.Payments
                    .OrderByDescending(y => y.PaidAt)
                    .Select(y => new SupplierPaymentItem(
                        y.Id,
                        y.PaymentNumber,
                        y.Amount,
                        y.Currency,
                        y.Method,
                        y.Reference,
                        y.PaidAt,
                        y.Notes))
                    .ToList()))
            .ToList();

        return new PurchaseOrderDetail(
            ToListItem(order),
            order.Notes,
            order.CreatedBy,
            order.ApprovedBy,
            order.SubmittedAt,
            order.ApprovedAt,
            order.CancelledAt,
            lines,
            receipts,
            invoices);
    }

    public async Task<string> CreateOrderAsync(
        CreatePurchaseOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        _permissions.Demand("purchases.manage");
        ValidateOrder(request);

        var actorId = CurrentUserId();
        var now = _clock.UtcNow;
        var number = CreateDocumentNumber("PO", now);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await InventoryWriteTransaction.BeginAsync(context, cancellationToken);

        var supplier = await context.Set<SupplierEntity>()
            .SingleOrDefaultAsync(x => x.Id == request.SupplierId && x.IsActive, cancellationToken)
            ?? throw new InvalidOperationException("Supplier was not found or is inactive.");

        var medicineIds = request.Lines.Select(x => x.MedicineId).Distinct().ToList();
        var medicines = await context.Set<MedicineEntity>()
            .Where(x => medicineIds.Contains(x.Id) && x.IsActive)
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        if (medicines.Count != medicineIds.Count)
        {
            throw new InvalidOperationException("One or more medicines were not found or are inactive.");
        }

        decimal subtotal = 0m;
        decimal discountTotal = 0m;
        decimal landedTotal = 0m;
        var prepared = new List<(CreatePurchaseOrderLineRequest Request, MedicineEntity Medicine, decimal Qty, decimal UnitCost, decimal Discount, decimal Landed, decimal LineTotal)>();

        foreach (var line in request.Lines)
        {
            var medicine = medicines[line.MedicineId];
            var qty = ScaleQuantity(line.OrderedQuantity);
            var unitCost = ScaleMoney(line.UnitCost);
            var discount = ScaleMoney(line.DiscountAmount);
            var landed = ScaleMoney(line.LandedCostAllocated);
            var baseAmount = qty * unitCost;

            if (discount > baseAmount)
            {
                throw new ArgumentException("Line discount cannot exceed the line base amount.");
            }

            var lineTotal = ScaleMoney(baseAmount - discount + landed);
            if (lineTotal < 0m)
            {
                throw new ArgumentException("Line total cannot be negative.");
            }

            subtotal += baseAmount;
            discountTotal += discount;
            landedTotal += landed;
            prepared.Add((line, medicine, qty, unitCost, discount, landed, lineTotal));
        }

        subtotal = ScaleMoney(subtotal);
        discountTotal = ScaleMoney(discountTotal);
        landedTotal = ScaleMoney(landedTotal);
        var grandTotal = ScaleMoney(subtotal - discountTotal + landedTotal);

        var order = new PurchaseOrderEntity
        {
            Id = Guid.CreateVersion7().ToString(),
            SupplierId = supplier.Id,
            Number = number,
            Status = "draft",
            OrderDate = request.OrderDate,
            ExpectedDate = request.ExpectedDate,
            Currency = request.Currency.Trim().ToUpperInvariant(),
            Subtotal = subtotal,
            DiscountTotal = discountTotal,
            LandedCostTotal = landedTotal,
            GrandTotal = grandTotal,
            Notes = NormalizeOptional(request.Notes, 2000),
            CreatedBy = actorId,
            CreatedAt = now,
            UpdatedAt = now,
        };

        context.Add(order);

        foreach (var item in prepared)
        {
            context.Add(new PurchaseOrderLineEntity
            {
                Id = Guid.CreateVersion7().ToString(),
                PurchaseOrderId = order.Id,
                MedicineId = item.Medicine.Id,
                Description = BuildMedicineDescription(item.Medicine),
                OrderedQuantity = item.Qty,
                ReceivedQuantity = 0m,
                UnitCost = item.UnitCost,
                DiscountAmount = item.Discount,
                LandedCostAllocated = item.Landed,
                LineTotal = item.LineTotal,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return order.Id;
    }

    public async Task SubmitOrderAsync(
        string orderId,
        CancellationToken cancellationToken = default)
    {
        _permissions.Demand("purchases.manage");
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await InventoryWriteTransaction.BeginAsync(context, cancellationToken);

        var order = await context.Set<PurchaseOrderEntity>()
            .SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken)
            ?? throw new InvalidOperationException("Purchase order was not found.");

        if (!string.Equals(order.Status, "draft", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Only draft purchase orders can be submitted.");
        }

        order.Status = "submitted";
        order.SubmittedAt = _clock.UtcNow;
        order.UpdatedAt = order.SubmittedAt.Value;
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task ApproveOrderAsync(
        string orderId,
        CancellationToken cancellationToken = default)
    {
        _permissions.Demand("purchases.approve");
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);

        var actorId = CurrentUserId();

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await InventoryWriteTransaction.BeginAsync(context, cancellationToken);

        var order = await context.Set<PurchaseOrderEntity>()
            .SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken)
            ?? throw new InvalidOperationException("Purchase order was not found.");

        if (!string.Equals(order.Status, "submitted", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Only submitted purchase orders can be approved.");
        }

        order.Status = "approved";
        order.ApprovedBy = actorId;
        order.ApprovedAt = _clock.UtcNow;
        order.UpdatedAt = order.ApprovedAt.Value;
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task CancelOrderAsync(
        string orderId,
        CancellationToken cancellationToken = default)
    {
        _permissions.Demand("purchases.manage");
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await InventoryWriteTransaction.BeginAsync(context, cancellationToken);

        var order = await context.Set<PurchaseOrderEntity>()
            .Include(x => x.Receipts)
            .SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken)
            ?? throw new InvalidOperationException("Purchase order was not found.");

        if (order.Receipts.Count != 0)
        {
            throw new InvalidOperationException("A purchase order with goods receipts cannot be cancelled.");
        }

        if (order.Status is "cancelled" or "closed")
        {
            throw new InvalidOperationException("This purchase order cannot be cancelled.");
        }

        order.Status = "cancelled";
        order.CancelledAt = _clock.UtcNow;
        order.UpdatedAt = order.CancelledAt.Value;
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<string> CaptureGoodsReceiptAsync(
        string orderId,
        CaptureGoodsReceiptRequest request,
        CancellationToken cancellationToken = default)
    {
        _permissions.Demand("purchases.manage");
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        ValidateReceipt(request);

        var actorId = CurrentUserId();

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await InventoryWriteTransaction.BeginAsync(context, cancellationToken);

        var order = await context.Set<PurchaseOrderEntity>()
            .Include(x => x.Lines)
                .ThenInclude(x => x.Medicine)
            .SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken)
            ?? throw new InvalidOperationException("Purchase order was not found.");

        if (!ReceivableStatuses.Contains(order.Status))
        {
            throw new InvalidOperationException("This purchase order cannot receive more goods.");
        }

        var idempotencyKey = NormalizeOptional(request.IdempotencyKey, 120);
        if (idempotencyKey is not null)
        {
            var existing = await context.Set<GoodsReceiptEntity>()
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.IdempotencyKey == idempotencyKey, cancellationToken);

            if (existing is not null)
            {
                if (existing.PurchaseOrderId != order.Id)
                {
                    throw new InvalidOperationException("Idempotency key already belongs to another purchase order.");
                }

                await transaction.CommitAsync(cancellationToken);
                return existing.Id;
            }
        }

        var positiveLines = request.Lines
            .Where(x => x.ReceivedQuantity > 0m)
            .ToList();

        if (positiveLines.Count == 0)
        {
            throw new ArgumentException("Enter a received quantity for at least one purchase-order line.");
        }

        if (positiveLines.Select(x => x.PurchaseOrderLineId).Distinct().Count() != positiveLines.Count)
        {
            throw new ArgumentException("A purchase-order line can appear only once in a goods receipt.");
        }

        var orderLines = order.Lines.ToDictionary(x => x.Id);

        foreach (var line in positiveLines)
        {
            if (!orderLines.TryGetValue(line.PurchaseOrderLineId, out var orderLine))
            {
                throw new ArgumentException("A receipt line does not belong to this purchase order.");
            }

            var pending = await context.Set<GoodsReceiptLineEntity>()
                .Where(x =>
                    x.PurchaseOrderLineId == orderLine.Id &&
                    x.GoodsReceipt.PurchaseOrderId == order.Id &&
                    x.GoodsReceipt.InventoryPostedAt == null)
                .SumAsync(x => (decimal?)x.ReceivedQuantity, cancellationToken) ?? 0m;

            var received = ScaleQuantity(line.ReceivedQuantity);
            if (ScaleQuantity(orderLine.ReceivedQuantity + pending + received) > orderLine.OrderedQuantity)
            {
                throw new InvalidOperationException("Received quantity exceeds the remaining ordered quantity.");
            }

            if (orderLine.Medicine.BatchTrackingRequired &&
                string.IsNullOrWhiteSpace(line.BatchNumber))
            {
                throw new InvalidOperationException($"{orderLine.Medicine.BrandName} requires a batch number.");
            }

            if (orderLine.Medicine.ExpiryTrackingRequired &&
                line.ExpiresAt is null)
            {
                throw new InvalidOperationException($"{orderLine.Medicine.BrandName} requires an expiry date.");
            }
        }

        var now = _clock.UtcNow;
        var receipt = new GoodsReceiptEntity
        {
            Id = Guid.CreateVersion7().ToString(),
            PurchaseOrderId = order.Id,
            SupplierId = order.SupplierId,
            ReceiptNumber = CreateDocumentNumber("GRN", now),
            Status = "pending_inventory",
            ReceivedAt = request.ReceivedAt,
            IdempotencyKey = idempotencyKey,
            CreatedBy = actorId,
            Notes = NormalizeOptional(request.Notes, 2000),
            CreatedAt = now,
            UpdatedAt = now,
        };

        context.Add(receipt);

        foreach (var line in positiveLines)
        {
            var orderLine = orderLines[line.PurchaseOrderLineId];
            context.Add(new GoodsReceiptLineEntity
            {
                Id = Guid.CreateVersion7().ToString(),
                GoodsReceiptId = receipt.Id,
                PurchaseOrderLineId = orderLine.Id,
                MedicineId = orderLine.MedicineId,
                ReceivedQuantity = ScaleQuantity(line.ReceivedQuantity),
                BonusQuantity = ScaleQuantity(line.BonusQuantity),
                BatchNumber = NormalizeOptional(line.BatchNumber, 120),
                ManufacturedAt = line.ManufacturedAt,
                ExpiresAt = line.ExpiresAt,
                UnitCost = ScaleMoney(line.UnitCost),
                SalePrice = line.SalePrice is null ? null : ScaleMoney(line.SalePrice.Value),
                CreatedAt = now,
                UpdatedAt = now,
            });
        }

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return receipt.Id;
    }

    public async Task PostGoodsReceiptAsync(
        string receiptId,
        string stockLocationId,
        CancellationToken cancellationToken = default)
    {
        _permissions.Demand("inventory.manage");
        ArgumentException.ThrowIfNullOrWhiteSpace(receiptId);
        ArgumentException.ThrowIfNullOrWhiteSpace(stockLocationId);

        var actorId = CurrentUserId();

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await InventoryWriteTransaction.BeginAsync(context, cancellationToken);

        var receipt = await context.Set<GoodsReceiptEntity>()
            .Include(x => x.Lines)
                .ThenInclude(x => x.PurchaseOrderLine)
            .Include(x => x.Lines)
                .ThenInclude(x => x.Medicine)
            .SingleOrDefaultAsync(x => x.Id == receiptId, cancellationToken)
            ?? throw new InvalidOperationException("Goods receipt was not found.");

        if (receipt.InventoryPostedAt is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        var location = await context.Set<StockLocationEntity>()
            .Include(x => x.Branch)
            .SingleOrDefaultAsync(
                x => x.Id == stockLocationId && x.IsActive && x.Branch.IsActive,
                cancellationToken)
            ?? throw new InvalidOperationException("Stock location was not found or is inactive.");

        foreach (var receiptLine in receipt.Lines)
        {
            var orderLine = receiptLine.PurchaseOrderLine;
            var received = ScaleQuantity(receiptLine.ReceivedQuantity);
            var bonus = ScaleQuantity(receiptLine.BonusQuantity);
            var incomingQuantity = ScaleQuantity(received + bonus);

            if (incomingQuantity <= 0m)
            {
                continue;
            }

            if (orderLine.OrderedQuantity <= 0m)
            {
                throw new InvalidOperationException("Purchase-order quantity must be greater than zero.");
            }

            var batchKey = BuildBatchKey(receiptLine.BatchNumber, receiptLine.ExpiresAt, receiptLine.Id);
            var batch = await context.Set<ProductBatchEntity>()
                .SingleOrDefaultAsync(
                    x => x.MedicineId == receiptLine.MedicineId &&
                         x.StockLocationId == location.Id &&
                         x.BatchKey == batchKey,
                    cancellationToken);

            var now = _clock.UtcNow;
            if (batch is null)
            {
                batch = new ProductBatchEntity
                {
                    Id = Guid.CreateVersion7().ToString(),
                    MedicineId = receiptLine.MedicineId,
                    SupplierId = receipt.SupplierId,
                    PurchaseOrderId = receipt.PurchaseOrderId,
                    GoodsReceiptId = receipt.Id,
                    BranchId = location.BranchId,
                    StockLocationId = location.Id,
                    BatchNumber = receiptLine.BatchNumber,
                    BatchKey = batchKey,
                    ManufacturedAt = receiptLine.ManufacturedAt,
                    ExpiresAt = receiptLine.ExpiresAt,
                    Status = "active",
                    ReceivedQuantity = 0m,
                    AvailableQuantity = 0m,
                    PurchaseCost = 0m,
                    SalePrice = receiptLine.SalePrice,
                    CreatedAt = now,
                    UpdatedAt = now,
                };

                context.Add(batch);
                await context.SaveChangesAsync(cancellationToken);
            }

            var ratio = decimal.Round(
                received / orderLine.OrderedQuantity,
                12,
                MidpointRounding.AwayFromZero);
            var baseAmount = received * receiptLine.UnitCost;
            var discount = orderLine.DiscountAmount * ratio;
            var landed = orderLine.LandedCostAllocated * ratio;
            var incomingCostTotal = baseAmount - discount + landed;

            if (incomingCostTotal < 0m)
            {
                throw new InvalidOperationException("Allocated receipt cost cannot be negative.");
            }

            var oldReceived = batch.ReceivedQuantity;
            var newReceived = ScaleQuantity(oldReceived + incomingQuantity);
            var oldCostTotal = oldReceived * batch.PurchaseCost;
            var weightedCost = newReceived == 0m
                ? 0m
                : ScaleMoney((oldCostTotal + incomingCostTotal) / newReceived);
            var effectiveIncomingCost = ScaleMoney(incomingCostTotal / incomingQuantity);

            batch.ReceivedQuantity = newReceived;
            batch.PurchaseCost = weightedCost;
            if (receiptLine.SalePrice is not null)
            {
                batch.SalePrice = receiptLine.SalePrice;
            }

            if (batch.Status == "depleted")
            {
                batch.Status = "active";
            }

            batch.UpdatedAt = now;
            await context.SaveChangesAsync(cancellationToken);

            await _ledger.RecordAsync(
                context,
                batch,
                incomingQuantity,
                "purchase_receipt",
                "goods_receipt",
                receipt.Id,
                $"grn:{receipt.Id}:{receiptLine.Id}",
                actorId,
                receiptLine.Id,
                "Goods received from supplier",
                effectiveIncomingCost,
                $"{{\"bonus_quantity\":\"{bonus.ToString("0.####", CultureInfo.InvariantCulture)}\"}}",
                cancellationToken);

            var nextReceived = ScaleQuantity(orderLine.ReceivedQuantity + received);
            if (nextReceived > orderLine.OrderedQuantity)
            {
                throw new InvalidOperationException("Posting this receipt would exceed the purchase-order quantity.");
            }

            orderLine.ReceivedQuantity = nextReceived;
            orderLine.UpdatedAt = now;
            await context.SaveChangesAsync(cancellationToken);
        }

        receipt.StockLocationId = location.Id;
        receipt.Status = "posted";
        receipt.InventoryPostedAt = _clock.UtcNow;
        receipt.UpdatedAt = receipt.InventoryPostedAt.Value;

        var allOrderLines = await context.Set<PurchaseOrderLineEntity>()
            .Where(x => x.PurchaseOrderId == receipt.PurchaseOrderId)
            .ToListAsync(cancellationToken);

        var fullyReceived = allOrderLines.All(x => x.ReceivedQuantity >= x.OrderedQuantity);
        var order = await context.Set<PurchaseOrderEntity>()
            .SingleAsync(x => x.Id == receipt.PurchaseOrderId, cancellationToken);
        order.Status = fullyReceived ? "received" : "partially_received";
        order.UpdatedAt = _clock.UtcNow;

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<string> CreateInvoiceAsync(
        string orderId,
        CreatePurchaseInvoiceRequest request,
        CancellationToken cancellationToken = default)
    {
        _permissions.Demand("purchases.manage");
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        ValidateInvoice(request);

        var actorId = CurrentUserId();

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await InventoryWriteTransaction.BeginAsync(context, cancellationToken);

        var order = await context.Set<PurchaseOrderEntity>()
            .Include(x => x.Supplier)
            .Include(x => x.Invoices)
            .SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken)
            ?? throw new InvalidOperationException("Purchase order was not found.");

        if (order.Status is not ("approved" or "partially_received" or "received"))
        {
            throw new InvalidOperationException("This purchase order cannot be invoiced.");
        }

        if (order.Invoices.Any(x => x.Status != "cancelled"))
        {
            throw new InvalidOperationException("This purchase order already has an active invoice.");
        }

        var goodsReceiptId = NormalizeOptional(request.GoodsReceiptId, 36);
        if (goodsReceiptId is not null)
        {
            var belongs = await context.Set<GoodsReceiptEntity>()
                .AnyAsync(x => x.Id == goodsReceiptId && x.PurchaseOrderId == order.Id, cancellationToken);
            if (!belongs)
            {
                throw new ArgumentException("The selected goods receipt does not belong to this purchase order.");
            }
        }

        var supplierInvoiceNumber = NormalizeOptional(request.SupplierInvoiceNumber, 120);
        if (supplierInvoiceNumber is not null &&
            await context.Set<PurchaseInvoiceEntity>().AnyAsync(
                x => x.SupplierId == order.SupplierId &&
                     x.SupplierInvoiceNumber == supplierInvoiceNumber,
                cancellationToken))
        {
            throw new ArgumentException("This supplier invoice number has already been recorded.");
        }

        var dueDate = request.DueDate ??
                      request.InvoiceDate.AddDays(order.Supplier.PaymentTermsDays);

        var now = _clock.UtcNow;
        var invoice = new PurchaseInvoiceEntity
        {
            Id = Guid.CreateVersion7().ToString(),
            SupplierId = order.SupplierId,
            PurchaseOrderId = order.Id,
            GoodsReceiptId = goodsReceiptId,
            InvoiceNumber = CreateDocumentNumber("PINV", now),
            SupplierInvoiceNumber = supplierInvoiceNumber,
            InvoiceDate = request.InvoiceDate,
            DueDate = dueDate,
            Currency = order.Currency,
            Status = "open",
            Subtotal = order.Subtotal,
            DiscountTotal = order.DiscountTotal,
            LandedCostTotal = order.LandedCostTotal,
            GrandTotal = order.GrandTotal,
            PaidTotal = 0m,
            BalanceDue = order.GrandTotal,
            CreatedBy = actorId,
            Notes = NormalizeOptional(request.Notes, 2000),
            CreatedAt = now,
            UpdatedAt = now,
        };

        context.Add(invoice);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return invoice.Id;
    }

    public async Task<string> RecordSupplierPaymentAsync(
        string invoiceId,
        RecordSupplierPaymentRequest request,
        CancellationToken cancellationToken = default)
    {
        _permissions.Demand("purchases.pay");
        ArgumentException.ThrowIfNullOrWhiteSpace(invoiceId);
        ValidatePayment(request);

        var actorId = CurrentUserId();

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await InventoryWriteTransaction.BeginAsync(context, cancellationToken);

        var idempotencyKey = NormalizeOptional(request.IdempotencyKey, 120);
        if (idempotencyKey is not null)
        {
            var existing = await context.Set<SupplierPaymentEntity>()
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.IdempotencyKey == idempotencyKey, cancellationToken);

            if (existing is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                return existing.Id;
            }
        }

        var invoice = await context.Set<PurchaseInvoiceEntity>()
            .SingleOrDefaultAsync(x => x.Id == invoiceId, cancellationToken)
            ?? throw new InvalidOperationException("Purchase invoice was not found.");

        if (invoice.Status == "cancelled")
        {
            throw new InvalidOperationException("Cancelled invoices cannot receive payments.");
        }

        var currency = request.Currency.Trim().ToUpperInvariant();
        if (currency != invoice.Currency)
        {
            throw new ArgumentException("Payment currency must match invoice currency.");
        }

        var amount = ScaleMoney(request.Amount);
        if (amount <= 0m || amount > invoice.BalanceDue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request.Amount),
                "Payment must be greater than zero and cannot exceed the balance due.");
        }

        var now = _clock.UtcNow;
        var payment = new SupplierPaymentEntity
        {
            Id = Guid.CreateVersion7().ToString(),
            PurchaseInvoiceId = invoice.Id,
            SupplierId = invoice.SupplierId,
            PaymentNumber = CreateDocumentNumber("PAY", now),
            Amount = amount,
            Currency = currency,
            Method = request.Method.Trim().ToLowerInvariant(),
            Reference = NormalizeOptional(request.Reference, 160),
            PaidAt = request.PaidAt,
            IdempotencyKey = idempotencyKey,
            CreatedBy = actorId,
            Notes = NormalizeOptional(request.Notes, 2000),
            CreatedAt = now,
            UpdatedAt = now,
        };

        context.Add(payment);

        invoice.PaidTotal = ScaleMoney(invoice.PaidTotal + amount);
        invoice.BalanceDue = ScaleMoney(invoice.GrandTotal - invoice.PaidTotal);
        invoice.Status = invoice.BalanceDue == 0m ? "paid" : "partially_paid";
        invoice.UpdatedAt = now;

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return payment.Id;
    }

    private string CurrentUserId() =>
        _sessions.Current?.UserId
        ?? throw new InvalidOperationException("A pharmacy user must be signed in.");

    private static PurchaseOrderListItem ToListItem(PurchaseOrderEntity order) =>
        new(
            order.Id,
            order.Number,
            order.SupplierId,
            order.Supplier.Name,
            order.Status,
            order.OrderDate,
            order.ExpectedDate,
            order.Currency,
            order.Subtotal,
            order.DiscountTotal,
            order.LandedCostTotal,
            order.GrandTotal);

    private static string BuildMedicineDescription(MedicineEntity medicine) =>
        string.Join(
            " ",
            new[] { medicine.BrandName, medicine.Strength }
                .Where(x => !string.IsNullOrWhiteSpace(x)))
        .Trim();

    private static void ValidateOrder(CreatePurchaseOrderRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SupplierId);
        ValidateCurrency(request.Currency);
        _ = NormalizeOptional(request.Notes, 2000);

        if (request.ExpectedDate is not null && request.ExpectedDate.Value < request.OrderDate)
        {
            throw new ArgumentException("Expected date cannot be before order date.");
        }

        if (request.Lines is null || request.Lines.Count is < 1 or > 250)
        {
            throw new ArgumentOutOfRangeException(nameof(request.Lines));
        }

        foreach (var line in request.Lines)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(line.MedicineId);
            ValidateQuantity(line.OrderedQuantity, nameof(line.OrderedQuantity));
            ValidateMoney(line.UnitCost, nameof(line.UnitCost));
            ValidateMoney(line.DiscountAmount, nameof(line.DiscountAmount));
            ValidateMoney(line.LandedCostAllocated, nameof(line.LandedCostAllocated));
        }
    }

    private static void ValidateReceipt(CaptureGoodsReceiptRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        _ = NormalizeOptional(request.IdempotencyKey, 120);
        _ = NormalizeOptional(request.Notes, 2000);

        if (request.Lines is null || request.Lines.Count is < 1 or > 250)
        {
            throw new ArgumentOutOfRangeException(nameof(request.Lines));
        }

        foreach (var line in request.Lines)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(line.PurchaseOrderLineId);
            if (line.ReceivedQuantity < 0m || line.ReceivedQuantity > 999_999_999m)
            {
                throw new ArgumentOutOfRangeException(nameof(line.ReceivedQuantity));
            }

            if (line.BonusQuantity < 0m || line.BonusQuantity > 999_999_999m)
            {
                throw new ArgumentOutOfRangeException(nameof(line.BonusQuantity));
            }

            ValidateMoney(line.UnitCost, nameof(line.UnitCost));
            if (line.SalePrice is not null)
            {
                ValidateMoney(line.SalePrice.Value, nameof(line.SalePrice));
            }

            _ = NormalizeOptional(line.BatchNumber, 120);
        }
    }

    private static void ValidateInvoice(CreatePurchaseInvoiceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        _ = NormalizeOptional(request.SupplierInvoiceNumber, 120);
        _ = NormalizeOptional(request.GoodsReceiptId, 36);
        _ = NormalizeOptional(request.Notes, 2000);

        if (request.DueDate is not null && request.DueDate.Value < request.InvoiceDate)
        {
            throw new ArgumentException("Due date cannot be before invoice date.");
        }
    }

    private static void ValidatePayment(RecordSupplierPaymentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateMoney(request.Amount, nameof(request.Amount));
        if (request.Amount <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(request.Amount));
        }

        ValidateCurrency(request.Currency);

        if (!PaymentMethods.Contains(request.Method))
        {
            throw new ArgumentException("Unsupported supplier payment method.", nameof(request.Method));
        }

        _ = NormalizeOptional(request.Reference, 160);
        _ = NormalizeOptional(request.IdempotencyKey, 120);
        _ = NormalizeOptional(request.Notes, 2000);
    }

    private static void ValidateCurrency(string currency)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);
        currency = currency.Trim();
        if (currency.Length != 3 || !currency.All(char.IsLetter))
        {
            throw new ArgumentException("Currency must be a three-letter code.", nameof(currency));
        }
    }

    private static void ValidateQuantity(decimal value, string parameter)
    {
        if (value <= 0m || value > 999_999_999m)
        {
            throw new ArgumentOutOfRangeException(parameter);
        }
    }

    private static void ValidateMoney(decimal value, string parameter)
    {
        if (value < 0m || value > 999_999_999_999m)
        {
            throw new ArgumentOutOfRangeException(parameter);
        }
    }

    private static decimal ScaleQuantity(decimal value) =>
        decimal.Round(value, 4, MidpointRounding.AwayFromZero);

    private static decimal ScaleMoney(decimal value) =>
        decimal.Round(value, 4, MidpointRounding.AwayFromZero);

    private static string CreateDocumentNumber(string prefix, DateTimeOffset now) =>
        $"{prefix}-{StockLedger.BusinessDate(now):yyyyMMdd}-{Guid.CreateVersion7():N}".ToUpperInvariant();

    private static string BuildBatchKey(
        string? batchNumber,
        DateOnly? expiresAt,
        string fallback)
    {
        var normalizedBatch = NormalizeOptional(batchNumber, 120);
        var expiry = expiresAt?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var identity = normalizedBatch is not null
            ? $"{normalizedBatch.ToLowerInvariant()}|{expiry ?? "no-expiry"}"
            : $"unbatched|{expiry ?? fallback}";

        return Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(identity)))
            .ToLowerInvariant();
    }

    private static string? NormalizeOptional(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        value = value.Trim();
        if (value.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        return value;
    }
}
