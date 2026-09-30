using BusinessOS.Pharmacy.Application.Abstractions.Authentication;
using BusinessOS.Pharmacy.Application.Abstractions.Networking;
using BusinessOS.Pharmacy.Domain.Authentication;

namespace BusinessOS.Pharmacy.LocalServer.Security;

public sealed class RequestUserSessionService(IHttpContextAccessor httpContextAccessor) : IUserSessionService
{
    public UserSessionSnapshot? Current
    {
        get
        {
            var http = httpContextAccessor.HttpContext;
            if (http?.Items[LanRequestKeys.Session] is not LocalLanSessionPrincipal session)
                return null;

            return new UserSessionSnapshot(
                session.UserId,
                session.TenantId,
                session.SessionId,
                session.TerminalId,
                session.Name,
                session.Email,
                session.Roles,
                session.Permissions,
                session.IssuedAt,
                session.ExpiresAt);
        }
    }

    public Task<UserSessionSnapshot> LoginAsync(string email, string password, bool allowOfflineSignIn, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("LAN server requests authenticate through the terminal login endpoint.");

    public Task<UserSessionSnapshot> RefreshAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("LAN server request sessions are refreshed by signing in again.");

    public Task LogoutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
