using System.Collections.ObjectModel;

namespace BusinessOS.Pharmacy.Desktop.Localization;

public static class UiLanguageCatalog
{
    public static ReadOnlyCollection<UiLanguage> All { get; } =
        new(new List<UiLanguage>
        {
            new("en", "English", "en-US", false),
            new("fa", "دری", "fa-AF", true),
            new("ps", "پښتو", "ps-AF", true)
        });
}