using BusinessOS.Pharmacy.Application.Abstractions.Accounting;
using BusinessOS.Pharmacy.Application.Abstractions.Customers;
using BusinessOS.Pharmacy.Application.Abstractions.DailyClosing;
using BusinessOS.Pharmacy.Application.Abstractions.Inventory;
using BusinessOS.Pharmacy.Application.Abstractions.Purchasing;
using BusinessOS.Pharmacy.Application.Abstractions.Reports;
using BusinessOS.Pharmacy.Application.Abstractions.Sales;

namespace BusinessOS.Pharmacy.LocalClient;

public sealed class LanCustomerService(LanApiClient api) : ICustomerService
{
    public async Task<IReadOnlyList<CustomerListItem>> SearchAsync(CustomerSearchFilter filter, CancellationToken ct = default) =>
        await api.PostAsync<CustomerSearchFilter, List<CustomerListItem>>("customers/search", filter, ct);
    public Task<CustomerEditorModel?> GetAsync(string id, CancellationToken ct = default) =>
        api.GetOptionalAsync<CustomerEditorModel>($"customers/{E(id)}", ct);
    public async Task<string> CreateAsync(SaveCustomerRequest request, CancellationToken ct = default) =>
        (await api.PostAsync<SaveCustomerRequest, IdResponse>("customers", request, ct)).Id;
    public Task UpdateAsync(string id, SaveCustomerRequest request, CancellationToken ct = default) =>
        api.PutAsync($"customers/{E(id)}", request, ct);
    private static string E(string value) => Uri.EscapeDataString(value);
}

public sealed class LanInventoryService(LanApiClient api) : IInventoryService
{
    public Task EnsureDefaultsAsync(CancellationToken ct = default) => api.PostAsync("inventory/defaults", ct);
    public Task<InventoryReferenceData> GetReferenceDataAsync(CancellationToken ct = default) => api.GetAsync<InventoryReferenceData>("inventory/references", ct);
    public async Task<IReadOnlyList<InventoryBatchListItem>> SearchBatchesAsync(InventoryBatchFilter filter, CancellationToken ct = default) =>
        await api.PostAsync<InventoryBatchFilter, List<InventoryBatchListItem>>("inventory/batches/search", filter, ct);
    public Task<InventoryBatchDetail?> GetBatchAsync(string id, CancellationToken ct = default) => api.GetOptionalAsync<InventoryBatchDetail>($"inventory/batches/{E(id)}", ct);
    public async Task<string> CreateOpeningStockAsync(CreateOpeningStockRequest request, CancellationToken ct = default) =>
        (await api.PostAsync<CreateOpeningStockRequest, IdResponse>("inventory/opening-stock", request, ct)).Id;
    public Task<InventoryAdjustmentResult> AdjustAsync(InventoryAdjustmentRequest request, CancellationToken ct = default) =>
        api.PostAsync<InventoryAdjustmentRequest, InventoryAdjustmentResult>("inventory/adjust", request, ct);
    public Task ChangeBatchStatusAsync(ChangeBatchStatusRequest request, CancellationToken ct = default) => api.PostAsync("inventory/change-status", request, ct);
    private static string E(string value) => Uri.EscapeDataString(value);
}

public sealed class LanSupplierService(LanApiClient api) : ISupplierService
{
    public async Task<IReadOnlyList<SupplierListItem>> SearchAsync(SupplierSearchFilter filter, CancellationToken ct = default) =>
        await api.PostAsync<SupplierSearchFilter, List<SupplierListItem>>("suppliers/search", filter, ct);
    public Task<SupplierEditorModel?> GetAsync(string id, CancellationToken ct = default) => api.GetOptionalAsync<SupplierEditorModel>($"suppliers/{E(id)}", ct);
    public async Task<string> CreateAsync(SaveSupplierRequest request, CancellationToken ct = default) =>
        (await api.PostAsync<SaveSupplierRequest, IdResponse>("suppliers", request, ct)).Id;
    public Task UpdateAsync(string id, SaveSupplierRequest request, CancellationToken ct = default) => api.PutAsync($"suppliers/{E(id)}", request, ct);
    private static string E(string value) => Uri.EscapeDataString(value);
}

