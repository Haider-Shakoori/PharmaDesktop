namespace BusinessOS.Pharmacy.Application.Abstractions.Networking;

public interface ILocalLanCredentialStore
{
    Task<CachedLanUser?> FindUserByEmailAsync(
        string email,
        CancellationToken cancellationToken = default);

    Task UpsertUserAsync(
        CachedLanUser user,
        CancellationToken cancellationToken = default);

    Task<LocalLanSessionCredential> CreateSessionAsync(
        LocalLanSessionPrincipal principal,
        TimeSpan lifetime,
        CancellationToken cancellationToken = default);

    Task<LocalLanSessionPrincipal?> AuthenticateSessionAsync(
        string terminalId,
        string sessionToken,
        CancellationToken cancellationToken = default);

    Task RevokeSessionsForTerminalAsync(
        string terminalId,
        CancellationToken cancellationToken = default);
}

public sealed record CachedLanUser(
    string UserId,
    string TenantId,
    string Name,
    string Email,
    IReadOnlySet<string> Roles,
    IReadOnlySet<string> Permissions,
    string PasswordSaltBase64,
    string PasswordHashBase64,
    int PasswordIterations,
    DateTimeOffset LastOnlineVerifiedAt,
    DateTimeOffset IdentityValidUntil,
    bool IsActive);

public sealed record LocalLanSessionPrincipal(
    string SessionId,
    string TerminalId,
    string UserId,
    string TenantId,
    string Name,
    string Email,
    IReadOnlySet<string> Roles,
    IReadOnlySet<string> Permissions,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? LastSeenAt)
{
    public bool HasPermission(string permission) =>
        !string.IsNullOrWhiteSpace(permission) &&
        Permissions.Contains(permission);
}

public sealed record LocalLanSessionCredential(
    string SessionToken,
    LocalLanSessionPrincipal Principal);
