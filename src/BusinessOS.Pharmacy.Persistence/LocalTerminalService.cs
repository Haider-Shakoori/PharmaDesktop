using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BusinessOS.Pharmacy.Application.Abstractions.Networking;
using BusinessOS.Pharmacy.Application.Abstractions.Time;
using BusinessOS.Pharmacy.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace BusinessOS.Pharmacy.Persistence;

public sealed class LocalTerminalService : ILocalTerminalService
{
    private const int PairingCodeDigits = 6;
    private const int TerminalSecretBytes = 32;
    private const int PairingSaltBytes = 16;
    private const int MaxPairingAttempts = 5;

    private readonly IDbContextFactory<PharmacyDbContext> _contextFactory;
    private readonly IClock _clock;

    public LocalTerminalService(
        IDbContextFactory<PharmacyDbContext> contextFactory,
        IClock clock)
    {
        _contextFactory = contextFactory;
        _clock = clock;
    }

    public async Task<LocalServerIdentity> GetOrCreateServerIdentityAsync(
        string tenantId,
        string serverName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(serverName);

        tenantId = tenantId.Trim();
        serverName = Normalize(serverName, 160, nameof(serverName));

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var entity = await context.Set<LocalServerIdentityEntity>()
            .SingleOrDefaultAsync(x => x.Id == 1, cancellationToken);

        var now = _clock.UtcNow;

        if (entity is null)
        {
            entity = new LocalServerIdentityEntity
            {
                Id = 1,
                ServerId = Guid.CreateVersion7().ToString(),
                TenantId = tenantId,
                ServerName = serverName,
                CreatedAt = now,
                UpdatedAt = now,
            };

            context.Add(entity);
            await AddAuditAsync(
                context,
                "server.identity.created",
                "success",
                null,
                null,
                entity.ServerId,
                null,
                $"Server identity created for tenant {tenantId}.",
                cancellationToken);
        }
        else
        {
            if (!string.Equals(entity.TenantId, tenantId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The local server identity is already bound to another pharmacy tenant.");
            }

            if (!string.Equals(entity.ServerName, serverName, StringComparison.Ordinal))
            {
                entity.ServerName = serverName;
                entity.UpdatedAt = now;
            }
        }

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return ToModel(entity);
    }

    public async Task<PairingCodeIssue> CreatePairingCodeAsync(
        TimeSpan lifetime,
        CancellationToken cancellationToken = default)
    {
        if (lifetime < TimeSpan.FromMinutes(1) || lifetime > TimeSpan.FromMinutes(30))
        {
            throw new ArgumentOutOfRangeException(
                nameof(lifetime),
                "Pairing code lifetime must be between 1 and 30 minutes.");
        }

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var identityExists = await context.Set<LocalServerIdentityEntity>()
            .AnyAsync(x => x.Id == 1, cancellationToken);

        if (!identityExists)
        {
            throw new InvalidOperationException(
                "The Main Pharmacy Server identity must be initialized before pairing terminals.");
        }

        var now = _clock.UtcNow;
        var pairingId = Guid.CreateVersion7().ToString();
        var code = RandomNumberGenerator
            .GetInt32(0, (int)Math.Pow(10, PairingCodeDigits))
            .ToString($"D{PairingCodeDigits}", CultureInfo.InvariantCulture);
        var salt = RandomNumberGenerator.GetBytes(PairingSaltBytes);
        var hash = HashPairingCode(pairingId, code, salt);

        context.Add(new TerminalPairingCodeEntity
        {
            Id = pairingId,
            SaltBase64 = Convert.ToBase64String(salt),
            CodeHashBase64 = Convert.ToBase64String(hash),
            CreatedAt = now,
            ExpiresAt = now.Add(lifetime),
            FailedAttempts = 0,
            MaxAttempts = MaxPairingAttempts,
        });

        await AddAuditAsync(
            context,
            "terminal.pairing_code.created",
            "success",
            null,
            null,
            pairingId,
            null,
            $"Pairing code issued; expires at {now.Add(lifetime):O}.",
            cancellationToken);

        await context.SaveChangesAsync(cancellationToken);

        CryptographicOperations.ZeroMemory(salt);
        CryptographicOperations.ZeroMemory(hash);

        return new PairingCodeIssue(pairingId, code, now.Add(lifetime));
    }

    public async Task<PairTerminalResult> PairAsync(
        PairTerminalRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var terminalId = NormalizeTerminalId(request.TerminalId);
        var name = Normalize(request.Name, 160, nameof(request.Name));
        var computerName = Normalize(request.ComputerName, 160, nameof(request.ComputerName));
        var terminalRole = Normalize(request.TerminalRole, 80, nameof(request.TerminalRole));
        var pairingCode = NormalizePairingCode(request.PairingCode);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var now = _clock.UtcNow;
        var candidates = await context.Set<TerminalPairingCodeEntity>()
            .Where(x =>
                x.UsedAt == null &&
                x.ExpiresAt >= now &&
                x.FailedAttempts < x.MaxAttempts)
            .OrderByDescending(x => x.CreatedAt)
            .Take(25)
            .ToListAsync(cancellationToken);

        TerminalPairingCodeEntity? matched = null;

        foreach (var candidate in candidates)
        {
            var salt = Convert.FromBase64String(candidate.SaltBase64);
            var expected = Convert.FromBase64String(candidate.CodeHashBase64);
            var actual = HashPairingCode(candidate.Id, pairingCode, salt);

            var matches = CryptographicOperations.FixedTimeEquals(actual, expected);

            CryptographicOperations.ZeroMemory(salt);
            CryptographicOperations.ZeroMemory(expected);
            CryptographicOperations.ZeroMemory(actual);

            if (matches)
            {
                matched = candidate;
                break;
            }
        }

        if (matched is null)
        {
            var newest = candidates.FirstOrDefault();
            if (newest is not null)
            {
                newest.FailedAttempts++;
            }

            await AddAuditAsync(
                context,
                "terminal.pairing",
                "rejected",
                terminalId,
                null,
                null,
                null,
                "Invalid, expired or already-used pairing code.",
                cancellationToken);

            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            throw new InvalidOperationException(
                "The pairing code is invalid, expired, already used or locked.");
        }

        if (await context.Set<RegisteredTerminalEntity>()
            .AnyAsync(x => x.Id == terminalId && x.IsActive, cancellationToken))
        {
            throw new InvalidOperationException(
                "This terminal is already registered and active.");
        }

        var identity = await context.Set<LocalServerIdentityEntity>()
            .SingleAsync(x => x.Id == 1, cancellationToken);

        var terminalSecretBytes = RandomNumberGenerator.GetBytes(TerminalSecretBytes);
        var terminalSecret = Base64UrlEncode(terminalSecretBytes);
        var secretHashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(terminalSecret));
        var secretHash = Convert.ToHexString(secretHashBytes);

        var existing = await context.Set<RegisteredTerminalEntity>()
            .SingleOrDefaultAsync(x => x.Id == terminalId, cancellationToken);

        if (existing is null)
        {
            existing = new RegisteredTerminalEntity
            {
                Id = terminalId,
                RegisteredAt = now,
            };
            context.Add(existing);
        }

        existing.Name = name;
        existing.ComputerName = computerName;
        existing.TerminalRole = terminalRole;
        existing.SecretHash = secretHash;
        existing.AllowedPermissionsJson = "[]";
        existing.IsActive = true;
        existing.RevokedAt = null;
        existing.LastSeenAt = now;

        matched.UsedAt = now;

        await AddAuditAsync(
            context,
            "terminal.pairing",
            "success",
            terminalId,
            null,
            matched.Id,
            null,
            $"Registered {computerName} as {terminalRole}.",
            cancellationToken);

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        CryptographicOperations.ZeroMemory(terminalSecretBytes);
        CryptographicOperations.ZeroMemory(secretHashBytes);

        return new PairTerminalResult(
            terminalId,
            terminalSecret,
            identity.ServerId,
            identity.TenantId,
            existing.RegisteredAt);
    }

