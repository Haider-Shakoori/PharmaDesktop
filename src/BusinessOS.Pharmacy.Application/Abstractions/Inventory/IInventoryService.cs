namespace BusinessOS.Pharmacy.Application.Abstractions.Inventory;

public interface IInventoryService
{
    Task<IReadOnlyList<InventoryBatchListItem>> SearchAsync(
        InventorySearchFilter filter,
        CancellationToken cancellationToken = default);

    Task<InventoryBatchDetails?> GetBatchAsync(
        string productBatchId,
        CancellationToken cancellationToken = default);

    Task<InventoryReferenceData> GetReferenceDataAsync(
        CancellationToken cancellationToken = default);

    Task<string> CreateOpeningStockAsync(
        CreateOpeningStockRequest request,
        CancellationToken cancellationToken = default);

    Task<string> AdjustAsync(
        InventoryAdjustmentRequest request,
        CancellationToken cancellationToken = default);

    Task ChangeBatchStatusAsync(
        ChangeBatchStatusRequest request,
        CancellationToken cancellationToken = default);

    Task<string> CreateLocationAsync(
        CreateStockLocationRequest request,
        CancellationToken cancellationToken = default);

    Task UpdatePolicyAsync(
        InventoryPolicy policy,
        CancellationToken cancellationToken = default);
}
