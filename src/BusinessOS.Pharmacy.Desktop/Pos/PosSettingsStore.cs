using System.IO;
using System.Text.Json;

namespace BusinessOS.Pharmacy.Desktop.Pos;

/// <summary>
/// Workstation point-of-sale preferences, editable from Settings.
/// </summary>
public sealed record PosSettings(
    bool ShowTopSellers = true,
    int TopSellerCount = 10,
    int TopSellerDays = 30);

public sealed class PosSettingsStore
{
    private readonly string _file;

    public PosSettingsStore()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BusinessOS",
            "Pharmacy");
        Directory.CreateDirectory(folder);
        _file = Path.Combine(folder, "pos-settings.json");
    }

    public event Action<PosSettings>? SettingsChanged;

    public PosSettings Load()
    {
        try
        {
            if (File.Exists(_file))
            {
                var settings = JsonSerializer.Deserialize<PosSettings>(File.ReadAllText(_file));
                if (settings is not null)
                {
                    return settings with
                    {
                        TopSellerCount = Math.Clamp(settings.TopSellerCount, 5, 30),
                        TopSellerDays = Math.Clamp(settings.TopSellerDays, 7, 365),
                    };
                }
            }
        }
        catch
        {
            // Corrupt settings fall back to defaults.
        }

        return new PosSettings();
    }

    public void Save(PosSettings settings)
    {
        settings = settings with
        {
            TopSellerCount = Math.Clamp(settings.TopSellerCount, 5, 30),
            TopSellerDays = Math.Clamp(settings.TopSellerDays, 7, 365),
        };

        File.WriteAllText(_file, JsonSerializer.Serialize(settings));
        SettingsChanged?.Invoke(settings);
    }
}