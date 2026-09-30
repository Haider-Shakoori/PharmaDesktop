using System.Collections.ObjectModel;
using BusinessOS.Pharmacy.Application.Abstractions.Sales;
using BusinessOS.Pharmacy.Desktop.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BusinessOS.Pharmacy.Desktop.Returns;

public sealed partial class ReturnsViewModel : ObservableObject
{
    private readonly ISaleReturnService _returns;
    private UiLanguage _language = UiLanguageCatalog.All[0];
    private string _pendingIdempotencyKey = Guid.NewGuid().ToString("N");

    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string statusMessage = string.Empty;
    [ObservableProperty] private string saleSearch = string.Empty;
    [ObservableProperty] private SaleListItem? selectedSale;
    [ObservableProperty] private ReturnableSaleDetail? selectedSaleDetail;
    [ObservableProperty] private ReturnableSaleLineItem? selectedLine;
    [ObservableProperty] private decimal returnQuantity;
    [ObservableProperty] private string reason = string.Empty;
    [ObservableProperty] private string selectedRefundMethod = "cash";
    [ObservableProperty] private decimal refundAmount;
    [ObservableProperty] private string refundReference = string.Empty;
    [ObservableProperty] private SaleReturnDetail? lastReturn;

    public ReturnsViewModel(ISaleReturnService returns)
    {
        _returns = returns;
        LoadCommand = new AsyncRelayCommand(LoadAsync, () => !IsBusy);
        SearchSalesCommand = new AsyncRelayCommand(SearchSalesAsync, () => !IsBusy);
        LoadSelectedSaleCommand = new AsyncRelayCommand(LoadSelectedSaleAsync, () => !IsBusy && SelectedSale is not null);
        AddReturnLineCommand = new RelayCommand(AddReturnLine, CanAddReturnLine);
        RemoveReturnLineCommand = new RelayCommand<ReturnLineDraftViewModel>(RemoveReturnLine, x => !IsBusy && x is not null);
        AddRefundCommand = new RelayCommand(AddRefund, CanAddRefund);
        RemoveRefundCommand = new RelayCommand<ReturnRefundDraftViewModel>(RemoveRefund, x => !IsBusy && x is not null);
        ProcessReturnCommand = new AsyncRelayCommand(ProcessReturnAsync, CanProcessReturn);
    }

    public IAsyncRelayCommand LoadCommand { get; }
    public IAsyncRelayCommand SearchSalesCommand { get; }
    public IAsyncRelayCommand LoadSelectedSaleCommand { get; }
    public IRelayCommand AddReturnLineCommand { get; }
    public IRelayCommand<ReturnLineDraftViewModel> RemoveReturnLineCommand { get; }
    public IRelayCommand AddRefundCommand { get; }
    public IRelayCommand<ReturnRefundDraftViewModel> RemoveRefundCommand { get; }
    public IAsyncRelayCommand ProcessReturnCommand { get; }

    public ObservableCollection<SaleListItem> Sales { get; } = new();
    public ObservableCollection<ReturnLineDraftViewModel> ReturnLines { get; } = new();
    public ObservableCollection<ReturnRefundDraftViewModel> Refunds { get; } = new();
    public ObservableCollection<SaleReturnListItem> RecentReturns { get; } = new();
    public IReadOnlyList<string> RefundMethods { get; } = ["cash", "bank", "mobile", "credit"];

    public string WorkspaceTitle => Translate("Sale Returns", "برگشت فروش", "د خرڅلاو بېرته ستنول");
    public string SaleTitle => Translate("Completed sale", "فروش تکمیل‌شده", "بشپړ شوی خرڅلاو");
    public string LinesTitle => Translate("Items to return", "اقلام برگشتی", "د ستنولو توکي");
    public string RefundsTitle => Translate("Refund settlement", "تسویه بازپرداخت", "د بېرته تادیې تصفیه");
    public string ProcessLabel => Translate("Complete return", "تکمیل برگشت", "ستنېدل بشپړ کړئ");
    public decimal CalculatedRefund => decimal.Round(ReturnLines.Sum(x => x.RefundAmount), 4, MidpointRounding.AwayFromZero);
    public string CalculatedRefundText => $"AFN {CalculatedRefund:N4}";

    public void SetLanguage(UiLanguage language)
    {
        _language = language;
        OnPropertyChanged(nameof(WorkspaceTitle));
        OnPropertyChanged(nameof(SaleTitle));
        OnPropertyChanged(nameof(LinesTitle));
        OnPropertyChanged(nameof(RefundsTitle));
        OnPropertyChanged(nameof(ProcessLabel));
    }

    public async Task LoadAsync() => await ExecuteBusyAsync(async () =>
    {
        await SearchSalesCoreAsync();
        await RefreshReturnsAsync();
        StatusMessage = Translate("Select a completed sale to process a return.", "برای برگشت، یک فروش تکمیل‌شده را انتخاب کنید.", "د بېرته ستنولو لپاره بشپړ شوی خرڅلاو وټاکئ.");
    });

    partial void OnSelectedSaleChanged(SaleListItem? value)
    {
        SelectedSaleDetail = null;
        SelectedLine = null;
        ReturnLines.Clear();
        Refunds.Clear();
        RaiseTotals();
        NotifyCommands();
    }
    partial void OnReturnQuantityChanged(decimal value) => NotifyCommands();
    partial void OnRefundAmountChanged(decimal value) => NotifyCommands();
    partial void OnSelectedRefundMethodChanged(string value) => NotifyCommands();

