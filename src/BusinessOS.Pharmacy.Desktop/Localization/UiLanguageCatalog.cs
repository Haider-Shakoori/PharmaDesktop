using System.Collections.ObjectModel;

namespace BusinessOS.Pharmacy.Desktop.Localization;

public static class UiLanguageCatalog
{
    public static ReadOnlyCollection<UiLanguage> All { get; } =
        new(new List<UiLanguage>
        {
            new("en", "English", "en-US", false),
            new("fa", "دری (Dari)", "fa-AF", true),
            new("ps", "پښتو (Pashto)", "ps-AF", true)
        });
}