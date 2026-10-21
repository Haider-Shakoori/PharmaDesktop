using BusinessOS.Pharmacy.Application.Abstractions.Sync;
using Microsoft.Extensions.Hosting;

namespace BusinessOS.Pharmacy.Sync;

public sealed class CloudSyncWorker(
    ICloudSyncService sync,
    CloudSyncOptions options,
    ICloudSyncChangeSignal changes) : IHostedService, IDisposable
{
    private CancellationTokenSource? _stop;
    private Task? _loop;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _stop = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        _loop = RunAsync(_stop.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_stop is null || _loop is null)
            return;

        _stop.Cancel();

        try
        {
            await _loop.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    _ = await sync.SyncOnceAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                    when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch
                {
                    // A failed cloud request must not interrupt offline operations.
                }

                // Wake as soon as a committed medicine/customer/sale generates
                // a local event. Poll on the normal interval as a safety net.
                var notified = await changes.WaitAsync(
                    TimeSpan.FromSeconds(options.IntervalSeconds),
                    cancellationToken);
                if (notified)
                    await Task.Delay(TimeSpan.FromMilliseconds(350), cancellationToken);
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    public void Dispose()
    {
        _stop?.Dispose();
    }
}
