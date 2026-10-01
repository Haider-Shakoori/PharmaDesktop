using BusinessOS.Pharmacy.Application.Abstractions.Authentication;

namespace BusinessOS.Pharmacy.LocalClient;

public sealed class LanClientPermissionAuthorizer(
    IUserSessionService sessions) : IPermissionAuthorizer
{
    public bool HasPermission(string permission) =>
        sessions.Current?.HasPermission(permission) == true;

    public void Demand(string permission)
    {
        if (!HasPermission(permission))
        {
            throw new PermissionDeniedException(permission);
        }
    }
}
