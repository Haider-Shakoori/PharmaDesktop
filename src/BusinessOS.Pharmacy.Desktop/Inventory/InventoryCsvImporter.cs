using System.Globalization;
using System.IO;
using System.Text;
using BusinessOS.Pharmacy.Application.Abstractions.Inventory;
using BusinessOS.Pharmacy.Application.Abstractions.Medicines;

namespace BusinessOS.Pharmacy.Desktop.Inventory;

/// <summary>
/// Presentation-level CSV import for opening stock. Reuses the validated inventory service
/// for every row so stock movements and audit trails stay identical to manual entry.
/// </summary>
public static class InventoryCsvImporter
{
    public const string TemplateFileName = "inventory-template.csv";

    public static string CreateTemplate()
    {
        var builder = new StringBuilder();
        builder.AppendLine("medicine_code,batch_number,expiry_date,manufactured_date,quantity,purchase_cost,sale_price,notes");
        builder.AppendLine("PARA-500,PA-1026,2027-02-28,2026-02-01,100,12.50,20.00,Opening stock import");
        builder.AppendLine("AMOX-250,AM-0327,2027-03-31,,60,35.00,45.00,");
        return builder.ToString();
    }

    public static Task<InventoryCsvImportResult> ImportAsync(
        string csvText,
        IReadOnlyList<MedicineListItem> medicines,
        IInventoryService inventory,
        string stockLocationId,
        CancellationToken cancellationToken = default)
    {
        var rows = ParseRows(csvText);
        return ImportRowsAsync(rows, medicines, inventory, stockLocationId, cancellationToken);
    }

    public static IReadOnlyList<CsvStockRow> ParseRows(string csvText)
    {
        var records = ParseCsv(csvText);
        if (records.Count == 0)
        {
            return [];
        }

        var headerIndex = 0;
        var first = records[0];
        if (first.Count > 0 &&
            first[0].Trim().TrimStart('\uFEFF').Equals("medicine_code", StringComparison.OrdinalIgnoreCase))
        {
            headerIndex = 1;
        }

        var rows = new List<CsvStockRow>();
        for (var i = headerIndex; i < records.Count; i++)
        {
            var record = records[i];
            if (record.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            rows.Add(new CsvStockRow(
                i + 1,
                GetField(record, 0),
                GetField(record, 1),
                GetField(record, 2),
                GetField(record, 3),
                GetField(record, 4),
                GetField(record, 5),
                GetField(record, 6),
                GetField(record, 7)));
        }

        return rows;
    }

    private static async Task<InventoryCsvImportResult> ImportRowsAsync(
        IReadOnlyList<CsvStockRow> rows,
        IReadOnlyList<MedicineListItem> medicines,
        IInventoryService inventory,
        string stockLocationId,
        CancellationToken cancellationToken)
    {
        var byCode = medicines
            .GroupBy(m => m.MedicineCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var byName = medicines
            .GroupBy(m => m.BrandName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var errors = new List<string>();
        var imported = 0;

        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(row.MedicineCode))
            {
                errors.Add($"Row {row.LineNumber}: medicine_code is required.");
                continue;
            }

            if (!byCode.TryGetValue(row.MedicineCode.Trim(), out var medicine) &&
                !byName.TryGetValue(row.MedicineCode.Trim(), out medicine))
            {
                errors.Add($"Row {row.LineNumber}: medicine '{row.MedicineCode}' was not found.");
                continue;
            }

            if (!decimal.TryParse(row.Quantity, NumberStyles.Number, CultureInfo.InvariantCulture, out var quantity) ||
                quantity <= 0m)
            {
                errors.Add($"Row {row.LineNumber}: quantity must be a positive number.");
                continue;
            }

            if (!TryParseDate(row.ExpiryDate, out var expiryDate, out var expiryError))
            {
                errors.Add($"Row {row.LineNumber}: {expiryError}");
                continue;
            }

            if (!TryParseDate(row.ManufacturedDate, out var manufacturedDate, out var manufacturedError))
            {
                errors.Add($"Row {row.LineNumber}: {manufacturedError}");
                continue;
            }

            if (!TryParseOptionalDecimal(row.PurchaseCost, out var purchaseCost) ||
                !TryParseOptionalDecimal(row.SalePrice, out var salePrice))
            {
                errors.Add($"Row {row.LineNumber}: cost and price must be numbers when provided.");
                continue;
            }

            try
            {
                await inventory.CreateOpeningStockAsync(
                    new CreateOpeningStockRequest(
                        medicine.Id,
                        stockLocationId,
                        string.IsNullOrWhiteSpace(row.BatchNumber) ? null : row.BatchNumber.Trim(),
                        manufacturedDate,
                        expiryDate,
                        quantity,
                        purchaseCost ?? 0m,
                        salePrice,
                        string.IsNullOrWhiteSpace(row.Notes) ? "CSV import" : row.Notes.Trim()),
                    cancellationToken);
                imported++;
            }
            catch (Exception exception)
            {
                errors.Add($"Row {row.LineNumber}: {exception.Message}");
            }
        }

        return new InventoryCsvImportResult(imported, errors);
    }

    private static bool TryParseDate(string? text, out DateOnly? value, out string error)
    {
        value = null;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (DateOnly.TryParseExact(
                text.Trim(),
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
        {
            value = parsed;
            return true;
        }

        error = $"date '{text.Trim()}' must use yyyy-MM-dd.";
        return false;
    }

    private static bool TryParseOptionalDecimal(string? text, out decimal? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (decimal.TryParse(text.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
        {
            value = parsed;
            return true;
        }

        return false;
    }

    private static string GetField(IReadOnlyList<string> record, int index) =>
        index < record.Count ? record[index].Trim() : string.Empty;

    private static List<List<string>> ParseCsv(string text)
    {
        var records = new List<List<string>>();
        var record = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var hasContent = false;

        for (var i = 0; i < text.Length; i++)
        {
            var character = text[i];
            if (inQuotes)
            {
                if (character == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(character);
                }

                continue;
            }

            switch (character)
            {
                case '"':
                    inQuotes = true;
                    hasContent = true;
                    break;
                case ',':
                    record.Add(field.ToString());
                    field.Clear();
                    hasContent = true;
                    break;
                case '\r':
                    break;
                case '\n':
                    record.Add(field.ToString());
                    field.Clear();
                    records.Add(record);
                    record = new List<string>();
                    hasContent = false;
                    break;
                default:
                    field.Append(character);
                    hasContent = true;
                    break;
            }
        }

        if (hasContent || field.Length > 0 || record.Count > 0)
        {
            record.Add(field.ToString());
            records.Add(record);
        }

        return records;
    }
}

public sealed record CsvStockRow(
    int LineNumber,
    string MedicineCode,
    string? BatchNumber,
    string? ExpiryDate,
    string? ManufacturedDate,
    string? Quantity,
    string? PurchaseCost,
    string? SalePrice,
    string? Notes);

public sealed record InventoryCsvImportResult(
    int ImportedCount,
    IReadOnlyList<string> Errors)
{
    public int FailedCount => Errors.Count;
}
