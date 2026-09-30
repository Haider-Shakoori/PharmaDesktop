namespace BusinessOS.Pharmacy.Application.Abstractions.Purchasing;

public interface IPurchasingService
{
    Task<PurchaseReferenceData> GetReferenceDataAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PurchaseOrderListItem>> SearchOrdersAsync(
        PurchaseOrderSearchFilter filter,
        CancellationToken cancellationToken = default);

    Task<PurchaseOrderDetail?> GetOrderAsync(
        string id,
        CancellationToken cancellationToken = default);

    Task<string> CreateOrderAsync(
        CreatePurchaseOrderRequest request,
        CancellationToken cancellationToken = default);

    Task SubmitOrderAsync(
        string orderId,
        CancellationToken cancellationToken = default);

    Task ApproveOrderAsync(
        string orderId,
        CancellationToken cancellationToken = default);

    Task CancelOrderAsync(
        string orderId,
        CancellationToken cancellationToken = default);

    Task<string> CaptureGoodsReceiptAsync(
        string orderId,
        CaptureGoodsReceiptRequest request,
        CancellationToken cancellationToken = default);

    Task PostGoodsReceiptAsync(
        string receiptId,
        string stockLocationId,
        CancellationToken cancellationToken = default);

    Task<string> CreateInvoiceAsync(
        string orderId,
        CreatePurchaseInvoiceRequest request,
        CancellationToken cancellationToken = default);

    Task<string> RecordSupplierPaymentAsync(
        string invoiceId,
        RecordSupplierPaymentRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record PurchaseOrderSearchFilter(
    string? Search = null,
    string? Status = null,
    int Take = 250);

public sealed record PurchaseReferenceData(
    IReadOnlyList<PurchaseSupplierReferenceItem> Suppliers,
    IReadOnlyList<PurchaseMedicineReferenceItem> Medicines,
    IReadOnlyList<PurchaseStockLocationReferenceItem> StockLocations);

public sealed record PurchaseSupplierReferenceItem(
    string Id,
    string Code,
    string Name,
    int PaymentTermsDays);

public sealed record PurchaseMedicineReferenceItem(
    string Id,
    string MedicineCode,
    string BrandName,
    string? GenericName,
    string? Strength,
    string PurchaseUnit,
    bool BatchTrackingRequired,
    bool ExpiryTrackingRequired);

public sealed record PurchaseStockLocationReferenceItem(
    string Id,
    string BranchName,
    string Name,
    bool IsDefault);

public sealed record PurchaseOrderListItem(
    string Id,
    string Number,
    string SupplierId,
    string SupplierName,
    string Status,
    DateOnly OrderDate,
    DateOnly? ExpectedDate,
    string Currency,
    decimal Subtotal,
    decimal DiscountTotal,
    decimal LandedCostTotal,
    decimal GrandTotal);

public sealed record PurchaseOrderDetail(
    PurchaseOrderListItem Order,
    string? Notes,
    string CreatedBy,
    string? ApprovedBy,
    DateTimeOffset? SubmittedAt,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset? CancelledAt,
    IReadOnlyList<PurchaseOrderLineItem> Lines,
    IReadOnlyList<GoodsReceiptItem> Receipts,
    IReadOnlyList<PurchaseInvoiceItem> Invoices);

public sealed record PurchaseOrderLineItem(
    string Id,
    string MedicineId,
    string MedicineCode,
    string Description,
    decimal OrderedQuantity,
    decimal ReceivedQuantity,
    decimal UnitCost,
    decimal DiscountAmount,
    decimal LandedCostAllocated,
    decimal LineTotal,
    bool BatchTrackingRequired,
    bool ExpiryTrackingRequired);

public sealed record CreatePurchaseOrderRequest(
    string SupplierId,
    DateOnly OrderDate,
    DateOnly? ExpectedDate,
    string Currency,
    string? Notes,
    IReadOnlyList<CreatePurchaseOrderLineRequest> Lines);

public sealed record CreatePurchaseOrderLineRequest(
    string MedicineId,
    decimal OrderedQuantity,
    decimal UnitCost,
    decimal DiscountAmount = 0m,
    decimal LandedCostAllocated = 0m);

public sealed record CaptureGoodsReceiptRequest(
    DateTimeOffset ReceivedAt,
    string? IdempotencyKey,
    string? Notes,
    IReadOnlyList<CaptureGoodsReceiptLineRequest> Lines);

public sealed record CaptureGoodsReceiptLineRequest(
    string PurchaseOrderLineId,
    decimal ReceivedQuantity,
    decimal BonusQuantity,
    string? BatchNumber,
    DateOnly? ManufacturedAt,
    DateOnly? ExpiresAt,
    decimal UnitCost,
    decimal? SalePrice);

public sealed record GoodsReceiptItem(
    string Id,
    string ReceiptNumber,
    string Status,
    DateTimeOffset ReceivedAt,
    string? StockLocationId,
    string? StockLocationName,
    DateTimeOffset? InventoryPostedAt,
    string? Notes,
    IReadOnlyList<GoodsReceiptLineItem> Lines);

public sealed record GoodsReceiptLineItem(
    string Id,
    string PurchaseOrderLineId,
    string MedicineId,
    string MedicineName,
    decimal ReceivedQuantity,
    decimal BonusQuantity,
    string? BatchNumber,
    DateOnly? ManufacturedAt,
    DateOnly? ExpiresAt,
    decimal UnitCost,
    decimal? SalePrice);

public sealed record CreatePurchaseInvoiceRequest(
    string? SupplierInvoiceNumber,
    string? GoodsReceiptId,
    DateOnly InvoiceDate,
    DateOnly? DueDate,
    string? Notes);

public sealed record PurchaseInvoiceItem(
    string Id,
    string InvoiceNumber,
    string? SupplierInvoiceNumber,
    DateOnly InvoiceDate,
    DateOnly? DueDate,
    string Currency,
    string Status,
    decimal GrandTotal,
    decimal PaidTotal,
    decimal BalanceDue,
    string? GoodsReceiptId,
    string? Notes,
    IReadOnlyList<SupplierPaymentItem> Payments);

public sealed record RecordSupplierPaymentRequest(
    decimal Amount,
    string Currency,
    string Method,
    string? Reference,
    DateTimeOffset PaidAt,
    string? IdempotencyKey,
    string? Notes);

public sealed record SupplierPaymentItem(
    string Id,
    string PaymentNumber,
    decimal Amount,
    string Currency,
    string Method,
    string? Reference,
    DateTimeOffset PaidAt,
    string? Notes);
