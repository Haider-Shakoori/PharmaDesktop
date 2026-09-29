using System.Globalization;
using System.Text;
using BusinessOS.Pharmacy.Application.Abstractions.Authentication;
using BusinessOS.Pharmacy.Application.Abstractions.Medicines;
using BusinessOS.Pharmacy.Application.Abstractions.Time;
using BusinessOS.Pharmacy.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace BusinessOS.Pharmacy.Persistence;

public sealed class MedicineCsvService : IMedicineCsvService
{
    private static readonly string[] TemplateColumns =
    [
        "medicine_code",
        "brand_name",
        "generic_name",
        "strength",
        "dosage_form",
        "category",
        "manufacturer",
        "manufacturer_country",
        "barcode",
        "purchase_unit",
        "sale_unit",
        "units_per_purchase_unit",
        "reorder_level",
        "prescription_required",
        "batch_tracking_required",
        "expiry_tracking_required",
        "is_active",
        "notes",
    ];

    private readonly IDbContextFactory<PharmacyDbContext> _contextFactory;
    private readonly IPermissionAuthorizer _permissions;
    private readonly IClock _clock;

    public MedicineCsvService(
        IDbContextFactory<PharmacyDbContext> contextFactory,
        IPermissionAuthorizer permissions,
        IClock clock)
    {
        _contextFactory = contextFactory;
        _permissions = permissions;
        _clock = clock;
    }

    public IReadOnlyList<string> Columns => TemplateColumns;

