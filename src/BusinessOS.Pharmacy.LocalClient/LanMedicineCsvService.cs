using System.Globalization;
using System.Text;
using BusinessOS.Pharmacy.Application.Abstractions.Medicines;

namespace BusinessOS.Pharmacy.LocalClient;

public sealed class LanMedicineCsvService(
    IMedicineCatalogService catalog) : IMedicineCsvService
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

    public IReadOnlyList<string> Columns => TemplateColumns;

    public async Task WriteTemplateAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        await File.WriteAllTextAsync(
            path,
            string.Join(",", TemplateColumns) + Environment.NewLine,
            new UTF8Encoding(true),
            cancellationToken);
    }

    public async Task<MedicineCsvPreview> PreviewAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var parsed = await ParseAsync(path, cancellationToken);

        return new MedicineCsvPreview(
            parsed.Count,
            parsed.Count(x => x.Result.IsValid),
            parsed.Count(x => !x.Result.IsValid),
            parsed.Select(x => x.Result).ToList());
    }

    public async Task<MedicineCsvImportResult> ImportAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var parsed = await ParseAsync(path, cancellationToken);
        var references = await catalog.GetReferenceDataAsync(cancellationToken);
        var categories = references.Categories
            .ToDictionary(x => x.Name, x => x.Id, StringComparer.OrdinalIgnoreCase);
        var manufacturers = references.Manufacturers
            .ToDictionary(x => x.Name, x => x.Id, StringComparer.OrdinalIgnoreCase);

        var results = new List<MedicineCsvRowResult>();
        var imported = 0;

        foreach (var row in parsed)
        {
            if (!row.Result.IsValid || row.Candidate is null)
            {
                results.Add(row.Result);
                continue;
            }

            try
            {
                var candidate = row.Candidate;

                string? categoryId = null;
                if (!string.IsNullOrWhiteSpace(candidate.Category))
                {
                    if (!categories.TryGetValue(candidate.Category, out categoryId))
                    {
                        categoryId = await catalog.CreateCategoryAsync(
                            candidate.Category,
                            cancellationToken);
                        categories[candidate.Category] = categoryId;
                    }
                }

                string? manufacturerId = null;
                if (!string.IsNullOrWhiteSpace(candidate.Manufacturer))
                {
                    if (!manufacturers.TryGetValue(candidate.Manufacturer, out manufacturerId))
                    {
                        manufacturerId = await catalog.CreateManufacturerAsync(
                            candidate.Manufacturer,
                            candidate.ManufacturerCountry,
                            cancellationToken);
                        manufacturers[candidate.Manufacturer] = manufacturerId;
                    }
                }

                await catalog.CreateAsync(
                    new SaveMedicineRequest(
                        categoryId,
                        manufacturerId,
                        candidate.MedicineCode,
                        candidate.Barcode,
                        candidate.BrandName,
                        candidate.GenericName,
                        candidate.Strength,
                        candidate.DosageForm,
                        candidate.PurchaseUnit,
                        candidate.SaleUnit,
                        candidate.UnitsPerPurchaseUnit,
                        candidate.ReorderLevel,
                        candidate.PrescriptionRequired,
                        candidate.BatchTrackingRequired,
                        candidate.ExpiryTrackingRequired,
                        candidate.IsActive,
                        candidate.Notes),
                    cancellationToken);

                imported++;
                results.Add(row.Result);
            }
            catch (Exception exception)
            {
                results.Add(row.Result with
                {
                    IsValid = false,
                    Errors = [exception.Message],
                });
            }
        }

        return new MedicineCsvImportResult(
            imported,
            results.Count - imported,
            results);
    }

    private static async Task<IReadOnlyList<ParsedRow>> ParseAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Medicine CSV file was not found.", path);

        var lines = await File.ReadAllLinesAsync(path, cancellationToken);
        if (lines.Length == 0)
            throw new InvalidDataException("Medicine CSV is empty.");

        var header = ParseLine(lines[0])
            .Select(x => x.Trim().TrimStart('﻿'))
            .ToArray();

        var missing = TemplateColumns
            .Where(x => !header.Contains(x, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        if (missing.Length > 0)
            throw new InvalidDataException(
                $"Medicine CSV is missing required columns: {string.Join(", ", missing)}.");

        var indexes = header
            .Select((name, index) => (name, index))
            .ToDictionary(x => x.name, x => x.index, StringComparer.OrdinalIgnoreCase);

        var results = new List<ParsedRow>();
        var seenCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenBarcodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var lineIndex = 1; lineIndex < lines.Length; lineIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(lines[lineIndex]))
                continue;

            var errors = new List<string>();
            CsvCandidate? candidate = null;

            try
            {
                var fields = ParseLine(lines[lineIndex]);
                string Get(string name)
                {
                    var index = indexes[name];
                    return index < fields.Count ? fields[index].Trim() : string.Empty;
                }

                var code = Required(Get("medicine_code"), "medicine_code");
                var brand = Required(Get("brand_name"), "brand_name");
                var barcode = Optional(Get("barcode"));

                if (!seenCodes.Add(code))
                    errors.Add($"Medicine code '{code}' is duplicated in this CSV.");

                if (barcode is not null && !seenBarcodes.Add(barcode))
                    errors.Add($"Barcode '{barcode}' is duplicated in this CSV.");

                candidate = new CsvCandidate(
                    code,
                    brand,
                    Optional(Get("generic_name")),
                    Optional(Get("strength")),
                    Optional(Get("dosage_form")),
                    Optional(Get("category")),
                    Optional(Get("manufacturer")),
                    Optional(Get("manufacturer_country")),
                    barcode,
                    Optional(Get("purchase_unit")) ?? "pack",
                    Optional(Get("sale_unit")) ?? "unit",
                    ParseDecimal(Get("units_per_purchase_unit"), 1m, "units_per_purchase_unit"),
                    ParseDecimal(Get("reorder_level"), 0m, "reorder_level"),
                    ParseBoolean(Get("prescription_required"), false, "prescription_required"),
                    ParseBoolean(Get("batch_tracking_required"), true, "batch_tracking_required"),
                    ParseBoolean(Get("expiry_tracking_required"), true, "expiry_tracking_required"),
                    ParseBoolean(Get("is_active"), true, "is_active"),
                    Optional(Get("notes")));

                if (candidate.UnitsPerPurchaseUnit <= 0)
                    errors.Add("units_per_purchase_unit must be greater than zero.");

                if (candidate.ReorderLevel < 0)
                    errors.Add("reorder_level cannot be negative.");
            }
            catch (Exception exception) when (
                exception is ArgumentException or FormatException or InvalidDataException)
            {
                errors.Add(exception.Message);
            }

            results.Add(new ParsedRow(
                candidate,
                new MedicineCsvRowResult(
                    lineIndex + 1,
                    candidate?.MedicineCode,
                    candidate?.BrandName,
                    candidate is not null && errors.Count == 0,
                    errors)));
        }

        return results;
    }

    private static IReadOnlyList<string> ParseLine(string line)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        var quoted = false;

        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];

            if (ch == '"')
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

            if (ch == ',' && !quoted)
            {
                fields.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(ch);
        }

        if (quoted)
            throw new InvalidDataException("CSV contains an unclosed quoted value.");

        fields.Add(current.ToString());
        return fields;
    }

    private static decimal ParseDecimal(string value, decimal fallback, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var result))
            throw new FormatException($"Column '{field}' must contain a valid number.");

        return result;
    }

    private static bool ParseBoolean(string value, bool fallback, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

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
        string? Notes);
}
