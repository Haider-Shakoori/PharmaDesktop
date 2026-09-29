using BusinessOS.Pharmacy.Application.Abstractions.Authentication;

namespace BusinessOS.Pharmacy.Licensing;

public sealed class PermissionAuthorizer : IPermissionAuthorizer
{
    private readonly IUserSessionService _sessions;

    public PermissionAuthorizer(IUserSessionService sessions) => _sessions = sessions;

    public bool HasPermission(string permission) =>
        _sessions.Current?.HasPermission(permission) == true;

    public void Demand(string permission)
    {
        if (!HasPermission(permission))
        {
            throw new PermissionDeniedException(permission);
        }
    }
}