    public async Task WriteTemplateAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        _permissions.Demand("medicines.manage");
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        await File.WriteAllTextAsync(
            path,
            string.Join(",", TemplateColumns) + Environment.NewLine,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
            cancellationToken);
    }

    public async Task<MedicineCsvPreview> PreviewAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        _permissions.Demand("medicines.manage");
        var parsed = await ParseAsync(path, cancellationToken);
        return new MedicineCsvPreview(
            parsed.Rows.Count,
            parsed.Rows.Count(x => x.Result.IsValid),
            parsed.Rows.Count(x => !x.Result.IsValid),
            parsed.Rows.Select(x => x.Result).ToList());
    }

    public async Task<MedicineCsvImportResult> ImportAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        _permissions.Demand("medicines.manage");
        var parsed = await ParseAsync(path, cancellationToken);

        var valid = parsed.Rows.Where(x => x.Result.IsValid).ToList();
        if (valid.Count == 0)
        {
            return new MedicineCsvImportResult(
                0,
                parsed.Rows.Count,
                parsed.Rows.Select(x => x.Result).ToList());
        }

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var now = _clock.UtcNow;
        var categoryCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var manufacturerCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in valid)
        {
            var row = item.Candidate!;
            var categoryId = await ResolveCategoryAsync(
                context,
                categoryCache,
                row.Category,
                now,
                cancellationToken);

            var manufacturerId = await ResolveManufacturerAsync(
                context,
                manufacturerCache,
                row.Manufacturer,
                row.ManufacturerCountry,
                now,
                cancellationToken);

            context.Add(new MedicineEntity
            {
                Id = Guid.CreateVersion7().ToString(),
                MedicineCategoryId = categoryId,
                ManufacturerId = manufacturerId,
                MedicineCode = row.MedicineCode,
                Barcode = row.Barcode,
                BrandName = row.BrandName,
                GenericName = row.GenericName,
                Strength = row.Strength,
                DosageForm = row.DosageForm,
                PurchaseUnit = row.PurchaseUnit,
                SaleUnit = row.SaleUnit,
                UnitsPerPurchaseUnit = row.UnitsPerPurchaseUnit,
                ReorderLevel = row.ReorderLevel,
                PrescriptionRequired = row.PrescriptionRequired,
                BatchTrackingRequired = row.BatchTrackingRequired,
                ExpiryTrackingRequired = row.ExpiryTrackingRequired,
                IsActive = row.IsActive,
                Notes = row.Notes,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new MedicineCsvImportResult(
            valid.Count,
            parsed.Rows.Count - valid.Count,
            parsed.Rows.Select(x => x.Result).ToList());
    }

    private async Task<ParsedCsv> ParseAsync(
        string path,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Medicine CSV file was not found.", path);
        }

        var lines = await File.ReadAllLinesAsync(path, cancellationToken);
        if (lines.Length == 0)
        {
            throw new InvalidDataException("Medicine CSV is empty.");
        }

        var header = ParseLine(lines[0])
            .Select(x => x.Trim().TrimStart('﻿'))
            .ToArray();

        var missing = TemplateColumns
            .Where(required => !header.Contains(required, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        if (missing.Length > 0)
        {
            throw new InvalidDataException(
                $"Medicine CSV is missing required columns: {string.Join(", ", missing)}.");
        }

        var indexes = header
            .Select((name, index) => (name, index))
            .ToDictionary(x => x.name, x => x.index, StringComparer.OrdinalIgnoreCase);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var existingCodes = (await context.Set<MedicineEntity>()
                .AsNoTracking()
                .Select(x => x.MedicineCode)
                .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var existingBarcodes = (await context.Set<MedicineEntity>()
                .AsNoTracking()
                .Where(x => x.Barcode != null)
                .Select(x => x.Barcode!)
                .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var seenCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenBarcodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rows = new List<ParsedRow>();

        for (var lineIndex = 1; lineIndex < lines.Length; lineIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(lines[lineIndex]))
            {
                continue;
            }

            var fields = ParseLine(lines[lineIndex]);
            var errors = new List<string>();
            CsvCandidate? candidate = null;

            try
            {
                candidate = BuildCandidate(fields, indexes);

                var validationRequest = candidate.ToSaveRequest();
                MedicineCatalogService.Validate(validationRequest);

                if (existingCodes.Contains(candidate.MedicineCode))
                {
                    errors.Add($"Medicine code '{candidate.MedicineCode}' already exists.");
                }
                else if (!seenCodes.Add(candidate.MedicineCode))
                {
                    errors.Add($"Medicine code '{candidate.MedicineCode}' is duplicated in this CSV.");
                }

                if (candidate.Barcode is not null)
                {
                    if (existingBarcodes.Contains(candidate.Barcode))
                    {
                        errors.Add($"Barcode '{candidate.Barcode}' already exists.");
                    }
                    else if (!seenBarcodes.Add(candidate.Barcode))
                    {
                        errors.Add($"Barcode '{candidate.Barcode}' is duplicated in this CSV.");
                    }
                }
            }
            catch (Exception exception) when (
                exception is ArgumentException or
                FormatException or
                InvalidDataException)
            {
                errors.Add(exception.Message);
            }

            var rowNumber = lineIndex + 1;
            rows.Add(new ParsedRow(
                candidate,
                new MedicineCsvRowResult(
                    rowNumber,
                    candidate?.MedicineCode,
                    candidate?.BrandName,
                    errors.Count == 0 && candidate is not null,
                    errors)));
        }

        return new ParsedCsv(rows);
    }

    private static CsvCandidate BuildCandidate(
        IReadOnlyList<string> fields,
        IReadOnlyDictionary<string, int> indexes)
    {
        string Get(string name)
        {
            var index = indexes[name];
            return index < fields.Count ? fields[index].Trim() : string.Empty;
        }

        var medicineCode = Required(Get("medicine_code"), "medicine_code");
        var brandName = Required(Get("brand_name"), "brand_name");

        return new CsvCandidate(
            medicineCode,
            brandName,
            Optional(Get("generic_name")),
            Optional(Get("strength")),
            Optional(Get("dosage_form")),
            Optional(Get("category")),
            Optional(Get("manufacturer")),
            Optional(Get("manufacturer_country")),
            Optional(Get("barcode")),
            Optional(Get("purchase_unit")) ?? "pack",
            Optional(Get("sale_unit")) ?? "unit",
            ParseDecimal(Get("units_per_purchase_unit"), 1m, "units_per_purchase_unit"),
            ParseDecimal(Get("reorder_level"), 0m, "reorder_level"),
            ParseBoolean(Get("prescription_required"), false, "prescription_required"),
            ParseBoolean(Get("batch_tracking_required"), true, "batch_tracking_required"),
            ParseBoolean(Get("expiry_tracking_required"), true, "expiry_tracking_required"),
            ParseBoolean(Get("is_active"), true, "is_active"),
            Optional(Get("notes")));
    }

    private static async Task<string?> ResolveCategoryAsync(
        PharmacyDbContext context,
        IDictionary<string, string> cache,
        string? name,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (name is null)
        {
            return null;
        }

        if (cache.TryGetValue(name, out var cached))
        {
            return cached;
        }

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

        cache[name] = existing.Id;
        return existing.Id;
    }

    private static async Task<string?> ResolveManufacturerAsync(
        PharmacyDbContext context,
        IDictionary<string, string> cache,
        string? name,
        string? country,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (name is null)
        {
            return null;
        }

        if (cache.TryGetValue(name, out var cached))
        {
            return cached;
        }

        var existing = await context.Set<ManufacturerEntity>()
            .SingleOrDefaultAsync(x => x.Name == name, cancellationToken);

        if (existing is null)
        {
            existing = new ManufacturerEntity
            {
                Id = Guid.CreateVersion7().ToString(),
                Name = name,
                Country = country,
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now,
            };
            context.Add(existing);
        }

        cache[name] = existing.Id;
        return existing.Id;
    }

    private static IReadOnlyList<string> ParseLine(string line)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        var quoted = false;

        for (var i = 0; i < line.Length; i++)
        {
            var character = line[i];

            if (character == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    quoted = !quoted;
                }

                continue;
            }

            if (character == ',' && !quoted)
            {
                fields.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(character);
        }

        if (quoted)
        {
            throw new InvalidDataException("CSV contains an unclosed quoted value.");
        }

        fields.Add(current.ToString());
        return fields;
    }

    private static decimal ParseDecimal(string value, decimal defaultValue, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        if (!decimal.TryParse(
                value,
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out var result))
        {
            throw new FormatException($"Column '{field}' must contain a valid number.");
        }

        return result;
    }

    private static bool ParseBoolean(string value, bool defaultValue, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "1" or "true" or "yes" or "y" => true,
            "0" or "false" or "no" or "n" => false,
            _ => throw new FormatException(
                $"Column '{field}' must be true/false, yes/no, or 1/0."),
        };
    }

    private static string Required(string value, string field) =>
        !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : throw new InvalidDataException($"Column '{field}' is required.");

    private static string? Optional(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record ParsedCsv(IReadOnlyList<ParsedRow> Rows);

    private sealed record ParsedRow(
        CsvCandidate? Candidate,
        MedicineCsvRowResult Result);

    private sealed record CsvCandidate(
        string MedicineCode,
        string BrandName,
        string? GenericName,
        string? Strength,
        string? DosageForm,
        string? Category,
        string? Manufacturer,
        string? ManufacturerCountry,
        string? Barcode,
        string PurchaseUnit,
        string SaleUnit,
        decimal UnitsPerPurchaseUnit,
        decimal ReorderLevel,
        bool PrescriptionRequired,
        bool BatchTrackingRequired,
        bool ExpiryTrackingRequired,
        bool IsActive,
        string? Notes)
    {
        public SaveMedicineRequest ToSaveRequest() => new(
            null,
            null,
            MedicineCode,
            Barcode,
            BrandName,
            GenericName,
            Strength,
            DosageForm,
            PurchaseUnit,
            SaleUnit,
            UnitsPerPurchaseUnit,
            ReorderLevel,
            PrescriptionRequired,
            BatchTrackingRequired,
            ExpiryTrackingRequired,
            IsActive,
            Notes);
    }
}
