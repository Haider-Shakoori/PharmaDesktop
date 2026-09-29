namespace BusinessOS.Pharmacy.Application.Abstractions.Medicines;

public interface IMedicineSeedService
{
    Task<int> SeedDefaultsOnceAsync(CancellationToken cancellationToken = default);
}
