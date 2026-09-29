using BusinessOS.Pharmacy.Application.Abstractions.Persistence;
using BusinessOS.Pharmacy.Application.Abstractions.Time;
using BusinessOS.Pharmacy.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace BusinessOS.Pharmacy.Persistence;

public sealed class LocalSettingsStore : ILocalSettingsStore
{
    private static readonly string[] ForbiddenKeyFragments =
    [
        "password",
        "access_token",
        "refresh_token",
        "license_key",
        "private_key",
        "secret",
        "credential",
    ];

    private readonly IDbContextFactory<PharmacyDbContext> _contextFactory;
    private readonly IClock _clock;

    public LocalSettingsStore(
        IDbContextFactory<PharmacyDbContext> contextFactory,
        IClock clock)
    {
        _contextFactory = contextFactory;
        _clock = clock;
    }

    public async Task<string?> GetAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        ValidateKey(key);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Set<LocalSettingEntity>()
            .Where(x => x.Key == key)
            .Select(x => x.Value)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task SetAsync(
        string key,
        string? value,
        CancellationToken cancellationToken = default)
    {
        ValidateKey(key);

        if (value?.Length > 4000)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var setting = await context.Set<LocalSettingEntity>()
            .SingleOrDefaultAsync(x => x.Key == key, cancellationToken);

        if (setting is null)
        {
            context.Add(new LocalSettingEntity
            {
                Key = key,
                Value = value,
                UpdatedAt = _clock.UtcNow,
            });
        }
        else
        {
            setting.Value = value;
            setting.UpdatedAt = _clock.UtcNow;
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        ValidateKey(key);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await context.Set<LocalSettingEntity>()
            .Where(x => x.Key == key)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private static void ValidateKey(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (key.Length > 160)
        {
            throw new ArgumentOutOfRangeException(nameof(key));
        }

        if (ForbiddenKeyFragments.Any(fragment =>
                key.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException(
                "Protected authentication or licensing secrets must not be stored in the local settings table.",
                nameof(key));
        }
    }
}
