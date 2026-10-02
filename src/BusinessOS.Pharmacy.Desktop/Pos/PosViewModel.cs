using System.Collections.ObjectModel;
using System.ComponentModel;
using BusinessOS.Pharmacy.Application.Abstractions.Authentication;
using BusinessOS.Pharmacy.Application.Abstractions.Sales;
using BusinessOS.Pharmacy.Application.Abstractions.Time;
using BusinessOS.Pharmacy.Desktop.Localization;
using BusinessOS.Pharmacy.Desktop.Printing;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BusinessOS.Pharmacy.Desktop.Pos;

public sealed partial class PosViewModel : ObservableObject
{
    private readonly IPosService _pos;
    private readonly IPermissionAuthorizer _permissions;
    private readonly IClock _clock;
    private readonly ISaleReceiptPrinter _receiptPrinter;
    private UiLanguage _language = UiLanguageCatalog.All[0];
    private bool _suppressSearchTextChanged;

    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool isSearching;
    [ObservableProperty] private bool isSearchDropdownOpen;
    [ObservableProperty] private string statusMessage = string.Empty;
    [ObservableProperty] private string searchText = string.Empty;
    [ObservableProperty] private PosStockLocationItem? selectedLocation;
    [ObservableProperty] private PosCustomerItem? selectedCustomer;
    [ObservableProperty] private PosProductSearchItem? selectedProduct;
    [ObservableProperty] private decimal quantity = 1m;
    [ObservableProperty] private bool overridePrice;
    [ObservableProperty] private decimal? overrideUnitPrice;
    [ObservableProperty] private decimal discountAmount;
    [ObservableProperty] private string selectedPaymentMethod = "cash";
    [ObservableProperty] private decimal paymentAmount;
    [ObservableProperty] private string paymentReference = string.Empty;
    [ObservableProperty] private string prescriptionReference = string.Empty;
    [ObservableProperty] private string prescriberName = string.Empty;
    [ObservableProperty] private string prescriptionDateText = string.Empty;
    [ObservableProperty] private string saleNotes = string.Empty;
    [ObservableProperty] private bool printInvoiceAfterPayment;
    [ObservableProperty] private SaleDetail? lastSale;

    public PosViewModel(
        IPosService pos,
        IPermissionAuthorizer permissions,
        IClock clock,
        ISaleReceiptPrinter receiptPrinter)
    {
        _pos = pos;
        _permissions = permissions;
        _clock = clock;
        _receiptPrinter = receiptPrinter;

        LoadCommand = new AsyncRelayCommand(LoadAsync, () => !IsBusy);
        FocusSearchCommand = new RelayCommand(RequestSearchFocus);
        FocusCustomerCommand = new RelayCommand(
            () => CustomerFocusRequested?.Invoke(this, EventArgs.Empty),
            () => !IsBusy);
        SearchCommand = new AsyncRelayCommand(
            SearchAsync,
            () => !IsBusy &&
                  SelectedLocation is not null &&
                  !string.IsNullOrWhiteSpace(SearchText));
        AddSearchResultCommand = new RelayCommand<PosProductSearchItem>(
            AddSearchResult,
            item => !IsBusy && item is not null);
        AddToCartCommand = new RelayCommand(AddToCart, CanAddToCart);
        RemoveFromCartCommand = new RelayCommand<PosCartLineViewModel>(
            RemoveFromCart,
            item => !IsBusy && item is not null);
        ClearCartCommand = new RelayCommand(ClearCart, () => !IsBusy && Cart.Count > 0);
        AddPaymentCommand = new RelayCommand(AddPayment, CanAddPayment);
        AddSplitPaymentCommand = new RelayCommand(AddSplitPayment, () => !IsBusy && Payments.Count < 10);
        SetCashToTotalCommand = new RelayCommand(SetCashToTotal, () => !IsBusy && Cart.Count > 0);
        RemovePaymentCommand = new RelayCommand<PosPaymentDraftViewModel>(
            RemovePayment,
            item => !IsBusy && item is not null && Payments.Count > 1);
        OpenPaymentCommand = new RelayCommand(OpenPayment, CanOpenPayment);
        CheckoutCommand = new AsyncRelayCommand(CheckoutAsync, CanCheckout);
        RefreshSalesCommand = new AsyncRelayCommand(RefreshSalesAsync, () => !IsBusy);

        ResetPaymentsToCash();
    }

    public event EventHandler? SearchFocusRequested;
    public event EventHandler? CustomerFocusRequested;
    public event EventHandler? PaymentRequested;
    public event EventHandler? PaymentCloseRequested;

