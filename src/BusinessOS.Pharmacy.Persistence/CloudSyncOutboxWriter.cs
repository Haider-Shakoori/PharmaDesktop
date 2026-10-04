using System.Globalization;
using System.Text.Json;
using BusinessOS.Pharmacy.Domain.Authentication;
using BusinessOS.Pharmacy.Persistence.Entities;

namespace BusinessOS.Pharmacy.Persistence;

internal static class CloudSyncOutboxWriter
{
    public static void QueueMedicineUpsert(
        PharmacyDbContext context,
        UserSessionSnapshot? session,
        MedicineEntity medicine,
        DateTimeOffset now)
    {
        if (session is null)
            return;

        var key = $"medicine:{medicine.Id}:upsert:{now.UtcDateTime.Ticks}";
        var payload = new Dictionary<string, object?>
        {
            ["v"] = 1,
            ["local_id"] = medicine.Id,
            ["idempotency_key"] = key,
            ["medicine_code"] = medicine.MedicineCode,
            ["barcode"] = medicine.Barcode,
            ["brand_name"] = medicine.BrandName,
            ["generic_name"] = medicine.GenericName,
            ["strength"] = medicine.Strength,
            ["dosage_form"] = medicine.DosageForm,
            ["purchase_unit"] = medicine.PurchaseUnit,
            ["sale_unit"] = medicine.SaleUnit,
            ["units_per_purchase_unit"] = medicine.UnitsPerPurchaseUnit.ToString("0.0000", CultureInfo.InvariantCulture),
            ["reorder_level"] = medicine.ReorderLevel.ToString("0.0000", CultureInfo.InvariantCulture),
            ["prescription_required"] = medicine.PrescriptionRequired,
            ["batch_tracking_required"] = medicine.BatchTrackingRequired,
            ["expiry_tracking_required"] = medicine.ExpiryTrackingRequired,
            ["is_active"] = medicine.IsActive,
            ["notes"] = medicine.Notes,
            ["local_updated_at"] = medicine.UpdatedAt.ToString("O", CultureInfo.InvariantCulture),
        };

        Queue(
            context,
            session,
            "medicine.upsert",
            key,
            payload,
            now);
    }

    public static void QueueCustomerUpsert(
        PharmacyDbContext context,
        UserSessionSnapshot? session,
        CustomerEntity customer,
        DateTimeOffset now)
    {
        if (session is null)
            return;

        var key = $"customer:{customer.Id}:upsert:{now.UtcDateTime.Ticks}";
        var payload = new Dictionary<string, object?>
        {
            ["v"] = 1,
            ["local_id"] = customer.Id,
            ["idempotency_key"] = key,
            ["name"] = customer.Name,
            ["phone"] = customer.Phone,
            ["email"] = customer.Email,
            ["credit_limit"] = customer.CreditLimit.ToString("0.0000", CultureInfo.InvariantCulture),
            ["is_active"] = customer.IsActive,
            ["notes"] = customer.Notes,
            ["local_updated_at"] = customer.UpdatedAt.ToString("O", CultureInfo.InvariantCulture),
        };

        Queue(
            context,
            session,
            "customer.upsert",
            key,
            payload,
            now);
    }

    private static void Queue(
        PharmacyDbContext context,
        UserSessionSnapshot session,
        string eventType,
        string idempotencyKey,
        IReadOnlyDictionary<string, object?> payload,
        DateTimeOffset now)
    {
        context.Add(new CloudSyncOutboxEntity
        {
            Id = Guid.CreateVersion7().ToString(),
            TenantId = session.TenantId,
            ActorUserId = session.UserId,
            EventType = eventType,
            IdempotencyKey = idempotencyKey,
            PayloadJson = JsonSerializer.Serialize(payload),
            Status = "pending",
            AttemptCount = 0,
            CreatedAt = now,
            UpdatedAt = now,
        });
    }
}
