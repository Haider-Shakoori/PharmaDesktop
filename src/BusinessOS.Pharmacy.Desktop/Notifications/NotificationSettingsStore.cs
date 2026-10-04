using System.IO;
using System.Text.Json;

namespace BusinessOS.Pharmacy.Desktop.Notifications;

public sealed record NotificationSettings(
    bool EnableToasts = true,
    bool SuccessToasts = true,
    bool InformationToasts = true,
    bool WarningToasts = true,
    bool ErrorToasts = true,
    bool SyncToasts = true,
    bool ConnectivityToasts = true,
    bool StockAlerts = true,
    int ToastDurationSeconds = 5);

public sealed class NotificationSettingsStore
{
    private readonly string _file;

    public NotificationSettingsStore()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BusinessOS",
            "Pharmacy");

        Directory.CreateDirectory(folder);
        _file = Path.Combine(folder, "notification-settings.json");
    }

    public event Action<NotificationSettings>? SettingsChanged;

    public NotificationSettings Load()
    {
        try
        {
            if (File.Exists(_file))
            {
                var settings = JsonSerializer.Deserialize<NotificationSettings>(
                    File.ReadAllText(_file));

                if (settings is not null)
                {
                    return settings with
                    {
                        ToastDurationSeconds = Math.Clamp(
                            settings.ToastDurationSeconds,
                            2,
                            15),
                    };
                }
            }
        }
        catch
        {
            // Corrupt workstation settings fall back to safe defaults.
        }

        return new NotificationSettings();
    }

    public void Save(NotificationSettings settings)
    {
        settings = settings with
        {
            ToastDurationSeconds = Math.Clamp(
                settings.ToastDurationSeconds,
                2,
                15),
        };

        File.WriteAllText(
            _file,
            JsonSerializer.Serialize(
                settings,
                new JsonSerializerOptions { WriteIndented = true }));

        SettingsChanged?.Invoke(settings);
    }
}
