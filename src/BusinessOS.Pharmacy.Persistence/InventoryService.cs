using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
        new(StringComparer.Ordinal)
        {
            "active",
            "quarantined",
            "recalled",
            "damaged",
        };

    private static readonly HashSet<string> AllowedAdjustmentReasons =
        new(StringComparer.Ordinal)
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
    private readonly ILocalSettingsStore _settings;
    private readonly IInventoryProvisioner _provisioner;
    private readonly IClock _clock;

    public InventoryService(
        IDbContextFactory<PharmacyDbContext> contextFactory,
        IPermissionAuthorizer permissions,
        IUserSessionService sessions,
        ILocalSettingsStore settings,
        IInventoryProvisioner provisioner,
        IClock clock)
    {
        _contextFactory = contextFactory;
        _permissions = permissions;
        _sessions = sessions;
        _settings = settings;
        _provisioner = provisioner;
        _clock = clock;
    }

    public async Task<IReadOnlyList<InventoryBatchListItem>> SearchAsync(
        InventorySearchFilter filter,
        CancellationToken cancellationToken = default)
    {
        _permissions.Demand("inventory.manage");
        ArgumentNullException.ThrowIfNull(filter);

        var take = Math.Clamp(filter.Take, 1, 1000);
        var search = filter.Search?.Trim();
        var businessDate = BusinessDate();
        var policy = await GetPolicyAsync(cancellationToken);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var query = context.Set<ProductBatchEntity>()
            .AsNoTracking()
            .Include(x => x.Medicine)
            .Include(x => x.StockLocation)
                .ThenInclude(x => x.Branch)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search}%";
            query = query.Where(x =>
                (x.BatchNumber != null && EF.Functions.Like(x.BatchNumber, pattern)) ||
                EF.Functions.Like(x.Medicine.BrandName, pattern) ||
                EF.Functions.Like(x.Medicine.MedicineCode, pattern) ||
                (x.Medicine.GenericName != null &&
                    EF.Functions.Like(x.Medicine.GenericName, pattern)));
        }

        if (!string.IsNullOrWhiteSpace(filter.Status))
        {
            var status = filter.Status.Trim().ToLowerInvariant();
            query = query.Where(x => x.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(filter.StockLocationId))
        {
            query = query.Where(x => x.StockLocationId == filter.StockLocationId);
        }

        query = filter.Expiry?.Trim().ToLowerInvariant() switch
        {
            "expired" => query.Where(x => x.ExpiresAt != null && x.ExpiresAt < businessDate),
            "near" => query.Where(x =>
                x.ExpiresAt != null &&
                x.ExpiresAt >= businessDate &&
                x.ExpiresAt <= businessDate.AddDays(policy.NearExpiryDays)),
            "no_expiry" => query.Where(x => x.ExpiresAt == null),
            _ => query,
        };

        return await query
            .OrderBy(x => x.ExpiresAt == null)
            .ThenBy(x => x.ExpiresAt)
            .ThenBy(x => x.CreatedAt)
            .Take(take)
            .Select(x => new InventoryBatchListItem(
                x.Id,
                x.MedicineId,
                x.Medicine.MedicineCode,
                x.Medicine.BrandName,
                x.Medicine.GenericName,
                x.Medicine.Strength,
                x.BatchNumber,
                x.StockLocation.Branch.Name,
                x.StockLocation.Name,
                x.Status,
                x.ExpiresAt,
                x.ReceivedQuantity,
                x.AvailableQuantity,
                x.PurchaseCost,
                x.SalePrice,
                x.Medicine.ReorderLevel,
                x.LastMovementAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<InventoryBatchDetails?> GetBatchAsync(
        string productBatchId,
        CancellationToken cancellationToken = default)
    {
        _permissions.Demand("inventory.manage");
        ArgumentException.ThrowIfNullOrWhiteSpace(productBatchId);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var batch = await context.Set<ProductBatchEntity>()
            .AsNoTracking()
            .Where(x => x.Id == productBatchId)
            .Select(x => new InventoryBatchListItem(
                x.Id,
                x.MedicineId,
                x.Medicine.MedicineCode,
                x.Medicine.BrandName,
                x.Medicine.GenericName,
                x.Medicine.Strength,
                x.BatchNumber,
                x.StockLocation.Branch.Name,
                x.StockLocation.Name,
                x.Status,
                x.ExpiresAt,
                x.ReceivedQuantity,
                x.AvailableQuantity,
                x.PurchaseCost,
                x.SalePrice,
                x.Medicine.ReorderLevel,
                x.LastMovementAt))
            .SingleOrDefaultAsync(cancellationToken);

        if (batch is null)
        {
            return null;
        }

        var movements = await context.Set<StockMovementEntity>()
            .AsNoTracking()
            .Where(x => x.ProductBatchId == productBatchId)
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
                x.OccurredAt,
                x.MetadataJson))
            .ToListAsync(cancellationToken);

        var statusEvents = await context.Set<BatchStatusEventEntity>()
            .AsNoTracking()
            .Where(x => x.ProductBatchId == productBatchId)
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

        return new InventoryBatchDetails(batch, movements, statusEvents);
    }

    public async Task<InventoryReferenceData> GetReferenceDataAsync(
        CancellationToken cancellationToken = default)
    {
        _permissions.Demand("inventory.manage");
        await _provisioner.EnsureDefaultsAsync(cancellationToken);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var branches = await context.Set<BranchEntity>()
            .AsNoTracking()
            .OrderByDescending(x => x.IsDefault)
            .ThenBy(x => x.Name)
            .Select(x => new BranchReference(
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
            .Select(x => new StockLocationReference(
                x.Id,
                x.BranchId,
                x.Branch.Name,
                x.Code,
                x.Name,
                x.Kind,
                x.IsDefault,
                x.IsActive))
            .ToListAsync(cancellationToken);

        var medicines = await context.Set<MedicineEntity>()
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.BrandName)
            .Select(x => new InventoryMedicineReference(
                x.Id,
                x.MedicineCode,
                x.BrandName,
                x.GenericName,
                x.Strength,
                x.BatchTrackingRequired,
                x.ExpiryTrackingRequired,
                x.IsActive))
            .ToListAsync(cancellationToken);

        return new InventoryReferenceData(
            branches,
            locations,
            medicines,
            await GetPolicyAsync(cancellationToken));
    }

    public async Task<string> CreateOpeningStockAsync(
        CreateOpeningStockRequest request,
        CancellationToken cancellationToken = default)
    {
        _permissions.Demand("inventory.adjust");
        ArgumentNullException.ThrowIfNull(request);

        var medicineId = Required(request.MedicineId, 36, nameof(request.MedicineId));
        var locationId = Required(request.StockLocationId, 36, nameof(request.StockLocationId));
        var reason = Required(request.Reason, 255, nameof(request.Reason));
        var idempotencyKey = Required(request.IdempotencyKey, 191, nameof(request.IdempotencyKey));
        var batchNumber = Optional(request.BatchNumber, 120, nameof(request.BatchNumber));
        var quantity = StockLedger.Scale4(request.Quantity);
        var purchaseCost = StockLedger.Scale4(request.PurchaseCost);
        decimal? salePrice = request.SalePrice is null
            ? null
            : StockLedger.Scale4(request.SalePrice.Value);

        if (quantity <= 0m)
        {
            throw new InventoryValidationException("Opening stock quantity must be greater than zero.");
        }

        if (purchaseCost < 0m || salePrice is < 0m)
        {
            throw new InventoryValidationException("Inventory prices cannot be negative.");
        }

        if (request.ManufacturedAt is not null &&
            request.ExpiresAt is not null &&
            request.ManufacturedAt > request.ExpiresAt)
        {
            throw new InventoryValidationException(
                "Manufacturing date cannot be after the expiry date.");
        }

        var actorId = CurrentUserId();

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var connection = context.Database.GetDbConnection();

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await StockLedger.BeginImmediateAsync(connection, cancellationToken);

        try
        {
            var replayBatchId = await ReadOpeningStockReplayAsync(
                connection,
                idempotencyKey,
                cancellationToken);

            if (replayBatchId is not null)
            {
                await StockLedger.CommitAsync(connection, cancellationToken);
                return replayBatchId;
            }

            var medicine = await ReadMedicineTrackingAsync(
                connection,
                medicineId,
                cancellationToken)
                ?? throw new InventoryValidationException("Medicine was not found.");

            if (!medicine.IsActive)
            {
                throw new InventoryValidationException("Inactive medicine cannot receive opening stock.");
            }

            if (medicine.BatchTrackingRequired && string.IsNullOrWhiteSpace(batchNumber))
            {
                throw new InventoryValidationException(
                    "This medicine requires a batch/lot number.");
            }

            if (medicine.ExpiryTrackingRequired && request.ExpiresAt is null)
            {
                throw new InventoryValidationException(
                    "This medicine requires an expiry date.");
            }

            var location = await ReadLocationAsync(
                connection,
                locationId,
                cancellationToken)
                ?? throw new InventoryValidationException("Stock location was not found.");

            if (!location.IsActive)
            {
                throw new InventoryValidationException("Stock location is inactive.");
            }

            var batchKey = BuildBatchKey(
                batchNumber,
                request.ExpiresAt,
                idempotencyKey);

            var batch = await ReadBatchByIdentityAsync(
                connection,
                medicineId,
                locationId,
                batchKey,
                cancellationToken);

            var now = _clock.UtcNow;

            if (batch is null)
            {
                var batchId = Guid.CreateVersion7().ToString();

                await InsertBatchAsync(
                    connection,
                    batchId,
                    medicineId,
                    location.BranchId,
                    locationId,
                    batchNumber,
                    batchKey,
                    request.ManufacturedAt,
                    request.ExpiresAt,
                    now,
                    cancellationToken);

                batch = await StockLedger.LoadBatchAsync(
                    connection,
                    batchId,
                    cancellationToken)
                    ?? throw new InventoryValidationException("Opening stock batch could not be created.");
            }
            else if (batch.Status is "recalled" or "damaged")
            {
                throw new InventoryValidationException(
                    "Recalled or damaged stock cannot receive opening quantity.");
            }

            var received = await ReadReceivedAndCostAsync(
                connection,
                batch.Id,
                cancellationToken);

            var newReceived = StockLedger.Scale4(received.ReceivedQuantity + quantity);
            var weightedCost = newReceived == 0m
                ? 0m
                : StockLedger.Scale4(
                    ((received.ReceivedQuantity * received.PurchaseCost) +
                     (quantity * purchaseCost)) / newReceived);

            var newAvailable = StockLedger.Scale4(batch.AvailableQuantity + quantity);
            var newStatus =
                batch.Status == "depleted" && !IsExpired(request.ExpiresAt)
                    ? "active"
                    : batch.Status;

            await UpdateIncomingBatchAsync(
                connection,
                batch.Id,
                newReceived,
                newAvailable,
                weightedCost,
                salePrice,
                newStatus,
                now,
                cancellationToken);

            var movement = new StockMovementItem(
                Guid.CreateVersion7().ToString(),
                "opening_stock",
                quantity,
                newAvailable,
                purchaseCost,
                "opening_stock",
                batch.Id,
                null,
                reason,
                idempotencyKey,
                actorId,
                now,
                null);

            var refreshedBatch = batch with
            {
                AvailableQuantity = newAvailable,
                PurchaseCost = weightedCost,
                SalePrice = salePrice ?? batch.SalePrice,
                Status = newStatus,
            };

            await StockLedger.InsertMovementAsync(
                connection,
                movement,
                refreshedBatch,
                now,
                cancellationToken);

            await StockLedger.CommitAsync(connection, cancellationToken);
            return batch.Id;
        }
        catch
        {
            await StockLedger.RollbackQuietlyAsync(connection, cancellationToken);
            throw;
        }
    }

    public async Task<string> AdjustAsync(
        InventoryAdjustmentRequest request,
        CancellationToken cancellationToken = default)
    {
        _permissions.Demand("inventory.adjust");
        ArgumentNullException.ThrowIfNull(request);

        var batchId = Required(request.ProductBatchId, 36, nameof(request.ProductBatchId));
        var reasonCode = Required(request.ReasonCode, 48, nameof(request.ReasonCode));
        var reason = Required(request.Reason, 500, nameof(request.Reason));
        var idempotencyKey = Required(request.IdempotencyKey, 191, nameof(request.IdempotencyKey));
        var quantityDelta = StockLedger.Scale4(request.QuantityDelta);

        if (quantityDelta == 0m)
        {
            throw new InventoryValidationException("Adjustment quantity cannot be zero.");
        }

        if (!AllowedAdjustmentReasons.Contains(reasonCode))
        {
            throw new InventoryValidationException("Unsupported inventory adjustment reason.");
        }

        var actorId = CurrentUserId();

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var connection = context.Database.GetDbConnection();

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await StockLedger.BeginImmediateAsync(connection, cancellationToken);

        try
        {
            var replay = await ReadAdjustmentReplayAsync(
                connection,
                idempotencyKey,
                cancellationToken);

            if (replay is not null)
            {
                await StockLedger.CommitAsync(connection, cancellationToken);
                return replay;
            }

            var batch = await StockLedger.LoadBatchAsync(
                connection,
                batchId,
                cancellationToken)
                ?? throw new InventoryValidationException("Inventory batch was not found.");

            if (batch.Status == "recalled")
            {
                throw new InventoryValidationException(
                    "Recalled stock cannot be adjusted until its status is reviewed.");
            }

            var after = StockLedger.Scale4(batch.AvailableQuantity + quantityDelta);
            if (after < 0m)
            {
                throw new InventoryValidationException(
                    "This adjustment would make the batch quantity negative.");
            }

            var now = _clock.UtcNow;
            var adjustmentId = Guid.CreateVersion7().ToString();
            var lineId = Guid.CreateVersion7().ToString();
            var number = $"ADJ-{now:yyyyMMdd}-{Guid.CreateVersion7():N}"[..31].ToUpperInvariant();

            await InsertAdjustmentAsync(
                connection,
                adjustmentId,
                lineId,
                number,
                batch,
                quantityDelta,
                reasonCode,
                reason,
                actorId,
                now,
                cancellationToken);

            var status = batch.Status;
            if (after == 0m && status == "active")
            {
                status = "depleted";
            }
            else if (quantityDelta > 0m &&
                     status == "depleted" &&
                     !IsExpired(batch.ExpiresAt))
            {
                status = "active";
            }

            await StockLedger.UpdateBatchAsync(
                connection,
                batch.Id,
                after,
                status,
                now,
                cancellationToken);

            var movement = new StockMovementItem(
                Guid.CreateVersion7().ToString(),
                "adjustment",
                quantityDelta,
                after,
                batch.PurchaseCost,
                "inventory_adjustment",
                adjustmentId,
                lineId,
                reason,
                idempotencyKey,
                actorId,
                now,
                JsonSerializer.Serialize(new { reason_code = reasonCode }));

            await StockLedger.InsertMovementAsync(
                connection,
                movement,
                batch with
                {
                    AvailableQuantity = after,
                    Status = status,
                },
                now,
                cancellationToken);

            await MarkAdjustmentPostedAsync(
                connection,
                adjustmentId,
                actorId,
                now,
                cancellationToken);

            await StockLedger.CommitAsync(connection, cancellationToken);
            return adjustmentId;
        }
        catch
        {
            await StockLedger.RollbackQuietlyAsync(connection, cancellationToken);
            throw;
        }
    }

    public async Task ChangeBatchStatusAsync(
        ChangeBatchStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        _permissions.Demand("inventory.status");
        ArgumentNullException.ThrowIfNull(request);

        var batchId = Required(request.ProductBatchId, 36, nameof(request.ProductBatchId));
        var status = Required(request.Status, 32, nameof(request.Status)).ToLowerInvariant();
        var reason = Required(request.Reason, 500, nameof(request.Reason));

        if (!AllowedBatchStatuses.Contains(status))
        {
            throw new InventoryValidationException("Unsupported batch status.");
        }

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var batch = await context.Set<ProductBatchEntity>()
            .SingleOrDefaultAsync(x => x.Id == batchId, cancellationToken)
            ?? throw new InventoryValidationException("Inventory batch was not found.");

        if (batch.Status == status)
        {
            return;
        }

        var now = _clock.UtcNow;
        var from = batch.Status;
        batch.Status = status;
        batch.UpdatedAt = now;

        context.Add(new BatchStatusEventEntity
        {
            Id = Guid.CreateVersion7().ToString(),
            ProductBatchId = batch.Id,
            FromStatus = from,
            ToStatus = status,
            Reason = reason,
            ActorId = CurrentUserId(),
            ChangedAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        });

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<string> CreateLocationAsync(
        CreateStockLocationRequest request,
        CancellationToken cancellationToken = default)
    {
        _permissions.Demand("inventory.manage");
        ArgumentNullException.ThrowIfNull(request);

        var branchId = Required(request.BranchId, 36, nameof(request.BranchId));
        var code = Required(request.Code, 60, nameof(request.Code)).ToUpperInvariant();
        var name = Required(request.Name, 160, nameof(request.Name));
        var kind = Required(request.Kind, 40, nameof(request.Kind)).ToLowerInvariant();

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        if (!await context.Set<BranchEntity>()
            .AnyAsync(x => x.Id == branchId && x.IsActive, cancellationToken))
        {
            throw new InventoryValidationException("Active branch was not found.");
        }

        if (await context.Set<StockLocationEntity>()
            .AnyAsync(x => x.BranchId == branchId && x.Code == code, cancellationToken))
        {
            throw new InventoryValidationException(
                $"Stock location code '{code}' already exists in this branch.");
        }

        var now = _clock.UtcNow;
        var entity = new StockLocationEntity
        {
            Id = Guid.CreateVersion7().ToString(),
            BranchId = branchId,
            Code = code,
            Name = name,
            Kind = kind,
            IsDefault = false,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
        };

        context.Add(entity);
        await context.SaveChangesAsync(cancellationToken);
        return entity.Id;
    }

    public async Task UpdatePolicyAsync(
        InventoryPolicy policy,
        CancellationToken cancellationToken = default)
    {
        _permissions.Demand("settings.manage");
        ArgumentNullException.ThrowIfNull(policy);
        policy.Validate();

        await _settings.SetAsync(
            "inventory.low_stock_threshold",
            policy.LowStockThreshold.ToString(CultureInfo.InvariantCulture),
            cancellationToken);
        await _settings.SetAsync(
            "inventory.near_expiry_days",
            policy.NearExpiryDays.ToString(CultureInfo.InvariantCulture),
            cancellationToken);
        await _settings.SetAsync(
            "inventory.fefo_enabled",
            policy.FefoEnabled ? "1" : "0",
            cancellationToken);
        await _settings.SetAsync(
            "inventory.block_expired_sales",
            policy.BlockExpiredSales ? "1" : "0",
            cancellationToken);
    }

    private async Task<InventoryPolicy> GetPolicyAsync(
        CancellationToken cancellationToken)
    {
        var lowText = await _settings.GetAsync(
            "inventory.low_stock_threshold",
            cancellationToken);
        var nearText = await _settings.GetAsync(
            "inventory.near_expiry_days",
            cancellationToken);
        var fefoText = await _settings.GetAsync(
            "inventory.fefo_enabled",
            cancellationToken);
        var blockText = await _settings.GetAsync(
            "inventory.block_expired_sales",
            cancellationToken);

        var policy = new InventoryPolicy(
            int.TryParse(lowText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var low)
                ? low
                : 10,
            int.TryParse(nearText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var near)
                ? near
                : 90,
            fefoText is null || fefoText == "1",
            blockText is null || blockText == "1");

        policy.Validate();
        return policy;
    }

    private string CurrentUserId() =>
        _sessions.Current?.UserId
        ?? throw new InventoryValidationException(
            "A signed-in pharmacy user is required for inventory changes.");

    private DateOnly BusinessDate() =>
        DateOnly.FromDateTime(
            _clock.UtcNow.ToOffset(TimeSpan.FromMinutes(270)).DateTime);

    private bool IsExpired(DateOnly? expiresAt) =>
        expiresAt is not null && expiresAt.Value < BusinessDate();

    private static string BuildBatchKey(
        string? batchNumber,
        DateOnly? expiresAt,
        string fallback)
    {
        var expiry = expiresAt?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var identity = !string.IsNullOrWhiteSpace(batchNumber)
            ? $"{batchNumber.Trim().ToLowerInvariant()}|{expiry ?? "no-expiry"}"
            : $"unbatched|{expiry ?? fallback}";

        return Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(identity)))
            .ToLowerInvariant();
    }

    private static async Task<string?> ReadOpeningStockReplayAsync(
        DbConnection connection,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT product_batch_id, source_type
            FROM stock_movements
            WHERE idempotency_key = $key
            LIMIT 1;
            """;
        StockLedger.AddParameter(command, "$key", idempotencyKey);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        if (!string.Equals(reader.GetString(1), "opening_stock", StringComparison.Ordinal))
        {
            throw new InventoryValidationException(
                "This inventory idempotency key was already used for another operation.");
        }

        return reader.GetString(0);
    }

    private static async Task<string?> ReadAdjustmentReplayAsync(
        DbConnection connection,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT source_id, source_type
            FROM stock_movements
            WHERE idempotency_key = $key
            LIMIT 1;
            """;
        StockLedger.AddParameter(command, "$key", idempotencyKey);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        if (!string.Equals(reader.GetString(1), "inventory_adjustment", StringComparison.Ordinal))
        {
            throw new InventoryValidationException(
                "This inventory idempotency key was already used for another operation.");
        }

        return reader.GetString(0);
    }

    private static async Task<MedicineTrackingRow?> ReadMedicineTrackingAsync(
        DbConnection connection,
        string medicineId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT batch_tracking_required, expiry_tracking_required, is_active
            FROM medicines
            WHERE id = $id
            LIMIT 1;
            """;
        StockLedger.AddParameter(command, "$id", medicineId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new MedicineTrackingRow(
            Convert.ToBoolean(reader.GetValue(0), CultureInfo.InvariantCulture),
            Convert.ToBoolean(reader.GetValue(1), CultureInfo.InvariantCulture),
            Convert.ToBoolean(reader.GetValue(2), CultureInfo.InvariantCulture));
    }

    private static async Task<LocationRow?> ReadLocationAsync(
        DbConnection connection,
        string locationId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT branch_id, is_active
            FROM stock_locations
            WHERE id = $id
            LIMIT 1;
            """;
        StockLedger.AddParameter(command, "$id", locationId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new LocationRow(
            reader.GetString(0),
            Convert.ToBoolean(reader.GetValue(1), CultureInfo.InvariantCulture));
    }

    private static async Task<StockLedger.BatchRow?> ReadBatchByIdentityAsync(
        DbConnection connection,
        string medicineId,
        string locationId,
        string batchKey,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                id,
                medicine_id,
                branch_id,
                stock_location_id,
                status,
                available_quantity,
                purchase_cost,
                sale_price,
                expires_at
            FROM product_batches
            WHERE medicine_id = $medicineId
              AND stock_location_id = $locationId
              AND batch_key = $batchKey
            LIMIT 1;
            """;
        StockLedger.AddParameter(command, "$medicineId", medicineId);
        StockLedger.AddParameter(command, "$locationId", locationId);
        StockLedger.AddParameter(command, "$batchKey", batchKey);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new StockLedger.BatchRow(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            StockLedger.ReadDecimal(reader, 5),
            StockLedger.ReadDecimal(reader, 6),
            reader.IsDBNull(7) ? null : StockLedger.ReadDecimal(reader, 7),
            reader.IsDBNull(8)
                ? null
                : DateOnly.Parse(
                    Convert.ToString(reader.GetValue(8), CultureInfo.InvariantCulture)!,
                    CultureInfo.InvariantCulture));
    }

    private static async Task InsertBatchAsync(
        DbConnection connection,
        string batchId,
        string medicineId,
        string branchId,
        string locationId,
        string? batchNumber,
        string batchKey,
        DateOnly? manufacturedAt,
        DateOnly? expiresAt,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO product_batches (
                id,
                medicine_id,
                supplier_id,
                purchase_order_id,
                goods_receipt_id,
                branch_id,
                stock_location_id,
                batch_number,
                batch_key,
                manufactured_at,
                expires_at,
                status,
                received_quantity,
                available_quantity,
                purchase_cost,
                sale_price,
                last_movement_at,
                created_at,
                updated_at
            )
            VALUES (
                $id,
                $medicineId,
                NULL,
                NULL,
                NULL,
                $branchId,
                $locationId,
                $batchNumber,
                $batchKey,
                $manufacturedAt,
                $expiresAt,
                'active',
                '0.0000',
                '0.0000',
                '0.0000',
                NULL,
                NULL,
                $createdAt,
                $updatedAt
            );
            """;
        StockLedger.AddParameter(command, "$id", batchId);
        StockLedger.AddParameter(command, "$medicineId", medicineId);
        StockLedger.AddParameter(command, "$branchId", branchId);
        StockLedger.AddParameter(command, "$locationId", locationId);
        StockLedger.AddParameter(command, "$batchNumber", (object?)batchNumber ?? DBNull.Value);
        StockLedger.AddParameter(command, "$batchKey", batchKey);
        StockLedger.AddParameter(
            command,
            "$manufacturedAt",
            manufacturedAt is null
                ? DBNull.Value
                : manufacturedAt.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        StockLedger.AddParameter(
            command,
            "$expiresAt",
            expiresAt is null
                ? DBNull.Value
                : expiresAt.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        StockLedger.AddParameter(command, "$createdAt", now.ToString("O", CultureInfo.InvariantCulture));
        StockLedger.AddParameter(command, "$updatedAt", now.ToString("O", CultureInfo.InvariantCulture));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<ReceivedCostRow> ReadReceivedAndCostAsync(
        DbConnection connection,
        string batchId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT received_quantity, purchase_cost
            FROM product_batches
            WHERE id = $id
            LIMIT 1;
            """;
        StockLedger.AddParameter(command, "$id", batchId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InventoryValidationException("Inventory batch was not found.");
        }

        return new ReceivedCostRow(
            StockLedger.ReadDecimal(reader, 0),
            StockLedger.ReadDecimal(reader, 1));
    }

    private static async Task UpdateIncomingBatchAsync(
        DbConnection connection,
        string batchId,
        decimal received,
        decimal available,
        decimal purchaseCost,
        decimal? salePrice,
        string status,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE product_batches
            SET
                received_quantity = $received,
                available_quantity = $available,
                purchase_cost = $purchaseCost,
                sale_price = COALESCE($salePrice, sale_price),
                status = $status,
                last_movement_at = $lastMovementAt,
                updated_at = $updatedAt
            WHERE id = $id;
            """;
        StockLedger.AddParameter(command, "$received", StockLedger.FormatDecimal(received));
        StockLedger.AddParameter(command, "$available", StockLedger.FormatDecimal(available));
        StockLedger.AddParameter(command, "$purchaseCost", StockLedger.FormatDecimal(purchaseCost));
        StockLedger.AddParameter(
            command,
            "$salePrice",
            salePrice is null
                ? DBNull.Value
                : StockLedger.FormatDecimal(salePrice.Value));
        StockLedger.AddParameter(command, "$status", status);
        StockLedger.AddParameter(command, "$lastMovementAt", now.ToString("O", CultureInfo.InvariantCulture));
        StockLedger.AddParameter(command, "$updatedAt", now.ToString("O", CultureInfo.InvariantCulture));
        StockLedger.AddParameter(command, "$id", batchId);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertAdjustmentAsync(
        DbConnection connection,
        string adjustmentId,
        string lineId,
        string number,
        StockLedger.BatchRow batch,
        decimal quantityDelta,
        string reasonCode,
        string reason,
        string actorId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using (var adjustment = connection.CreateCommand())
        {
            adjustment.CommandText = """
                INSERT INTO inventory_adjustments (
                    id,
                    number,
                    stock_location_id,
                    reason_code,
                    status,
                    notes,
                    created_by,
                    posted_by,
                    posted_at,
                    created_at,
                    updated_at
                )
                VALUES (
                    $id,
                    $number,
                    $stockLocationId,
                    $reasonCode,
                    'draft',
                    $notes,
                    $createdBy,
                    NULL,
                    NULL,
                    $createdAt,
                    $updatedAt
                );
                """;
            StockLedger.AddParameter(adjustment, "$id", adjustmentId);
            StockLedger.AddParameter(adjustment, "$number", number);
            StockLedger.AddParameter(adjustment, "$stockLocationId", batch.StockLocationId);
            StockLedger.AddParameter(adjustment, "$reasonCode", reasonCode);
            StockLedger.AddParameter(adjustment, "$notes", reason);
            StockLedger.AddParameter(adjustment, "$createdBy", actorId);
            StockLedger.AddParameter(adjustment, "$createdAt", now.ToString("O", CultureInfo.InvariantCulture));
            StockLedger.AddParameter(adjustment, "$updatedAt", now.ToString("O", CultureInfo.InvariantCulture));
            await adjustment.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var line = connection.CreateCommand();
        line.CommandText = """
            INSERT INTO inventory_adjustment_lines (
                id,
                inventory_adjustment_id,
                product_batch_id,
                quantity_delta,
                reason,
                created_at,
                updated_at
            )
            VALUES (
                $id,
                $adjustmentId,
                $productBatchId,
                $quantityDelta,
                $reason,
                $createdAt,
                $updatedAt
            );
            """;
        StockLedger.AddParameter(line, "$id", lineId);
        StockLedger.AddParameter(line, "$adjustmentId", adjustmentId);
        StockLedger.AddParameter(line, "$productBatchId", batch.Id);
        StockLedger.AddParameter(line, "$quantityDelta", StockLedger.FormatDecimal(quantityDelta));
        StockLedger.AddParameter(line, "$reason", reason);
        StockLedger.AddParameter(line, "$createdAt", now.ToString("O", CultureInfo.InvariantCulture));
        StockLedger.AddParameter(line, "$updatedAt", now.ToString("O", CultureInfo.InvariantCulture));
        await line.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task MarkAdjustmentPostedAsync(
        DbConnection connection,
        string adjustmentId,
        string actorId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE inventory_adjustments
            SET
                status = 'posted',
                posted_by = $postedBy,
                posted_at = $postedAt,
                updated_at = $updatedAt
            WHERE id = $id;
            """;
        StockLedger.AddParameter(command, "$postedBy", actorId);
        StockLedger.AddParameter(command, "$postedAt", now.ToString("O", CultureInfo.InvariantCulture));
        StockLedger.AddParameter(command, "$updatedAt", now.ToString("O", CultureInfo.InvariantCulture));
        StockLedger.AddParameter(command, "$id", adjustmentId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string Required(string value, int maxLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        value = value.Trim();

        if (value.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }

        return value;
    }

    private static string? Optional(string? value, int maxLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        value = value.Trim();
        if (value.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }

        return value;
    }

    private sealed record MedicineTrackingRow(
        bool BatchTrackingRequired,
        bool ExpiryTrackingRequired,
        bool IsActive);

    private sealed record LocationRow(
        string BranchId,
        bool IsActive);

    private sealed record ReceivedCostRow(
        decimal ReceivedQuantity,
        decimal PurchaseCost);
}
