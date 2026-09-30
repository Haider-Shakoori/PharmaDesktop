using System.Net.Http.Headers;
using BusinessOS.Pharmacy.Application.Abstractions.Networking;

namespace BusinessOS.Pharmacy.LocalServer.Security;

public sealed class UserSessionAuthenticationFilter(
    ILocalLanCredentialStore sessions) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var http = context.HttpContext;

        if (http.Items[LanRequestKeys.Terminal] is not RegisteredTerminal terminal)
        {
            return Results.Unauthorized();
        }

        if (!AuthenticationHeaderValue.TryParse(
                http.Request.Headers.Authorization,
                out var authorization) ||
            !string.Equals(authorization.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(authorization.Parameter))
        {
            return Results.Unauthorized();
        }

        var principal = await sessions.AuthenticateSessionAsync(
            terminal.TerminalId,
            authorization.Parameter,
            http.RequestAborted);

        if (principal is null)
        {
            return Results.Unauthorized();
        }

        http.Items[LanRequestKeys.Session] = principal;
        return await next(context);
    }
}