public sealed class LanPurchasingService(LanApiClient api) : IPurchasingService
{
    public Task<PurchaseReferenceData> GetReferenceDataAsync(CancellationToken ct = default) => api.GetAsync<PurchaseReferenceData>("purchasing/references", ct);
    public async Task<IReadOnlyList<PurchaseOrderListItem>> SearchOrdersAsync(PurchaseOrderSearchFilter filter, CancellationToken ct = default) =>
        await api.PostAsync<PurchaseOrderSearchFilter, List<PurchaseOrderListItem>>("purchasing/orders/search", filter, ct);
    public Task<PurchaseOrderDetail?> GetOrderAsync(string id, CancellationToken ct = default) => api.GetOptionalAsync<PurchaseOrderDetail>($"purchasing/orders/{E(id)}", ct);
    public async Task<string> CreateOrderAsync(CreatePurchaseOrderRequest request, CancellationToken ct = default) =>
        (await api.PostAsync<CreatePurchaseOrderRequest, IdResponse>("purchasing/orders", request, ct)).Id;
    public Task SubmitOrderAsync(string orderId, CancellationToken ct = default) => api.PostAsync($"purchasing/orders/{E(orderId)}/submit", ct);
    public Task ApproveOrderAsync(string orderId, CancellationToken ct = default) => api.PostAsync($"purchasing/orders/{E(orderId)}/approve", ct);
    public Task CancelOrderAsync(string orderId, CancellationToken ct = default) => api.PostAsync($"purchasing/orders/{E(orderId)}/cancel", ct);
    public async Task<string> CaptureGoodsReceiptAsync(string orderId, CaptureGoodsReceiptRequest request, CancellationToken ct = default) =>
        (await api.PostAsync<CaptureGoodsReceiptRequest, IdResponse>($"purchasing/orders/{E(orderId)}/receipts", request, ct)).Id;
    public Task PostGoodsReceiptAsync(string receiptId, string stockLocationId, CancellationToken ct = default) =>
        api.PostAsync($"purchasing/receipts/{E(receiptId)}/post", new StockLocationRequest(stockLocationId), ct);
    public async Task<string> CreateInvoiceAsync(string orderId, CreatePurchaseInvoiceRequest request, CancellationToken ct = default) =>
        (await api.PostAsync<CreatePurchaseInvoiceRequest, IdResponse>($"purchasing/orders/{E(orderId)}/invoices", request, ct)).Id;
    public async Task<string> RecordSupplierPaymentAsync(string invoiceId, RecordSupplierPaymentRequest request, CancellationToken ct = default) =>
        (await api.PostAsync<RecordSupplierPaymentRequest, IdResponse>($"purchasing/invoices/{E(invoiceId)}/payments", request, ct)).Id;
    private static string E(string value) => Uri.EscapeDataString(value);
}

public sealed class LanPosService(LanApiClient api) : IPosService
{
    public Task<PosReferenceData> GetReferenceDataAsync(CancellationToken ct = default) => api.GetAsync<PosReferenceData>("pos/references", ct);
    public async Task<IReadOnlyList<PosProductSearchItem>> SearchProductsAsync(PosProductSearchFilter filter, CancellationToken ct = default) =>
        await api.PostAsync<PosProductSearchFilter, List<PosProductSearchItem>>("pos/products/search", filter, ct);
    public Task<SaleDetail> CheckoutAsync(PosCheckoutRequest request, CancellationToken ct = default) => api.PostAsync<PosCheckoutRequest, SaleDetail>("pos/checkout", request, ct);
    public async Task<IReadOnlyList<SaleListItem>> SearchSalesAsync(SaleSearchFilter filter, CancellationToken ct = default) =>
        await api.PostAsync<SaleSearchFilter, List<SaleListItem>>("pos/sales/search", filter, ct);
    public Task<SaleDetail?> GetSaleAsync(string id, CancellationToken ct = default) => api.GetOptionalAsync<SaleDetail>($"pos/sales/{E(id)}", ct);
    public async Task<IReadOnlyList<PosTopProductItem>> GetTopProductsAsync(string stockLocationId, int take = 10, int days = 30, CancellationToken ct = default) =>
        await api.GetAsync<List<PosTopProductItem>>($"pos/top-products?stockLocationId={E(stockLocationId)}&take={take}&days={days}", ct);
    private static string E(string value) => Uri.EscapeDataString(value);
}

