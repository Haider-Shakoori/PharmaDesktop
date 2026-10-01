using System.Collections.ObjectModel;
using BusinessOS.Pharmacy.Application.Abstractions.Authentication;
using BusinessOS.Pharmacy.Application.Abstractions.Sales;
using BusinessOS.Pharmacy.Application.Abstractions.Time;
using BusinessOS.Pharmacy.Desktop.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BusinessOS.Pharmacy.Desktop.Pos;

public sealed partial class PosViewModel : ObservableObject
{
    private readonly IPosService _pos;
    private readonly IPermissionAuthorizer _permissions;
    private readonly IClock _clock;
    private UiLanguage _language = UiLanguageCatalog.All[0];

    [ObservableProperty] private bool isBusy;
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
    [ObservableProperty] private SaleDetail? lastSale;

    public PosViewModel(
        IPosService pos,
        IPermissionAuthorizer permissions,
        IClock clock)
    {
        _pos = pos;
        _permissions = permissions;
        _clock = clock;

        LoadCommand = new AsyncRelayCommand(LoadAsync, () => !IsBusy);
        SearchCommand = new AsyncRelayCommand(SearchAsync, () => !IsBusy && SelectedLocation is not null);
        AddToCartCommand = new RelayCommand(AddToCart, CanAddToCart);
        RemoveFromCartCommand = new RelayCommand<PosCartLineViewModel>(RemoveFromCart, x => !IsBusy && x is not null);
        AddPaymentCommand = new RelayCommand(AddPayment, CanAddPayment);
        RemovePaymentCommand = new RelayCommand<PosPaymentDraftViewModel>(RemovePayment, x => !IsBusy && x is not null);
        CheckoutCommand = new AsyncRelayCommand(CheckoutAsync, CanCheckout);
        RefreshSalesCommand = new AsyncRelayCommand(RefreshSalesAsync, () => !IsBusy);
    }

    public IAsyncRelayCommand LoadCommand { get; }
    public IAsyncRelayCommand SearchCommand { get; }
    public IRelayCommand AddToCartCommand { get; }
    public IRelayCommand<PosCartLineViewModel> RemoveFromCartCommand { get; }
    public IRelayCommand AddPaymentCommand { get; }
    public IRelayCommand<PosPaymentDraftViewModel> RemovePaymentCommand { get; }
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
    public string SearchLabel => Translate("Medicine / code / barcode", "دوا / کد / بارکد", "درمل / کوډ / بارکوډ");
    public string CartTitle => Translate("Current sale", "فروش جاری", "اوسنی خرڅلاو");
    public string PaymentTitle => Translate("Settlement", "پرداخت", "تادیه");
    public string RecentSalesTitle => Translate("Recent invoices", "فاکتورهای اخیر", "وروستي بلونه");
    public string LocationLabel => Translate("Stock location", "محل موجودی", "د زېرمتون ځای");
    public string CustomerLabel => Translate("Customer", "مشتری", "پېرودونکی");
    public string CheckoutLabel => Translate("Complete sale", "تکمیل فروش", "خرڅلاو بشپړ کړئ");
    public string TotalText => $"AFN {EstimatedGrandTotal:N4}";
    public decimal EstimatedGrandTotal => decimal.Round(
        Cart.Sum(x => x.EstimatedLineTotal),
        4,
        MidpointRounding.AwayFromZero);
    public bool CanOverridePrice => _permissions.HasPermission("pos.price_override");
    public bool CanDiscount => _permissions.HasPermission("pos.discount");

