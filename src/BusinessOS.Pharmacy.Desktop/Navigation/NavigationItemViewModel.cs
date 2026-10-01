namespace BusinessOS.Pharmacy.Desktop.Navigation;

public sealed record NavigationItemViewModel(
    string Key,
    string Label,
    string Glyph,
    bool IsSelected = false);
