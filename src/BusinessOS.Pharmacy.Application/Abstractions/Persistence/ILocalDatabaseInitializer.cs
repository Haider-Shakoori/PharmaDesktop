namespace BusinessOS.Pharmacy.Application.Abstractions.Persistence;

public interface ILocalDatabaseInitializer
{
    Task<LocalDatabaseIdentity> InitializeAsync(
        string tenantId,
        CancellationToken cancellationToken = default);
}

public sealed record LocalDatabaseIdentity(
    Guid DatabaseInstanceId,
    string TenantId,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastOpenedAt);
