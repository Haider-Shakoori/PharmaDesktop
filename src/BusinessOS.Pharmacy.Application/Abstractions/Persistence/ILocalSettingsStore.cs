namespace BusinessOS.Pharmacy.Application.Abstractions.Persistence;

public interface ILocalSettingsStore
{
    Task<string?> GetAsync(string key, CancellationToken cancellationToken = default);

    Task SetAsync(
        string key,
        string? value,
        CancellationToken cancellationToken = default);

    Task RemoveAsync(string key, CancellationToken cancellationToken = default);
}