    public void SetLanguage(UiLanguage language)
    {
        _language = language;
        OnPropertyChanged(nameof(WorkspaceTitle));
        OnPropertyChanged(nameof(SearchLabel));
        OnPropertyChanged(nameof(CartTitle));
        OnPropertyChanged(nameof(PaymentTitle));
        OnPropertyChanged(nameof(RecentSalesTitle));
        OnPropertyChanged(nameof(LocationLabel));
        OnPropertyChanged(nameof(CustomerLabel));
        OnPropertyChanged(nameof(CheckoutLabel));
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
                "POS ready. Search stock to begin a sale.",
                "فروش آماده است. برای شروع دوا را جستجو کنید.",
                "خرڅلاو چمتو دی. د پیل لپاره درمل ولټوئ.");
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
        NotifyCommands();
    }

    partial void OnSelectedProductChanged(PosProductSearchItem? value)
    {
        OverrideUnitPrice = value?.FefoPrice;
        NotifyCommands();
    }

    partial void OnQuantityChanged(decimal value) => NotifyCommands();
    partial void OnDiscountAmountChanged(decimal value) => NotifyCommands();
    partial void OnPaymentAmountChanged(decimal value) => NotifyCommands();
    partial void OnSelectedPaymentMethodChanged(string value) => NotifyCommands();
    partial void OnOverridePriceChanged(bool value) => NotifyCommands();

    private async Task SearchAsync()
    {
        if (SelectedLocation is null)
        {
            return;
        }

        await ExecuteBusyAsync(async () =>
        {
            var results = await _pos.SearchProductsAsync(
                new PosProductSearchFilter(
                    SearchText,
                    SelectedLocation.Id,
                    20));

            SearchResults.Clear();
            foreach (var item in results)
            {
                SearchResults.Add(item);
            }

            SelectedProduct = SearchResults.FirstOrDefault();
            StatusMessage = SearchResults.Count == 0
                ? Translate("No sellable stock found.", "موجودی قابل فروش یافت نشد.", "د خرڅلاو وړ زېرمه ونه موندل شوه.")
                : Translate(
                    $"{SearchResults.Count} medicine(s) found.",
                    $"{SearchResults.Count} دوا یافت شد.",
                    $"{SearchResults.Count} درمل وموندل شول.");
        });
    }

    private bool CanAddToCart()
    {
        if (IsBusy || SelectedProduct is null)
        {
            return false;
        }

        var roundedQuantity = decimal.Round(
            Quantity,
            4,
            MidpointRounding.AwayFromZero);

        if (roundedQuantity <= 0m ||
            roundedQuantity > SelectedProduct.AvailableQuantity ||
            DiscountAmount < 0m)
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

        var draft = new PosCartLineViewModel(
            SelectedProduct,
            roundedQuantity,
            OverridePrice ? OverrideUnitPrice : null,
            OverridePrice,
            decimal.Round(DiscountAmount, 4, MidpointRounding.AwayFromZero));

        return draft.DiscountAmount <= draft.EstimatedSubtotal;
    }

    private void AddToCart()
    {
        if (SelectedProduct is null)
        {
            return;
        }

        Cart.Add(new PosCartLineViewModel(
            SelectedProduct,
            decimal.Round(Quantity, 4, MidpointRounding.AwayFromZero),
            OverridePrice ? OverrideUnitPrice : null,
            OverridePrice,
            decimal.Round(DiscountAmount, 4, MidpointRounding.AwayFromZero)));

        Quantity = 1m;
        DiscountAmount = 0m;
        OverridePrice = false;
        OverrideUnitPrice = SelectedProduct.FefoPrice;

        RaiseTotals();
        NotifyCommands();
    }

    private void RemoveFromCart(PosCartLineViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        Cart.Remove(item);
        RaiseTotals();
        NotifyCommands();
    }

    private bool CanAddPayment() =>
        !IsBusy &&
        decimal.Round(PaymentAmount, 4, MidpointRounding.AwayFromZero) > 0m &&
        PaymentMethods.Contains(SelectedPaymentMethod);

    private void AddPayment()
    {
        Payments.Add(new PosPaymentDraftViewModel(
            SelectedPaymentMethod,
            decimal.Round(PaymentAmount, 4, MidpointRounding.AwayFromZero),
            string.IsNullOrWhiteSpace(PaymentReference) ? null : PaymentReference.Trim()));

        PaymentAmount = 0m;
        PaymentReference = string.Empty;
        NotifyCommands();
    }

    private void RemovePayment(PosPaymentDraftViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        Payments.Remove(item);
        NotifyCommands();
    }

    private bool CanCheckout() =>
        !IsBusy &&
        SelectedLocation is not null &&
        Cart.Count > 0 &&
        Payments.Count > 0;

    private async Task CheckoutAsync()
    {
        if (SelectedLocation is null)
        {
            return;
        }

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

            LastSale = sale;
            Cart.Clear();
            Payments.Clear();
            SearchResults.Clear();
            SearchText = string.Empty;
            SaleNotes = string.Empty;
            PrescriptionReference = string.Empty;
            PrescriberName = string.Empty;
            PrescriptionDateText = string.Empty;
            PaymentAmount = 0m;
            SelectedCustomer = null;

            await RefreshSalesCoreAsync();
            RaiseTotals();

            StatusMessage = Translate(
                $"Sale {sale.Sale.SaleNumber} completed.",
                $"فروش {sale.Sale.SaleNumber} تکمیل شد.",
                $"خرڅلاو {sale.Sale.SaleNumber} بشپړ شو.");
        });
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

    private void RaiseTotals()
    {
        OnPropertyChanged(nameof(EstimatedGrandTotal));
        OnPropertyChanged(nameof(TotalText));
    }

    private void NotifyCommands()
    {
        LoadCommand.NotifyCanExecuteChanged();
        SearchCommand.NotifyCanExecuteChanged();
        AddToCartCommand.NotifyCanExecuteChanged();
        RemoveFromCartCommand.NotifyCanExecuteChanged();
        AddPaymentCommand.NotifyCanExecuteChanged();
        RemovePaymentCommand.NotifyCanExecuteChanged();
        CheckoutCommand.NotifyCanExecuteChanged();
        RefreshSalesCommand.NotifyCanExecuteChanged();
    }

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

public sealed record PosCartLineViewModel(
    PosProductSearchItem Product,
    decimal Quantity,
    decimal? UnitPrice,
    bool OverridePrice,
    decimal DiscountAmount)
{
    public string MedicineLabel => string.Join(
        " ",
        new[] { Product.BrandName, Product.Strength }
            .Where(x => !string.IsNullOrWhiteSpace(x)));

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

            var remaining = Quantity;
            decimal subtotal = 0m;

            foreach (var batch in Product.Batches)
            {
                if (remaining <= 0m)
                {
                    break;
                }

                var allocated = Math.Min(remaining, batch.AvailableQuantity);
                subtotal += allocated * batch.SalePrice;
                remaining -= allocated;
            }

            return decimal.Round(
                subtotal,
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
}

public sealed record PosPaymentDraftViewModel(
    string Method,
    decimal Amount,
    string? Reference);
