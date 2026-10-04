using System.Globalization;
using System.Text.Json;
using BusinessOS.Pharmacy.Application.Abstractions.Sync;
using BusinessOS.Pharmacy.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace BusinessOS.Pharmacy.Persistence;

internal static class CloudRemoteRecordApplier
{
    public static async Task ApplyAsync(
        PharmacyDbContext context,
        string tenantId,
        string stream,
        IReadOnlyList<CloudSyncRemoteRecord> records,
        DateTimeOffset receivedAt,
        CancellationToken cancellationToken)
    {
        switch (stream)
        {
            case "branches":
                foreach (var record in records)
                    await ApplyBranchAsync(context, record, receivedAt, cancellationToken);
                break;

            case "stock_locations":
                foreach (var record in records)
                    await ApplyStockLocationAsync(context, tenantId, record, receivedAt, cancellationToken);
                break;

            case "medicines":
                foreach (var record in records)
                    await ApplyMedicineAsync(context, record, receivedAt, cancellationToken);
                break;

            case "customers":
                foreach (var record in records)
                    await ApplyCustomerAsync(context, record, receivedAt, cancellationToken);
                break;

            case "inventory":
                foreach (var record in records)
                    await ApplyInventoryAsync(context, tenantId, record, receivedAt, cancellationToken);
                break;

            case "users":
                foreach (var record in records)
                    await ApplyUserDirectoryAsync(context, tenantId, record, receivedAt, cancellationToken);
                break;

            // Roles and permissions are intentionally persisted in
            // cloud_sync_remote_records. They are consumed together with the
            // users stream and do not have independent operational tables in
            // the desktop database.
            case "roles":
            case "permissions":
            default:
                break;
        }
    }

    private static async Task ApplyBranchAsync(
        PharmacyDbContext context,
        CloudSyncRemoteRecord record,
        DateTimeOffset receivedAt,
        CancellationToken cancellationToken)
    {
        using var document = Parse(record);
        var root = document.RootElement;
        var code = RequiredString(root, "code");
        var serverUpdatedAt = ServerUpdatedAt(root, record, receivedAt);

        var entity = await context.Set<BranchEntity>()
            .SingleOrDefaultAsync(
                x => x.Id == record.ServerId || x.Code == code,
                cancellationToken);

        if (entity is null)
        {
            entity = new BranchEntity
            {
                Id = record.ServerId,
                Code = code,
                CreatedAt = ServerCreatedAt(root, receivedAt),
            };
            context.Add(entity);
        }
        else if (!ShouldApply(serverUpdatedAt, entity.UpdatedAt))
        {
            return;
        }

        entity.Code = code;
        entity.Name = RequiredString(root, "name");
        entity.Address = OptionalString(root, "address");
        entity.IsDefault = Bool(root, "is_default");
        entity.IsActive = Bool(root, "is_active", true) && !Bool(root, "is_deleted");
        entity.UpdatedAt = serverUpdatedAt;
    }

    private static async Task ApplyStockLocationAsync(
        PharmacyDbContext context,
        string tenantId,
        CloudSyncRemoteRecord record,
        DateTimeOffset receivedAt,
        CancellationToken cancellationToken)
    {
        using var document = Parse(record);
        var root = document.RootElement;
        var serverBranchId = RequiredString(root, "branch_id");
        var branchId = await ResolveBranchIdAsync(
            context,
            tenantId,
            serverBranchId,
            cancellationToken);

        if (branchId is null)
            return;

        var code = RequiredString(root, "code");
        var serverUpdatedAt = ServerUpdatedAt(root, record, receivedAt);

        var entity = await context.Set<StockLocationEntity>()
            .SingleOrDefaultAsync(
                x => x.Id == record.ServerId ||
                     (x.BranchId == branchId && x.Code == code),
                cancellationToken);

        if (entity is null)
        {
            entity = new StockLocationEntity
            {
                Id = record.ServerId,
                BranchId = branchId,
                Code = code,
                CreatedAt = ServerCreatedAt(root, receivedAt),
            };
            context.Add(entity);
        }
        else if (!ShouldApply(serverUpdatedAt, entity.UpdatedAt))
        {
            return;
        }

        entity.BranchId = branchId;
        entity.Code = code;
        entity.Name = RequiredString(root, "name");
        entity.Kind = OptionalString(root, "kind") ?? "store";
        entity.IsDefault = Bool(root, "is_default");
        entity.IsActive = Bool(root, "is_active", true) && !Bool(root, "is_deleted");
        entity.UpdatedAt = serverUpdatedAt;
    }

