using System.IO;
using System.Text.Json;

namespace BusinessOS.Pharmacy.Desktop.Appearance;

public enum AppearanceTheme
{
    Classic,
    Glass,
}

public sealed record AppearanceSettings(string Theme = nameof(AppearanceTheme.Classic));

/// <summary>
/// Persists the workstation application theme in one central location.
/// </summary>
public sealed class AppearanceSettingsStore
{
    private readonly string _file;

    public AppearanceSettingsStore()
        : this(null)
    {
    }

    public AppearanceSettingsStore(string? filePath)
    {
        if (!string.IsNullOrWhiteSpace(filePath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(filePath))!);
            _file = Path.GetFullPath(filePath);
            return;
        }

        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BusinessOS",
            "Pharmacy");
        Directory.CreateDirectory(folder);
        _file = Path.Combine(folder, "appearance.json");
    }

    public AppearanceSettings Load()
    {
        try
        {
            if (File.Exists(_file))
            {
                return JsonSerializer.Deserialize<AppearanceSettings>(File.ReadAllText(_file))
                       ?? new AppearanceSettings();
            }
        }
        catch
        {
            // Corrupt settings fall back to the default theme.
        }

        return new AppearanceSettings();
    }

    public void Save(AppearanceSettings settings) =>
        File.WriteAllText(_file, JsonSerializer.Serialize(settings));
}