    public async Task<RegisteredTerminal?> AuthenticateTerminalAsync(
        string terminalId,
        string terminalSecret,
        CancellationToken cancellationToken = default)
    {
        terminalId = NormalizeTerminalId(terminalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(terminalSecret);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.Set<RegisteredTerminalEntity>()
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == terminalId, cancellationToken);

        if (entity is null || !entity.IsActive || entity.RevokedAt is not null)
        {
            return null;
        }

        var actualHash = SHA256.HashData(Encoding.UTF8.GetBytes(terminalSecret));
        var expectedHash = Convert.FromHexString(entity.SecretHash);

        try
        {
            if (!CryptographicOperations.FixedTimeEquals(actualHash, expectedHash))
            {
                return null;
            }
        }
        catch (FormatException)
        {
            return null;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(actualHash);
            CryptographicOperations.ZeroMemory(expectedHash);
        }

        return ToModel(entity);
    }

    public async Task<IReadOnlyList<RegisteredTerminal>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entities = await context.Set<RegisteredTerminalEntity>()
            .AsNoTracking()
            .OrderByDescending(x => x.IsActive)
            .ThenBy(x => x.Name)
            .ToListAsync(cancellationToken);

        return entities.Select(ToModel).ToList();
    }

    public async Task RenameAsync(
        string terminalId,
        string name,
        CancellationToken cancellationToken = default)
    {
        terminalId = NormalizeTerminalId(terminalId);
        name = Normalize(name, 160, nameof(name));

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.Set<RegisteredTerminalEntity>()
            .SingleOrDefaultAsync(x => x.Id == terminalId, cancellationToken)
            ?? throw new InvalidOperationException("Terminal was not found.");

        entity.Name = name;

        await AddAuditAsync(
            context,
            "terminal.rename",
            "success",
            terminalId,
            null,
            null,
            null,
            $"Terminal renamed to {name}.",
            cancellationToken);

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task RevokeAsync(
        string terminalId,
        CancellationToken cancellationToken = default)
    {
        terminalId = NormalizeTerminalId(terminalId);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.Set<RegisteredTerminalEntity>()
            .SingleOrDefaultAsync(x => x.Id == terminalId, cancellationToken)
            ?? throw new InvalidOperationException("Terminal was not found.");

        var now = _clock.UtcNow;
        entity.IsActive = false;
        entity.RevokedAt = now;

        await AddAuditAsync(
            context,
            "terminal.revoke",
            "success",
            terminalId,
            null,
            null,
            null,
            "Terminal access revoked.",
            cancellationToken);

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task TouchAsync(
        string terminalId,
        CancellationToken cancellationToken = default)
    {
        terminalId = NormalizeTerminalId(terminalId);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.Set<RegisteredTerminalEntity>()
            .SingleOrDefaultAsync(
                x => x.Id == terminalId && x.IsActive && x.RevokedAt == null,
                cancellationToken)
            ?? throw new InvalidOperationException("Terminal is not active.");

        entity.LastSeenAt = _clock.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
    }

    private static byte[] HashPairingCode(string pairingId, string code, byte[] salt)
    {
        var payload = Encoding.UTF8.GetBytes($"{pairingId}:{code}");
        try
        {
            using var hmac = new HMACSHA256(salt);
            return hmac.ComputeHash(payload);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }
    }

    private static RegisteredTerminal ToModel(RegisteredTerminalEntity entity)
    {
        IReadOnlySet<string> permissions;

        try
        {
            permissions = (JsonSerializer.Deserialize<string[]>(
                    entity.AllowedPermissionsJson) ?? [])
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            permissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        return new RegisteredTerminal(
            entity.Id,
            entity.Name,
            entity.ComputerName,
            entity.TerminalRole,
            entity.IsActive,
            entity.RegisteredAt,
            entity.LastSeenAt,
            entity.RevokedAt,
            permissions);
    }

    private static LocalServerIdentity ToModel(LocalServerIdentityEntity entity) =>
        new(
            entity.ServerId,
            entity.TenantId,
            entity.ServerName,
            entity.CreatedAt,
            entity.UpdatedAt);

    private static async Task AddAuditAsync(
        PharmacyDbContext context,
        string operation,
        string outcome,
        string? terminalId,
        string? userId,
        string? recordUuid,
        string? remoteAddress,
        string? detail,
        CancellationToken cancellationToken)
    {
        context.Add(new NetworkAuditEntity
        {
            OccurredAt = DateTimeOffset.UtcNow,
            Operation = operation,
            Outcome = outcome,
            TerminalId = terminalId,
            UserId = userId,
            RecordUuid = recordUuid,
            RemoteAddress = remoteAddress,
            Detail = detail,
        });

        await Task.CompletedTask;
    }

    private static string NormalizeTerminalId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        if (!Guid.TryParse(value, out var parsed))
        {
            throw new ArgumentException("Terminal ID must be a UUID.", nameof(value));
        }

        return parsed.ToString();
    }

    private static string NormalizePairingCode(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        value = value.Trim();

        if (value.Length != PairingCodeDigits || value.Any(character => !char.IsDigit(character)))
        {
            throw new ArgumentException(
                $"Pairing code must contain exactly {PairingCodeDigits} digits.",
                nameof(value));
        }

        return value;
    }

    private static string Normalize(string value, int maxLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        value = value.Trim();

        if (value.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }

        return value;
    }

    private static string Base64UrlEncode(byte[] value) =>
        Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
