using BusinessOS.Pharmacy.Application.Abstractions.Networking;

namespace BusinessOS.Pharmacy.LocalServer.Security;

public sealed class TerminalAuthenticationFilter(
    ILocalTerminalService terminals,
    ILogger<TerminalAuthenticationFilter> logger) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var terminalId = http.Request.Headers["X-BusinessOS-Terminal-Id"].FirstOrDefault();
        var terminalSecret = http.Request.Headers["X-BusinessOS-Terminal-Secret"].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(terminalId) ||
            string.IsNullOrWhiteSpace(terminalSecret))
        {
            return Results.Unauthorized();
        }

        RegisteredTerminal? terminal;

        try
        {
            terminal = await terminals.AuthenticateTerminalAsync(
                terminalId,
                terminalSecret,
                http.RequestAborted);
        }
        catch (ArgumentException)
        {
            return Results.Unauthorized();
        }

        if (terminal is null)
        {
            logger.LogWarning(
                "Rejected LAN request from unregistered/revoked terminal {TerminalId}.",
                terminalId);
            return Results.Unauthorized();
        }

        http.Items[LanRequestKeys.Terminal] = terminal;

        if (terminal.LastSeenAt is null ||
            DateTimeOffset.UtcNow - terminal.LastSeenAt > TimeSpan.FromSeconds(30))
        {
            await terminals.TouchAsync(terminal.TerminalId, http.RequestAborted);
        }

        return await next(context);
    }
}
