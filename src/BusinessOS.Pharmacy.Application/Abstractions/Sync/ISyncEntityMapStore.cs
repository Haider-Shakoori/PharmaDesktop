namespace BusinessOS.Pharmacy.Application.Abstractions.Sync;

public interface ISyncEntityMapStore
{
    Task<SyncEntityMap?> FindByLocalIdAsync(
        string stream,
        string localEntityId,
        CancellationToken cancellationToken = default);

    Task<SyncEntityMap?> FindByCloudIdAsync(
        string stream,
        string cloudEntityId,
        CancellationToken cancellationToken = default);

    Task UpsertAsync(
        SyncEntityMap mapping,
        CancellationToken cancellationToken = default);
}

public sealed record SyncEntityMap(
    string Stream,
    string LocalEntityId,
    string CloudEntityId,
    string? CloudVersion,
    DateTimeOffset UpdatedAt);
