namespace BusinessOS.Pharmacy.Application.Abstractions.Sync;

public interface ISyncEngine
{
    Task<SyncRunResult> RunOnceAsync(
        int pushBatchSize = 100,
        int pullBatchSize = 200,
        CancellationToken cancellationToken = default);
}
