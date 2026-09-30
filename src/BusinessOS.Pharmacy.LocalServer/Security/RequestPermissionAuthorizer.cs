using BusinessOS.Pharmacy.Application.Abstractions.Authentication;
using BusinessOS.Pharmacy.Application.Abstractions.Networking;

namespace BusinessOS.Pharmacy.LocalServer.Security;

public sealed class RequestPermissionAuthorizer(
    IHttpContextAccessor httpContextAccessor) : IPermissionAuthorizer
{
    public bool HasPermission(string permission)
    {
        if (string.IsNullOrWhiteSpace(permission))
        {
            return false;
        }

        var http = httpContextAccessor.HttpContext;
        if (http?.Items[LanRequestKeys.Session] is not LocalLanSessionPrincipal session ||
            http.Items[LanRequestKeys.Terminal] is not RegisteredTerminal terminal)
        {
            return false;
        }

        if (!session.HasPermission(permission))
        {
            return false;
        }

        return terminal.AllowedPermissions.Count == 0 ||
               terminal.AllowedPermissions.Contains(permission);
    }

    public void Demand(string permission)
    {
        if (!HasPermission(permission))
        {
            throw new PermissionDeniedException(permission);
        }
    }
}
