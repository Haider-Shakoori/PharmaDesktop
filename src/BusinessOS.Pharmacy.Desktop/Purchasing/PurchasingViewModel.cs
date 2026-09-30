using System.Collections.ObjectModel;
using System.Globalization;
using BusinessOS.Pharmacy.Application.Abstractions.Authentication;
using BusinessOS.Pharmacy.Application.Abstractions.Purchasing;
using BusinessOS.Pharmacy.Application.Abstractions.Time;
using BusinessOS.Pharmacy.Desktop.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BusinessOS.Pharmacy.Desktop.Purchasing;

public sealed partial class PurchasingViewModel : ObservableObject
{
    private readonly ISupplierService _suppliers;
    private readonly IPurchasingService _purchasing;
    private readonly IPermissionAuthorizer _permissions;
    private readonly IClock _clock;
    private UiLanguage _language = UiLanguageCatalog.All[0];

    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string statusMessage = string.Empty;

    [ObservableProperty] private string supplierSearchText = string.Empty;
    [ObservableProperty] private SupplierListItem? selectedSupplier;
    [ObservableProperty] private string supplierCode = string.Empty;
    [ObservableProperty] private string supplierName = string.Empty;
    [ObservableProperty] private string supplierContactPerson = string.Empty;
    [ObservableProperty] private string supplierPhone = string.Empty;
    [ObservableProperty] private string supplierWhatsapp = string.Empty;
    [ObservableProperty] private string supplierEmail = string.Empty;
    [ObservableProperty] private string supplierAddress = string.Empty;
    [ObservableProperty] private string supplierCity = string.Empty;
    [ObservableProperty] private string supplierProvince = string.Empty;
    [ObservableProperty] private int supplierPaymentTermsDays;
    [ObservableProperty] private bool supplierIsActive = true;
    [ObservableProperty] private string supplierNotes = string.Empty;

    [ObservableProperty] private string orderSearchText = string.Empty;
    [ObservableProperty] private string selectedOrderStatus = "All";
    [ObservableProperty] private PurchaseOrderListItem? selectedOrder;
    [ObservableProperty] private PurchaseOrderDetail? selectedOrderDetail;

    [ObservableProperty] private PurchaseSupplierReferenceItem? selectedOrderSupplier;
    [ObservableProperty] private string orderDateText;
    [ObservableProperty] private string expectedDateText = string.Empty;
    [ObservableProperty] private string orderCurrency = "AFN";
    [ObservableProperty] private string orderNotes = string.Empty;
    [ObservableProperty] private PurchaseMedicineReferenceItem? selectedDraftMedicine;
    [ObservableProperty] private decimal draftQuantity = 1m;
    [ObservableProperty] private decimal draftUnitCost;
    [ObservableProperty] private decimal draftDiscount;
    [ObservableProperty] private decimal draftLandedCost;

    [ObservableProperty] private PurchaseOrderLineItem? selectedOrderLine;
    [ObservableProperty] private decimal receiptQuantity;
    [ObservableProperty] private decimal receiptBonusQuantity;
    [ObservableProperty] private string receiptBatchNumber = string.Empty;
    [ObservableProperty] private string manufacturedAtText = string.Empty;
    [ObservableProperty] private string expiresAtText = string.Empty;
    [ObservableProperty] private decimal receiptUnitCost;
    [ObservableProperty] private decimal? receiptSalePrice;
    [ObservableProperty] private string receiptNotes = string.Empty;

    [ObservableProperty] private GoodsReceiptItem? selectedReceipt;
    [ObservableProperty] private PurchaseStockLocationReferenceItem? selectedStockLocation;

    [ObservableProperty] private string supplierInvoiceNumber = string.Empty;
    [ObservableProperty] private string invoiceDateText;
    [ObservableProperty] private string invoiceDueDateText = string.Empty;
    [ObservableProperty] private GoodsReceiptItem? selectedInvoiceReceipt;
    [ObservableProperty] private string invoiceNotes = string.Empty;

