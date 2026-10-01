using BusinessOS.Pharmacy.Application.Abstractions.Sync;
using Microsoft.Extensions.Hosting;

namespace BusinessOS.Pharmacy.Sync;

public sealed class CloudSyncWorker(
    ICloudSyncService sync,
    CloudSyncOptions options) : IHostedService, IDisposable
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

            using var timer = new PeriodicTimer(
                TimeSpan.FromSeconds(options.IntervalSeconds));

            do
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
                    // Synchronization is deliberately best-effort. Operational
                    // pharmacy transactions must never fail because cloud
                    // connectivity is unavailable.
                }
            }
            while (await timer.WaitForNextTickAsync(cancellationToken));
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
