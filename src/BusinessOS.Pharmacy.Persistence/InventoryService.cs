using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using BusinessOS.Pharmacy.Application.Abstractions.Authentication;
using BusinessOS.Pharmacy.Application.Abstractions.Inventory;
using BusinessOS.Pharmacy.Application.Abstractions.Persistence;
using BusinessOS.Pharmacy.Application.Abstractions.Time;
using BusinessOS.Pharmacy.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace BusinessOS.Pharmacy.Persistence;

public sealed class InventoryService : IInventoryService
{
    private static readonly HashSet<string> AllowedBatchStatuses =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "active",
            "quarantined",
            "recalled",
            "damaged",
        };

    private static readonly HashSet<string> AllowedAdjustmentReasons =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "count_correction",
            "damage",
            "wastage",
            "found_stock",
            "other",
        };

    private readonly IDbContextFactory<PharmacyDbContext> _contextFactory;
    private readonly IPermissionAuthorizer _permissions;
    private readonly IUserSessionService _sessions;
    private readonly ILocalSequenceService _sequences;
    private readonly IClock _clock;
    private readonly StockLedger _ledger;

    public InventoryService(
        IDbContextFactory<PharmacyDbContext> contextFactory,
        IPermissionAuthorizer permissions,
        IUserSessionService sessions,
        ILocalSequenceService sequences,
        IClock clock,
        StockLedger ledger)
    {
        _contextFactory = contextFactory;
        _permissions = permissions;
        _sessions = sessions;
        _sequences = sequences;
        _clock = clock;
        _ledger = ledger;
    }

    public async Task EnsureDefaultsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await InventoryWriteTransaction.BeginAsync(
            context,
            cancellationToken);

        var now = _clock.UtcNow;

        var branch = await context.Set<BranchEntity>()
            .SingleOrDefaultAsync(x => x.Code == "MAIN", cancellationToken);

        if (branch is null)
        {
            branch = new BranchEntity
            {
                Id = Guid.CreateVersion7().ToString(),
                Code = "MAIN",
                Name = "Main Branch",
                IsDefault = true,
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now,
            };
            context.Add(branch);
            await context.SaveChangesAsync(cancellationToken);
        }

        var location = await context.Set<StockLocationEntity>()
            .SingleOrDefaultAsync(
                x => x.BranchId == branch.Id && x.Code == "MAIN",
                cancellationToken);

        if (location is null)
        {
            location = new StockLocationEntity
            {
                Id = Guid.CreateVersion7().ToString(),
                BranchId = branch.Id,
                Code = "MAIN",
                Name = "Main Stock",
                Kind = "store",
                IsDefault = true,
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now,
            };
            context.Add(location);
            await context.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<InventoryReferenceData> GetReferenceDataAsync(
        CancellationToken cancellationToken = default)
    {
        DemandView();

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var branches = await context.Set<BranchEntity>()
            .AsNoTracking()
            .OrderByDescending(x => x.IsDefault)
            .ThenBy(x => x.Name)
            .Select(x => new BranchReferenceItem(
                x.Id,
                x.Code,
                x.Name,
                x.IsDefault,
                x.IsActive))
            .ToListAsync(cancellationToken);

        var locations = await context.Set<StockLocationEntity>()
            .AsNoTracking()
            .Include(x => x.Branch)
            .OrderByDescending(x => x.IsDefault)
            .ThenBy(x => x.Name)
            .Select(x => new StockLocationReferenceItem(
                x.Id,
                x.BranchId,
                x.Branch.Name,
                x.Code,
                x.Name,
                x.Kind,
                x.IsDefault,
                x.IsActive))
            .ToListAsync(cancellationToken);

        return new InventoryReferenceData(branches, locations);
    }

    public async Task<IReadOnlyList<InventoryBatchListItem>> SearchBatchesAsync(
        InventoryBatchFilter filter,
        CancellationToken cancellationToken = default)
    {
        DemandView();

        var take = Math.Clamp(filter.Take, 1, 1000);
        var nearExpiryDays = Math.Clamp(filter.NearExpiryDays, 1, 3650);
        var businessDate = filter.BusinessDate ?? StockLedger.BusinessDate(_clock.UtcNow);
        var nearExpiryDate = businessDate.AddDays(nearExpiryDays);
        var search = filter.Search?.Trim();

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var query = context.Set<ProductBatchEntity>()
            .AsNoTracking()
            .Include(x => x.Medicine)
            .Include(x => x.StockLocation)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search}%";
            query = query.Where(x =>
                (x.BatchNumber != null && EF.Functions.Like(x.BatchNumber, pattern)) ||
                EF.Functions.Like(x.Medicine.BrandName, pattern) ||
                (x.Medicine.GenericName != null && EF.Functions.Like(x.Medicine.GenericName, pattern)) ||
                EF.Functions.Like(x.Medicine.MedicineCode, pattern));
        }

        if (!string.IsNullOrWhiteSpace(filter.Status))
        {
            query = query.Where(x => x.Status == filter.Status);
        }

        if (!string.IsNullOrWhiteSpace(filter.StockLocationId))
        {
            query = query.Where(x => x.StockLocationId == filter.StockLocationId);
        }

        query = filter.Expiry?.Trim().ToLowerInvariant() switch
        {
            null or "" => query,
            "expired" => query.Where(x => x.ExpiresAt != null && x.ExpiresAt < businessDate),
            "near" => query.Where(x =>
                x.ExpiresAt != null &&
                x.ExpiresAt >= businessDate &&
                x.ExpiresAt <= nearExpiryDate),
            "no_expiry" => query.Where(x => x.ExpiresAt == null),
            _ => throw new ArgumentException("Unsupported expiry filter.", nameof(filter)),
        };

        var batches = await query
            .OrderBy(x => x.ExpiresAt == null)
            .ThenBy(x => x.ExpiresAt)
            .ThenBy(x => x.Medicine.BrandName)
            .ThenBy(x => x.CreatedAt)
            .Take(take)
            .ToListAsync(cancellationToken);

        return batches
            .Select(x => ToListItem(x, businessDate, nearExpiryDate))
            .ToList();
    }

    public async Task<InventoryBatchDetail?> GetBatchAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        DemandView();
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var batch = await context.Set<ProductBatchEntity>()
            .AsNoTracking()
            .Include(x => x.Medicine)
            .Include(x => x.StockLocation)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (batch is null)
        {
            return null;
        }

        var movements = await context.Set<StockMovementEntity>()
            .AsNoTracking()
            .Where(x => x.ProductBatchId == id)
            .OrderByDescending(x => x.OccurredAt)
            .Take(100)
            .Select(x => new StockMovementItem(
                x.Id,
                x.MovementType,
                x.QuantityDelta,
                x.BalanceAfter,
                x.UnitCost,
                x.SourceType,
                x.SourceId,
                x.SourceLineId,
                x.Reason,
                x.IdempotencyKey,
                x.ActorId,
                x.OccurredAt))
            .ToListAsync(cancellationToken);

        var statusEvents = await context.Set<BatchStatusEventEntity>()
            .AsNoTracking()
            .Where(x => x.ProductBatchId == id)
            .OrderByDescending(x => x.ChangedAt)
            .Take(50)
            .Select(x => new BatchStatusEventItem(
                x.Id,
                x.FromStatus,
                x.ToStatus,
                x.Reason,
                x.ActorId,
                x.ChangedAt))
            .ToListAsync(cancellationToken);

        var businessDate = StockLedger.BusinessDate(_clock.UtcNow);

        return new InventoryBatchDetail(
            ToListItem(batch, businessDate, businessDate.AddDays(90)),
            movements,
            statusEvents);
    }

    public async Task<string> CreateOpeningStockAsync(
        CreateOpeningStockRequest request,
        CancellationToken cancellationToken = default)
    {
        _permissions.Demand("inventory.manage");
        ValidateOpeningStock(request);

        var actorId = CurrentUserId();
        var quantity = StockLedger.Scale(request.Quantity);
        var purchaseCost = StockLedger.ScaleMoney(request.PurchaseCost);
        var salePrice = request.SalePrice is null
            ? null
            : StockLedger.ScaleMoney(request.SalePrice.Value);
        var operationId = Guid.CreateVersion7().ToString();
        var batchKey = BuildBatchKey(
            request.BatchNumber,
            request.ExpiresAt,
            operationId);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await InventoryWriteTransaction.BeginAsync(
            context,
            cancellationToken);

        var medicine = await context.Set<MedicineEntity>()
            .SingleOrDefaultAsync(
                x => x.Id == request.MedicineId && x.IsActive,
                cancellationToken)
            ?? throw new InvalidOperationException("Medicine was not found or is inactive.");

        var location = await context.Set<StockLocationEntity>()
            .Include(x => x.Branch)
            .SingleOrDefaultAsync(
                x => x.Id == request.StockLocationId &&
                     x.IsActive &&
                     x.Branch.IsActive,
                cancellationToken)
            ?? throw new InvalidOperationException("Stock location was not found or is inactive.");

        var batch = await context.Set<ProductBatchEntity>()
            .SingleOrDefaultAsync(
                x => x.MedicineId == medicine.Id &&
                     x.StockLocationId == location.Id &&
                     x.BatchKey == batchKey,
                cancellationToken);

        var now = _clock.UtcNow;

        if (batch is null)
        {
            batch = new ProductBatchEntity
            {
                Id = Guid.CreateVersion7().ToString(),
                MedicineId = medicine.Id,
                BranchId = location.BranchId,
                StockLocationId = location.Id,
                BatchNumber = NormalizeOptional(request.BatchNumber, 120),
                BatchKey = batchKey,
                ManufacturedAt = request.ManufacturedAt,
                ExpiresAt = request.ExpiresAt,
                Status = "active",
                ReceivedQuantity = 0m,
                AvailableQuantity = 0m,
                PurchaseCost = 0m,
                SalePrice = salePrice,
                CreatedAt = now,
                UpdatedAt = now,
            };
            context.Add(batch);
            await context.SaveChangesAsync(cancellationToken);
        }

        var oldReceived = batch.ReceivedQuantity;
        var newReceived = StockLedger.Scale(oldReceived + quantity);
        var oldCostTotal = oldReceived * batch.PurchaseCost;
        var incomingCostTotal = quantity * purchaseCost;

        batch.ReceivedQuantity = newReceived;
        batch.PurchaseCost = newReceived == 0m
            ? 0m
            : StockLedger.ScaleMoney((oldCostTotal + incomingCostTotal) / newReceived);

        if (salePrice is not null)
        {
            batch.SalePrice = salePrice;
        }

        batch.UpdatedAt = now;
        await context.SaveChangesAsync(cancellationToken);

        await _ledger.RecordAsync(
            context,
            batch,
            quantity,
            "opening_stock",
            "opening_stock",
            operationId,
            $"opening:{operationId}",
            actorId,
            null,
            NormalizeOptional(request.Notes, 255) ?? "Opening stock",
            purchaseCost,
            null,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return batch.Id;
    }

    public async Task<InventoryAdjustmentResult> AdjustAsync(
        InventoryAdjustmentRequest request,
        CancellationToken cancellationToken = default)
    {
        _permissions.Demand("inventory.adjust");
        ValidateAdjustment(request);

        var actorId = CurrentUserId();
        var sequence = await _sequences.NextAsync(
            "inventory-adjustment",
            cancellationToken);
        var now = _clock.UtcNow;
        var number = $"ADJ-{StockLedger.BusinessDate(now):yyyyMMdd}-{sequence:000000}";

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await InventoryWriteTransaction.BeginAsync(
            context,
            cancellationToken);

        var batch = await context.Set<ProductBatchEntity>()
            .SingleOrDefaultAsync(
                x => x.Id == request.ProductBatchId,
                cancellationToken)
            ?? throw new InvalidOperationException("Product batch was not found.");

        if (string.Equals(batch.Status, "recalled", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Recalled stock cannot be adjusted until its status is reviewed.");
        }

        var adjustment = new InventoryAdjustmentEntity
        {
            Id = Guid.CreateVersion7().ToString(),
            Number = number,
            StockLocationId = batch.StockLocationId,
            ReasonCode = request.ReasonCode,
            Status = "draft",
            Notes = request.Reason,
            CreatedBy = actorId,
            CreatedAt = now,
            UpdatedAt = now,
        };

        var line = new InventoryAdjustmentLineEntity
        {
            Id = Guid.CreateVersion7().ToString(),
            InventoryAdjustmentId = adjustment.Id,
            ProductBatchId = batch.Id,
            QuantityDelta = StockLedger.Scale(request.QuantityDelta),
            Reason = request.Reason,
            CreatedAt = now,
            UpdatedAt = now,
        };

        context.Add(adjustment);
        context.Add(line);
        await context.SaveChangesAsync(cancellationToken);

        var movement = await _ledger.RecordAsync(
            context,
            batch,
            line.QuantityDelta,
            "adjustment",
            "inventory_adjustment",
            adjustment.Id,
            $"adjustment:{adjustment.Id}:{line.Id}",
            actorId,
            line.Id,
            request.Reason,
            batch.PurchaseCost,
            $"{{\"reason_code\":\"{request.ReasonCode}\"}}",
            cancellationToken);

        adjustment.Status = "posted";
        adjustment.PostedBy = actorId;
        adjustment.PostedAt = _clock.UtcNow;
        adjustment.UpdatedAt = adjustment.PostedAt.Value;

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new InventoryAdjustmentResult(
            adjustment.Id,
            adjustment.Number,
            movement.BalanceAfter,
            adjustment.PostedAt.Value);
    }

    public async Task ChangeBatchStatusAsync(
        ChangeBatchStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        _permissions.Demand("inventory.status");
        ValidateStatusChange(request);

        var actorId = CurrentUserId();

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await InventoryWriteTransaction.BeginAsync(
            context,
            cancellationToken);

        var batch = await context.Set<ProductBatchEntity>()
            .SingleOrDefaultAsync(
                x => x.Id == request.ProductBatchId,
                cancellationToken)
            ?? throw new InvalidOperationException("Product batch was not found.");

        if (string.Equals(batch.Status, request.Status, StringComparison.OrdinalIgnoreCase))
        {
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        var now = _clock.UtcNow;
        var from = batch.Status;
        batch.Status = request.Status.ToLowerInvariant();
        batch.UpdatedAt = now;

        context.Add(new BatchStatusEventEntity
        {
            Id = Guid.CreateVersion7().ToString(),
            ProductBatchId = batch.Id,
            FromStatus = from,
            ToStatus = batch.Status,
            Reason = request.Reason.Trim(),
            ActorId = actorId,
            ChangedAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        });

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private void DemandView()
    {
        if (!_permissions.HasPermission("inventory.status") &&
            !_permissions.HasPermission("inventory.manage"))
        {
            _permissions.Demand("inventory.status");
        }
    }

    private string CurrentUserId() =>
        _sessions.Current?.UserId
        ?? throw new InvalidOperationException("A pharmacy user must be signed in.");

    private static InventoryBatchListItem ToListItem(
        ProductBatchEntity batch,
        DateOnly businessDate,
        DateOnly nearExpiryDate)
    {
        var expired = batch.ExpiresAt is not null &&
                      batch.ExpiresAt.Value < businessDate;
        var near = batch.ExpiresAt is not null &&
                   batch.ExpiresAt.Value >= businessDate &&
                   batch.ExpiresAt.Value <= nearExpiryDate;

        return new InventoryBatchListItem(
            batch.Id,
            batch.MedicineId,
            batch.Medicine.MedicineCode,
            batch.Medicine.BrandName,
            batch.Medicine.GenericName,
            batch.Medicine.Strength,
            batch.StockLocationId,
            batch.StockLocation.Name,
            batch.BatchNumber,
            batch.ManufacturedAt,
            batch.ExpiresAt,
            batch.Status,
            batch.ReceivedQuantity,
            batch.AvailableQuantity,
            batch.PurchaseCost,
            batch.SalePrice,
            batch.LastMovementAt,
            expired,
            near,
            string.Equals(batch.Status, "active", StringComparison.OrdinalIgnoreCase) &&
            !expired &&
            batch.AvailableQuantity > 0m);
    }

    private static void ValidateOpeningStock(CreateOpeningStockRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.MedicineId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.StockLocationId);

        if (request.Quantity <= 0 || request.Quantity > 999_999_999m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request.Quantity),
                "Opening quantity must be greater than zero and within the supported range.");
        }

        if (request.PurchaseCost < 0 || request.PurchaseCost > 999_999_999_999m)
        {
            throw new ArgumentOutOfRangeException(nameof(request.PurchaseCost));
        }

        if (request.SalePrice is < 0 or > 999_999_999_999m)
        {
            throw new ArgumentOutOfRangeException(nameof(request.SalePrice));
        }

        if (request.ManufacturedAt is not null &&
            request.ExpiresAt is not null &&
            request.ExpiresAt.Value < request.ManufacturedAt.Value)
        {
            throw new ArgumentException(
                "Expiry date cannot be before the manufactured date.");
        }

        _ = NormalizeOptional(request.BatchNumber, 120);
        _ = NormalizeOptional(request.Notes, 255);
    }

    private static void ValidateAdjustment(InventoryAdjustmentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ProductBatchId);

        if (request.QuantityDelta == 0 ||
            request.QuantityDelta > 999_999_999m ||
            request.QuantityDelta < -999_999_999m)
        {
            throw new ArgumentOutOfRangeException(nameof(request.QuantityDelta));
        }

        if (!AllowedAdjustmentReasons.Contains(request.ReasonCode))
        {
            throw new ArgumentException(
                "Unsupported inventory adjustment reason.",
                nameof(request.ReasonCode));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(request.Reason);
        if (request.Reason.Trim().Length > 500)
        {
            throw new ArgumentOutOfRangeException(nameof(request.Reason));
        }
    }

    private static void ValidateStatusChange(ChangeBatchStatusRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ProductBatchId);

        if (!AllowedBatchStatuses.Contains(request.Status))
        {
            throw new ArgumentException(
                "Unsupported batch status.",
                nameof(request.Status));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(request.Reason);
        if (request.Reason.Trim().Length > 500)
        {
            throw new ArgumentOutOfRangeException(nameof(request.Reason));
        }
    }

    private static string BuildBatchKey(
        string? batchNumber,
        DateOnly? expiresAt,
        string fallback)
    {
        var normalizedBatch = NormalizeOptional(batchNumber, 120);
        var expiry = expiresAt?.ToString(
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture);

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