    private static async Task ApplyMedicineAsync(
        PharmacyDbContext context,
        CloudSyncRemoteRecord record,
        DateTimeOffset receivedAt,
        CancellationToken cancellationToken)
    {
        using var document = Parse(record);
        var root = document.RootElement;
        var code = RequiredString(root, "medicine_code");
        var desktopSourceId = OptionalString(root, "desktop_source_id");
        var serverUpdatedAt = ServerUpdatedAt(root, record, receivedAt);

        var entity = await context.Set<MedicineEntity>()
            .SingleOrDefaultAsync(
                x => x.Id == record.ServerId ||
                     (!string.IsNullOrWhiteSpace(desktopSourceId) &&
                      x.Id == desktopSourceId) ||
                     x.MedicineCode == code,
                cancellationToken);

        if (entity is null)
        {
            entity = new MedicineEntity
            {
                Id = record.ServerId,
                MedicineCode = code,
                CreatedAt = ServerCreatedAt(root, receivedAt),
            };
            context.Add(entity);
        }
        else if (!ShouldApply(serverUpdatedAt, entity.UpdatedAt))
        {
            return;
        }

        entity.MedicineCode = code;
        entity.Barcode = OptionalString(root, "barcode");
        entity.BrandName = RequiredString(root, "brand_name");
        entity.GenericName = OptionalString(root, "generic_name");
        entity.Strength = OptionalString(root, "strength");
        entity.DosageForm = OptionalString(root, "dosage_form");
        entity.PurchaseUnit = OptionalString(root, "purchase_unit") ?? "pack";
        entity.SaleUnit = OptionalString(root, "sale_unit") ?? "unit";
        entity.UnitsPerPurchaseUnit = Decimal(root, "units_per_purchase_unit", 1m);
        entity.ReorderLevel = Decimal(root, "reorder_level");
        entity.PrescriptionRequired = Bool(root, "prescription_required");
        entity.BatchTrackingRequired = Bool(root, "batch_tracking_required", true);
        entity.ExpiryTrackingRequired = Bool(root, "expiry_tracking_required", true);
        entity.IsActive = Bool(root, "is_active", true) && !Bool(root, "is_deleted");
        entity.Notes = OptionalString(root, "notes");
        entity.UpdatedAt = serverUpdatedAt;
    }

    private static async Task ApplyCustomerAsync(
        PharmacyDbContext context,
        CloudSyncRemoteRecord record,
        DateTimeOffset receivedAt,
        CancellationToken cancellationToken)
    {
        using var document = Parse(record);
        var root = document.RootElement;
        var email = OptionalString(root, "email");
        var phone = OptionalString(root, "phone");
        var desktopSourceId = OptionalString(root, "desktop_source_id");
        var serverUpdatedAt = ServerUpdatedAt(root, record, receivedAt);

        var entity = await context.Set<CustomerEntity>()
            .SingleOrDefaultAsync(
                x => x.Id == record.ServerId ||
                     (!string.IsNullOrWhiteSpace(desktopSourceId) &&
                      x.Id == desktopSourceId),
                cancellationToken);

        if (entity is null && !string.IsNullOrWhiteSpace(email))
        {
            var emailMatches = await context.Set<CustomerEntity>()
                .Where(x => x.Email == email)
                .Take(2)
                .ToListAsync(cancellationToken);

            if (emailMatches.Count == 1)
                entity = emailMatches[0];
        }

        if (entity is null && !string.IsNullOrWhiteSpace(phone))
        {
            var phoneMatches = await context.Set<CustomerEntity>()
                .Where(x => x.Phone == phone)
                .Take(2)
                .ToListAsync(cancellationToken);

            if (phoneMatches.Count == 1)
                entity = phoneMatches[0];
        }

        if (entity is null)
        {
            entity = new CustomerEntity
            {
                Id = record.ServerId,
                CreatedAt = ServerCreatedAt(root, receivedAt),
            };
            context.Add(entity);
        }
        else if (!ShouldApply(serverUpdatedAt, entity.UpdatedAt))
        {
            return;
        }

        entity.Name = RequiredString(root, "name");
        entity.Phone = phone;
        entity.Email = email;
        entity.CreditLimit = Decimal(root, "credit_limit");
        entity.IsActive = Bool(root, "is_active", true) && !Bool(root, "is_deleted");
        entity.Notes = OptionalString(root, "notes");
        entity.UpdatedAt = serverUpdatedAt;
    }

    private static async Task ApplyInventoryAsync(
        PharmacyDbContext context,
        string tenantId,
        CloudSyncRemoteRecord record,
        DateTimeOffset receivedAt,
        CancellationToken cancellationToken)
    {
        using var document = Parse(record);
        var root = document.RootElement;

        var medicineId = await ResolveMedicineIdAsync(
            context,
            tenantId,
            RequiredString(root, "medicine_id"),
            cancellationToken);

        var stockLocationId = await ResolveStockLocationIdAsync(
            context,
            tenantId,
            RequiredString(root, "stock_location_id"),
            cancellationToken);

        if (medicineId is null || stockLocationId is null)
            return;

        var location = await context.Set<StockLocationEntity>()
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == stockLocationId, cancellationToken);

