namespace BusinessOS.Pharmacy.Sync;

public sealed record CloudSyncOptions(
    string BaseUrl,
    string PushPath = "/api/v1/desktop/sync/push",
    string PullPath = "/api/v1/desktop/sync/pull",
    int TimeoutSeconds = 20,
    int BatchSize = 25,
    int PullPageSize = 100,
    int MaxPullPagesPerRun = 5,
    int IntervalSeconds = 30,
    int ConflictReviewLimit = 50)
{
    public void Validate()
    {
        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidOperationException(
                "BusinessOS cloud synchronization requires an HTTPS base URL.");

        if (!IsApiPath(PushPath) || !IsApiPath(PullPath))
            throw new InvalidOperationException(
                "Cloud synchronization API paths must be relative /api/ application paths without query strings or fragments.");

        if (TimeoutSeconds is < 5 or > 300)
            throw new InvalidOperationException("Cloud sync timeout must be between 5 and 300 seconds.");
        if (BatchSize is < 1 or > 50)
            throw new InvalidOperationException("Cloud sync batch size must be between 1 and 50.");
        if (PullPageSize is < 1 or > 250)
            throw new InvalidOperationException("Cloud sync page size must be between 1 and 250.");
        if (MaxPullPagesPerRun is < 1 or > 20)
            throw new InvalidOperationException("Cloud sync max pages per run must be between 1 and 20.");
        if (IntervalSeconds is < 15 or > 3600)
            throw new InvalidOperationException("Cloud sync interval must be between 15 and 3600 seconds.");
        if (ConflictReviewLimit is < 1 or > 200)
            throw new InvalidOperationException("Cloud sync conflict review limit must be between 1 and 200.");
    }

    private static bool IsApiPath(string? path) =>
        !string.IsNullOrWhiteSpace(path) &&
        path.StartsWith("/api/", StringComparison.Ordinal) &&
        !path.StartsWith("//", StringComparison.Ordinal) &&
        !path.Contains('?', StringComparison.Ordinal) &&
        !path.Contains('#', StringComparison.Ordinal) &&
        Uri.TryCreate(path, UriKind.Relative, out _);
}
