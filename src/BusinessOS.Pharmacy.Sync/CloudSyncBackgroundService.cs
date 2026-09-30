using BusinessOS.Pharmacy.Application.Abstractions.Sync;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BusinessOS.Pharmacy.Sync;

public sealed class CloudSyncBackgroundService : BackgroundService
{
    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan NormalDelay = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan MoreWorkDelay = TimeSpan.FromSeconds(2);

    private readonly ISyncEngine _engine;
    private readonly ILogger<CloudSyncBackgroundService> _logger;

    public CloudSyncBackgroundService(
        ISyncEngine engine,
        ILogger<CloudSyncBackgroundService> logger)
    {
        _engine = engine;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await DelayAsync(InitialDelay, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan nextDelay;

            try
            {
                var result = await _engine.RunOnceAsync(
                    cancellationToken: stoppingToken);

                nextDelay = result.HasMore
                    ? MoreWorkDelay
                    : NormalDelay;

                if (result.Conflicts > 0 || result.Failed > 0)
                {
                    _logger.LogWarning(
                        "Cloud synchronization completed with {ConflictCount} conflicts and {FailedCount} failed items.",
                        result.Conflicts,
                        result.Failed);
                }
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    exception,
                    "Cloud synchronization is temporarily unavailable. Local pharmacy work remains queued.");
                nextDelay = NormalDelay;
            }

            await DelayAsync(nextDelay, stoppingToken);
        }
    }

    private static async Task DelayAsync(
        TimeSpan delay,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
    }
}
