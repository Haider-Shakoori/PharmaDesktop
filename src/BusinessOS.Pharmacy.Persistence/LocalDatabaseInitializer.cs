using BusinessOS.Pharmacy.Application.Abstractions.Persistence;
using BusinessOS.Pharmacy.Application.Abstractions.Time;
using BusinessOS.Pharmacy.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace BusinessOS.Pharmacy.Persistence;

public sealed class LocalDatabaseInitializer : ILocalDatabaseInitializer
{
    private readonly IDbContextFactory<PharmacyDbContext> _contextFactory;
    private readonly IClock _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public LocalDatabaseInitializer(
        IDbContextFactory<PharmacyDbContext> contextFactory,
        IClock clock)
    {
        _contextFactory = contextFactory;
        _clock = clock;
    }

    public async Task<LocalDatabaseIdentity> InitializeAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        if (tenantId.Length > 64)
        {
            throw new ArgumentOutOfRangeException(nameof(tenantId));
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            await context.Database.MigrateAsync(cancellationToken);
            await ConfigureSqliteAsync(context, cancellationToken);

            var identity = await context.Set<LocalDatabaseIdentityEntity>()
                .SingleOrDefaultAsync(cancellationToken);

            var now = _clock.UtcNow;

            if (identity is null)
            {
                identity = new LocalDatabaseIdentityEntity
                {
                    Id = 1,
                    DatabaseInstanceId = Guid.NewGuid(),
                    TenantId = tenantId,
                    CreatedAt = now,
                    LastOpenedAt = now,
                };

                context.Add(identity);
                await context.SaveChangesAsync(cancellationToken);
            }
            else
            {
                if (!string.Equals(identity.TenantId, tenantId, StringComparison.Ordinal))
                {
                    throw new LocalDatabaseTenantMismatchException(identity.TenantId, tenantId);
                }

                identity.LastOpenedAt = now;
                await context.SaveChangesAsync(cancellationToken);
            }

            return new LocalDatabaseIdentity(
                identity.DatabaseInstanceId,
                identity.TenantId,
                identity.CreatedAt,
                identity.LastOpenedAt);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static async Task ConfigureSqliteAsync(
        PharmacyDbContext context,
        CancellationToken cancellationToken)
    {
        await context.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", cancellationToken);
        await context.Database.ExecuteSqlRawAsync("PRAGMA synchronous=NORMAL;", cancellationToken);
        await context.Database.ExecuteSqlRawAsync("PRAGMA busy_timeout=5000;", cancellationToken);
        await context.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys=ON;", cancellationToken);
    }
}
