using System.Globalization;
using System.Text.Json;
using BusinessOS.Pharmacy.Application.Abstractions.Sync;
using BusinessOS.Pharmacy.Application.Abstractions.Time;
using BusinessOS.Pharmacy.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace BusinessOS.Pharmacy.Persistence;

public sealed class DesktopSyncInboxApplier : ISyncInboxApplier
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly IDbContextFactory<PharmacyDbContext> _contextFactory;
    private readonly IClock _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public DesktopSyncInboxApplier(
        IDbContextFactory<PharmacyDbContext> contextFactory,
        IClock clock)
    {
        _contextFactory = contextFactory;
        _clock = clock;
    }

    public Task<SyncApplyResult> ApplyAsync(
        SyncPullItem item,
        CancellationToken cancellationToken = default) =>
        item.Stream switch
        {
            "medicines" => ApplyMedicineAsync(item, cancellationToken),
            _ => throw new InvalidOperationException(
                $"No local inbox adapter is registered for sync stream '{item.Stream}'."),
        };

    private async Task<SyncApplyResult> ApplyMedicineAsync(
        SyncPullItem item,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var document = JsonDocument.Parse(item.PayloadJson);
            var root = document.RootElement;

            var cloudId = RequiredString(root, "id");
            var medicineCode = RequiredString(root, "medicine_code");
            var brandName = RequiredString(root, "brand_name");
            var genericName = OptionalString(root, "generic_name");
            var saleUnit = OptionalString(root, "sale_unit") ?? "unit";
            var isActive = !OptionalBoolean(root, "is_deleted") &&
                OptionalBoolean(root, "is_active", defaultValue: true);

            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

            var mapping = await context.Set<SyncEntityMapEntity>()
                .SingleOrDefaultAsync(
                    x => x.Stream == item.Stream &&
                         x.CloudEntityId == cloudId,
                    cancellationToken);

            MedicineEntity? medicine = null;

            if (mapping is not null)
            {
                medicine = await context.Set<MedicineEntity>()
                    .SingleOrDefaultAsync(
                        x => x.Id == mapping.LocalEntityId,
                        cancellationToken);

                if (medicine is null)
                {
                    throw new InvalidOperationException(
                        "The medicine sync mapping points to a missing local medicine.");
                }

                if (string.Equals(
                    mapping.CloudVersion,
                    item.CloudVersion,
                    StringComparison.Ordinal))
                {
                    await transaction.CommitAsync(cancellationToken);
                    return new SyncApplyResult(
                        SyncApplyDisposition.AlreadyApplied,
                        medicine.Id,
                        null,
                        null);
                }

                if (medicine.UpdatedAt > mapping.UpdatedAt)
                {
                    await transaction.CommitAsync(cancellationToken);
                    return new SyncApplyResult(
                        SyncApplyDisposition.Conflict,
                        medicine.Id,
                        "The medicine was edited locally after the last cloud synchronization.",
                        SerializeLocalMedicine(medicine));
                }
            }
            else
            {
                medicine = await context.Set<MedicineEntity>()
                    .SingleOrDefaultAsync(
                        x => x.MedicineCode == medicineCode,
                        cancellationToken);

                if (medicine is null)
                {
                    medicine = new MedicineEntity
                    {
                        Id = Guid.CreateVersion7().ToString(),
                        MedicineCode = medicineCode,
                        BrandName = brandName,
                        GenericName = genericName,
                        PurchaseUnit = "pack",
                        SaleUnit = saleUnit,
                        UnitsPerPurchaseUnit = 1m,
                        ReorderLevel = 0m,
                        PrescriptionRequired = false,
                        BatchTrackingRequired = true,
                        ExpiryTrackingRequired = true,
                        IsActive = isActive,
                        Notes = "Imported from BusinessOS cloud synchronization.",
                        CreatedAt = item.OccurredAt,
                        UpdatedAt = item.OccurredAt,
                    };
                    context.Add(medicine);
                }

                mapping = new SyncEntityMapEntity
                {
                    Stream = item.Stream,
                    LocalEntityId = medicine.Id,
                    CloudEntityId = cloudId,
                    CloudVersion = item.CloudVersion,
                    UpdatedAt = _clock.UtcNow,
                };
                context.Add(mapping);
            }

            if (item.Operation == SyncOperation.Delete)
            {
                medicine.IsActive = false;
            }
            else
            {
                medicine.MedicineCode = medicineCode;
                medicine.BrandName = brandName;
                medicine.GenericName = genericName;
                medicine.SaleUnit = saleUnit;
                medicine.IsActive = isActive;
            }

            medicine.UpdatedAt = item.OccurredAt;
            mapping.CloudVersion = item.CloudVersion;
            mapping.UpdatedAt = _clock.UtcNow;

            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new SyncApplyResult(
                SyncApplyDisposition.Applied,
                medicine.Id,
                null,
                null);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                "The cloud medicine payload is invalid JSON.",
                exception);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static string SerializeLocalMedicine(MedicineEntity medicine) =>
        JsonSerializer.Serialize(
            new
            {
                id = medicine.Id,
                medicine_code = medicine.MedicineCode,
                brand_name = medicine.BrandName,
                generic_name = medicine.GenericName,
                sale_unit = medicine.SaleUnit,
                is_active = medicine.IsActive,
                updated_at = medicine.UpdatedAt.ToString(
                    "O",
                    CultureInfo.InvariantCulture),
            },
            JsonOptions);

    private static string RequiredString(JsonElement element, string property)
    {
        var value = OptionalString(element, property);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"The cloud medicine payload omitted '{property}'.");
        }

        return value;
    }

    private static string? OptionalString(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value) ||
            value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : value.ToString();
    }

    private static bool OptionalBoolean(
        JsonElement element,
        string property,
        bool defaultValue = false)
    {
        if (!element.TryGetProperty(property, out var value))
        {
            return defaultValue;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => defaultValue,
        };
    }
}
