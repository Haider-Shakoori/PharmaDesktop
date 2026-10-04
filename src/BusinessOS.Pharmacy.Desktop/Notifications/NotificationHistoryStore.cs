using System.IO;
using System.Text.Json;

namespace BusinessOS.Pharmacy.Desktop.Notifications;

public sealed class NotificationHistoryStore
{
    private const int MaximumItems = 1000;
    private readonly object _gate = new();
    private readonly string _file;

    public NotificationHistoryStore()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BusinessOS",
            "Pharmacy");

        Directory.CreateDirectory(folder);
        _file = Path.Combine(folder, "notifications.json");
    }

    public IReadOnlyList<AppNotification> Load()
    {
        lock (_gate)
        {
            try
            {
                if (!File.Exists(_file))
                {
                    return Array.Empty<AppNotification>();
                }

                return JsonSerializer.Deserialize<List<AppNotification>>(
                           File.ReadAllText(_file))
                       ?? new List<AppNotification>();
            }
            catch
            {
                return Array.Empty<AppNotification>();
            }
        }
    }

    public void Add(AppNotification notification)
    {
        lock (_gate)
        {
            var items = LoadUnsafe().ToList();
            items.Insert(0, notification);

            if (items.Count > MaximumItems)
            {
                items.RemoveRange(
                    MaximumItems,
                    items.Count - MaximumItems);
            }

            SaveUnsafe(items);
        }
    }

    public void MarkAllRead()
    {
        lock (_gate)
        {
            var items = LoadUnsafe()
                .Select(x => x.IsRead ? x : x with { IsRead = true })
                .ToList();

            SaveUnsafe(items);
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            SaveUnsafe(Array.Empty<AppNotification>());
        }
    }

    private IReadOnlyList<AppNotification> LoadUnsafe()
    {
        try
        {
            if (!File.Exists(_file))
            {
                return Array.Empty<AppNotification>();
            }

            return JsonSerializer.Deserialize<List<AppNotification>>(
                       File.ReadAllText(_file))
                   ?? new List<AppNotification>();
        }
        catch
        {
            return Array.Empty<AppNotification>();
        }
    }

    private void SaveUnsafe(IReadOnlyList<AppNotification> items)
    {
        File.WriteAllText(
            _file,
            JsonSerializer.Serialize(
                items,
                new JsonSerializerOptions { WriteIndented = true }));
    }
}
