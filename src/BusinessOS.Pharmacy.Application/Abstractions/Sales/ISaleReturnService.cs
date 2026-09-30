namespace BusinessOS.Pharmacy.Application.Abstractions.Sales;

public interface ISaleReturnService
{
    Task<IReadOnlyList<SaleListItem>> SearchReturnableSalesAsync(SaleSearchFilter filter, CancellationToken cancellationToken = default);
    Task<ReturnableSaleDetail?> GetReturnableSaleAsync(string saleId, CancellationToken cancellationToken = default);
    Task<SaleReturnDetail> ProcessAsync(ProcessSaleReturnRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SaleReturnListItem>> SearchReturnsAsync(SaleReturnSearchFilter filter, CancellationToken cancellationToken = default);
    Task<SaleReturnDetail?> GetReturnAsync(string returnId, CancellationToken cancellationToken = default);
}

public sealed record ReturnableSaleDetail(SaleListItem Sale, IReadOnlyList<ReturnableSaleLineItem> Lines);
public sealed record ReturnableSaleLineItem(string Id, string MedicineId, string Description, decimal SoldQuantity, decimal ReturnedQuantity, decimal RemainingQuantity, decimal NetUnitAmount);
public sealed record ProcessSaleReturnRequest(string SaleId, string IdempotencyKey, string Reason, IReadOnlyList<ProcessSaleReturnLineRequest> Lines, IReadOnlyList<ProcessSaleReturnRefundRequest> Refunds);
public sealed record ProcessSaleReturnLineRequest(string SaleLineId, decimal Quantity);
public sealed record ProcessSaleReturnRefundRequest(string Method, decimal Amount, string? Reference = null);
public sealed record SaleReturnSearchFilter(string? Search = null, DateOnly? From = null, DateOnly? To = null, int Take = 250);
public sealed record SaleReturnListItem(string Id, string ReturnNumber, string SaleId, string SaleNumber, DateOnly BusinessDate, string StockLocationName, string Status, decimal RefundTotal, string Reason, DateTimeOffset? CompletedAt);
public sealed record SaleReturnDetail(SaleReturnListItem Return, IReadOnlyList<SaleReturnLineItem> Lines, IReadOnlyList<SaleReturnRefundItem> Refunds);
public sealed record SaleReturnLineItem(string Id, string SaleLineId, string MedicineId, string Description, decimal Quantity, decimal RefundAmount, string Disposition, IReadOnlyList<SaleReturnAllocationItem> Allocations);
public sealed record SaleReturnAllocationItem(string Id, string SaleBatchAllocationId, string ProductBatchId, string? BatchNumber, decimal Quantity, bool Restocked, string? StockMovementId);
public sealed record SaleReturnRefundItem(string Id, string Method, decimal Amount, string Currency, string? Reference);
