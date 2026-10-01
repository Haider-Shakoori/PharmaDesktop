namespace BusinessOS.Pharmacy.Sync;

public sealed record CloudSyncOptions(
    string BaseUrl,
    string PushPath = "/api/v1/desktop/sync/push",
    string PullPath = "/api/v1/desktop/sync/pull",
    int TimeoutSeconds = 20,
    int BatchSize = 25,
    int PullPageSize = 100,
    int MaxPullPagesPerRun = 5,
    int IntervalSeconds = 30)
{
    public void Validate()
    {
        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException(
                "BusinessOS cloud synchronization requires an HTTPS base URL.");

        if (!PushPath.StartsWith("/", StringComparison.Ordinal) ||
            !PullPath.StartsWith("/", StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Cloud synchronization API paths must be absolute application paths.");

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
    }
}
