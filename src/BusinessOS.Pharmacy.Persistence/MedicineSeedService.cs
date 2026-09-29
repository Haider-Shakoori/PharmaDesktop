using BusinessOS.Pharmacy.Application.Abstractions.Medicines;
using BusinessOS.Pharmacy.Application.Abstractions.Persistence;
using BusinessOS.Pharmacy.Application.Abstractions.Time;
using BusinessOS.Pharmacy.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace BusinessOS.Pharmacy.Persistence;

public sealed class MedicineSeedService : IMedicineSeedService
{
    private const string SeedMarker = "seed.medicine_master.v1";

    private readonly IDbContextFactory<PharmacyDbContext> _contextFactory;
    private readonly ILocalSettingsStore _settings;
    private readonly IClock _clock;

    public MedicineSeedService(
        IDbContextFactory<PharmacyDbContext> contextFactory,
        ILocalSettingsStore settings,
        IClock clock)
    {
        _contextFactory = contextFactory;
        _settings = settings;
        _clock = clock;
    }

    public async Task<int> SeedDefaultsOnceAsync(
        CancellationToken cancellationToken = default)
    {
        if (await _settings.GetAsync(SeedMarker, cancellationToken) == "1")
        {
            return 0;
        }

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var now = _clock.UtcNow;
        var categories = await EnsureCategoriesAsync(context, now, cancellationToken);

        var seeds = SeedMedicines(categories);
        var added = 0;

        foreach (var seed in seeds)
        {
            if (await context.Set<MedicineEntity>()
                .AnyAsync(x => x.MedicineCode == seed.MedicineCode, cancellationToken))
            {
                continue;
            }

            context.Add(seed with
            {
                Id = Guid.CreateVersion7().ToString(),
                CreatedAt = now,
                UpdatedAt = now,
            }.ToEntity());

            added++;
        }

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await _settings.SetAsync(SeedMarker, "1", cancellationToken);

        return added;
    }

    private static async Task<Dictionary<string, string>> EnsureCategoriesAsync(
        PharmacyDbContext context,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var names = new[]
        {
            "Pain & Fever",
            "Antibiotics",
            "Gastrointestinal",
            "Diabetes",
            "Cardiovascular",
            "Allergy",
            "Respiratory",
            "Rehydration",
        };

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var name in names)
        {
            var existing = await context.Set<MedicineCategoryEntity>()
                .SingleOrDefaultAsync(x => x.Name == name, cancellationToken);

            if (existing is null)
            {
                existing = new MedicineCategoryEntity
                {
                    Id = Guid.CreateVersion7().ToString(),
                    Name = name,
                    IsActive = true,
                    CreatedAt = now,
                    UpdatedAt = now,
                };
                context.Add(existing);
            }

            result[name] = existing.Id;
        }

        await context.SaveChangesAsync(cancellationToken);
        return result;
    }

    private static IReadOnlyList<SeedMedicine> SeedMedicines(
        IReadOnlyDictionary<string, string> categories) =>
    [
        new("MED-0001", "Paracetamol", "Paracetamol", "500 mg", "Tablet", categories["Pain & Fever"], "box", "tablet", 100, 50, false),
        new("MED-0002", "Ibuprofen", "Ibuprofen", "400 mg", "Tablet", categories["Pain & Fever"], "box", "tablet", 100, 30, false),
        new("MED-0003", "Amoxicillin", "Amoxicillin", "500 mg", "Capsule", categories["Antibiotics"], "box", "capsule", 100, 30, true),
        new("MED-0004", "Azithromycin", "Azithromycin", "500 mg", "Tablet", categories["Antibiotics"], "box", "tablet", 30, 15, true),
        new("MED-0005", "Cefixime", "Cefixime", "400 mg", "Tablet", categories["Antibiotics"], "box", "tablet", 30, 15, true),
        new("MED-0006", "Omeprazole", "Omeprazole", "20 mg", "Capsule", categories["Gastrointestinal"], "box", "capsule", 100, 30, false),
        new("MED-0007", "Metformin", "Metformin", "500 mg", "Tablet", categories["Diabetes"], "box", "tablet", 100, 30, true),
        new("MED-0008", "Amlodipine", "Amlodipine", "5 mg", "Tablet", categories["Cardiovascular"], "box", "tablet", 100, 30, true),
        new("MED-0009", "Losartan", "Losartan", "50 mg", "Tablet", categories["Cardiovascular"], "box", "tablet", 100, 30, true),
        new("MED-0010", "Cetirizine", "Cetirizine", "10 mg", "Tablet", categories["Allergy"], "box", "tablet", 100, 20, false),
        new("MED-0011", "Oral Rehydration Salts", "Oral Rehydration Salts", null, "Sachet", categories["Rehydration"], "pack", "sachet", 20, 10, false),
        new("MED-0012", "Salbutamol Inhaler", "Salbutamol", "100 mcg/dose", "Inhaler", categories["Respiratory"], "box", "inhaler", 1, 5, false),
    ];

    private sealed record SeedMedicine(
        string MedicineCode,
        string BrandName,
        string GenericName,
        string? Strength,
        string DosageForm,
        string MedicineCategoryId,
        string PurchaseUnit,
        string SaleUnit,
        decimal UnitsPerPurchaseUnit,
        decimal ReorderLevel,
        bool PrescriptionRequired)
    {
        public string Id { get; init; } = string.Empty;
        public DateTimeOffset CreatedAt { get; init; }
        public DateTimeOffset UpdatedAt { get; init; }

        public MedicineEntity ToEntity() => new()
        {
            Id = Id,
            MedicineCategoryId = MedicineCategoryId,
            ManufacturerId = null,
            MedicineCode = MedicineCode,
            Barcode = null,
            BrandName = BrandName,
            GenericName = GenericName,
            Strength = Strength,
            DosageForm = DosageForm,
            PurchaseUnit = PurchaseUnit,
            SaleUnit = SaleUnit,
            UnitsPerPurchaseUnit = UnitsPerPurchaseUnit,
            ReorderLevel = ReorderLevel,
            PrescriptionRequired = PrescriptionRequired,
            BatchTrackingRequired = true,
            ExpiryTrackingRequired = true,
            IsActive = true,
            Notes = "Darmaltoon starter medicine master data. No batch or stock record created.",
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
        };
    }
}
