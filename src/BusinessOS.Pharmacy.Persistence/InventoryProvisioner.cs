using BusinessOS.Pharmacy.Application.Abstractions.Inventory;
using BusinessOS.Pharmacy.Application.Abstractions.Time;
using BusinessOS.Pharmacy.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace BusinessOS.Pharmacy.Persistence;

public sealed class InventoryProvisioner : IInventoryProvisioner
{
    private readonly IDbContextFactory<PharmacyDbContext> _contextFactory;
    private readonly IClock _clock;

    public InventoryProvisioner(
        IDbContextFactory<PharmacyDbContext> contextFactory,
        IClock clock)
    {
        _contextFactory = contextFactory;
        _clock = clock;
    }

    public async Task<StockLocationReference> EnsureDefaultsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var now = _clock.UtcNow;

        var branch = await context.Set<BranchEntity>()
            .SingleOrDefaultAsync(x => x.Code == "MAIN", cancellationToken);

        if (branch is null)
        {
            branch = new BranchEntity
            {
                Id = Guid.CreateVersion7().ToString(),
                Code = "MAIN",
                Name = "Main Branch",
                IsDefault = true,
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now,
            };
            context.Add(branch);
            await context.SaveChangesAsync(cancellationToken);
        }

        var location = await context.Set<StockLocationEntity>()
            .SingleOrDefaultAsync(
                x => x.BranchId == branch.Id && x.Code == "MAIN",
                cancellationToken);

        if (location is null)
        {
            location = new StockLocationEntity
            {
                Id = Guid.CreateVersion7().ToString(),
                BranchId = branch.Id,
                Code = "MAIN",
                Name = "Main Stock",
                Kind = "store",
                IsDefault = true,
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now,
            };
            context.Add(location);
            await context.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        return new StockLocationReference(
            location.Id,
            branch.Id,
            branch.Name,
            location.Code,
            location.Name,
            location.Kind,
            location.IsDefault,
            location.IsActive);
    }
}
