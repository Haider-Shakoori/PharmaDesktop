namespace BusinessOS.Pharmacy.Desktop.Dashboard;

public sealed record DashboardAlertViewModel(
    string Kind,
    string Title,
    string Detail,
    string Severity);
