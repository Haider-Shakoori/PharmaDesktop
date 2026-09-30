using BusinessOS.Pharmacy.Application.Abstractions.Sync;
using BusinessOS.Pharmacy.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace BusinessOS.Pharmacy.Persistence;

public sealed class SqliteSyncEntityMapStore : ISyncEntityMapStore
{
    private readonly IDbContextFactory<PharmacyDbContext> _contextFactory;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public SqliteSyncEntityMapStore(
        IDbContextFactory<PharmacyDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<SyncEntityMap?> FindByLocalIdAsync(
        string stream,
        string localEntityId,
        CancellationToken cancellationToken = default)
    {
        ValidateKey(stream, localEntityId);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var streamKey = stream.Trim();
        var localKey = localEntityId.Trim();

        var entity = await context.Set<SyncEntityMapEntity>()
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.Stream == streamKey && x.LocalEntityId == localKey,
                cancellationToken);

        return entity is null ? null : ToContract(entity);
    }

    public async Task<SyncEntityMap?> FindByCloudIdAsync(
        string stream,
        string cloudEntityId,
        CancellationToken cancellationToken = default)
    {
        ValidateKey(stream, cloudEntityId);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var streamKey = stream.Trim();
        var cloudKey = cloudEntityId.Trim();

        var entity = await context.Set<SyncEntityMapEntity>()
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.Stream == streamKey && x.CloudEntityId == cloudKey,
                cancellationToken);

        return entity is null ? null : ToContract(entity);
    }

    public async Task UpsertAsync(
        SyncEntityMap mapping,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        ValidateKey(mapping.Stream, mapping.LocalEntityId);
        ValidateKey(mapping.Stream, mapping.CloudEntityId);

        if (mapping.CloudVersion?.Trim().Length > 191)
        {
            throw new ArgumentOutOfRangeException(nameof(mapping.CloudVersion));
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            var stream = mapping.Stream.Trim();
            var localId = mapping.LocalEntityId.Trim();
            var cloudId = mapping.CloudEntityId.Trim();

            var conflicting = await context.Set<SyncEntityMapEntity>()
                .AsNoTracking()
                .AnyAsync(
                    x => x.Stream == stream &&
                         x.CloudEntityId == cloudId &&
                         x.LocalEntityId != localId,
                    cancellationToken);

            if (conflicting)
            {
                throw new InvalidOperationException(
                    "The cloud entity is already mapped to another local record.");
            }

            var entity = await context.Set<SyncEntityMapEntity>()
                .SingleOrDefaultAsync(
                    x => x.Stream == stream && x.LocalEntityId == localId,
                    cancellationToken);

            if (entity is null)
            {
                context.Add(new SyncEntityMapEntity
                {
                    Stream = stream,
                    LocalEntityId = localId,
                    CloudEntityId = cloudId,
                    CloudVersion = NullIfWhiteSpace(mapping.CloudVersion),
                    UpdatedAt = mapping.UpdatedAt,
                });
            }
            else
            {
                entity.CloudEntityId = cloudId;
                entity.CloudVersion = NullIfWhiteSpace(mapping.CloudVersion);
                entity.UpdatedAt = mapping.UpdatedAt;
            }

            await context.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static SyncEntityMap ToContract(SyncEntityMapEntity entity) => new(
        entity.Stream,
        entity.LocalEntityId,
        entity.CloudEntityId,
        entity.CloudVersion,
        entity.UpdatedAt);

    private static void ValidateKey(string stream, string entityId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stream);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityId);

        if (stream.Trim().Length > 80)
        {
            throw new ArgumentOutOfRangeException(nameof(stream));
        }

        if (entityId.Trim().Length > 80)
        {
            throw new ArgumentOutOfRangeException(nameof(entityId));
        }
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