    public IAsyncRelayCommand LoadCommand { get; }
    public IRelayCommand FocusSearchCommand { get; }
    public IRelayCommand FocusCustomerCommand { get; }
    public IAsyncRelayCommand SearchCommand { get; }
    public IRelayCommand<PosProductSearchItem> AddSearchResultCommand { get; }
    public IRelayCommand AddToCartCommand { get; }
    public IRelayCommand<PosCartLineViewModel> RemoveFromCartCommand { get; }
    public IRelayCommand ClearCartCommand { get; }
    public IRelayCommand AddPaymentCommand { get; }
    public IRelayCommand AddSplitPaymentCommand { get; }
    public IRelayCommand SetCashToTotalCommand { get; }
    public IRelayCommand<PosPaymentDraftViewModel> RemovePaymentCommand { get; }
    public IRelayCommand OpenPaymentCommand { get; }
    public IAsyncRelayCommand CheckoutCommand { get; }
    public IAsyncRelayCommand RefreshSalesCommand { get; }

    public ObservableCollection<PosStockLocationItem> StockLocations { get; } = new();
    public ObservableCollection<PosCustomerItem> Customers { get; } = new();
    public ObservableCollection<PosProductSearchItem> SearchResults { get; } = new();
    public ObservableCollection<PosCartLineViewModel> Cart { get; } = new();
    public ObservableCollection<PosPaymentDraftViewModel> Payments { get; } = new();
    public ObservableCollection<SaleListItem> RecentSales { get; } = new();

    public IReadOnlyList<string> PaymentMethods { get; } = ["cash", "bank", "mobile", "credit"];

    public string WorkspaceTitle => Translate("Point of Sale", "فروش", "خرڅلاو");
    public string WorkspaceSubtitle => Translate(
        "Full-screen Point of Sale · FEFO batch pricing",
        "فروش تمام‌صفحه · قیمت‌گذاری FEFO",
        "بشپړ سکرین خرڅلاو · FEFO بیه");
    public string SearchLabel => Translate(
        "Scan barcode or search medicine, generic, code, strength…",
        "بارکد را اسکن کنید یا دوا، نام عمومی، کد و قوت را جستجو کنید…",
        "بارکوډ سکین کړئ یا درمل، عام نوم، کوډ او قوت ولټوئ…");
    public string CartTitle => Translate("Cart", "سبد فروش", "د خرڅلاو ټوکرۍ");
    public string PaymentTitle => Translate("Payments", "پرداخت‌ها", "تادیات");
    public string RecentSalesTitle => Translate("Recent invoices", "فاکتورهای اخیر", "وروستي بلونه");
    public string LocationLabel => Translate("Stock location", "محل موجودی", "د زېرمتون ځای");
    public string CustomerLabel => Translate("Customer", "مشتری", "پېرودونکی");
    public string CheckoutLabel => Translate("Complete sale", "تکمیل فروش", "خرڅلاو بشپړ کړئ");
    public string WalkInCustomerText => Translate("Walk-in customer", "مشتری حضوری", "عمومي پېرودونکی");
    public string SearchResultsTitle => Translate("Search results", "نتایج جستجو", "د لټون پایلې");

    public decimal Subtotal => ScaleMoney(Cart.Sum(x => x.EstimatedSubtotal));
    public decimal DiscountTotal => ScaleMoney(Cart.Sum(x => x.DiscountAmount));
    public decimal EstimatedGrandTotal => ScaleMoney(Math.Max(0m, Subtotal - DiscountTotal));
    public decimal PaymentTotal => ScaleMoney(Payments.Sum(x => Math.Max(0m, x.Amount)));
    public decimal CreditTotal => ScaleMoney(
        Payments
            .Where(x => string.Equals(x.Method, "credit", StringComparison.OrdinalIgnoreCase))
            .Sum(x => Math.Max(0m, x.Amount)));
    public decimal DueAmount => ScaleMoney(Math.Max(0m, EstimatedGrandTotal - PaymentTotal));
    public decimal ChangeAmount => ScaleMoney(Math.Max(0m, PaymentTotal - EstimatedGrandTotal));
    public bool RequiresPrescription => Cart.Any(x => x.Product.PrescriptionRequired);
    public bool HasShortage => Cart.Any(x => x.HasShortage);
    public bool HasInvalidCartLines => Cart.Any(x =>
        x.Quantity <= 0m ||
        x.DiscountAmount < 0m ||
        x.DiscountAmount > x.EstimatedSubtotal ||
        (x.OverridePrice && (!CanOverridePrice || x.UnitPrice is null or < 0m)));
    public bool CanOverridePrice => _permissions.HasPermission("pos.price_override");
    public bool CanDiscount => _permissions.HasPermission("pos.discount");
    public string TotalText => $"AFN {EstimatedGrandTotal:N2}";
    public string CartLineCountText => Translate(
        $"{Cart.Count} item line{(Cart.Count == 1 ? string.Empty : "s")}",
        $"{Cart.Count} قلم",
        $"{Cart.Count} کرښې");
    public string PaymentCustomerText =>
        SelectedCustomer?.Name ?? WalkInCustomerText;

    public string PaymentBalanceText => ChangeAmount > 0m
        ? Translate(
            $"Change AFN {ChangeAmount:N2}",
            $"باقی AFN {ChangeAmount:N2}",
            $"بېرته AFN {ChangeAmount:N2}")
        : Translate(
            $"Remaining AFN {DueAmount:N2}",
            $"باقیمانده AFN {DueAmount:N2}",
            $"پاتې AFN {DueAmount:N2}");

