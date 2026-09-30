namespace BusinessOS.Pharmacy.Application.Abstractions.Sync;

public interface ISyncInboxApplier
{
    Task<SyncApplyResult> ApplyAsync(
        SyncPullItem item,
        CancellationToken cancellationToken = default);
}