    [ObservableProperty] private PurchaseInvoiceItem? selectedInvoice;
    [ObservableProperty] private decimal paymentAmount;
    [ObservableProperty] private string selectedPaymentMethod = "cash";
    [ObservableProperty] private string paymentReference = string.Empty;
    [ObservableProperty] private string paymentNotes = string.Empty;

    [ObservableProperty] private bool canManagePurchases;
    [ObservableProperty] private bool canApprovePurchases;
    [ObservableProperty] private bool canPostInventory;
    [ObservableProperty] private bool canPaySuppliers;

    public PurchasingViewModel(
        ISupplierService suppliers,
        IPurchasingService purchasing,
        IPermissionAuthorizer permissions,
        IClock clock)
    {
        _suppliers = suppliers;
        _purchasing = purchasing;
        _permissions = permissions;
        _clock = clock;

        var today = DateOnly.FromDateTime(_clock.UtcNow.LocalDateTime);
        orderDateText = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        invoiceDateText = orderDateText;

        RefreshCommand = new AsyncRelayCommand(LoadAsync, () => !IsBusy);
        SearchSuppliersCommand = new AsyncRelayCommand(SearchSuppliersAsync, () => !IsBusy);
        NewSupplierCommand = new RelayCommand(NewSupplier, () => !IsBusy);
        SaveSupplierCommand = new AsyncRelayCommand(SaveSupplierAsync, () => !IsBusy && CanManagePurchases);

        SearchOrdersCommand = new AsyncRelayCommand(SearchOrdersAsync, () => !IsBusy);
        AddDraftLineCommand = new RelayCommand(AddDraftLine, () => !IsBusy && SelectedDraftMedicine is not null);
        RemoveDraftLineCommand = new RelayCommand<PurchaseOrderDraftLineViewModel>(RemoveDraftLine, _ => !IsBusy);
        ClearDraftLinesCommand = new RelayCommand(() => DraftLines.Clear(), () => !IsBusy);
        CreateOrderCommand = new AsyncRelayCommand(CreateOrderAsync, () => !IsBusy && CanManagePurchases && DraftLines.Count > 0);
        SubmitOrderCommand = new AsyncRelayCommand(SubmitOrderAsync, CanSubmitOrder);
        ApproveOrderCommand = new AsyncRelayCommand(ApproveOrderAsync, CanApproveOrder);
        CancelOrderCommand = new AsyncRelayCommand(CancelOrderAsync, CanCancelOrder);

        CaptureReceiptCommand = new AsyncRelayCommand(CaptureReceiptAsync, CanCaptureReceipt);
        PostReceiptCommand = new AsyncRelayCommand(PostReceiptAsync, CanPostReceipt);
        CreateInvoiceCommand = new AsyncRelayCommand(CreateInvoiceAsync, CanCreateInvoice);
        RecordPaymentCommand = new AsyncRelayCommand(RecordPaymentAsync, CanRecordPayment);
    }

    public ObservableCollection<SupplierListItem> Suppliers { get; } = new();
    public ObservableCollection<PurchaseOrderListItem> Orders { get; } = new();
    public ObservableCollection<PurchaseSupplierReferenceItem> SupplierReferences { get; } = new();
    public ObservableCollection<PurchaseMedicineReferenceItem> MedicineReferences { get; } = new();
    public ObservableCollection<PurchaseStockLocationReferenceItem> StockLocations { get; } = new();
    public ObservableCollection<PurchaseOrderDraftLineViewModel> DraftLines { get; } = new();

    public IReadOnlyList<string> OrderStatuses { get; } =
        ["All", "draft", "submitted", "approved", "partially_received", "received", "cancelled", "closed"];

    public IReadOnlyList<string> PaymentMethods { get; } =
        ["cash", "bank", "hawala", "other"];

    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand SearchSuppliersCommand { get; }
    public IRelayCommand NewSupplierCommand { get; }
    public IAsyncRelayCommand SaveSupplierCommand { get; }
    public IAsyncRelayCommand SearchOrdersCommand { get; }
    public IRelayCommand AddDraftLineCommand { get; }
    public IRelayCommand<PurchaseOrderDraftLineViewModel> RemoveDraftLineCommand { get; }
    public IRelayCommand ClearDraftLinesCommand { get; }
    public IAsyncRelayCommand CreateOrderCommand { get; }
    public IAsyncRelayCommand SubmitOrderCommand { get; }
    public IAsyncRelayCommand ApproveOrderCommand { get; }
    public IAsyncRelayCommand CancelOrderCommand { get; }
    public IAsyncRelayCommand CaptureReceiptCommand { get; }
    public IAsyncRelayCommand PostReceiptCommand { get; }
    public IAsyncRelayCommand CreateInvoiceCommand { get; }
    public IAsyncRelayCommand RecordPaymentCommand { get; }

