namespace BusinessOS.Pharmacy.Desktop.Dashboard;

public sealed record DashboardQuickActionViewModel(
    string Key,
    string Label,
    string Permission,
    bool IsAvailable,
    string Accent,
    string Shortcut);
