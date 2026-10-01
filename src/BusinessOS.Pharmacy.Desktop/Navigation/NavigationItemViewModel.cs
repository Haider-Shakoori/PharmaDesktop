namespace BusinessOS.Pharmacy.Desktop.Navigation;

public sealed record NavigationItemViewModel(
    string Key,
    string Label,
    string Group,
    bool IsSelected = false)
{
    public string AccentHex => Key switch
    {
        "dashboard" => "#3B82F6",
        "pos" => "#10B981",
        "medicines" => "#8B5CF6",
        "inventory" => "#F59E0B",
        "batches" => "#F43F5E",
        "purchases" => "#06B6D4",
        "suppliers" => "#6366F1",
        "customers" => "#14B8A6",
        "expenses" => "#EF4444",
        "closing" => "#F97316",
        "reports" => "#2563EB",
        "returns" => "#0EA5E9",
        "users" => "#7C3AED",
        "roles" => "#9333EA",
        "backup" => "#4F46E5",
        "updates" => "#0F8A83",
        "sync" => "#0F8A83",
        "network" => "#0891B2",
        "settings" => "#64748B",
        _ => "#64748B",
    };

    public string AccentSoftHex => Key switch
    {
        "dashboard" => "#E8F2FF",
        "pos" => "#E8FBF3",
        "medicines" => "#F2ECFF",
        "inventory" => "#FFF5D9",
        "batches" => "#FFEAF0",
        "purchases" => "#E8FAFD",
        "suppliers" => "#EEF0FF",
        "customers" => "#E8FAF7",
        "expenses" => "#FEEBEC",
        "closing" => "#FFF0E6",
        "reports" => "#EAF1FF",
        "returns" => "#EAF8FF",
        "users" => "#F2EAFE",
        "roles" => "#F5EAFE",
        "backup" => "#EEF0FF",
        "updates" => "#E8F7F5",
        "sync" => "#E8F7F5",
        "network" => "#E9F8FB",
        "settings" => "#F0F3F7",
        _ => "#F0F3F7",
    };
}