        if (location is null)
            return;

        var batchKey = RequiredString(root, "batch_key");
        var serverUpdatedAt = ServerUpdatedAt(root, record, receivedAt);

        var entity = await context.Set<ProductBatchEntity>()
            .SingleOrDefaultAsync(
                x => x.Id == record.ServerId ||
                     (x.MedicineId == medicineId &&
                      x.StockLocationId == stockLocationId &&
                      x.BatchKey == batchKey),
                cancellationToken);

        if (entity is null)
        {
            entity = new ProductBatchEntity
            {
                Id = record.ServerId,
                MedicineId = medicineId,
                BranchId = location.BranchId,
                StockLocationId = stockLocationId,
                BatchKey = batchKey,
                CreatedAt = ServerCreatedAt(root, receivedAt),
            };
            context.Add(entity);
        }
        else if (!ShouldApply(serverUpdatedAt, entity.UpdatedAt))
        {
            return;
        }

        entity.MedicineId = medicineId;
        entity.BranchId = location.BranchId;
        entity.StockLocationId = stockLocationId;
        entity.BatchNumber = OptionalString(root, "batch_number");
        entity.BatchKey = batchKey;
        entity.ManufacturedAt = Date(root, "manufactured_at");
        entity.ExpiresAt = Date(root, "expires_at");
        entity.Status = OptionalString(root, "status") ?? "active";
        entity.ReceivedQuantity = Decimal(root, "received_quantity");
        entity.AvailableQuantity = Decimal(root, "available_quantity");
        entity.PurchaseCost = Decimal(root, "purchase_cost");
        entity.SalePrice = NullableDecimal(root, "sale_price");
        entity.LastMovementAt = DateTime(root, "last_movement_at");
        entity.UpdatedAt = serverUpdatedAt;
    }

    private static async Task ApplyUserDirectoryAsync(
        PharmacyDbContext context,
        string tenantId,
        CloudSyncRemoteRecord record,
        DateTimeOffset receivedAt,
        CancellationToken cancellationToken)
    {
        using var document = Parse(record);
        var root = document.RootElement;

        var existing = await context.Set<LocalLanUserCredentialEntity>()
            .SingleOrDefaultAsync(
                x => x.UserId == record.ServerId &&
                     x.TenantId == tenantId,
                cancellationToken);

        // Never manufacture offline credentials for a web-created user.
        // Password verifiers are cached only after that user completes a real
        // online sign-in. Existing cached identities can safely receive
        // role/permission/deactivation updates here.
        if (existing is null)
            return;

        existing.Name = RequiredString(root, "name");
        existing.Email = RequiredString(root, "email");
        existing.RolesJson = StringArrayJson(root, "roles", "code");
        existing.PermissionsJson = StringArrayJson(root, "permissions", "code");
        existing.IsActive = Bool(root, "is_active", true) && !Bool(root, "is_deleted");
        existing.UpdatedAt = ServerUpdatedAt(root, record, receivedAt);

        if (!existing.IsActive && existing.IdentityValidUntil > receivedAt)
            existing.IdentityValidUntil = receivedAt;
    }

    private static async Task<string?> ResolveBranchIdAsync(
        PharmacyDbContext context,
        string tenantId,
        string serverId,
        CancellationToken cancellationToken)
    {
        var direct = await context.Set<BranchEntity>()
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == serverId, cancellationToken);
        if (direct is not null)
            return direct.Id;

        var code = await RemoteStringAsync(
            context,
            tenantId,
            "branches",
            serverId,
            "code",
            cancellationToken);
        if (string.IsNullOrWhiteSpace(code))
            return null;

        return await context.Set<BranchEntity>()
            .AsNoTracking()
            .Where(x => x.Code == code)
            .Select(x => x.Id)
            .SingleOrDefaultAsync(cancellationToken);
    }

    private static async Task<string?> ResolveStockLocationIdAsync(
        PharmacyDbContext context,
        string tenantId,
        string serverId,
        CancellationToken cancellationToken)
    {
        var direct = await context.Set<StockLocationEntity>()
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == serverId, cancellationToken);
        if (direct is not null)
            return direct.Id;

        var remote = await RemotePayloadAsync(
            context,
            tenantId,
            "stock_locations",
            serverId,
            cancellationToken);
        if (remote is null)
            return null;

        using (remote)
        {
            var root = remote.RootElement;
            var code = OptionalString(root, "code");
            var branchServerId = OptionalString(root, "branch_id");
            if (string.IsNullOrWhiteSpace(code) ||
                string.IsNullOrWhiteSpace(branchServerId))
                return null;

            var branchId = await ResolveBranchIdAsync(
                context,
                tenantId,
                branchServerId,
                cancellationToken);
            if (branchId is null)
                return null;

            return await context.Set<StockLocationEntity>()
                .AsNoTracking()
                .Where(x => x.BranchId == branchId && x.Code == code)
                .Select(x => x.Id)
                .SingleOrDefaultAsync(cancellationToken);
        }
    }

    private static async Task<string?> ResolveMedicineIdAsync(
        PharmacyDbContext context,
        string tenantId,
        string serverId,
        CancellationToken cancellationToken)
    {
        var direct = await context.Set<MedicineEntity>()
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == serverId, cancellationToken);
        if (direct is not null)
            return direct.Id;

        var code = await RemoteStringAsync(
            context,
            tenantId,
            "medicines",
            serverId,
            "medicine_code",
            cancellationToken);
        if (string.IsNullOrWhiteSpace(code))
            return null;

        return await context.Set<MedicineEntity>()
            .AsNoTracking()
            .Where(x => x.MedicineCode == code)
            .Select(x => x.Id)
            .SingleOrDefaultAsync(cancellationToken);
    }

    private static async Task<string?> RemoteStringAsync(
        PharmacyDbContext context,
        string tenantId,
        string stream,
        string serverId,
        string property,
        CancellationToken cancellationToken)
    {
        var payload = await RemotePayloadAsync(
            context,
            tenantId,
            stream,
            serverId,
            cancellationToken);
        if (payload is null)
            return null;

        using (payload)
            return OptionalString(payload.RootElement, property);
    }

    private static async Task<JsonDocument?> RemotePayloadAsync(
        PharmacyDbContext context,
        string tenantId,
        string stream,
        string serverId,
        CancellationToken cancellationToken)
    {
        var json = await context.Set<CloudSyncRemoteRecordEntity>()
            .AsNoTracking()
            .Where(x =>
                x.TenantId == tenantId &&
                x.Stream == stream &&
                x.ServerId == serverId)
            .Select(x => x.PayloadJson)
            .SingleOrDefaultAsync(cancellationToken);

        return string.IsNullOrWhiteSpace(json)
            ? null
            : JsonDocument.Parse(json);
    }

    private static JsonDocument Parse(CloudSyncRemoteRecord record) =>
        JsonDocument.Parse(record.PayloadJson);

    private static bool ShouldApply(
        DateTimeOffset serverUpdatedAt,
        DateTimeOffset localUpdatedAt) =>
        serverUpdatedAt >= localUpdatedAt;

    private static DateTimeOffset ServerCreatedAt(
        JsonElement root,
        DateTimeOffset fallback) =>
        DateTime(root, "server_created_at") ?? fallback;

    private static DateTimeOffset ServerUpdatedAt(
        JsonElement root,
        CloudSyncRemoteRecord record,
        DateTimeOffset fallback) =>
        record.ServerUpdatedAt ??
        DateTime(root, "server_updated_at") ??
        fallback;

    private static string RequiredString(JsonElement root, string property) =>
        OptionalString(root, property)
        ?? throw new InvalidOperationException(
            $"Cloud sync record is missing required property '{property}'.");

    private static string? OptionalString(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var value) ||
            value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null,
        };
    }

    private static bool Bool(
        JsonElement root,
        string property,
        bool fallback = false)
    {
        if (!root.TryGetProperty(property, out var value))
            return fallback;

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(value.GetString(), out var parsed) => parsed,
            _ => fallback,
        };
    }

    private static decimal Decimal(
        JsonElement root,
        string property,
        decimal fallback = 0m) =>
        NullableDecimal(root, property) ?? fallback;

    private static decimal? NullableDecimal(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var value) ||
            value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;

        if (value.ValueKind == JsonValueKind.Number &&
            value.TryGetDecimal(out var numeric))
            return numeric;

        return value.ValueKind == JsonValueKind.String &&
               decimal.TryParse(
                   value.GetString(),
                   NumberStyles.Number,
                   CultureInfo.InvariantCulture,
                   out var parsed)
            ? parsed
            : null;
    }

    private static DateOnly? Date(JsonElement root, string property)
    {
        var value = OptionalString(root, property);
        return DateOnly.TryParseExact(
            value,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsed)
            ? parsed
            : null;
    }

    private static DateTimeOffset? DateTime(JsonElement root, string property)
    {
        var value = OptionalString(root, property);
        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal,
            out var parsed)
            ? parsed
            : null;
    }

    private static string StringArrayJson(
        JsonElement root,
        string property,
        string itemProperty)
    {
        if (!root.TryGetProperty(property, out var array) ||
            array.ValueKind != JsonValueKind.Array)
            return "[]";

        var values = array.EnumerateArray()
            .Select(x => OptionalString(x, itemProperty))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return JsonSerializer.Serialize(values);
    }
}
