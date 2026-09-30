using System.Net.Mail;
using BusinessOS.Pharmacy.Application.Abstractions.Authentication;
using BusinessOS.Pharmacy.Application.Abstractions.Purchasing;
using BusinessOS.Pharmacy.Application.Abstractions.Time;
using BusinessOS.Pharmacy.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace BusinessOS.Pharmacy.Persistence;

public sealed class SupplierService : ISupplierService
{
    private readonly IDbContextFactory<PharmacyDbContext> _contextFactory;
    private readonly IPermissionAuthorizer _permissions;
    private readonly IClock _clock;

    public SupplierService(
        IDbContextFactory<PharmacyDbContext> contextFactory,
        IPermissionAuthorizer permissions,
        IClock clock)
    {
        _contextFactory = contextFactory;
        _permissions = permissions;
        _clock = clock;
    }

    public async Task<IReadOnlyList<SupplierListItem>> SearchAsync(
        SupplierSearchFilter filter,
        CancellationToken cancellationToken = default)
    {
        _permissions.Demand("purchases.manage");

        var take = Math.Clamp(filter.Take, 1, 1000);
        var search = NormalizeOptional(filter.Search, 255, nameof(filter.Search));

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var query = context.Set<SupplierEntity>().AsNoTracking().AsQueryable();

        if (search is not null)
        {
            var pattern = $"%{search}%";
            query = query.Where(x =>
                EF.Functions.Like(x.Name, pattern) ||
                EF.Functions.Like(x.Code, pattern) ||
                (x.Phone != null && EF.Functions.Like(x.Phone, pattern)) ||
                (x.Whatsapp != null && EF.Functions.Like(x.Whatsapp, pattern)));
        }

        if (filter.IsActive is not null)
        {
            query = query.Where(x => x.IsActive == filter.IsActive.Value);
        }

        return await query
            .OrderBy(x => x.Name)
            .ThenBy(x => x.Code)
            .Take(take)
            .Select(x => new SupplierListItem(
                x.Id,
                x.Code,
                x.Name,
                x.ContactPerson,
                x.Phone,
                x.Whatsapp,
                x.Email,
                x.City,
                x.Province,
                x.PaymentTermsDays,
                x.IsActive,
                x.PurchaseOrders.Count,
                x.Invoices.Count))
            .ToListAsync(cancellationToken);
    }

    public async Task<SupplierEditorModel?> GetAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        _permissions.Demand("purchases.manage");
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.Set<SupplierEntity>()
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new SupplierEditorModel(
                x.Id,
                x.Code,
                x.Name,
                x.ContactPerson,
                x.Phone,
                x.Whatsapp,
                x.Email,
                x.Address,
                x.City,
                x.Province,
                x.PaymentTermsDays,
                x.IsActive,
                x.Notes))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<string> CreateAsync(
        SaveSupplierRequest request,
        CancellationToken cancellationToken = default)
    {
        _permissions.Demand("purchases.manage");
        Validate(request);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var code = request.Code.Trim();
        if (await context.Set<SupplierEntity>().AnyAsync(x => x.Code == code, cancellationToken))
        {
            throw new ArgumentException($"Supplier code '{code}' already exists.");
        }

        var now = _clock.UtcNow;
        var entity = new SupplierEntity
        {
            Id = Guid.CreateVersion7().ToString(),
            CreatedAt = now,
            UpdatedAt = now,
        };

        Apply(entity, request);
        context.Add(entity);
        await context.SaveChangesAsync(cancellationToken);
        return entity.Id;
    }

    public async Task UpdateAsync(
        string id,
        SaveSupplierRequest request,
        CancellationToken cancellationToken = default)
    {
        _permissions.Demand("purchases.manage");
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        Validate(request);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entity = await context.Set<SupplierEntity>()
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new InvalidOperationException("Supplier was not found.");

        var code = request.Code.Trim();
        if (await context.Set<SupplierEntity>()
            .AnyAsync(x => x.Code == code && x.Id != id, cancellationToken))
        {
            throw new ArgumentException($"Supplier code '{code}' already exists.");
        }

        Apply(entity, request);
        entity.UpdatedAt = _clock.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
    }

    private static void Apply(SupplierEntity entity, SaveSupplierRequest request)
    {
        entity.Code = request.Code.Trim();
        entity.Name = request.Name.Trim();
        entity.ContactPerson = NormalizeOptional(request.ContactPerson, 160, nameof(request.ContactPerson));
        entity.Phone = NormalizeOptional(request.Phone, 64, nameof(request.Phone));
        entity.Whatsapp = NormalizeOptional(request.Whatsapp, 64, nameof(request.Whatsapp));
        entity.Email = NormalizeOptional(request.Email, 255, nameof(request.Email));
        entity.Address = NormalizeOptional(request.Address, 255, nameof(request.Address));
        entity.City = NormalizeOptional(request.City, 100, nameof(request.City));
        entity.Province = NormalizeOptional(request.Province, 100, nameof(request.Province));
        entity.PaymentTermsDays = request.PaymentTermsDays;
        entity.IsActive = request.IsActive;
        entity.Notes = NormalizeOptional(request.Notes, 2000, nameof(request.Notes));
    }

    internal static void Validate(SaveSupplierRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        _ = NormalizeRequired(request.Code, 60, nameof(request.Code));
        _ = NormalizeRequired(request.Name, 180, nameof(request.Name));
        _ = NormalizeOptional(request.ContactPerson, 160, nameof(request.ContactPerson));
        _ = NormalizeOptional(request.Phone, 64, nameof(request.Phone));
        _ = NormalizeOptional(request.Whatsapp, 64, nameof(request.Whatsapp));
        var email = NormalizeOptional(request.Email, 255, nameof(request.Email));
        _ = NormalizeOptional(request.Address, 255, nameof(request.Address));
        _ = NormalizeOptional(request.City, 100, nameof(request.City));
        _ = NormalizeOptional(request.Province, 100, nameof(request.Province));
        _ = NormalizeOptional(request.Notes, 2000, nameof(request.Notes));

        if (request.PaymentTermsDays is < 0 or > 3650)
        {
            throw new ArgumentOutOfRangeException(nameof(request.PaymentTermsDays));
        }

        if (email is not null && !MailAddress.TryCreate(email, out _))
        {
            throw new ArgumentException("Supplier email is not valid.", nameof(request.Email));
        }
    }

    internal static string NormalizeRequired(string value, int maxLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        value = value.Trim();
        if (value.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }

        return value;
    }

    internal static string? NormalizeOptional(string? value, int maxLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        value = value.Trim();
        if (value.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }

        return value;
    }
}
