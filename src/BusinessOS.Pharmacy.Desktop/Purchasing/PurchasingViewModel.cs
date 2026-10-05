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
    private List<SupplierListItem> _matchingSuppliers = [];

    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string statusMessage = string.Empty;

    [ObservableProperty] private string supplierSearchText = string.Empty;
    [ObservableProperty] private string selectedSupplierActiveFilter = "All";
    [ObservableProperty] private SupplierListItem? selectedSupplier;
    [ObservableProperty] private bool isEditorOpen;
    [ObservableProperty] private int supplierCurrentPage = 1;
    [ObservableProperty] private int supplierPageSize = 15;
    [ObservableProperty] private int supplierTotalItems;
    [ObservableProperty] private decimal supplierTotalDealValue;
    [ObservableProperty] private decimal supplierTotalPaid;
    [ObservableProperty] private decimal supplierOutstandingPayable;
    [ObservableProperty] private decimal supplierTotalOpeningBalance;
    [ObservableProperty] private int supplierTotalCount;
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
    [ObservableProperty] private decimal supplierOpeningBalance;
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
    [ObservableProperty] private string draftBatchNumber = string.Empty;
    [ObservableProperty] private string draftExpiresAtText = string.Empty;
    [ObservableProperty] private decimal? draftSalePrice;

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

        var today = DateOnly.FromDateTime(
            _clock.UtcNow.ToOffset(TimeSpan.FromMinutes(270)).DateTime);
        orderDateText = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        invoiceDateText = orderDateText;

        RefreshCommand = new AsyncRelayCommand(LoadAsync, () => !IsBusy);
        SearchSuppliersCommand = new AsyncRelayCommand(SearchSuppliersAsync, () => !IsBusy);
        NewSupplierCommand = new RelayCommand(NewSupplier, () => !IsBusy && CanManagePurchases);
        EditSupplierCommand = new AsyncRelayCommand<SupplierListItem>(
            EditSupplierAsync,
            item => item is not null && !IsBusy && CanManagePurchases);
        CloseSupplierEditorCommand = new RelayCommand(() => IsEditorOpen = false);
        SaveSupplierCommand = new AsyncRelayCommand(SaveSupplierAsync, () => !IsBusy && CanManagePurchases);
        SupplierPreviousPageCommand = new RelayCommand(
            SupplierPreviousPage,
            () => SupplierCurrentPage > 1);
        SupplierNextPageCommand = new RelayCommand(
            SupplierNextPage,
            () => SupplierCurrentPage < SupplierTotalPages);
        SupplierGoToPageCommand = new RelayCommand<int>(
            SupplierGoToPage,
            page => page >= 1 && page <= SupplierTotalPages);

        SearchOrdersCommand = new AsyncRelayCommand(SearchOrdersAsync, () => !IsBusy);
        AddDraftLineCommand = new RelayCommand(AddDraftLine, () => !IsBusy && SelectedDraftMedicine is not null);
        RemoveDraftLineCommand = new RelayCommand<PurchaseOrderDraftLineViewModel>(RemoveDraftLine, _ => !IsBusy);
        ClearDraftLinesCommand = new RelayCommand(ClearDraftLines, () => !IsBusy);
        CreateOrderCommand = new AsyncRelayCommand(CreateOrderAsync, CanCreateOrder);
        QuickCreateOrderCommand = new AsyncRelayCommand(QuickCreateOrderAsync, CanCreateOrder);
        SavePurchaseCommand = new AsyncRelayCommand(SavePurchaseAsync, CanSavePurchase);
        SubmitOrderCommand = new AsyncRelayCommand(SubmitOrderAsync, CanSubmitOrder);
        ApproveOrderCommand = new AsyncRelayCommand(ApproveOrderAsync, CanApproveOrder);
        CancelOrderCommand = new AsyncRelayCommand(CancelOrderAsync, CanCancelOrder);

        CaptureReceiptCommand = new AsyncRelayCommand(CaptureReceiptAsync, CanCaptureReceipt);
        ReceiveAndPostCommand = new AsyncRelayCommand(ReceiveAndPostAsync, CanReceiveAndPost);
        PostReceiptCommand = new AsyncRelayCommand(PostReceiptAsync, CanPostReceipt);
        CreateInvoiceCommand = new AsyncRelayCommand(CreateInvoiceAsync, CanCreateInvoice);
        RecordPaymentCommand = new AsyncRelayCommand(RecordPaymentAsync, CanRecordPayment);
    }

    public ObservableCollection<SupplierListItem> Suppliers { get; } = new();
    public ObservableCollection<int> SupplierPageNumbers { get; } = new();
    public ObservableCollection<PurchaseOrderListItem> Orders { get; } = new();
    public ObservableCollection<PurchaseSupplierReferenceItem> SupplierReferences { get; } = new();
    public ObservableCollection<PurchaseMedicineReferenceItem> MedicineReferences { get; } = new();
    public ObservableCollection<PurchaseStockLocationReferenceItem> StockLocations { get; } = new();
    public ObservableCollection<PurchaseOrderDraftLineViewModel> DraftLines { get; } = new();

    public IReadOnlyList<string> SupplierActiveFilters { get; } = ["All", "Active", "Inactive"];
    public IReadOnlyList<int> SupplierPageSizeOptions { get; } = [10, 15, 25, 50];

    public IReadOnlyList<string> OrderStatuses { get; } =
        ["All", "draft", "submitted", "approved", "partially_received", "received", "cancelled", "closed"];

    public IReadOnlyList<string> PaymentMethods { get; } =
        ["cash", "bank", "hawala", "other"];

    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand SearchSuppliersCommand { get; }
    public IRelayCommand NewSupplierCommand { get; }
    public IAsyncRelayCommand<SupplierListItem> EditSupplierCommand { get; }
    public IRelayCommand CloseSupplierEditorCommand { get; }
    public IAsyncRelayCommand SaveSupplierCommand { get; }
    public IRelayCommand SupplierPreviousPageCommand { get; }
    public IRelayCommand SupplierNextPageCommand { get; }
    public IRelayCommand<int> SupplierGoToPageCommand { get; }
    public IAsyncRelayCommand SearchOrdersCommand { get; }
    public IRelayCommand AddDraftLineCommand { get; }
    public IRelayCommand<PurchaseOrderDraftLineViewModel> RemoveDraftLineCommand { get; }
    public IRelayCommand ClearDraftLinesCommand { get; }
    public IAsyncRelayCommand CreateOrderCommand { get; }
    public IAsyncRelayCommand QuickCreateOrderCommand { get; }
    public IAsyncRelayCommand SavePurchaseCommand { get; }
    public IAsyncRelayCommand SubmitOrderCommand { get; }
    public IAsyncRelayCommand ApproveOrderCommand { get; }
    public IAsyncRelayCommand CancelOrderCommand { get; }
    public IAsyncRelayCommand CaptureReceiptCommand { get; }
    public IAsyncRelayCommand ReceiveAndPostCommand { get; }
    public IAsyncRelayCommand PostReceiptCommand { get; }
    public IAsyncRelayCommand CreateInvoiceCommand { get; }
    public IAsyncRelayCommand RecordPaymentCommand { get; }

    public string Title => Translate("Purchasing", "خریداری", "پېرود");
    public string Subtitle => Translate(
        "Suppliers, purchase orders, receiving, invoices and supplier payments",
        "تأمین‌کنندگان، سفارش خرید، دریافت، فاکتور و پرداخت",
        "عرضه کوونکي، پېرود امرونه، ترلاسه کول، بلونه او تادیات");

    public string SupplierEyebrow => Translate("Supplier & purchasing control", "کنترل تأمین‌کننده و خرید", "د عرضه کوونکو او پېرود کنټرول");
    public string SupplierTitle => Translate("Suppliers", "تأمین‌کنندگان", "عرضه کوونکي");
    public string SupplierSubtitle => Translate(
        "Supplier relationships, purchase deals, payments and outstanding balances in one place",
        "روابط تأمین‌کنندگان، معاملات خرید، پرداخت‌ها و مانده بدهی در یک بخش",
        "د عرضه کوونکو اړیکې، د پېرود معاملې، تادیات او پاتې پورونه په یوه برخه کې");
    public string NewSupplierLabel => Translate("New supplier", "تأمین‌کننده جدید", "نوی عرضه کوونکی");
    public string EditSupplierLabel => Translate("Edit", "ویرایش", "سمون");
    public string SaveSupplierLabel => Translate("Save supplier", "ذخیره تأمین‌کننده", "عرضه کوونکی خوندي کړئ");
    public string SupplierEditorTitle => SelectedSupplier is null
        ? NewSupplierLabel
        : Translate("Edit supplier", "ویرایش تأمین‌کننده", "عرضه کوونکی سمول");
    public bool CanEditSupplierOpeningBalance => SelectedSupplier is null;

    public string TotalDealValueLabel => Translate("Total deal value", "ارزش مجموع معاملات", "د معاملو ټول ارزښت");
    public string TotalPaidLabel => Translate("Total paid", "مجموع پرداخت‌شده", "ټولې تادیې");
    public string OutstandingPayableLabel => Translate("Outstanding payable", "بدهی باقی‌مانده", "پاتې تادیه");
    public string OpeningBalancesLabel => Translate("Opening balances", "مانده‌های افتتاحیه", "پیل بیلانسونه");
    public string TotalDealValueText => $"؋ {SupplierTotalDealValue:N2}";
    public string TotalPaidText => $"؋ {SupplierTotalPaid:N2}";
    public string OutstandingPayableText => $"؋ {SupplierOutstandingPayable:N2}";
    public string OpeningBalancesText => $"؋ {SupplierTotalOpeningBalance:N2}";
    public string TotalDealValueHint => Translate(
        "Non-cancelled supplier invoices",
        "فاکتورهای لغونشده تأمین‌کنندگان",
        "د عرضه کوونکو نه لغوه شوي بلونه");
    public string TotalPaidHint => Translate(
        "Payments recorded against supplier invoices",
        "پرداخت‌های ثبت‌شده در برابر فاکتورها",
        "د عرضه کوونکو د بلونو ثبت شوې تادیې");
    public string OutstandingPayableHint => Translate(
        "Opening balances plus unpaid invoice balances",
        "مانده افتتاحیه به‌علاوه فاکتورهای پرداخت‌نشده",
        "پیل بیلانسونه او د نه تادیه شوو بلونو پاتې");
    public string OpeningBalancesHint => Translate(
        "Historical supplier balances captured at creation",
        "مانده تاریخی تأمین‌کنندگان ثبت‌شده هنگام ایجاد",
        "د جوړولو پر مهال ثبت شوي تاریخي پیل بیلانسونه");

    public int SupplierTotalPages => Math.Max(
        1,
        (int)Math.Ceiling(SupplierTotalItems / (double)Math.Max(1, SupplierPageSize)));
    public string SupplierPageSummary
    {
        get
        {
            if (SupplierTotalItems == 0)
            {
                return Translate("Showing 0 suppliers", "نمایش ۰ تأمین‌کننده", "۰ عرضه کوونکي ښودل کېږي");
            }

            var from = ((SupplierCurrentPage - 1) * SupplierPageSize) + 1;
            var to = Math.Min(SupplierCurrentPage * SupplierPageSize, SupplierTotalItems);
            return Translate(
                $"Showing {from}-{to} of {SupplierTotalItems} suppliers",
                $"نمایش {from}-{to} از {SupplierTotalItems} تأمین‌کننده",
                $"له {SupplierTotalItems} عرضه کوونکو څخه {from}-{to} ښودل کېږي");
        }
    }

    public string QuickCreateLabel => CanApprovePurchases
        ? Translate("Create & approve", "ایجاد و تأیید", "جوړ او تایید")
        : Translate("Create & submit", "ایجاد و ارسال", "جوړ او وسپارئ");

    public string QuickCreateHint => CanApprovePurchases
        ? Translate(
            "One click creates the PO, submits it and approves it while preserving the audit trail.",
            "با یک کلیک سفارش ایجاد، ارسال و تأیید می‌شود و مسیر حسابرسی حفظ می‌گردد.",
            "په یوه کلیک امر جوړېږي، سپارل کېږي او تاییدېږي؛ د پلټنې لړۍ خوندي پاتې کېږي.")
        : Translate(
            "One click creates the PO and submits it for approval.",
            "با یک کلیک سفارش ایجاد و برای تأیید ارسال می‌شود.",
            "په یوه کلیک امر جوړ او د تایید لپاره سپارل کېږي.");

    public decimal DraftTotal => decimal.Round(
        DraftLines.Sum(x => x.LineTotal),
        4,
        MidpointRounding.AwayFromZero);

    public string DraftSummary => Translate(
        $"{DraftLines.Count} line(s) · AFN {DraftTotal:N2}",
        $"{DraftLines.Count} قلم · AFN {DraftTotal:N2}",
        $"{DraftLines.Count} کرښې · AFN {DraftTotal:N2}");

    public string SelectedOrderNextStep => SelectedOrder?.Status switch
    {
        "draft" => Translate("Next: submit for approval", "مرحله بعد: ارسال برای تأیید", "بل: د تایید لپاره وسپارئ"),
        "submitted" => Translate("Next: approve purchase order", "مرحله بعد: تأیید سفارش", "بل: د پېرود امر تایید"),
        "approved" => Translate("Next: receive stock", "مرحله بعد: دریافت کالا", "بل: توکي ترلاسه کړئ"),
        "partially_received" => Translate("Next: receive remaining stock", "مرحله بعد: دریافت باقی‌مانده", "بل: پاتې توکي ترلاسه کړئ"),
        "received" => Translate("Next: record supplier invoice", "مرحله بعد: ثبت فاکتور", "بل: د عرضه کوونکي بل ثبت"),
        "closed" => Translate("Workflow complete", "فرآیند تکمیل است", "بهیر بشپړ دی"),
        "cancelled" => Translate("Order cancelled", "سفارش لغو شده", "امر لغوه شوی"),
        _ => Translate("Select an order to continue", "برای ادامه یک سفارش را انتخاب کنید", "د دوام لپاره امر وټاکئ"),
    };

    public void SetLanguage(UiLanguage language)
    {
        _language = language;
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(SupplierEyebrow));
        OnPropertyChanged(nameof(SupplierTitle));
        OnPropertyChanged(nameof(SupplierSubtitle));
        OnPropertyChanged(nameof(NewSupplierLabel));
        OnPropertyChanged(nameof(EditSupplierLabel));
        OnPropertyChanged(nameof(SaveSupplierLabel));
        OnPropertyChanged(nameof(SupplierEditorTitle));
        OnPropertyChanged(nameof(TotalDealValueLabel));
        OnPropertyChanged(nameof(TotalPaidLabel));
        OnPropertyChanged(nameof(OutstandingPayableLabel));
        OnPropertyChanged(nameof(OpeningBalancesLabel));
        OnPropertyChanged(nameof(TotalDealValueHint));
        OnPropertyChanged(nameof(TotalPaidHint));
        OnPropertyChanged(nameof(OutstandingPayableHint));
        OnPropertyChanged(nameof(OpeningBalancesHint));
        OnPropertyChanged(nameof(SupplierPageSummary));
        OnPropertyChanged(nameof(QuickCreateLabel));
        OnPropertyChanged(nameof(QuickCreateHint));
        OnPropertyChanged(nameof(DraftSummary));
        OnPropertyChanged(nameof(SelectedOrderNextStep));
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
            await LoadSupplierSummaryAsync();
            await SearchOrdersCoreAsync();

            StatusMessage = Translate(
                "Purchasing workspace ready.",
                "بخش خریداری آماده است.",
                "د پېرود برخه چمتو ده.");
        });
    }

    partial void OnSelectedSupplierChanged(SupplierListItem? value)
    {
        OnPropertyChanged(nameof(SupplierEditorTitle));
        OnPropertyChanged(nameof(CanEditSupplierOpeningBalance));
        EditSupplierCommand.NotifyCanExecuteChanged();
    }

    partial void OnSupplierCurrentPageChanged(int value)
    {
        OnPropertyChanged(nameof(SupplierTotalPages));
        OnPropertyChanged(nameof(SupplierPageSummary));
        SupplierPreviousPageCommand.NotifyCanExecuteChanged();
        SupplierNextPageCommand.NotifyCanExecuteChanged();
        SupplierGoToPageCommand.NotifyCanExecuteChanged();
        RebuildSupplierPageNumbers();
    }

    partial void OnSupplierPageSizeChanged(int value)
    {
        SupplierCurrentPage = 1;
        RefreshSupplierPage();
    }

    partial void OnSupplierTotalItemsChanged(int value)
    {
        OnPropertyChanged(nameof(SupplierTotalPages));
        OnPropertyChanged(nameof(SupplierPageSummary));
        SupplierPreviousPageCommand.NotifyCanExecuteChanged();
        SupplierNextPageCommand.NotifyCanExecuteChanged();
        SupplierGoToPageCommand.NotifyCanExecuteChanged();
        RebuildSupplierPageNumbers();
    }

    partial void OnSupplierTotalDealValueChanged(decimal value) =>
        OnPropertyChanged(nameof(TotalDealValueText));

    partial void OnSupplierTotalPaidChanged(decimal value) =>
        OnPropertyChanged(nameof(TotalPaidText));

    partial void OnSupplierOutstandingPayableChanged(decimal value) =>
        OnPropertyChanged(nameof(OutstandingPayableText));

    partial void OnSupplierTotalOpeningBalanceChanged(decimal value) =>
        OnPropertyChanged(nameof(OpeningBalancesText));

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

        OnPropertyChanged(nameof(SelectedOrderNextStep));
        NotifyCommands();
    }

    partial void OnCanApprovePurchasesChanged(bool value)
    {
        OnPropertyChanged(nameof(QuickCreateLabel));
        OnPropertyChanged(nameof(QuickCreateHint));
    }

    partial void OnPaymentAmountChanged(decimal value) => SavePurchaseCommand.NotifyCanExecuteChanged();

    partial void OnCanPaySuppliersChanged(bool value) => SavePurchaseCommand.NotifyCanExecuteChanged();

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

        bool? active = SelectedSupplierActiveFilter switch
        {
            "Active" => true,
            "Inactive" => false,
            _ => null,
        };

        var result = await _suppliers.SearchAsync(
            new SupplierSearchFilter(
                string.IsNullOrWhiteSpace(SupplierSearchText)
                    ? null
                    : SupplierSearchText.Trim(),
                active,
                1000));

        var selectedId = SelectedSupplier?.Id;
        _matchingSuppliers = result.ToList();
        SupplierTotalItems = _matchingSuppliers.Count;
        SupplierCurrentPage = 1;
        RefreshSupplierPage();

        if (selectedId is not null)
        {
            SelectedSupplier = _matchingSuppliers.FirstOrDefault(x => x.Id == selectedId);
        }
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
        SupplierOpeningBalance = 0m;
        SupplierIsActive = true;
        SupplierNotes = string.Empty;
        IsEditorOpen = true;
    }

    private async Task EditSupplierAsync(SupplierListItem? item)
    {
        if (item is null || IsBusy || !CanManagePurchases)
        {
            return;
        }

        SelectedSupplier = item;
        await LoadSupplierAsync(item.Id);
        IsEditorOpen = true;
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
            SupplierOpeningBalance = supplier.OpeningBalance;
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
                SupplierOpeningBalance,
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
            await LoadSupplierSummaryAsync();
            await LoadReferencesAsync();
            SelectSupplierAndPage(id);
            IsEditorOpen = false;

            StatusMessage = Translate("Supplier saved.", "تأمین‌کننده ذخیره شد.", "عرضه کوونکی خوندي شو.");
        });
    }

    private async Task LoadSupplierSummaryAsync()
    {
        if (!CanManagePurchases)
        {
            return;
        }

        var summary = await _suppliers.GetSummaryAsync();
        SupplierTotalCount = summary.TotalSuppliers;
        SupplierTotalDealValue = summary.TotalDealValue;
        SupplierTotalPaid = summary.TotalPaid;
        SupplierOutstandingPayable = summary.OutstandingPayable;
        SupplierTotalOpeningBalance = summary.TotalOpeningBalance;
    }

    private void SelectSupplierAndPage(string id)
    {
        var index = _matchingSuppliers.FindIndex(x => x.Id == id);
        if (index < 0)
        {
            SelectedSupplier = null;
            return;
        }

        SupplierCurrentPage = (index / Math.Max(1, SupplierPageSize)) + 1;
        RefreshSupplierPage();
        SelectedSupplier = Suppliers.FirstOrDefault(x => x.Id == id);
    }

    private void SupplierPreviousPage()
    {
        if (SupplierCurrentPage <= 1)
        {
            return;
        }

        SupplierCurrentPage--;
        RefreshSupplierPage();
    }

    private void SupplierNextPage()
    {
        if (SupplierCurrentPage >= SupplierTotalPages)
        {
            return;
        }

        SupplierCurrentPage++;
        RefreshSupplierPage();
    }

    private void SupplierGoToPage(int page)
    {
        if (page < 1 || page > SupplierTotalPages || page == SupplierCurrentPage)
        {
            return;
        }

        SupplierCurrentPage = page;
        RefreshSupplierPage();
    }

    private void RefreshSupplierPage()
    {
        if (SupplierCurrentPage > SupplierTotalPages)
        {
            SupplierCurrentPage = SupplierTotalPages;
        }

        var skip = Math.Max(
            0,
            (SupplierCurrentPage - 1) * Math.Max(1, SupplierPageSize));

        Suppliers.Clear();
        foreach (var item in _matchingSuppliers
                     .Skip(skip)
                     .Take(Math.Max(1, SupplierPageSize)))
        {
            Suppliers.Add(item);
        }

        OnPropertyChanged(nameof(SupplierPageSummary));
        RebuildSupplierPageNumbers();
    }

    private void RebuildSupplierPageNumbers()
    {
        var total = SupplierTotalPages;
        var start = Math.Max(1, SupplierCurrentPage - 2);
        var end = Math.Min(total, start + 4);
        start = Math.Max(1, end - 4);

        SupplierPageNumbers.Clear();
        for (var page = start; page <= end; page++)
        {
            SupplierPageNumbers.Add(page);
        }
    }

    private void AddDraftLine()
    {
        if (SelectedDraftMedicine is null)
        {
            return;
        }

        var expiry = ParseOptionalDate(DraftExpiresAtText, "Expiry date");

        DraftLines.Add(new PurchaseOrderDraftLineViewModel(
            SelectedDraftMedicine,
            DraftQuantity,
            DraftUnitCost,
            DraftDiscount,
            DraftLandedCost,
            string.IsNullOrWhiteSpace(DraftBatchNumber) ? null : DraftBatchNumber.Trim(),
            expiry,
            DraftSalePrice));

        DraftQuantity = 1m;
        DraftUnitCost = 0m;
        DraftDiscount = 0m;
        DraftLandedCost = 0m;
        DraftBatchNumber = string.Empty;
        DraftExpiresAtText = string.Empty;
        DraftSalePrice = null;
        RaiseDraftState();
    }

    private void RemoveDraftLine(PurchaseOrderDraftLineViewModel? line)
    {
        if (line is not null)
        {
            DraftLines.Remove(line);
            RaiseDraftState();
        }
    }

    private void ClearDraftLines()
    {
        DraftLines.Clear();
        RaiseDraftState();
    }

    private bool CanCreateOrder() =>
        !IsBusy &&
        CanManagePurchases &&
        SelectedOrderSupplier is not null &&
        DraftLines.Count > 0 &&
        DraftLines.All(x => x.OrderedQuantity > 0m && x.UnitCost >= 0m);

    private bool CanSavePurchase() =>
        !IsBusy &&
        CanManagePurchases &&
        SelectedOrderSupplier is not null &&
        DraftLines.Count > 0 &&
        PaymentAmount >= 0m &&
        PaymentAmount <= DraftTotal &&
        (PaymentAmount == 0m || CanPaySuppliers) &&
        DraftLines.All(x =>
            x.OrderedQuantity > 0m &&
            x.UnitCost >= 0m &&
            (!x.Medicine.BatchTrackingRequired || !string.IsNullOrWhiteSpace(x.BatchNumber)) &&
            (!x.Medicine.ExpiryTrackingRequired || x.ExpiresAt is not null));

    private async Task SavePurchaseAsync()
    {
        if (SelectedOrderSupplier is null)
        {
            throw new InvalidOperationException("Select a supplier.");
        }

        await ExecuteBusyAsync(async () =>
        {
            var id = await _purchasing.CompletePurchaseAsync(
                new CompletePurchaseRequest(
                    SelectedOrderSupplier.Id,
                    ParseRequiredDate(OrderDateText, "Order date"),
                    OrderCurrency,
                    OrderNotes,
                    PaymentAmount,
                    SelectedPaymentMethod,
                    PaymentReference,
                    PaymentNotes,
                    DraftLines.Select(x => new CompletePurchaseLineRequest(
                        x.Medicine.Id,
                        x.OrderedQuantity,
                        x.UnitCost,
                        x.DiscountAmount,
                        x.LandedCostAllocated,
                        x.BatchNumber,
                        x.ExpiresAt,
                        x.SalePrice)).ToList()));

            DraftLines.Clear();
            OrderNotes = string.Empty;
            PaymentAmount = 0m;
            PaymentReference = string.Empty;
            PaymentNotes = string.Empty;
            RaiseDraftState();

            await RefreshCreatedOrderAsync(id);

            StatusMessage = Translate(
                "Purchase saved. Inventory and supplier balance were updated.",
                "خرید ذخیره شد. موجودی و حساب تأمین‌کننده به‌روزرسانی شد.",
                "پېرود خوندي شو. زېرمتون او د عرضه کوونکي حساب تازه شول.");
        });
    }

    private async Task CreateOrderAsync()
    {
        await ExecuteBusyAsync(async () =>
        {
            var id = await CreateOrderCoreAsync();
            await RefreshCreatedOrderAsync(id);

            StatusMessage = Translate(
                "Purchase order draft created.",
                "پیش‌نویس سفارش خرید ایجاد شد.",
                "د پېرود امر مسوده جوړه شوه.");
        });
    }

    private async Task QuickCreateOrderAsync()
    {
        await ExecuteBusyAsync(async () =>
        {
            var id = await CreateOrderCoreAsync();
            await _purchasing.SubmitOrderAsync(id);

            if (CanApprovePurchases)
            {
                await _purchasing.ApproveOrderAsync(id);
            }

            await RefreshCreatedOrderAsync(id);

            StatusMessage = CanApprovePurchases
                ? Translate(
                    "Purchase order created and approved.",
                    "سفارش خرید ایجاد و تأیید شد.",
                    "د پېرود امر جوړ او تایید شو.")
                : Translate(
                    "Purchase order created and submitted for approval.",
                    "سفارش خرید ایجاد و برای تأیید ارسال شد.",
                    "د پېرود امر جوړ او د تایید لپاره وسپارل شو.");
        });
    }

    private async Task<string> CreateOrderCoreAsync()
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
        RaiseDraftState();

        return id;
    }

    private async Task RefreshCreatedOrderAsync(string id)
    {
        await SearchOrdersCoreAsync();
        SelectedOrder = Orders.FirstOrDefault(x => x.Id == id);
        await LoadOrderAsync(id);
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

    private async Task ReceiveAndPostAsync()
    {
        if (SelectedOrder is null ||
            SelectedOrderLine is null ||
            SelectedStockLocation is null)
        {
            return;
        }

        var orderId = SelectedOrder.Id;
        var stockLocationId = SelectedStockLocation.Id;

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

            await _purchasing.PostGoodsReceiptAsync(receiptId, stockLocationId);

            ReceiptNotes = string.Empty;
            await RefreshSelectedOrderAsync(orderId);
            SelectedReceipt = SelectedOrderDetail?.Receipts.FirstOrDefault(x => x.Id == receiptId);

            StatusMessage = Translate(
                "Stock received and posted to inventory.",
                "کالا دریافت و به موجودی منتقل شد.",
                "توکي ترلاسه او زېرمتون ته داخل شول.");
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
            await LoadSupplierSummaryAsync();
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
            await LoadSupplierSummaryAsync();
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
        SelectedOrder is not null &&
        (SelectedOrder.Status is "approved" or "partially_received");

    private bool CanReceiveAndPost() =>
        CanCaptureReceipt() &&
        CanPostInventory &&
        SelectedStockLocation is not null;

    private bool CanPostReceipt() =>
        !IsBusy &&
        CanPostInventory &&
        SelectedReceipt is not null &&
        SelectedReceipt.InventoryPostedAt is null &&
        SelectedStockLocation is not null;

    private bool CanCreateInvoice() =>
        !IsBusy &&
        CanManagePurchases &&
        SelectedOrder is not null &&
        (SelectedOrder.Status is "approved" or "partially_received" or "received") &&
        SelectedOrderDetail is not null &&
        SelectedOrderDetail.Invoices.All(x => x.Status == "cancelled");

    private bool CanRecordPayment() =>
        !IsBusy &&
        CanPaySuppliers &&
        SelectedInvoice is not null &&
        SelectedInvoice.Status != "cancelled" &&
        SelectedInvoice.BalanceDue > 0m;

    private void RaiseDraftState()
    {
        OnPropertyChanged(nameof(DraftTotal));
        OnPropertyChanged(nameof(DraftSummary));
        CreateOrderCommand.NotifyCanExecuteChanged();
        QuickCreateOrderCommand.NotifyCanExecuteChanged();
        SavePurchaseCommand.NotifyCanExecuteChanged();
    }

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
        EditSupplierCommand.NotifyCanExecuteChanged();
        SaveSupplierCommand.NotifyCanExecuteChanged();
        SupplierPreviousPageCommand.NotifyCanExecuteChanged();
        SupplierNextPageCommand.NotifyCanExecuteChanged();
        SupplierGoToPageCommand.NotifyCanExecuteChanged();
        SearchOrdersCommand.NotifyCanExecuteChanged();
        AddDraftLineCommand.NotifyCanExecuteChanged();
        RemoveDraftLineCommand.NotifyCanExecuteChanged();
        ClearDraftLinesCommand.NotifyCanExecuteChanged();
        CreateOrderCommand.NotifyCanExecuteChanged();
        QuickCreateOrderCommand.NotifyCanExecuteChanged();
        SavePurchaseCommand.NotifyCanExecuteChanged();
        SubmitOrderCommand.NotifyCanExecuteChanged();
        ApproveOrderCommand.NotifyCanExecuteChanged();
        CancelOrderCommand.NotifyCanExecuteChanged();
        CaptureReceiptCommand.NotifyCanExecuteChanged();
        ReceiveAndPostCommand.NotifyCanExecuteChanged();
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
    decimal LandedCostAllocated,
    string? BatchNumber,
    DateOnly? ExpiresAt,
    decimal? SalePrice)
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
