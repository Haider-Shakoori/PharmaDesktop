using System.Net.Mail;
using BusinessOS.Pharmacy.Application.Abstractions.Authentication;
using BusinessOS.Pharmacy.Application.Abstractions.Customers;
using BusinessOS.Pharmacy.Application.Abstractions.Time;
using BusinessOS.Pharmacy.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace BusinessOS.Pharmacy.Persistence;

public sealed class CustomerService : ICustomerService, ICustomerCreditPolicy
{
    private readonly IDbContextFactory<PharmacyDbContext> _contextFactory;
    private readonly IPermissionAuthorizer _permissions;
    private readonly IUserSessionService _sessions;
    private readonly IClock _clock;

    public CustomerService(
        IDbContextFactory<PharmacyDbContext> contextFactory,
        IPermissionAuthorizer permissions,
        IUserSessionService sessions,
        IClock clock)
    {
        _contextFactory = contextFactory;
        _permissions = permissions;
        _sessions = sessions;
        _clock = clock;
    }

    public async Task<IReadOnlyList<CustomerListItem>> SearchAsync(
        CustomerSearchFilter filter,
        CancellationToken cancellationToken = default)
    {
        _permissions.Demand("customers.manage");

        var take = Math.Clamp(filter.Take, 1, 1000);
        var search = NormalizeOptional(filter.Search, 255, nameof(filter.Search));

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var query = context.Set<CustomerEntity>().AsNoTracking().AsQueryable();

        if (search is not null)
        {
            var pattern = $"%{search}%";
            query = query.Where(x =>
                EF.Functions.Like(x.Name, pattern) ||
                (x.Phone != null && EF.Functions.Like(x.Phone, pattern)) ||
                (x.Email != null && EF.Functions.Like(x.Email, pattern)));
        }

        if (filter.IsActive is not null)
        {
            query = query.Where(x => x.IsActive == filter.IsActive.Value);
        }

        return await query
            .OrderBy(x => x.Name)
            .Take(take)
            .Select(x => new CustomerListItem(
                x.Id,
                x.Name,
                x.Phone,
                x.Email,
                x.CreditLimit,
                x.IsActive))
            .ToListAsync(cancellationToken);
    }

    public async Task<CustomerSummary> GetSummaryAsync(
        CancellationToken cancellationToken = default)
    {
        _permissions.Demand("customers.manage");

        await using var context =
            await _contextFactory.CreateDbContextAsync(cancellationToken);

        var query = context.Set<CustomerEntity>().AsNoTracking();

        var total = await query.CountAsync(cancellationToken);
        var active = await query.CountAsync(x => x.IsActive, cancellationToken);
        var totalCreditLimit = await query
            .Select(x => (decimal?)x.CreditLimit)
            .SumAsync(cancellationToken) ?? 0m;

        return new CustomerSummary(
            total,
            active,
            total - active,
            ScaleMoney(totalCreditLimit));
    }

    public async Task<CustomerEditorModel?> GetAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        _permissions.Demand("customers.manage");
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.Set<CustomerEntity>()
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new CustomerEditorModel(
                x.Id,
                x.Name,
                x.Phone,
                x.Email,
                x.CreditLimit,
                x.IsActive,
                x.Notes))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<string> CreateAsync(
        SaveCustomerRequest request,
        CancellationToken cancellationToken = default)
    {
        _permissions.Demand("customers.manage");
        Validate(request);

        var now = _clock.UtcNow;
        var entity = new CustomerEntity
        {
            Id = Guid.CreateVersion7().ToString(),
            CreatedAt = now,
            UpdatedAt = now,
        };

        Apply(entity, request);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        context.Add(entity);
        CloudSyncOutboxWriter.QueueCustomerUpsert(
            context,
            _sessions.Current,
            entity,
            now);
        await context.SaveChangesAsync(cancellationToken);
        return entity.Id;
    }

    public async Task UpdateAsync(
        string id,
        SaveCustomerRequest request,
        CancellationToken cancellationToken = default)
    {
        _permissions.Demand("customers.manage");
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        Validate(request);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.Set<CustomerEntity>()
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new InvalidOperationException("Customer was not found.");

        Apply(entity, request);
        entity.UpdatedAt = _clock.UtcNow;
        CloudSyncOutboxWriter.QueueCustomerUpsert(
            context,
            _sessions.Current,
            entity,
            entity.UpdatedAt);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<CustomerCreditProfile> ValidateAsync(
        string customerId,
        decimal creditAmount,
        CancellationToken cancellationToken = default)
    {
        _permissions.Demand("pos.sell");
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);

        if (creditAmount <= 0m || creditAmount > 999_999_999_999m)
        {
            throw new ArgumentOutOfRangeException(nameof(creditAmount));
        }

        creditAmount = ScaleMoney(creditAmount);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var customer = await context.Set<CustomerEntity>()
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == customerId && x.IsActive, cancellationToken)
            ?? throw new InvalidOperationException("Customer was not found or is inactive.");

        if (creditAmount > customer.CreditLimit)
        {
            throw new InvalidOperationException(
                "Credit amount exceeds this customer credit limit.");
        }

        return new CustomerCreditProfile(
            customer.Id,
            customer.Name,
            customer.CreditLimit,
            creditAmount,
            ScaleMoney(customer.CreditLimit - creditAmount));
    }

    internal static void Validate(SaveCustomerRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        _ = NormalizeRequired(request.Name, 180, nameof(request.Name));
        _ = NormalizeOptional(request.Phone, 64, nameof(request.Phone));
        var email = NormalizeOptional(request.Email, 255, nameof(request.Email));
        _ = NormalizeOptional(request.Notes, 2000, nameof(request.Notes));

        if (request.CreditLimit < 0m || request.CreditLimit > 999_999_999_999m)
        {
            throw new ArgumentOutOfRangeException(nameof(request.CreditLimit));
        }

        if (email is not null && !MailAddress.TryCreate(email, out _))
        {
            throw new ArgumentException("Customer email is not valid.", nameof(request.Email));
        }
    }

    private static void Apply(CustomerEntity entity, SaveCustomerRequest request)
    {
        entity.Name = request.Name.Trim();
        entity.Phone = NormalizeOptional(request.Phone, 64, nameof(request.Phone));
        entity.Email = NormalizeOptional(request.Email, 255, nameof(request.Email));
        entity.CreditLimit = ScaleMoney(request.CreditLimit);
        entity.IsActive = request.IsActive;
        entity.Notes = NormalizeOptional(request.Notes, 2000, nameof(request.Notes));
    }

    private static decimal ScaleMoney(decimal value) =>
        decimal.Round(value, 4, MidpointRounding.AwayFromZero);

    private static string NormalizeRequired(string value, int maxLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        value = value.Trim();

        if (value.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }

        return value;
    }

    private static string? NormalizeOptional(string? value, int maxLength, string parameterName)
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
