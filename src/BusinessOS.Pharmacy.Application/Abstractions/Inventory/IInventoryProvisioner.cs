namespace BusinessOS.Pharmacy.Application.Abstractions.Inventory;

public interface IInventoryProvisioner
{
    Task<StockLocationReference> EnsureDefaultsAsync(
        CancellationToken cancellationToken = default);
}