    private async Task SearchSalesAsync() => await ExecuteBusyAsync(SearchSalesCoreAsync);
    private async Task SearchSalesCoreAsync()
    {
        var results = await _returns.SearchReturnableSalesAsync(new SaleSearchFilter(Search: SaleSearch, Take: 100));
        Sales.Clear();
        foreach (var sale in results) Sales.Add(sale);
    }

    private async Task LoadSelectedSaleAsync()
    {
        if (SelectedSale is null) return;
        await ExecuteBusyAsync(async () =>
        {
            SelectedSaleDetail = await _returns.GetReturnableSaleAsync(SelectedSale.Id);
            SelectedLine = SelectedSaleDetail?.Lines.FirstOrDefault(x => x.RemainingQuantity > 0m);
            ReturnLines.Clear();
            Refunds.Clear();
            RaiseTotals();
        });
    }

    private bool CanAddReturnLine() => !IsBusy && SelectedLine is not null && ReturnQuantity > 0m && ReturnQuantity <= SelectedLine.RemainingQuantity && ReturnLines.All(x => x.Line.Id != SelectedLine.Id);
    private void AddReturnLine()
    {
        if (SelectedLine is null) return;
        ReturnLines.Add(new ReturnLineDraftViewModel(SelectedLine, decimal.Round(ReturnQuantity, 4, MidpointRounding.AwayFromZero)));
        ReturnQuantity = 0m;
        RaiseTotals();
        NotifyCommands();
    }
    private void RemoveReturnLine(ReturnLineDraftViewModel? item)
    {
        if (item is null) return;
        ReturnLines.Remove(item);
        RaiseTotals();
        NotifyCommands();
    }

    private bool CanAddRefund() => !IsBusy && RefundAmount > 0m && RefundMethods.Contains(SelectedRefundMethod);
    private void AddRefund()
    {
        Refunds.Add(new ReturnRefundDraftViewModel(SelectedRefundMethod, decimal.Round(RefundAmount, 4, MidpointRounding.AwayFromZero), string.IsNullOrWhiteSpace(RefundReference) ? null : RefundReference.Trim()));
        RefundAmount = 0m;
        RefundReference = string.Empty;
        NotifyCommands();
    }
    private void RemoveRefund(ReturnRefundDraftViewModel? item)
    {
        if (item is null) return;
        Refunds.Remove(item);
        NotifyCommands();
    }

    private bool CanProcessReturn() => !IsBusy && SelectedSale is not null && ReturnLines.Count > 0 && Refunds.Count > 0 && !string.IsNullOrWhiteSpace(Reason);
    private async Task ProcessReturnAsync()
    {
        if (SelectedSale is null) return;
        await ExecuteBusyAsync(async () =>
        {
            var result = await _returns.ProcessAsync(new ProcessSaleReturnRequest(
                SelectedSale.Id, _pendingIdempotencyKey, Reason,
                ReturnLines.Select(x => new ProcessSaleReturnLineRequest(x.Line.Id, x.Quantity)).ToList(),
                Refunds.Select(x => new ProcessSaleReturnRefundRequest(x.Method, x.Amount, x.Reference)).ToList()));
            LastReturn = result;
            _pendingIdempotencyKey = Guid.NewGuid().ToString("N");
            Reason = string.Empty;
            ReturnLines.Clear();
            Refunds.Clear();
            ReturnQuantity = 0m;
            RefundAmount = 0m;
            SelectedSaleDetail = await _returns.GetReturnableSaleAsync(SelectedSale.Id);
            SelectedLine = SelectedSaleDetail?.Lines.FirstOrDefault(x => x.RemainingQuantity > 0m);
            await RefreshReturnsAsync();
            RaiseTotals();
            StatusMessage = Translate($"Return {result.Return.ReturnNumber} completed.", $"برگشت {result.Return.ReturnNumber} تکمیل شد.", $"ستنېدل {result.Return.ReturnNumber} بشپړ شول.");
        });
    }

    private async Task RefreshReturnsAsync()
    {
        var results = await _returns.SearchReturnsAsync(new SaleReturnSearchFilter(Take: 25));
        RecentReturns.Clear();
        foreach (var item in results) RecentReturns.Add(item);
    }

    private async Task ExecuteBusyAsync(Func<Task> action)
    {
        if (IsBusy) return;
        IsBusy = true;
        NotifyCommands();
        try { await action(); }
        catch (Exception exception) { StatusMessage = exception.Message; }
        finally { IsBusy = false; NotifyCommands(); }
    }

    private void RaiseTotals()
    {
        OnPropertyChanged(nameof(CalculatedRefund));
        OnPropertyChanged(nameof(CalculatedRefundText));
    }
    private void NotifyCommands()
    {
        LoadCommand.NotifyCanExecuteChanged();
        SearchSalesCommand.NotifyCanExecuteChanged();
        LoadSelectedSaleCommand.NotifyCanExecuteChanged();
        AddReturnLineCommand.NotifyCanExecuteChanged();
        RemoveReturnLineCommand.NotifyCanExecuteChanged();
        AddRefundCommand.NotifyCanExecuteChanged();
        RemoveRefundCommand.NotifyCanExecuteChanged();
        ProcessReturnCommand.NotifyCanExecuteChanged();
    }
    private string Translate(string english, string dari, string pashto) => _language.Code switch { "fa" => dari, "ps" => pashto, _ => english };
}

public sealed record ReturnLineDraftViewModel(ReturnableSaleLineItem Line, decimal Quantity)
{
    public string Description => Line.Description;
    public decimal RefundAmount => decimal.Round(Line.NetUnitAmount * Quantity, 4, MidpointRounding.AwayFromZero);
}
public sealed record ReturnRefundDraftViewModel(string Method, decimal Amount, string? Reference);