public sealed class LanSaleReturnService(LanApiClient api) : ISaleReturnService
{
    public async Task<IReadOnlyList<SaleListItem>> SearchReturnableSalesAsync(SaleSearchFilter filter, CancellationToken ct = default) =>
        await api.PostAsync<SaleSearchFilter, List<SaleListItem>>("returns/returnable-sales/search", filter, ct);
    public Task<ReturnableSaleDetail?> GetReturnableSaleAsync(string saleId, CancellationToken ct = default) => api.GetOptionalAsync<ReturnableSaleDetail>($"returns/returnable-sales/{E(saleId)}", ct);
    public Task<SaleReturnDetail> ProcessAsync(ProcessSaleReturnRequest request, CancellationToken ct = default) => api.PostAsync<ProcessSaleReturnRequest, SaleReturnDetail>("returns/process", request, ct);
    public async Task<IReadOnlyList<SaleReturnListItem>> SearchReturnsAsync(SaleReturnSearchFilter filter, CancellationToken ct = default) =>
        await api.PostAsync<SaleReturnSearchFilter, List<SaleReturnListItem>>("returns/search", filter, ct);
    public Task<SaleReturnDetail?> GetReturnAsync(string returnId, CancellationToken ct = default) => api.GetOptionalAsync<SaleReturnDetail>($"returns/{E(returnId)}", ct);
    private static string E(string value) => Uri.EscapeDataString(value);
}

public sealed class LanExpenseService(LanApiClient api) : IExpenseService
{
    public Task EnsureDefaultsAsync(CancellationToken ct = default) => api.PostAsync("expenses/defaults", ct);
    public Task<ExpenseReferenceData> GetReferenceDataAsync(CancellationToken ct = default) => api.GetAsync<ExpenseReferenceData>("expenses/references", ct);
    public Task<ExpenseDetail> PostAsync(PostExpenseRequest request, CancellationToken ct = default) => api.PostAsync<PostExpenseRequest, ExpenseDetail>("expenses", request, ct);
    public Task<ExpenseDetail> ReverseAsync(string expenseId, string reason, CancellationToken ct = default) => api.PostAsync<ReasonRequest, ExpenseDetail>($"expenses/{E(expenseId)}/reverse", new ReasonRequest(reason), ct);
    public Task<ExpenseDetail> AmendAsync(string expenseId, PostExpenseRequest request, string reason, CancellationToken ct = default) => api.PostAsync<ExpenseAmendRequest, ExpenseDetail>($"expenses/{E(expenseId)}/amend", new ExpenseAmendRequest(request, reason), ct);
    public async Task<IReadOnlyList<ExpenseListItem>> SearchAsync(ExpenseSearchFilter filter, CancellationToken ct = default) =>
        await api.PostAsync<ExpenseSearchFilter, List<ExpenseListItem>>("expenses/search", filter, ct);
    public Task<ExpenseDetail?> GetAsync(string expenseId, CancellationToken ct = default) => api.GetOptionalAsync<ExpenseDetail>($"expenses/{E(expenseId)}", ct);
    public async Task<IReadOnlyList<JournalEntryDetail>> GetJournalsAsync(string expenseId, CancellationToken ct = default) =>
        await api.GetAsync<List<JournalEntryDetail>>($"expenses/{E(expenseId)}/journals", ct);
    private static string E(string value) => Uri.EscapeDataString(value);
}

