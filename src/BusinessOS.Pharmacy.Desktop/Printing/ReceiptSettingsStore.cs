using System.IO;
using System.Text.Json;

namespace BusinessOS.Pharmacy.Desktop.Printing;

public sealed record ReceiptSettings(
    bool ShowBatchDetails = true,
    bool ShowCashier = true,
    bool ShowCustomer = true,
    bool ShowPayments = true,
    bool ShowFooter = true,
    string FooterText = "Thank you for your purchase");

/// <summary>
/// Workstation receipt layout preferences, editable from Settings.
/// </summary>
public sealed class ReceiptSettingsStore
{
    private readonly string _file;

    public ReceiptSettingsStore()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BusinessOS",
            "Pharmacy");
        Directory.CreateDirectory(folder);
        _file = Path.Combine(folder, "receipt-settings.json");
    }

    public event Action<ReceiptSettings>? SettingsChanged;

    public ReceiptSettings Load()
    {
        try
        {
            if (File.Exists(_file))
            {
                return JsonSerializer.Deserialize<ReceiptSettings>(File.ReadAllText(_file))
                       ?? new ReceiptSettings();
            }
        }
        catch
        {
            // Corrupt settings fall back to defaults.
        }

        return new ReceiptSettings();
    }

    public void Save(ReceiptSettings settings)
    {
        File.WriteAllText(_file, JsonSerializer.Serialize(settings));
        SettingsChanged?.Invoke(settings);
    }
}
