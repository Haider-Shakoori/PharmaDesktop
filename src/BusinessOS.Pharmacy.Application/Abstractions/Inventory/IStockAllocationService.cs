namespace BusinessOS.Pharmacy.Application.Abstractions.Inventory;

public interface IStockAllocationService
{
    Task<IReadOnlyList<StockAllocationItem>> ConsumeFefoAsync(
        StockAllocationRequest request,
        CancellationToken cancellationToken = default);
}
