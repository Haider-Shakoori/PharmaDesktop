using BusinessOS.Pharmacy.Application.Abstractions.Licensing;
using BusinessOS.Pharmacy.Application.Abstractions.Persistence;
using BusinessOS.Pharmacy.Application.Abstractions.Networking;
using BusinessOS.Pharmacy.Persistence;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BusinessOS.Pharmacy.Desktop.Backup;

/// <summary>
/// While the main app is running, take tenant-verified SQLite snapshots without
/// interrupting POS writes. Scheduled backups never delete manual/pre-restore files.
/// </summary>
public sealed class AutomaticBackupWorker(
    ILocalSettingsStore settings,
    ILicenseService licenses,
    LocalBackupService backups,
    NetworkConfiguration network,
    ILogger<AutomaticBackupWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (network.Mode == DeploymentMode.Client)
            return; // Main-server/standalone database is authoritative.

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var policy = await AutomaticBackupPolicy.ReadAsync(settings, stoppingToken);
                    if (policy.Enabled)
                    {
                        var entitlement = await licenses.GetCachedEntitlementAsync(stoppingToken);
                        if (entitlement is not null)
                        {
                            var created = await backups.CreateAutomaticIfDueAsync(
                                entitlement.TenantId, policy.IntervalHours, policy.KeepCount,
                                stoppingToken);
                            if (created is not null)
                                logger.LogInformation("Scheduled pharmacy backup completed and verified.");
                        }
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    logger.LogWarning(exception, "Scheduled pharmacy backup failed; next check will retry.");
                }

                await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
