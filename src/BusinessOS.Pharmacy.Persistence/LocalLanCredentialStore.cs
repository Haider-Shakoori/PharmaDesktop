using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BusinessOS.Pharmacy.Application.Abstractions.Networking;
using BusinessOS.Pharmacy.Application.Abstractions.Time;
using BusinessOS.Pharmacy.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace BusinessOS.Pharmacy.Persistence;

public sealed class LocalLanCredentialStore : ILocalLanCredentialStore
{
    private const int SessionTokenBytes = 32;

    private readonly IDbContextFactory<PharmacyDbContext> _contextFactory;
    private readonly IClock _clock;

    public LocalLanCredentialStore(
        IDbContextFactory<PharmacyDbContext> contextFactory,
        IClock clock)
    {
        _contextFactory = contextFactory;
        _clock = clock;
    }

    public async Task<CachedLanUser?> FindUserByEmailAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        email = email.Trim();

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entity = await context.Set<LocalLanUserCredentialEntity>()
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.Email == email && x.IsActive,
                cancellationToken);

        return entity is null ? null : ToUser(entity);
    }

    public async Task UpsertUserAsync(
        CachedLanUser user,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entity = await context.Set<LocalLanUserCredentialEntity>()
            .SingleOrDefaultAsync(x => x.UserId == user.UserId, cancellationToken);

        if (entity is null)
        {
            entity = new LocalLanUserCredentialEntity
            {
                UserId = user.UserId,
            };
            context.Add(entity);
        }

        entity.TenantId = user.TenantId;
        entity.Name = user.Name;
        entity.Email = user.Email.Trim();
        entity.RolesJson = SerializeSet(user.Roles);
        entity.PermissionsJson = SerializeSet(user.Permissions);
        entity.PasswordSaltBase64 = user.PasswordSaltBase64;
        entity.PasswordHashBase64 = user.PasswordHashBase64;
        entity.PasswordIterations = user.PasswordIterations;
        entity.LastOnlineVerifiedAt = user.LastOnlineVerifiedAt;
        entity.IdentityValidUntil = user.IdentityValidUntil;
        entity.UpdatedAt = _clock.UtcNow;
        entity.IsActive = user.IsActive;

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<LocalLanSessionCredential> CreateSessionAsync(
        LocalLanSessionPrincipal principal,
        TimeSpan lifetime,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(principal);

        if (lifetime <= TimeSpan.Zero || lifetime > TimeSpan.FromDays(2))
        {
            throw new ArgumentOutOfRangeException(
                nameof(lifetime),
                "LAN user sessions must be positive and cannot exceed two days.");
        }

        var now = _clock.UtcNow;
        var tokenBytes = RandomNumberGenerator.GetBytes(SessionTokenBytes);
        var token = Base64UrlEncode(tokenBytes);
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        var hash = Convert.ToHexString(hashBytes);

        try
        {
            var session = principal with
            {
                SessionId = string.IsNullOrWhiteSpace(principal.SessionId)
                    ? Guid.CreateVersion7().ToString()
                    : principal.SessionId,
                IssuedAt = now,
                ExpiresAt = now.Add(lifetime),
                LastSeenAt = now,
            };

            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            context.Add(new LocalLanSessionEntity
            {
                Id = session.SessionId,
                TerminalId = session.TerminalId,
                UserId = session.UserId,
                TenantId = session.TenantId,
                Name = session.Name,
                Email = session.Email,
                RolesJson = SerializeSet(session.Roles),
                PermissionsJson = SerializeSet(session.Permissions),
                TokenHash = hash,
                IssuedAt = session.IssuedAt,
                ExpiresAt = session.ExpiresAt,
                LastSeenAt = session.LastSeenAt,
            });

            await context.SaveChangesAsync(cancellationToken);
            return new LocalLanSessionCredential(token, session);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(tokenBytes);
            CryptographicOperations.ZeroMemory(hashBytes);
        }
    }

    public async Task<LocalLanSessionPrincipal?> AuthenticateSessionAsync(
        string terminalId,
        string sessionToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(terminalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionToken);

        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(sessionToken));
        var hash = Convert.ToHexString(hashBytes);

        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var now = _clock.UtcNow;

            var entity = await context.Set<LocalLanSessionEntity>()
                .SingleOrDefaultAsync(
                    x =>
                        x.TokenHash == hash &&
                        x.TerminalId == terminalId &&
                        x.RevokedAt == null &&
                        x.ExpiresAt > now,
                    cancellationToken);

            if (entity is null)
            {
                return null;
            }

            entity.LastSeenAt = now;
            await context.SaveChangesAsync(cancellationToken);
            return ToPrincipal(entity);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(hashBytes);
        }
    }

    public async Task RevokeSessionsForTerminalAsync(
        string terminalId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(terminalId);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var now = _clock.UtcNow;

        var sessions = await context.Set<LocalLanSessionEntity>()
            .Where(x => x.TerminalId == terminalId && x.RevokedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var session in sessions)
        {
            session.RevokedAt = now;
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    private static CachedLanUser ToUser(LocalLanUserCredentialEntity entity) =>
        new(
            entity.UserId,
            entity.TenantId,
            entity.Name,
            entity.Email,
            DeserializeSet(entity.RolesJson),
            DeserializeSet(entity.PermissionsJson),
            entity.PasswordSaltBase64,
            entity.PasswordHashBase64,
            entity.PasswordIterations,
            entity.LastOnlineVerifiedAt,
            entity.IdentityValidUntil,
            entity.IsActive);

    private static LocalLanSessionPrincipal ToPrincipal(LocalLanSessionEntity entity) =>
        new(
            entity.Id,
            entity.TerminalId,
            entity.UserId,
            entity.TenantId,
            entity.Name,
            entity.Email,
            DeserializeSet(entity.RolesJson),
            DeserializeSet(entity.PermissionsJson),
            entity.IssuedAt,
            entity.ExpiresAt,
            entity.LastSeenAt);

    private static string SerializeSet(IReadOnlySet<string> values) =>
        JsonSerializer.Serialize(
            values.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray());

    private static IReadOnlySet<string> DeserializeSet(string json)
    {
        try
        {
            return (JsonSerializer.Deserialize<string[]>(json) ?? [])
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static string Base64UrlEncode(byte[] value) =>
        Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
