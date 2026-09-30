namespace BusinessOS.Pharmacy.Application.Abstractions.Sync;

public interface ICloudSyncTransport
{
    Task<IReadOnlyList<SyncPushAcknowledgement>> PushAsync(
        IReadOnlyList<SyncPushEnvelope> items,
        CancellationToken cancellationToken = default);

    Task<SyncPullBatch> PullAsync(
        SyncPullRequest request,
        CancellationToken cancellationToken = default);
}
