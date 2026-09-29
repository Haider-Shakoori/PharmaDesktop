namespace BusinessOS.Pharmacy.Desktop.Localization;

public sealed record UiLanguage(
    string Code,
    string DisplayName,
    string CultureName,
    bool IsRightToLeft);