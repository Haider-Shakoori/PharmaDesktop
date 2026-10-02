namespace BusinessOS.Pharmacy.Application.Abstractions.Sales;

public interface IPosService
{
    Task<PosReferenceData> GetReferenceDataAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PosProductSearchItem>> SearchProductsAsync(
        PosProductSearchFilter filter,
        CancellationToken cancellationToken = default);

    Task<SaleDetail> CheckoutAsync(
        PosCheckoutRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SaleListItem>> SearchSalesAsync(
        SaleSearchFilter filter,
        CancellationToken cancellationToken = default);

    Task<SaleDetail?> GetSaleAsync(
        string id,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PosTopProductItem>> GetTopProductsAsync(
        string stockLocationId,
        int take = 10,
        int days = 30,
        CancellationToken cancellationToken = default);
}

public sealed record PosTopProductItem(
    string MedicineId,
    string MedicineCode,
    string BrandName,
    string? Strength,
    string SaleUnit,
    decimal QuantitySold,
    decimal LastUnitPrice);

public sealed record PosReferenceData(
    IReadOnlyList<PosStockLocationItem> StockLocations,
    IReadOnlyList<PosCustomerItem> Customers);

public sealed record PosStockLocationItem(
    string Id,
    string BranchName,
    string Name,
    bool IsDefault);

public sealed record PosCustomerItem(
    string Id,
    string Name,
    string? Phone,
    decimal CreditLimit);

public sealed record PosProductSearchFilter(
    string Query,
    string StockLocationId,
    int Take = 20);

public sealed record PosProductSearchItem(
    string Id,
    string MedicineCode,
    string? Barcode,
    string BrandName,
    string? GenericName,
    string? Strength,
    string SaleUnit,
    decimal AvailableQuantity,
    decimal? FefoPrice,
    decimal? MinPrice,
    decimal? MaxPrice,
    bool PrescriptionRequired,
    IReadOnlyList<PosBatchPriceItem> Batches);

public sealed record PosBatchPriceItem(
    string Id,
    string? BatchNumber,
    decimal AvailableQuantity,
    decimal SalePrice,
    DateOnly? ExpiresAt);

public sealed record PosCheckoutRequest(
    string StockLocationId,
    string? CustomerId,
    string IdempotencyKey,
    string? Notes,
    string? PrescriptionReference,
    string? PrescriberName,
    DateOnly? PrescriptionDate,
    IReadOnlyList<PosCheckoutLineRequest> Lines,
    IReadOnlyList<PosPaymentRequest> Payments);

public sealed record PosCheckoutLineRequest(
    string MedicineId,
    decimal Quantity,
    decimal? UnitPrice = null,
    bool OverridePrice = false,
    decimal DiscountAmount = 0m);

public sealed record PosPaymentRequest(
    string Method,
    decimal Amount,
    string? Reference = null);

public sealed record SaleSearchFilter(
    string? Search = null,
    DateOnly? From = null,
    DateOnly? To = null,
    string? PaymentStatus = null,
    int Take = 250);

public sealed record SaleListItem(
    string Id,
    string SaleNumber,
    DateOnly BusinessDate,
    DateTimeOffset? CompletedAt,
    string? CustomerName,
    string StockLocationName,
    string PaymentStatus,
    decimal GrandTotal,
    decimal PaidTotal,
    decimal DueTotal,
    decimal ChangeTotal);

public sealed record SaleDetail(
    SaleListItem Sale,
    string Currency,
    decimal Subtotal,
    decimal DiscountTotal,
    decimal TaxTotal,
    string CashierName,
    string? CustomerPhone,
    string? PrescriptionReference,
    string? PrescriberName,
    DateOnly? PrescriptionDate,
    string? Notes,
    IReadOnlyList<SaleLineItem> Lines,
    IReadOnlyList<SalePaymentItem> Payments);

public sealed record SaleLineItem(
    string Id,
    string MedicineId,
    string Description,
    string SaleUnit,
    decimal Quantity,
    decimal UnitPrice,
    decimal DiscountAmount,
    decimal TaxAmount,
    decimal LineTotal,
    decimal CostTotal,
    bool PrescriptionRequired,
    IReadOnlyList<SaleBatchAllocationItem> Allocations);

public sealed record SaleBatchAllocationItem(
    string Id,
    string ProductBatchId,
    string? BatchNumber,
    DateOnly? ExpiresAt,
    decimal Quantity,
    decimal UnitCost,
    decimal UnitPrice,
    decimal LineTotal);

public sealed record SalePaymentItem(
    string Id,
    string PaymentNumber,
    string Method,
    decimal Amount,
    string Currency,
    string? Reference,
    DateTimeOffset PaidAt);