    public string Title => Translate("Purchasing", "خریداری", "پېرود");
    public string Subtitle => Translate(
        "Suppliers, purchase orders, receiving, invoices and supplier payments",
        "تأمین‌کنندگان، سفارش خرید، دریافت، فاکتور و پرداخت",
        "عرضه کوونکي، پېرود امرونه، ترلاسه کول، بلونه او تادیات");

    public void SetLanguage(UiLanguage language)
    {
        _language = language;
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Subtitle));
    }

    public async Task LoadAsync()
    {
        await ExecuteBusyAsync(async () =>
        {
            CanManagePurchases = _permissions.HasPermission("purchases.manage");
            CanApprovePurchases = _permissions.HasPermission("purchases.approve");
            CanPostInventory = _permissions.HasPermission("inventory.manage");
            CanPaySuppliers = _permissions.HasPermission("purchases.pay");

            await LoadReferencesAsync();
            await SearchSuppliersCoreAsync();
            await SearchOrdersCoreAsync();

            StatusMessage = Translate(
                "Purchasing workspace ready.",
                "بخش خریداری آماده است.",
                "د پېرود برخه چمتو ده.");
        });
    }

    partial void OnSelectedSupplierChanged(SupplierListItem? value)
    {
        if (value is not null)
        {
            _ = LoadSupplierAsync(value.Id);
        }
    }

    partial void OnSelectedOrderChanged(PurchaseOrderListItem? value)
    {
        if (value is null)
        {
            SelectedOrderDetail = null;
            SelectedOrderLine = null;
            SelectedReceipt = null;
            SelectedInvoice = null;
        }
        else
        {
            _ = LoadOrderAsync(value.Id);
        }

        NotifyCommands();
    }

    partial void OnSelectedOrderLineChanged(PurchaseOrderLineItem? value)
    {
        if (value is not null)
        {
            ReceiptQuantity = Math.Max(0m, value.OrderedQuantity - value.ReceivedQuantity);
            ReceiptUnitCost = value.UnitCost;
            ReceiptBonusQuantity = 0m;
            ReceiptBatchNumber = string.Empty;
            ManufacturedAtText = string.Empty;
            ExpiresAtText = string.Empty;
            ReceiptSalePrice = null;
        }

        NotifyCommands();
    }

    partial void OnSelectedReceiptChanged(GoodsReceiptItem? value) => NotifyCommands();
    partial void OnSelectedStockLocationChanged(PurchaseStockLocationReferenceItem? value) => NotifyCommands();
    partial void OnSelectedInvoiceChanged(PurchaseInvoiceItem? value)
    {
        PaymentAmount = value?.BalanceDue ?? 0m;
        NotifyCommands();
    }

    private async Task LoadReferencesAsync()
    {
        if (!CanManagePurchases)
        {
            return;
        }

        var references = await _purchasing.GetReferenceDataAsync();

        SupplierReferences.Clear();
        foreach (var item in references.Suppliers)
        {
            SupplierReferences.Add(item);
        }

        MedicineReferences.Clear();
        foreach (var item in references.Medicines)
        {
            MedicineReferences.Add(item);
        }

        StockLocations.Clear();
        foreach (var item in references.StockLocations)
        {
            StockLocations.Add(item);
        }

        SelectedOrderSupplier ??= SupplierReferences.FirstOrDefault();
        SelectedDraftMedicine ??= MedicineReferences.FirstOrDefault();
        SelectedStockLocation ??= StockLocations.FirstOrDefault(x => x.IsDefault) ?? StockLocations.FirstOrDefault();
    }

    private async Task SearchSuppliersAsync() => await ExecuteBusyAsync(SearchSuppliersCoreAsync);

    private async Task SearchSuppliersCoreAsync()
    {
        if (!CanManagePurchases)
        {
            return;
        }

        var result = await _suppliers.SearchAsync(new SupplierSearchFilter(SupplierSearchText, Take: 500));
        var selectedId = SelectedSupplier?.Id;

        Suppliers.Clear();
        foreach (var item in result)
        {
            Suppliers.Add(item);
        }

        SelectedSupplier = Suppliers.FirstOrDefault(x => x.Id == selectedId);
    }

    private async Task SearchOrdersAsync() => await ExecuteBusyAsync(SearchOrdersCoreAsync);

    private async Task SearchOrdersCoreAsync()
    {
        if (!CanManagePurchases)
        {
            return;
        }

        var status = SelectedOrderStatus == "All" ? null : SelectedOrderStatus;
        var result = await _purchasing.SearchOrdersAsync(
            new PurchaseOrderSearchFilter(OrderSearchText, status, 500));

        var selectedId = SelectedOrder?.Id;
        Orders.Clear();
        foreach (var item in result)
        {
            Orders.Add(item);
        }

        SelectedOrder = Orders.FirstOrDefault(x => x.Id == selectedId);
    }

    private void NewSupplier()
    {
        SelectedSupplier = null;
        SupplierCode = string.Empty;
        SupplierName = string.Empty;
        SupplierContactPerson = string.Empty;
        SupplierPhone = string.Empty;
        SupplierWhatsapp = string.Empty;
        SupplierEmail = string.Empty;
        SupplierAddress = string.Empty;
        SupplierCity = string.Empty;
        SupplierProvince = string.Empty;
        SupplierPaymentTermsDays = 0;
        SupplierIsActive = true;
        SupplierNotes = string.Empty;
    }

    private async Task LoadSupplierAsync(string id)
    {
        try
        {
            var supplier = await _suppliers.GetAsync(id);
            if (supplier is null)
            {
                return;
            }

            SupplierCode = supplier.Code;
            SupplierName = supplier.Name;
            SupplierContactPerson = supplier.ContactPerson ?? string.Empty;
            SupplierPhone = supplier.Phone ?? string.Empty;
            SupplierWhatsapp = supplier.Whatsapp ?? string.Empty;
            SupplierEmail = supplier.Email ?? string.Empty;
            SupplierAddress = supplier.Address ?? string.Empty;
            SupplierCity = supplier.City ?? string.Empty;
            SupplierProvince = supplier.Province ?? string.Empty;
            SupplierPaymentTermsDays = supplier.PaymentTermsDays;
            SupplierIsActive = supplier.IsActive;
            SupplierNotes = supplier.Notes ?? string.Empty;
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
        }
    }

    private async Task SaveSupplierAsync()
    {
        await ExecuteBusyAsync(async () =>
        {
            var request = new SaveSupplierRequest(
                SupplierCode,
                SupplierName,
                SupplierContactPerson,
                SupplierPhone,
                SupplierWhatsapp,
                SupplierEmail,
                SupplierAddress,
                SupplierCity,
                SupplierProvince,
                SupplierPaymentTermsDays,
                SupplierIsActive,
                SupplierNotes);

            string id;
            if (SelectedSupplier is null)
            {
                id = await _suppliers.CreateAsync(request);
            }
            else
            {
                id = SelectedSupplier.Id;
                await _suppliers.UpdateAsync(id, request);
            }

            await SearchSuppliersCoreAsync();
            await LoadReferencesAsync();
            SelectedSupplier = Suppliers.FirstOrDefault(x => x.Id == id);

            StatusMessage = Translate("Supplier saved.", "تأمین‌کننده ذخیره شد.", "عرضه کوونکی خوندي شو.");
        });
    }

    private void AddDraftLine()
    {
        if (SelectedDraftMedicine is null)
        {
            return;
        }

        DraftLines.Add(new PurchaseOrderDraftLineViewModel(
            SelectedDraftMedicine,
            DraftQuantity,
            DraftUnitCost,
            DraftDiscount,
            DraftLandedCost));

        DraftQuantity = 1m;
        DraftUnitCost = 0m;
        DraftDiscount = 0m;
        DraftLandedCost = 0m;
        CreateOrderCommand.NotifyCanExecuteChanged();
    }

    private void RemoveDraftLine(PurchaseOrderDraftLineViewModel? line)
    {
        if (line is not null)
        {
            DraftLines.Remove(line);
            CreateOrderCommand.NotifyCanExecuteChanged();
        }
    }

    private async Task CreateOrderAsync()
    {
        await ExecuteBusyAsync(async () =>
        {
            if (SelectedOrderSupplier is null)
            {
                throw new InvalidOperationException("Select a supplier.");
            }

            var orderDate = ParseRequiredDate(OrderDateText, "Order date");
            var expectedDate = ParseOptionalDate(ExpectedDateText, "Expected date");

            var id = await _purchasing.CreateOrderAsync(
                new CreatePurchaseOrderRequest(
                    SelectedOrderSupplier.Id,
                    orderDate,
                    expectedDate,
                    OrderCurrency,
                    OrderNotes,
                    DraftLines.Select(x => new CreatePurchaseOrderLineRequest(
                        x.Medicine.Id,
                        x.OrderedQuantity,
                        x.UnitCost,
                        x.DiscountAmount,
                        x.LandedCostAllocated)).ToList()));

            DraftLines.Clear();
            OrderNotes = string.Empty;
            ExpectedDateText = string.Empty;
            await SearchOrdersCoreAsync();
            SelectedOrder = Orders.FirstOrDefault(x => x.Id == id);
            await LoadOrderAsync(id);

            StatusMessage = Translate("Purchase order created.", "سفارش خرید ایجاد شد.", "د پېرود امر جوړ شو.");
        });
    }

    private async Task SubmitOrderAsync()
    {
        if (SelectedOrder is null)
        {
            return;
        }

        var id = SelectedOrder.Id;
        await ExecuteBusyAsync(async () =>
        {
            await _purchasing.SubmitOrderAsync(id);
            await RefreshSelectedOrderAsync(id);
            StatusMessage = Translate("Purchase order submitted.", "سفارش ارسال شد.", "د پېرود امر وسپارل شو.");
        });
    }

    private async Task ApproveOrderAsync()
    {
        if (SelectedOrder is null)
        {
            return;
        }

        var id = SelectedOrder.Id;
        await ExecuteBusyAsync(async () =>
        {
            await _purchasing.ApproveOrderAsync(id);
            await RefreshSelectedOrderAsync(id);
            StatusMessage = Translate("Purchase order approved.", "سفارش تأیید شد.", "د پېرود امر تایید شو.");
        });
    }

    private async Task CancelOrderAsync()
    {
        if (SelectedOrder is null)
        {
            return;
        }

        var id = SelectedOrder.Id;
        await ExecuteBusyAsync(async () =>
        {
            await _purchasing.CancelOrderAsync(id);
            await RefreshSelectedOrderAsync(id);
            StatusMessage = Translate("Purchase order cancelled.", "سفارش لغو شد.", "د پېرود امر لغوه شو.");
        });
    }

    private async Task CaptureReceiptAsync()
    {
        if (SelectedOrder is null || SelectedOrderLine is null)
        {
            return;
        }

        var orderId = SelectedOrder.Id;
        await ExecuteBusyAsync(async () =>
        {
            var receiptId = await _purchasing.CaptureGoodsReceiptAsync(
                orderId,
                new CaptureGoodsReceiptRequest(
                    _clock.UtcNow,
                    null,
                    ReceiptNotes,
                    [
                        new CaptureGoodsReceiptLineRequest(
                            SelectedOrderLine.Id,
                            ReceiptQuantity,
                            ReceiptBonusQuantity,
                            ReceiptBatchNumber,
                            ParseOptionalDate(ManufacturedAtText, "Manufactured date"),
                            ParseOptionalDate(ExpiresAtText, "Expiry date"),
                            ReceiptUnitCost,
                            ReceiptSalePrice)
                    ]));

            ReceiptNotes = string.Empty;
            await RefreshSelectedOrderAsync(orderId);
            SelectedReceipt = SelectedOrderDetail?.Receipts.FirstOrDefault(x => x.Id == receiptId);
            StatusMessage = Translate(
                "Goods receipt captured; post it to inventory when ready.",
                "رسید کالا ثبت شد؛ آن را به موجودی انتقال دهید.",
                "د توکو رسید ثبت شو؛ زېرمتون ته یې انتقال کړئ.");
        });
    }

    private async Task PostReceiptAsync()
    {
        if (SelectedOrder is null || SelectedReceipt is null || SelectedStockLocation is null)
        {
            return;
        }

        var orderId = SelectedOrder.Id;
        var receiptId = SelectedReceipt.Id;
        await ExecuteBusyAsync(async () =>
        {
            await _purchasing.PostGoodsReceiptAsync(receiptId, SelectedStockLocation.Id);
            await RefreshSelectedOrderAsync(orderId);
            SelectedReceipt = SelectedOrderDetail?.Receipts.FirstOrDefault(x => x.Id == receiptId);
            StatusMessage = Translate("Goods receipt posted to inventory.", "رسید به موجودی منتقل شد.", "رسید زېرمتون ته انتقال شو.");
        });
    }

    private async Task CreateInvoiceAsync()
    {
        if (SelectedOrder is null)
        {
            return;
        }

        var orderId = SelectedOrder.Id;
        await ExecuteBusyAsync(async () =>
        {
            var invoiceId = await _purchasing.CreateInvoiceAsync(
                orderId,
                new CreatePurchaseInvoiceRequest(
                    SupplierInvoiceNumber,
                    SelectedInvoiceReceipt?.Id,
                    ParseRequiredDate(InvoiceDateText, "Invoice date"),
                    ParseOptionalDate(InvoiceDueDateText, "Due date"),
                    InvoiceNotes));

            SupplierInvoiceNumber = string.Empty;
            InvoiceDueDateText = string.Empty;
            InvoiceNotes = string.Empty;
            await RefreshSelectedOrderAsync(orderId);
            SelectedInvoice = SelectedOrderDetail?.Invoices.FirstOrDefault(x => x.Id == invoiceId);
            StatusMessage = Translate("Supplier invoice recorded.", "فاکتور تأمین‌کننده ثبت شد.", "د عرضه کوونکي بل ثبت شو.");
        });
    }

    private async Task RecordPaymentAsync()
    {
        if (SelectedOrder is null || SelectedInvoice is null)
        {
            return;
        }

        var orderId = SelectedOrder.Id;
        var invoiceId = SelectedInvoice.Id;
        await ExecuteBusyAsync(async () =>
        {
            await _purchasing.RecordSupplierPaymentAsync(
                invoiceId,
                new RecordSupplierPaymentRequest(
                    PaymentAmount,
                    SelectedInvoice.Currency,
                    SelectedPaymentMethod,
                    PaymentReference,
                    _clock.UtcNow,
                    null,
                    PaymentNotes));

            PaymentReference = string.Empty;
            PaymentNotes = string.Empty;
            await RefreshSelectedOrderAsync(orderId);
            SelectedInvoice = SelectedOrderDetail?.Invoices.FirstOrDefault(x => x.Id == invoiceId);
            StatusMessage = Translate("Supplier payment recorded.", "پرداخت تأمین‌کننده ثبت شد.", "د عرضه کوونکي تادیه ثبت شوه.");
        });
    }

    private async Task RefreshSelectedOrderAsync(string id)
    {
        await SearchOrdersCoreAsync();
        SelectedOrder = Orders.FirstOrDefault(x => x.Id == id);
        await LoadOrderAsync(id);
    }

    private async Task LoadOrderAsync(string id)
    {
        try
        {
            SelectedOrderDetail = await _purchasing.GetOrderAsync(id);
            SelectedOrderLine = SelectedOrderDetail?.Lines.FirstOrDefault(x => x.ReceivedQuantity < x.OrderedQuantity);
            SelectedReceipt = SelectedOrderDetail?.Receipts.FirstOrDefault();
            SelectedInvoiceReceipt = SelectedOrderDetail?.Receipts.FirstOrDefault();
            SelectedInvoice = SelectedOrderDetail?.Invoices.FirstOrDefault();
            NotifyCommands();
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
        }
    }

    private bool CanSubmitOrder() =>
        !IsBusy &&
        CanManagePurchases &&
        SelectedOrder?.Status == "draft";

    private bool CanApproveOrder() =>
        !IsBusy &&
        CanApprovePurchases &&
        SelectedOrder?.Status == "submitted";

    private bool CanCancelOrder() =>
        !IsBusy &&
        CanManagePurchases &&
        SelectedOrder is not null &&
        SelectedOrder.Status is not ("cancelled" or "closed");

    private bool CanCaptureReceipt() =>
        !IsBusy &&
        CanManagePurchases &&
        SelectedOrderLine is not null &&
        SelectedOrder?.Status is "approved" or "partially_received";

    private bool CanPostReceipt() =>
        !IsBusy &&
        CanPostInventory &&
        SelectedReceipt is not null &&
        SelectedReceipt.InventoryPostedAt is null &&
        SelectedStockLocation is not null;

    private bool CanCreateInvoice() =>
        !IsBusy &&
        CanManagePurchases &&
        SelectedOrder?.Status is "approved" or "partially_received" or "received" &&
        SelectedOrderDetail?.Invoices.All(x => x.Status == "cancelled") != false;

    private bool CanRecordPayment() =>
        !IsBusy &&
        CanPaySuppliers &&
        SelectedInvoice is not null &&
        SelectedInvoice.Status != "cancelled" &&
        SelectedInvoice.BalanceDue > 0m;

    private async Task ExecuteBusyAsync(Func<Task> action)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        NotifyCommands();

        try
        {
            await action();
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
        }
        finally
        {
            IsBusy = false;
            NotifyCommands();
        }
    }

    private void NotifyCommands()
    {
        RefreshCommand.NotifyCanExecuteChanged();
        SearchSuppliersCommand.NotifyCanExecuteChanged();
        NewSupplierCommand.NotifyCanExecuteChanged();
        SaveSupplierCommand.NotifyCanExecuteChanged();
        SearchOrdersCommand.NotifyCanExecuteChanged();
        AddDraftLineCommand.NotifyCanExecuteChanged();
        RemoveDraftLineCommand.NotifyCanExecuteChanged();
        ClearDraftLinesCommand.NotifyCanExecuteChanged();
        CreateOrderCommand.NotifyCanExecuteChanged();
        SubmitOrderCommand.NotifyCanExecuteChanged();
        ApproveOrderCommand.NotifyCanExecuteChanged();
        CancelOrderCommand.NotifyCanExecuteChanged();
        CaptureReceiptCommand.NotifyCanExecuteChanged();
        PostReceiptCommand.NotifyCanExecuteChanged();
        CreateInvoiceCommand.NotifyCanExecuteChanged();
        RecordPaymentCommand.NotifyCanExecuteChanged();
    }

    private static DateOnly ParseRequiredDate(string value, string field)
    {
        var parsed = ParseOptionalDate(value, field);
        return parsed ?? throw new ArgumentException($"{field} is required.");
    }

    private static DateOnly? ParseOptionalDate(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateOnly.TryParseExact(
                value.Trim(),
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
        {
            return parsed;
        }

        throw new ArgumentException($"{field} must use yyyy-MM-dd.");
    }

    private string Translate(string english, string dari, string pashto) =>
        _language.Code switch
        {
            "fa" => dari,
            "ps" => pashto,
            _ => english,
        };
}

public sealed record PurchaseOrderDraftLineViewModel(
    PurchaseMedicineReferenceItem Medicine,
    decimal OrderedQuantity,
    decimal UnitCost,
    decimal DiscountAmount,
    decimal LandedCostAllocated)
{
    public string MedicineLabel => string.Join(
        " ",
        new[] { Medicine.BrandName, Medicine.Strength }
            .Where(x => !string.IsNullOrWhiteSpace(x)));

    public decimal LineTotal => decimal.Round(
        (OrderedQuantity * UnitCost) - DiscountAmount + LandedCostAllocated,
        4,
        MidpointRounding.AwayFromZero);
}
