namespace BusinessOS.Pharmacy.Application.Abstractions.Sync;

/// <summary>
/// A best-effort hint that a local transaction has committed new cloud outbox data.
/// Durable events remain in SQLite; notifications are coalesced and are not the queue.
/// </summary>
public interface ICloudSyncChangeSignal
{
    void Notify();
    Task<bool> WaitAsync(TimeSpan maximumWait, CancellationToken cancellationToken = default);
}