public sealed class LanDailyClosingService(LanApiClient api) : IDailyClosingService
{
    public DateOnly BusinessDate() => DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromMinutes(270)).DateTime);
    public Task<DailyClosingReferenceData> GetReferenceDataAsync(CancellationToken ct = default) => api.GetAsync<DailyClosingReferenceData>("closing/references", ct);
    public Task<DailyClosingWorkspace> GetWorkspaceAsync(string stockLocationId, DateOnly? businessDate = null, CancellationToken ct = default) =>
        api.PostAsync<WorkspaceRequest, DailyClosingWorkspace>("closing/workspace", new(stockLocationId, businessDate), ct);
    public Task<CashierShiftItem> OpenShiftAsync(string stockLocationId, decimal openingCash, CancellationToken ct = default) =>
        api.PostAsync<OpenShiftRequest, CashierShiftItem>("closing/shifts/open", new(stockLocationId, openingCash), ct);
    public Task<CashierShiftItem> CloseShiftAsync(string shiftId, decimal countedCash, string? notes, CancellationToken ct = default) =>
        api.PostAsync<CloseShiftRequest, CashierShiftItem>($"closing/shifts/{E(shiftId)}/close", new(countedCash, notes), ct);
    public Task<DailyClosingItem> FinalizeAsync(string stockLocationId, decimal? countedCash, string? notes, CancellationToken ct = default) =>
        api.PostAsync<FinalizeRequest, DailyClosingItem>("closing/finalize", new(stockLocationId, countedCash, notes), ct);
    public Task<DailyClosingItem> ApproveAsync(string closingId, CancellationToken ct = default) => api.PostAsync<DailyClosingItem>($"closing/{E(closingId)}/approve", ct);
    public Task<DailyClosingItem> ReopenAsync(string closingId, string reason, CancellationToken ct = default) =>
        api.PostAsync<ReasonRequest, DailyClosingItem>($"closing/{E(closingId)}/reopen", new(reason), ct);
    public Task<bool> SalesBlockedAsync(string stockLocationId, DateOnly? businessDate = null, CancellationToken ct = default) =>
        api.PostAsync<SalesBlockedRequest, bool>("closing/sales-blocked", new(stockLocationId, businessDate), ct);
    private static string E(string value) => Uri.EscapeDataString(value);
}

public sealed class LanPharmacyReportService(LanApiClient api) : IPharmacyReportService
{
    public ReportRange ResolveRange(DateOnly? from = null, DateOnly? to = null)
    {
        var end = to ?? DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromMinutes(270)).DateTime);
        var start = from ?? new DateOnly(end.Year, end.Month, 1);
        return start <= end ? new(start, end) : new(end, start);
    }
    public Task<PharmacyReportWorkspace> GetAsync(ReportRange range, CancellationToken ct = default) => api.PostAsync<ReportRange, PharmacyReportWorkspace>("reports/workspace", range, ct);
    public Task<ReportCsv> BuildCsvAsync(string type, ReportRange range, CancellationToken ct = default) => api.PostAsync<ReportCsvRequest, ReportCsv>("reports/csv", new(type, range), ct);
}

internal sealed record IdResponse(string Id);
internal sealed record StockLocationRequest(string StockLocationId);
internal sealed record ReasonRequest(string Reason);
internal sealed record ExpenseAmendRequest(PostExpenseRequest Expense, string Reason);
internal sealed record WorkspaceRequest(string StockLocationId, DateOnly? BusinessDate);
internal sealed record OpenShiftRequest(string StockLocationId, decimal OpeningCash);
internal sealed record CloseShiftRequest(decimal CountedCash, string? Notes);
internal sealed record FinalizeRequest(string StockLocationId, decimal? CountedCash, string? Notes);
internal sealed record SalesBlockedRequest(string StockLocationId, DateOnly? BusinessDate);
internal sealed record ReportCsvRequest(string Type, ReportRange Range);
