using BusinessOS.Pharmacy.Application.Abstractions.Authentication;
using BusinessOS.Pharmacy.Application.Abstractions.Medicines;
using BusinessOS.Pharmacy.Application.Abstractions.Time;
using BusinessOS.Pharmacy.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace BusinessOS.Pharmacy.Persistence;

public sealed class MedicineCatalogService : IMedicineCatalogService
{
    private readonly IDbContextFactory<PharmacyDbContext> _contextFactory;
    private readonly IPermissionAuthorizer _permissions;
    private readonly IUserSessionService _sessions;
    private readonly IClock _clock;

    public MedicineCatalogService(
        IDbContextFactory<PharmacyDbContext> contextFactory,
        IPermissionAuthorizer permissions,
        IUserSessionService sessions,
        IClock clock)
    {
        _contextFactory = contextFactory;
        _permissions = permissions;
        _sessions = sessions;
        _clock = clock;
    }

    public async Task<IReadOnlyList<MedicineListItem>> SearchAsync(
        MedicineSearchFilter filter,
        CancellationToken cancellationToken = default)
    {
        _permissions.Demand("medicines.manage");

        var take = Math.Clamp(filter.Take, 1, 1000);
        var search = filter.Search?.Trim();

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var query = context.Set<MedicineEntity>()
            .AsNoTracking()
            .Include(x => x.Category)
            .Include(x => x.Manufacturer)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search}%";
            query = query.Where(x =>
                EF.Functions.Like(x.BrandName, pattern) ||
                (x.GenericName != null && EF.Functions.Like(x.GenericName, pattern)) ||
                EF.Functions.Like(x.MedicineCode, pattern) ||
                (x.Barcode != null && EF.Functions.Like(x.Barcode, pattern)) ||
                (x.Strength != null && EF.Functions.Like(x.Strength, pattern)));
        }

        if (!string.IsNullOrWhiteSpace(filter.CategoryId))
        {
            query = query.Where(x => x.MedicineCategoryId == filter.CategoryId);
        }

        if (filter.IsActive is not null)
        {
            query = query.Where(x => x.IsActive == filter.IsActive.Value);
        }

        return await query
            .OrderBy(x => x.BrandName)
            .ThenBy(x => x.MedicineCode)
            .Take(take)
            .Select(x => new MedicineListItem(
                x.Id,
                x.MedicineCode,
                x.BrandName,
                x.GenericName,
                x.Strength,
                x.DosageForm,
                x.Category != null ? x.Category.Name : null,
                x.Manufacturer != null ? x.Manufacturer.Name : null,
                x.PurchaseUnit,
                x.SaleUnit,
                x.UnitsPerPurchaseUnit,
                x.ReorderLevel,
                x.BatchTrackingRequired,
                x.ExpiryTrackingRequired,
                x.IsActive))
            .ToListAsync(cancellationToken);
    }

    public async Task<MedicineEditorModel?> GetAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        _permissions.Demand("medicines.manage");
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.Set<MedicineEntity>()
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new MedicineEditorModel(
                x.Id,
                x.MedicineCategoryId,
                x.ManufacturerId,
                x.MedicineCode,
                x.Barcode,
                x.BrandName,
                x.GenericName,
                x.Strength,
                x.DosageForm,
                x.PurchaseUnit,
                x.SaleUnit,
                x.UnitsPerPurchaseUnit,
                x.ReorderLevel,
                x.PrescriptionRequired,
                x.BatchTrackingRequired,
                x.ExpiryTrackingRequired,
                x.IsActive,
                x.Notes))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<MedicineReferenceData> GetReferenceDataAsync(
        CancellationToken cancellationToken = default)
    {
        _permissions.Demand("medicines.manage");

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var categories = await context.Set<MedicineCategoryEntity>()
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => new MedicineReferenceItem(x.Id, x.Name, x.IsActive))
            .ToListAsync(cancellationToken);

        var manufacturers = await context.Set<ManufacturerEntity>()
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => new ManufacturerReferenceItem(x.Id, x.Name, x.Country, x.IsActive))
            .ToListAsync(cancellationToken);

        return new MedicineReferenceData(categories, manufacturers);
    }

    public async Task<string> CreateAsync(
        SaveMedicineRequest request,
        CancellationToken cancellationToken = default)
    {
        _permissions.Demand("medicines.manage");
        Validate(request);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await EnsureReferencesExistAsync(context, request, cancellationToken);
        await EnsureUniqueAsync(context, request, excludeId: null, cancellationToken);

        var now = _clock.UtcNow;
        var entity = new MedicineEntity
        {
            Id = Guid.CreateVersion7().ToString(),
            CreatedAt = now,
            UpdatedAt = now,
        };

        Apply(entity, request);
        context.Add(entity);
        CloudSyncOutboxWriter.QueueMedicineUpsert(
            context,
            _sessions.Current,
            entity,
            now);
        await context.SaveChangesAsync(cancellationToken);
        return entity.Id;
    }

    public async Task UpdateAsync(
        string id,
        SaveMedicineRequest request,
        CancellationToken cancellationToken = default)
    {
        _permissions.Demand("medicines.manage");
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        Validate(request);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.Set<MedicineEntity>()
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new InvalidOperationException("Medicine was not found.");

        await EnsureReferencesExistAsync(context, request, cancellationToken);
        await EnsureUniqueAsync(context, request, id, cancellationToken);

        Apply(entity, request);
        entity.UpdatedAt = _clock.UtcNow;
        CloudSyncOutboxWriter.QueueMedicineUpsert(
            context,
            _sessions.Current,
            entity,
            entity.UpdatedAt);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<string> CreateCategoryAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        _permissions.Demand("medicines.manage");
        name = NormalizeRequired(name, 120, nameof(name));

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var existing = await context.Set<MedicineCategoryEntity>()
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Name == name, cancellationToken);

        if (existing is not null)
        {
            return existing.Id;
        }

        var now = _clock.UtcNow;
        var entity = new MedicineCategoryEntity
        {
            Id = Guid.CreateVersion7().ToString(),
            Name = name,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
        };

        context.Add(entity);
        await context.SaveChangesAsync(cancellationToken);
        return entity.Id;
    }

    public async Task<string> CreateManufacturerAsync(
        string name,
        string? country,
        CancellationToken cancellationToken = default)
    {
        _permissions.Demand("medicines.manage");
        name = NormalizeRequired(name, 160, nameof(name));
        country = NormalizeOptional(country, 100, nameof(country));

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var existing = await context.Set<ManufacturerEntity>()
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Name == name, cancellationToken);

        if (existing is not null)
        {
            return existing.Id;
        }

        var now = _clock.UtcNow;
        var entity = new ManufacturerEntity
        {
            Id = Guid.CreateVersion7().ToString(),
            Name = name,
            Country = country,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
        };

        context.Add(entity);
        await context.SaveChangesAsync(cancellationToken);
        return entity.Id;
    }

    private static async Task EnsureReferencesExistAsync(
        PharmacyDbContext context,
        SaveMedicineRequest request,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.MedicineCategoryId) &&
            !await context.Set<MedicineCategoryEntity>()
                .AnyAsync(x => x.Id == request.MedicineCategoryId, cancellationToken))
        {
            throw new ArgumentException("The selected medicine category does not exist.");
        }

        if (!string.IsNullOrWhiteSpace(request.ManufacturerId) &&
            !await context.Set<ManufacturerEntity>()
                .AnyAsync(x => x.Id == request.ManufacturerId, cancellationToken))
        {
            throw new ArgumentException("The selected manufacturer does not exist.");
        }
    }

    private static async Task EnsureUniqueAsync(
        PharmacyDbContext context,
        SaveMedicineRequest request,
        string? excludeId,
        CancellationToken cancellationToken)
    {
        var code = request.MedicineCode.Trim();
        var codeExists = await context.Set<MedicineEntity>()
            .AnyAsync(x => x.MedicineCode == code && x.Id != excludeId, cancellationToken);

        if (codeExists)
        {
            throw new ArgumentException($"Medicine code '{code}' already exists.");
        }

        var barcode = NormalizeOptional(request.Barcode, 120, nameof(request.Barcode));
        if (barcode is null)
        {
            return;
        }

        var barcodeExists = await context.Set<MedicineEntity>()
            .AnyAsync(x => x.Barcode == barcode && x.Id != excludeId, cancellationToken);

        if (barcodeExists)
        {
            throw new ArgumentException($"Barcode '{barcode}' already exists.");
        }
    }

    private static void Apply(MedicineEntity entity, SaveMedicineRequest request)
    {
        entity.MedicineCategoryId = NullIfWhiteSpace(request.MedicineCategoryId);
        entity.ManufacturerId = NullIfWhiteSpace(request.ManufacturerId);
        entity.MedicineCode = request.MedicineCode.Trim();
        entity.Barcode = NormalizeOptional(request.Barcode, 120, nameof(request.Barcode));
        entity.BrandName = request.BrandName.Trim();
        entity.GenericName = NormalizeOptional(request.GenericName, 180, nameof(request.GenericName));
        entity.Strength = NormalizeOptional(request.Strength, 100, nameof(request.Strength));
        entity.DosageForm = NormalizeOptional(request.DosageForm, 80, nameof(request.DosageForm));
        entity.PurchaseUnit = request.PurchaseUnit.Trim();
        entity.SaleUnit = request.SaleUnit.Trim();
        entity.UnitsPerPurchaseUnit = request.UnitsPerPurchaseUnit;
        entity.ReorderLevel = request.ReorderLevel;
        entity.PrescriptionRequired = request.PrescriptionRequired;
        entity.BatchTrackingRequired = request.BatchTrackingRequired;
        entity.ExpiryTrackingRequired = request.ExpiryTrackingRequired;
        entity.IsActive = request.IsActive;
        entity.Notes = NormalizeOptional(request.Notes, 2000, nameof(request.Notes));
    }

    internal static void Validate(SaveMedicineRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        _ = NormalizeRequired(request.MedicineCode, 80, nameof(request.MedicineCode));
        _ = NormalizeRequired(request.BrandName, 180, nameof(request.BrandName));
        _ = NormalizeRequired(request.PurchaseUnit, 50, nameof(request.PurchaseUnit));
        _ = NormalizeRequired(request.SaleUnit, 50, nameof(request.SaleUnit));

        _ = NormalizeOptional(request.Barcode, 120, nameof(request.Barcode));
        _ = NormalizeOptional(request.GenericName, 180, nameof(request.GenericName));
        _ = NormalizeOptional(request.Strength, 100, nameof(request.Strength));
        _ = NormalizeOptional(request.DosageForm, 80, nameof(request.DosageForm));
        _ = NormalizeOptional(request.Notes, 2000, nameof(request.Notes));

        if (request.UnitsPerPurchaseUnit <= 0 || request.UnitsPerPurchaseUnit > 99_999_999m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request.UnitsPerPurchaseUnit),
                "Units per purchase unit must be greater than zero and within the supported range.");
        }

        if (request.ReorderLevel < 0 || request.ReorderLevel > 999_999_999m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request.ReorderLevel),
                "Reorder level must be zero or greater and within the supported range.");
        }
    }

    private static string NormalizeRequired(string value, int maxLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        value = value.Trim();

        if (value.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }

        return value;
    }

    private static string? NormalizeOptional(string? value, int maxLength, string parameterName)
    {
        value = NullIfWhiteSpace(value);
        if (value is not null && value.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }

        return value;
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
