using BusinessOS.Pharmacy.Application.Abstractions.Sync;

namespace BusinessOS.Pharmacy.Sync;

/// <summary>
/// Coalesces bursts of locally committed changes into one immediate sync wakeup.
/// Normal interval-based checks remain available when the signal is lost or offline.
/// </summary>
public sealed class CloudSyncChangeSignal : ICloudSyncChangeSignal, IDisposable
{
    private readonly SemaphoreSlim _changes = new(0, 1);

    public void Notify()
    {
        try
        {
            _changes.Release();
        }
        catch (SemaphoreFullException)
        {
            // One pending wakeup is sufficient.
        }
    }

    public Task<bool> WaitAsync(
        TimeSpan maximumWait,
        CancellationToken cancellationToken = default) =>
        _changes.WaitAsync(maximumWait, cancellationToken);

    public void Dispose() => _changes.Dispose();
}
