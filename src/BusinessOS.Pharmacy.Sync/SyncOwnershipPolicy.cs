using BusinessOS.Pharmacy.Application.Abstractions.Networking;

namespace BusinessOS.Pharmacy.Sync;

public static class SyncOwnershipPolicy
{
    public static bool CanOwnCloudSync(DeploymentMode mode) =>
        mode is DeploymentMode.Standalone or DeploymentMode.Server;
}