    public void SetLanguage(UiLanguage language)
    {
        _language = language;
        OnPropertyChanged(nameof(WorkspaceTitle));
        OnPropertyChanged(nameof(WorkspaceSubtitle));
        OnPropertyChanged(nameof(SearchLabel));
        OnPropertyChanged(nameof(CartTitle));
        OnPropertyChanged(nameof(PaymentTitle));
        OnPropertyChanged(nameof(RecentSalesTitle));
        OnPropertyChanged(nameof(LocationLabel));
        OnPropertyChanged(nameof(CustomerLabel));
        OnPropertyChanged(nameof(CheckoutLabel));
        OnPropertyChanged(nameof(WalkInCustomerText));
        OnPropertyChanged(nameof(PaymentCustomerText));
        OnPropertyChanged(nameof(SearchResultsTitle));
        RaiseCartState(autoFillSingleCash: false);
    }

    public async Task LoadAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        NotifyCommands();

        try
        {
            var references = await _pos.GetReferenceDataAsync();

            StockLocations.Clear();
            foreach (var location in references.StockLocations)
            {
                StockLocations.Add(location);
            }

            Customers.Clear();
            foreach (var customer in references.Customers)
            {
                Customers.Add(customer);
            }

            SelectedLocation ??= StockLocations.FirstOrDefault(x => x.IsDefault)
                ?? StockLocations.FirstOrDefault();

            await RefreshSalesCoreAsync();

            StatusMessage = Translate(
                "POS ready. Scan a barcode or search a medicine.",
                "فروش آماده است. بارکد را اسکن کنید یا دوا را جستجو کنید.",
                "خرڅلاو چمتو دی. بارکوډ سکین کړئ یا درمل ولټوئ.");

            RequestSearchFocus();
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

    partial void OnSelectedLocationChanged(PosStockLocationItem? value)
    {
        SearchResults.Clear();
        IsSearchDropdownOpen = false;

        if (Cart.Count > 0)
        {
            ClearCartCore(resetPayments: true);
        }

        SearchCommand.NotifyCanExecuteChanged();
        NotifyCommands();
        RequestSearchFocus();
    }

    partial void OnSelectedCustomerChanged(PosCustomerItem? value)
    {
        OnPropertyChanged(nameof(WalkInCustomerText));
        OnPropertyChanged(nameof(PaymentCustomerText));
        NotifyCommands();
    }

    partial void OnSelectedProductChanged(PosProductSearchItem? value)
    {
        OverrideUnitPrice = value?.FefoPrice;
        NotifyCommands();
    }

    partial void OnSearchTextChanged(string value)
    {
        SearchCommand.NotifyCanExecuteChanged();

        if (_suppressSearchTextChanged)
        {
            return;
        }

        var query = value.Trim();
        if (query.Length == 0)
        {
            SearchResults.Clear();
            IsSearchDropdownOpen = false;
            return;
        }

        if (query.Length >= 2 && SelectedLocation is not null)
        {
            _ = DebouncedSearchAsync(query);
        }
    }

    partial void OnQuantityChanged(decimal value) => NotifyCommands();
    partial void OnDiscountAmountChanged(decimal value) => NotifyCommands();
    partial void OnPaymentAmountChanged(decimal value) => NotifyCommands();
    partial void OnSelectedPaymentMethodChanged(string value) => NotifyCommands();
    partial void OnOverridePriceChanged(bool value) => NotifyCommands();
    partial void OnPrescriptionReferenceChanged(string value) => NotifyCommands();

    private async Task DebouncedSearchAsync(string query)
    {
        try
        {
            await Task.Delay(180);

            if (!string.Equals(SearchText.Trim(), query, StringComparison.Ordinal) ||
                SelectedLocation is null ||
                IsBusy)
            {
                return;
            }

            await SearchProductsCoreAsync(query, autoAddExactBarcode: true);
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
        }
    }

    private async Task SearchAsync()
    {
        var query = SearchText.Trim();
        if (query.Length == 0 || SelectedLocation is null)
        {
            return;
        }

        await SearchProductsCoreAsync(query, autoAddExactBarcode: true);
    }

    private async Task SearchProductsCoreAsync(
        string query,
        bool autoAddExactBarcode)
    {
        if (SelectedLocation is null || string.IsNullOrWhiteSpace(query))
        {
            return;
        }

        var locationId = SelectedLocation.Id;
        IsSearching = true;

        try
        {
            var results = await _pos.SearchProductsAsync(
                new PosProductSearchFilter(
                    query,
                    locationId,
                    20));

            if (SelectedLocation?.Id != locationId)
            {
                return;
            }

            // Barcode scanners often append Enter, while some do not. The
            // debounced lookup and Enter command may overlap, so only the query
            // that is still current may update results or add a scanned item.
            if (!string.Equals(SearchText.Trim(), query, StringComparison.Ordinal))
            {
                return;
            }

            var exactBarcode = results.FirstOrDefault(item =>
                !string.IsNullOrWhiteSpace(item.Barcode) &&
                string.Equals(item.Barcode.Trim(), query, StringComparison.OrdinalIgnoreCase));

            if (autoAddExactBarcode && exactBarcode is not null)
            {
                SearchResults.Clear();
                IsSearchDropdownOpen = false;
                AddProductAsIndependentLine(
                    exactBarcode,
                    1m,
                    unitPrice: null,
                    overridePrice: false,
                    discount: 0m,
                    scanned: true);
                return;
            }

            SearchResults.Clear();
            foreach (var item in results)
            {
                SearchResults.Add(item);
            }

            SelectedProduct = SearchResults.FirstOrDefault();
            IsSearchDropdownOpen = SearchResults.Count > 0;

            StatusMessage = SearchResults.Count == 0
                ? Translate(
                    "No sellable stock found.",
                    "موجودی قابل فروش یافت نشد.",
                    "د خرڅلاو وړ زېرمه ونه موندل شوه.")
                : Translate(
                    $"{SearchResults.Count} medicine(s) found. Choose from the dropdown.",
                    $"{SearchResults.Count} دوا یافت شد. از فهرست انتخاب کنید.",
                    $"{SearchResults.Count} درمل وموندل شول. له لېست څخه یې وټاکئ.");
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
            SearchResults.Clear();
            IsSearchDropdownOpen = false;
        }
        finally
        {
            IsSearching = false;
        }
    }

    private void AddSearchResult(PosProductSearchItem? item)
    {
        if (item is null)
        {
            return;
        }

        AddProductAsIndependentLine(
            item,
            1m,
            unitPrice: null,
            overridePrice: false,
            discount: 0m,
            scanned: false);
    }

    private bool CanAddToCart()
    {
        if (IsBusy || SelectedProduct is null)
        {
            return false;
        }

        var roundedQuantity = ScaleQuantity(Quantity);
        if (roundedQuantity <= 0m ||
            DiscountAmount < 0m ||
            CurrentCartQuantity(SelectedProduct.Id) + roundedQuantity > SelectedProduct.AvailableQuantity)
        {
            return false;
        }

        if (OverridePrice &&
            (!CanOverridePrice || OverrideUnitPrice is null or < 0m))
        {
            return false;
        }

        if (DiscountAmount > 0m && !CanDiscount)
        {
            return false;
        }

        var estimatedSubtotal = OverridePrice
            ? ScaleMoney(roundedQuantity * (OverrideUnitPrice ?? 0m))
            : EstimateStandardSubtotal(
                SelectedProduct,
                roundedQuantity,
                CurrentCartQuantity(SelectedProduct.Id));

        return ScaleMoney(DiscountAmount) <= estimatedSubtotal;
    }

    private void AddToCart()
    {
        var product = SelectedProduct;
        if (product is null)
        {
            return;
        }

        AddProductAsIndependentLine(
            product,
            ScaleQuantity(Quantity),
            OverridePrice ? OverrideUnitPrice : null,
            OverridePrice,
            ScaleMoney(DiscountAmount),
            scanned: false);

        Quantity = 1m;
        DiscountAmount = 0m;
        OverridePrice = false;
        OverrideUnitPrice = product.FefoPrice;
    }

    private void AddProductAsIndependentLine(
        PosProductSearchItem product,
        decimal quantityToAdd,
        decimal? unitPrice,
        bool overridePrice,
        decimal discount,
        bool scanned)
    {
        var roundedQuantity = ScaleQuantity(quantityToAdd);
        if (roundedQuantity <= 0m)
        {
            return;
        }

        var totalAfterAdd = CurrentCartQuantity(product.Id) + roundedQuantity;
        if (totalAfterAdd > product.AvailableQuantity)
        {
            StatusMessage = Translate(
                $"Cannot add {product.BrandName}. Requested cart quantity {totalAfterAdd:0.####} exceeds sellable stock {product.AvailableQuantity:0.####}.",
                $"امکان افزودن {product.BrandName} نیست. مقدار سبد از موجودی قابل فروش بیشتر است.",
                $"{product.BrandName} نشي زیاتېدلی. د ټوکرۍ مقدار له شته زېرمه زیات دی.");
            RequestSearchFocus();
            return;
        }

        var line = new PosCartLineViewModel(
            product,
            roundedQuantity,
            unitPrice,
            overridePrice,
            discount);

        TrackCartLine(line);
        Cart.Add(line);

        if (product.PrescriptionRequired &&
            string.IsNullOrWhiteSpace(PrescriptionDateText))
        {
            PrescriptionDateText = _clock.UtcNow
                .ToOffset(TimeSpan.FromMinutes(270))
                .ToString("yyyy-MM-dd");
        }

        ClearSearchDraft();
        RecalculateFefoPlans();
        RaiseCartState(autoFillSingleCash: true);

        StatusMessage = scanned
            ? Translate(
                $"{product.BrandName} scanned and added as a new cart line.",
                $"{product.BrandName} اسکن شد و به‌عنوان قلم جداگانه افزوده شد.",
                $"{product.BrandName} سکین او د جلا کرښې په توګه ټوکرۍ ته زیات شو.")
            : Translate(
                $"{product.BrandName} added to cart.",
                $"{product.BrandName} به سبد افزوده شد.",
                $"{product.BrandName} ټوکرۍ ته زیات شو.");

        RequestSearchFocus();
    }

    private void RemoveFromCart(PosCartLineViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        UntrackCartLine(item);
        Cart.Remove(item);
        RecalculateFefoPlans();
        RaiseCartState(autoFillSingleCash: true);
        RequestSearchFocus();
    }

    private void ClearCart()
    {
        ClearCartCore(resetPayments: true);
        StatusMessage = Translate(
            "Cart cleared.",
            "سبد فروش پاک شد.",
            "د خرڅلاو ټوکرۍ پاکه شوه.");
        RequestSearchFocus();
    }

    private void ClearCartCore(bool resetPayments)
    {
        foreach (var item in Cart)
        {
            UntrackCartLine(item);
        }

        Cart.Clear();
        ClearSearchDraft();
        SaleNotes = string.Empty;
        PrescriptionReference = string.Empty;
        PrescriberName = string.Empty;
        PrescriptionDateText = string.Empty;

        if (resetPayments)
        {
            ResetPaymentsToCash();
        }

        RaiseCartState(autoFillSingleCash: false);
    }

    private void TrackCartLine(PosCartLineViewModel item) =>
        item.PropertyChanged += OnCartLinePropertyChanged;

    private void UntrackCartLine(PosCartLineViewModel item) =>
        item.PropertyChanged -= OnCartLinePropertyChanged;

    private void OnCartLinePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PosCartLineViewModel.Quantity) or
            nameof(PosCartLineViewModel.UnitPrice) or
            nameof(PosCartLineViewModel.OverridePrice) or
            nameof(PosCartLineViewModel.DiscountAmount))
        {
            RecalculateFefoPlans();
            RaiseCartState(autoFillSingleCash: true);
        }
    }

    private void RecalculateFefoPlans()
    {
        var consumedByBatch = new Dictionary<string, decimal>(StringComparer.Ordinal);

        foreach (var line in Cart)
        {
            var remaining = Math.Max(0m, line.Quantity);
            decimal fefoSubtotal = 0m;
            var parts = new List<string>();

            foreach (var batch in line.Product.Batches)
            {
                if (remaining <= 0m)
                {
                    break;
                }

                consumedByBatch.TryGetValue(batch.Id, out var consumed);
                var available = Math.Max(0m, batch.AvailableQuantity - consumed);
                if (available <= 0m)
                {
                    continue;
                }

                var take = Math.Min(remaining, available);
                consumedByBatch[batch.Id] = consumed + take;
                remaining -= take;
                fefoSubtotal += take * batch.SalePrice;

                var expiry = batch.ExpiresAt is null
                    ? string.Empty
                    : Translate(
                        $" · exp {batch.ExpiresAt:dd MMM yyyy}",
                        $" · انقضا {batch.ExpiresAt:dd MMM yyyy}",
                        $" · تاریخ تېر {batch.ExpiresAt:dd MMM yyyy}");
                parts.Add(
                    $"{batch.BatchNumber ?? "Unbatched"}: {take:0.####} × AFN {batch.SalePrice:N2}{expiry}");
            }

            line.FefoSubtotal = ScaleMoney(fefoSubtotal);
            line.HasShortage = remaining > 0m;
            if (remaining > 0m)
            {
                parts.Add($"Short {remaining:0.####}");
            }

            line.FefoPlanText = parts.Count == 0
                ? Translate(
                    "No eligible batch",
                    "بچ واجد شرایط وجود ندارد",
                    "وړ بېچ نشته")
                : string.Join("  |  ", parts);
        }
    }

    private decimal CurrentCartQuantity(string medicineId) =>
        ScaleQuantity(
            Cart
                .Where(x => string.Equals(
                    x.Product.Id,
                    medicineId,
                    StringComparison.Ordinal))
                .Sum(x => Math.Max(0m, x.Quantity)));

    private static decimal EstimateStandardSubtotal(
        PosProductSearchItem product,
        decimal quantity,
        decimal alreadyReserved)
    {
        var skip = Math.Max(0m, alreadyReserved);
        var remaining = Math.Max(0m, quantity);
        decimal subtotal = 0m;

        foreach (var batch in product.Batches)
        {
            if (remaining <= 0m)
            {
                break;
            }

            var available = Math.Max(0m, batch.AvailableQuantity);
            if (skip > 0m)
            {
                var skipped = Math.Min(skip, available);
                skip -= skipped;
                available -= skipped;
            }

            if (available <= 0m)
            {
                continue;
            }

            var take = Math.Min(remaining, available);
            subtotal += take * batch.SalePrice;
            remaining -= take;
        }

        return ScaleMoney(subtotal);
    }

    private bool CanAddPayment() =>
        !IsBusy &&
        ScaleMoney(PaymentAmount) > 0m &&
        PaymentMethods.Contains(SelectedPaymentMethod, StringComparer.OrdinalIgnoreCase) &&
        Payments.Count < 10;

    private void AddPayment()
    {
        var payment = new PosPaymentDraftViewModel(
            SelectedPaymentMethod,
            ScaleMoney(PaymentAmount),
            string.IsNullOrWhiteSpace(PaymentReference) ? null : PaymentReference.Trim());

        TrackPayment(payment);
        Payments.Add(payment);

        PaymentAmount = 0m;
        PaymentReference = string.Empty;
        RaisePaymentState();
    }

    private void AddSplitPayment()
    {
        if (Payments.Count >= 10)
        {
            return;
        }

        var payment = new PosPaymentDraftViewModel("cash", 0m, null);
        TrackPayment(payment);
        Payments.Add(payment);
        RaisePaymentState();
    }

    private void RemovePayment(PosPaymentDraftViewModel? item)
    {
        if (item is null || Payments.Count <= 1)
        {
            return;
        }

        UntrackPayment(item);
        Payments.Remove(item);
        RaisePaymentState();
    }

    private void SetCashToTotal()
    {
        var cash = Payments.FirstOrDefault(x =>
            string.Equals(x.Method, "cash", StringComparison.OrdinalIgnoreCase));

        if (cash is null)
        {
            if (Payments.Count >= 10)
            {
                return;
            }

            cash = new PosPaymentDraftViewModel("cash", 0m, null);
            TrackPayment(cash);
            Payments.Add(cash);
        }

        var other = ScaleMoney(Payments.Where(x => !ReferenceEquals(x, cash)).Sum(x => Math.Max(0m, x.Amount)));
        cash.Amount = ScaleMoney(Math.Max(0m, EstimatedGrandTotal - other));
        RaisePaymentState();
    }

    private void ResetPaymentsToCash()
    {
        foreach (var payment in Payments)
        {
            UntrackPayment(payment);
        }

        Payments.Clear();

        var cash = new PosPaymentDraftViewModel("cash", EstimatedGrandTotal, null);
        TrackPayment(cash);
        Payments.Add(cash);
        RaisePaymentState();
    }

    private void TrackPayment(PosPaymentDraftViewModel payment) =>
        payment.PropertyChanged += OnPaymentPropertyChanged;

    private void UntrackPayment(PosPaymentDraftViewModel payment) =>
        payment.PropertyChanged -= OnPaymentPropertyChanged;

    private void OnPaymentPropertyChanged(object? sender, PropertyChangedEventArgs e) =>
        RaisePaymentState();

    private void FillSingleCashToTotal()
    {
        if (Payments.Count != 1 ||
            !string.Equals(Payments[0].Method, "cash", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Payments[0].Amount = EstimatedGrandTotal;
    }

    private bool CanOpenPayment() =>
        !IsBusy &&
        SelectedLocation is not null &&
        Cart.Count > 0 &&
        !HasShortage &&
        !HasInvalidCartLines &&
        (!RequiresPrescription || !string.IsNullOrWhiteSpace(PrescriptionReference));

    private void OpenPayment()
    {
        if (!CanOpenPayment())
        {
            return;
        }

        PrintInvoiceAfterPayment = false;
        FillSingleCashToTotal();
        RaisePaymentState();
        PaymentRequested?.Invoke(this, EventArgs.Empty);
    }

    private bool CanCheckout()
    {
        if (IsBusy ||
            SelectedLocation is null ||
            Cart.Count == 0 ||
            Payments.Count == 0 ||
            HasShortage ||
            HasInvalidCartLines ||
            Payments.Any(x => x.Amount <= 0m) ||
            PaymentTotal < EstimatedGrandTotal)
        {
            return false;
        }

        if (RequiresPrescription &&
            string.IsNullOrWhiteSpace(PrescriptionReference))
        {
            return false;
        }

        var hasCredit = CreditTotal > 0m;
        if (hasCredit)
        {
            if (SelectedCustomer is null ||
                CreditTotal > SelectedCustomer.CreditLimit ||
                PaymentTotal > EstimatedGrandTotal)
            {
                return false;
            }
        }

        if (ChangeAmount > 0m &&
            !Payments.Any(x => string.Equals(
                x.Method,
                "cash",
                StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return true;
    }

    private async Task CheckoutAsync()
    {
        if (SelectedLocation is null)
        {
            return;
        }

        var shouldPrint = PrintInvoiceAfterPayment;
        string? printError = null;
        bool? printed = null;
        SaleDetail? completedSale = null;

        await ExecuteBusyAsync(async () =>
        {
            var sale = await _pos.CheckoutAsync(
                new PosCheckoutRequest(
                    SelectedLocation.Id,
                    SelectedCustomer?.Id,
                    Guid.NewGuid().ToString("N"),
                    SaleNotes,
                    PrescriptionReference,
                    PrescriberName,
                    ParseOptionalDate(PrescriptionDateText),
                    Cart.Select(x => new PosCheckoutLineRequest(
                        x.Product.Id,
                        x.Quantity,
                        x.UnitPrice,
                        x.OverridePrice,
                        x.DiscountAmount)).ToList(),
                    Payments.Select(x => new PosPaymentRequest(
                        x.Method,
                        x.Amount,
                        x.Reference)).ToList()));

            completedSale = sale;
            LastSale = sale;

            if (shouldPrint)
            {
                try
                {
                    printed = _receiptPrinter.Print(sale);
                }
                catch (Exception exception)
                {
                    printError = exception.Message;
                }
            }

            foreach (var item in Cart)
            {
                UntrackCartLine(item);
            }

            Cart.Clear();
            ClearSearchDraft();
            SaleNotes = string.Empty;
            PrescriptionReference = string.Empty;
            PrescriberName = string.Empty;
            PrescriptionDateText = string.Empty;
            PaymentAmount = 0m;
            SelectedCustomer = null;
            PrintInvoiceAfterPayment = false;
            ResetPaymentsToCash();

            await RefreshSalesCoreAsync();
            RaiseCartState(autoFillSingleCash: false);

            StatusMessage = printError is not null
                ? Translate(
                    $"Sale {sale.Sale.SaleNumber} completed, but printing failed: {printError}",
                    $"فروش {sale.Sale.SaleNumber} تکمیل شد، اما چاپ ناموفق بود: {printError}",
                    $"خرڅلاو {sale.Sale.SaleNumber} بشپړ شو، خو چاپ ناکام شو: {printError}")
                : shouldPrint && printed == false
                    ? Translate(
                        $"Sale {sale.Sale.SaleNumber} completed; printing was cancelled.",
                        $"فروش {sale.Sale.SaleNumber} تکمیل شد؛ چاپ لغو شد.",
                        $"خرڅلاو {sale.Sale.SaleNumber} بشپړ شو؛ چاپ لغوه شو.")
                    : Translate(
                        shouldPrint
                            ? $"Sale {sale.Sale.SaleNumber} completed and printed."
                            : $"Sale {sale.Sale.SaleNumber} completed.",
                        shouldPrint
                            ? $"فروش {sale.Sale.SaleNumber} تکمیل و چاپ شد."
                            : $"فروش {sale.Sale.SaleNumber} تکمیل شد.",
                        shouldPrint
                            ? $"خرڅلاو {sale.Sale.SaleNumber} بشپړ او چاپ شو."
                            : $"خرڅلاو {sale.Sale.SaleNumber} بشپړ شو.");

            RequestSearchFocus();
        });

        if (completedSale is not null)
        {
            PaymentCloseRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task RefreshSalesAsync() =>
        await ExecuteBusyAsync(RefreshSalesCoreAsync);

    private async Task RefreshSalesCoreAsync()
    {
        var today = DateOnly.FromDateTime(
            _clock.UtcNow.ToOffset(TimeSpan.FromMinutes(270)).DateTime);

        var sales = await _pos.SearchSalesAsync(
            new SaleSearchFilter(
                From: today.AddDays(-30),
                To: today,
                Take: 25));

        RecentSales.Clear();
        foreach (var sale in sales)
        {
            RecentSales.Add(sale);
        }
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

    private void ClearSearchDraft()
    {
        _suppressSearchTextChanged = true;
        try
        {
            SearchText = string.Empty;
        }
        finally
        {
            _suppressSearchTextChanged = false;
        }

        SearchResults.Clear();
        IsSearchDropdownOpen = false;
        SelectedProduct = null;
    }

    private void RaiseCartState(bool autoFillSingleCash)
    {
        OnPropertyChanged(nameof(Subtotal));
        OnPropertyChanged(nameof(DiscountTotal));
        OnPropertyChanged(nameof(EstimatedGrandTotal));
        OnPropertyChanged(nameof(TotalText));
        OnPropertyChanged(nameof(CartLineCountText));
        OnPropertyChanged(nameof(RequiresPrescription));
        OnPropertyChanged(nameof(HasShortage));
        OnPropertyChanged(nameof(HasInvalidCartLines));

        if (autoFillSingleCash)
        {
            FillSingleCashToTotal();
        }

        RaisePaymentState();
        ClearCartCommand.NotifyCanExecuteChanged();
        SetCashToTotalCommand.NotifyCanExecuteChanged();
    }

    private void RaisePaymentState()
    {
        OnPropertyChanged(nameof(PaymentTotal));
        OnPropertyChanged(nameof(CreditTotal));
        OnPropertyChanged(nameof(DueAmount));
        OnPropertyChanged(nameof(ChangeAmount));
        OnPropertyChanged(nameof(PaymentBalanceText));
        NotifyCommands();
    }

    private void NotifyCommands()
    {
        LoadCommand.NotifyCanExecuteChanged();
        FocusCustomerCommand.NotifyCanExecuteChanged();
        SearchCommand.NotifyCanExecuteChanged();
        AddSearchResultCommand.NotifyCanExecuteChanged();
        AddToCartCommand.NotifyCanExecuteChanged();
        RemoveFromCartCommand.NotifyCanExecuteChanged();
        ClearCartCommand.NotifyCanExecuteChanged();
        AddPaymentCommand.NotifyCanExecuteChanged();
        AddSplitPaymentCommand.NotifyCanExecuteChanged();
        SetCashToTotalCommand.NotifyCanExecuteChanged();
        RemovePaymentCommand.NotifyCanExecuteChanged();
        OpenPaymentCommand.NotifyCanExecuteChanged();
        CheckoutCommand.NotifyCanExecuteChanged();
        RefreshSalesCommand.NotifyCanExecuteChanged();
    }

    private void RequestSearchFocus() =>
        SearchFocusRequested?.Invoke(this, EventArgs.Empty);

    private static decimal ScaleQuantity(decimal value) =>
        decimal.Round(value, 4, MidpointRounding.AwayFromZero);

    private static decimal ScaleMoney(decimal value) =>
        decimal.Round(value, 4, MidpointRounding.AwayFromZero);

    private static DateOnly? ParseOptionalDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateOnly.TryParse(value.Trim(), out var parsed))
        {
            return parsed;
        }

        throw new ArgumentException("Prescription date must be a valid date.");
    }

    private string Translate(string english, string dari, string pashto) =>
        _language.Code switch
        {
            "fa" => dari,
            "ps" => pashto,
            _ => english,
        };
}

public sealed partial class PosCartLineViewModel : ObservableObject
{
    [ObservableProperty] private decimal quantity;
    [ObservableProperty] private decimal? unitPrice;
    [ObservableProperty] private bool overridePrice;
    [ObservableProperty] private decimal discountAmount;
    [ObservableProperty] private decimal fefoSubtotal;
    [ObservableProperty] private string fefoPlanText = string.Empty;
    [ObservableProperty] private bool hasShortage;

    public PosCartLineViewModel(
        PosProductSearchItem product,
        decimal quantity,
        decimal? unitPrice,
        bool overridePrice,
        decimal discountAmount)
    {
        Product = product;
        this.quantity = quantity;
        this.unitPrice = unitPrice;
        this.overridePrice = overridePrice;
        this.discountAmount = discountAmount;
    }

    public PosProductSearchItem Product { get; }

    public string MedicineLabel => string.Join(
        " ",
        new[] { Product.BrandName, Product.Strength }
            .Where(x => !string.IsNullOrWhiteSpace(x)));

    public string SecondaryLabel => string.Join(
        " · ",
        new[]
        {
            Product.GenericName,
            Product.MedicineCode,
            Product.SaleUnit,
            $"Available {Product.AvailableQuantity:0.####}",
        }.Where(x => !string.IsNullOrWhiteSpace(x)));

    public decimal EstimatedSubtotal
    {
        get
        {
            if (OverridePrice)
            {
                return decimal.Round(
                    Quantity * (UnitPrice ?? 0m),
                    4,
                    MidpointRounding.AwayFromZero);
            }

            return decimal.Round(
                FefoSubtotal,
                4,
                MidpointRounding.AwayFromZero);
        }
    }

    public decimal DisplayUnitPrice =>
        Quantity <= 0m
            ? 0m
            : decimal.Round(
                EstimatedSubtotal / Quantity,
                4,
                MidpointRounding.AwayFromZero);

    public decimal EstimatedLineTotal => decimal.Round(
        Math.Max(0m, EstimatedSubtotal - DiscountAmount),
        4,
        MidpointRounding.AwayFromZero);

    partial void OnQuantityChanged(decimal value) => RaiseComputedValues();
    partial void OnUnitPriceChanged(decimal? value) => RaiseComputedValues();
    partial void OnOverridePriceChanged(bool value) => RaiseComputedValues();
    partial void OnDiscountAmountChanged(decimal value) => RaiseComputedValues();
    partial void OnFefoSubtotalChanged(decimal value) => RaiseComputedValues();

    private void RaiseComputedValues()
    {
        OnPropertyChanged(nameof(EstimatedSubtotal));
        OnPropertyChanged(nameof(DisplayUnitPrice));
        OnPropertyChanged(nameof(EstimatedLineTotal));
    }
}

public sealed partial class PosPaymentDraftViewModel : ObservableObject
{
    [ObservableProperty] private string method;
    [ObservableProperty] private decimal amount;
    [ObservableProperty] private string? reference;

    public PosPaymentDraftViewModel(
        string method,
        decimal amount,
        string? reference)
    {
        this.method = method;
        this.amount = amount;
        this.reference = reference;
    }
}
