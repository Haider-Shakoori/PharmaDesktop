using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using BusinessOS.Pharmacy.Application.Abstractions.Authentication;
using BusinessOS.Pharmacy.Application.Abstractions.Inventory;
using BusinessOS.Pharmacy.Application.Abstractions.Medicines;
using BusinessOS.Pharmacy.Desktop.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace BusinessOS.Pharmacy.Desktop.Inventory;

public sealed partial class InventoryViewModel : ObservableObject
{
    private readonly IInventoryService _inventory;
    private readonly IMedicineCatalogService _medicines;
    private readonly IPermissionAuthorizer _permissions;
    private UiLanguage _language = UiLanguageCatalog.All[0];
    private List<InventoryBatchListItem> _matchingBatches = [];

    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string statusMessage = string.Empty;
    [ObservableProperty] private string searchText = string.Empty;
    [ObservableProperty] private string selectedFilterStatus = "All";
    [ObservableProperty] private string selectedExpiryFilter = "All";
    [ObservableProperty] private StockLocationReferenceItem? selectedFilterLocation;
    [ObservableProperty] private InventoryBatchListItem? selectedBatch;
    [ObservableProperty] private bool isEditorOpen;
    [ObservableProperty] private string editorSection = "opening";
    [ObservableProperty] private int currentPage = 1;
    [ObservableProperty] private int pageSize = 15;
    [ObservableProperty] private int totalItems;
    [ObservableProperty] private decimal totalStockCost;
    [ObservableProperty] private decimal potentialSalesValue;
    [ObservableProperty] private decimal potentialGrossProfit;
    [ObservableProperty] private decimal availableQuantity;
    [ObservableProperty] private int stockBatchCount;
    [ObservableProperty] private int sellableBatchCount;

    [ObservableProperty] private MedicineListItem? selectedOpeningMedicine;
    [ObservableProperty] private StockLocationReferenceItem? selectedOpeningLocation;
    [ObservableProperty] private string openingBatchNumber = string.Empty;
    [ObservableProperty] private string manufacturedAtText = string.Empty;
    [ObservableProperty] private string expiresAtText = string.Empty;
    [ObservableProperty] private decimal openingQuantity = 1m;
    [ObservableProperty] private decimal purchaseCost;
    [ObservableProperty] private decimal? salePrice;
    [ObservableProperty] private string openingNotes = string.Empty;

    [ObservableProperty] private decimal adjustmentQuantity;
    [ObservableProperty] private string selectedAdjustmentReason = "count_correction";
    [ObservableProperty] private string adjustmentReason = string.Empty;

    [ObservableProperty] private string selectedBatchStatus = "active";
    [ObservableProperty] private string batchStatusReason = string.Empty;

    [ObservableProperty] private string detailTitle = "Select a batch to view its history.";
    [ObservableProperty] private bool canManageInventory;
    [ObservableProperty] private bool canAdjustInventory;
    [ObservableProperty] private bool canChangeBatchStatus;
    [ObservableProperty] private bool isBatchesMode;

    public InventoryViewModel(
        IInventoryService inventory,
        IMedicineCatalogService medicines,
        IPermissionAuthorizer permissions)
    {
        _inventory = inventory;
        _medicines = medicines;
        _permissions = permissions;

        RefreshCommand = new AsyncRelayCommand(LoadAsync, () => !IsBusy);
        SearchCommand = new AsyncRelayCommand(SearchAsync, () => !IsBusy);
        CreateOpeningStockCommand = new AsyncRelayCommand(CreateOpeningStockAsync, () => !IsBusy && CanManageInventory);
        AdjustCommand = new AsyncRelayCommand(AdjustAsync, () => !IsBusy && CanAdjustInventory && SelectedBatch is not null);
        ChangeStatusCommand = new AsyncRelayCommand(ChangeStatusAsync, () => !IsBusy && CanChangeBatchStatus && SelectedBatch is not null);
        LoadDetailCommand = new AsyncRelayCommand(LoadSelectedDetailAsync, () => !IsBusy && SelectedBatch is not null);
        DownloadCsvTemplateCommand = new AsyncRelayCommand(DownloadCsvTemplateAsync, () => !IsBusy);
        ImportCsvCommand = new AsyncRelayCommand(ImportCsvAsync, () => !IsBusy && CanManageInventory);
        OpenOpeningStockCommand = new RelayCommand(OpenOpeningStock, () => !IsBusy && CanManageInventory);
        OpenBatchEditorCommand = new RelayCommand<InventoryBatchListItem>(
            OpenBatchEditor,
            item => item is not null && !IsBusy);
        CloseEditorCommand = new RelayCommand(CloseEditor);
        ShowOpeningTabCommand = new RelayCommand(() => EditorSection = "opening");
        ShowAdjustmentTabCommand = new RelayCommand(
            () => EditorSection = "adjustment",
            () => SelectedBatch is not null && CanAdjustInventory);
        ShowStatusTabCommand = new RelayCommand(
            () => EditorSection = "status",
            () => SelectedBatch is not null && CanChangeBatchStatus);
        ShowHistoryTabCommand = new RelayCommand(
            () => EditorSection = "history",
            () => SelectedBatch is not null);
        PreviousPageCommand = new RelayCommand(PreviousPage, () => CurrentPage > 1);
        NextPageCommand = new RelayCommand(NextPage, () => CurrentPage < TotalPages);
        GoToPageCommand = new RelayCommand<int>(
            GoToPage,
            page => page >= 1 && page <= TotalPages);
    }

    public ObservableCollection<InventoryBatchListItem> Batches { get; } = new();
    public ObservableCollection<MedicineListItem> Medicines { get; } = new();
    public ObservableCollection<StockLocationReferenceItem> Locations { get; } = new();
    public ObservableCollection<StockMovementItem> Movements { get; } = new();
    public ObservableCollection<BatchStatusEventItem> StatusEvents { get; } = new();
    public ObservableCollection<int> PageNumbers { get; } = new();

    public IReadOnlyList<int> PageSizeOptions { get; } = [10, 15, 25, 50];
    public IReadOnlyList<string> FilterStatuses { get; } =
        ["All", "active", "depleted", "quarantined", "recalled", "damaged"];
    public IReadOnlyList<string> ExpiryFilters { get; } =
        ["All", "near", "expired", "no_expiry"];
    public IReadOnlyList<string> AdjustmentReasons { get; } =
        ["count_correction", "damage", "wastage", "found_stock", "other"];
    public IReadOnlyList<string> BatchStatuses { get; } =
        ["active", "quarantined", "recalled", "damaged"];

    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand SearchCommand { get; }
    public IAsyncRelayCommand CreateOpeningStockCommand { get; }
    public IAsyncRelayCommand AdjustCommand { get; }
    public IAsyncRelayCommand ChangeStatusCommand { get; }
    public IAsyncRelayCommand LoadDetailCommand { get; }
    public IAsyncRelayCommand DownloadCsvTemplateCommand { get; }
    public IAsyncRelayCommand ImportCsvCommand { get; }
    public IRelayCommand OpenOpeningStockCommand { get; }
    public IRelayCommand<InventoryBatchListItem> OpenBatchEditorCommand { get; }
    public IRelayCommand CloseEditorCommand { get; }
    public IRelayCommand ShowOpeningTabCommand { get; }
    public IRelayCommand ShowAdjustmentTabCommand { get; }
    public IRelayCommand ShowStatusTabCommand { get; }
    public IRelayCommand ShowHistoryTabCommand { get; }
    public IRelayCommand PreviousPageCommand { get; }
    public IRelayCommand NextPageCommand { get; }
    public IRelayCommand<int> GoToPageCommand { get; }

    public string Title => Translate("Inventory", "موجودی", "زېرمه");
    public string Eyebrow => Translate("Stock & batch control", "کنترل موجودی و بچ", "د زېرمې او بېچ کنټرول");
    public string Subtitle => Translate(
        "Stock value, batches, expiry, adjustments and movement history in one place",
        "ارزش موجودی، بچ‌ها، انقضا، تعدیلات و تاریخچه گردش در یک بخش",
        "د زېرمې ارزښت، بېچونه، تاریخ تېر، سمونونه او د حرکت تاریخچه په یوه برخه کې");
    public bool IsInventoryMode => true;
    public string OpeningStockTitle => Translate("Opening stock", "موجودی اولیه", "پیل زېرمه");
    public string AdjustmentTitle => Translate("Stock adjustment", "تعدیل موجودی", "د زېرمتون سمون");
    public string StatusTitle => Translate("Batch status", "وضعیت بچ", "د بېچ حالت");
    public string HistoryTitle => Translate("Movement & status history", "تاریخچه گردش و وضعیت", "د حرکت او حالت تاریخچه");

    public string OpeningStockLabel => Translate("Opening stock", "موجودی اولیه", "پیل زېرمه");
    public string ManageLabel => Translate("Manage", "مدیریت", "اداره");
    public string TemplateLabel => Translate("CSV template", "قالب CSV", "CSV نمونه");
    public string ImportLabel => Translate("Import CSV", "وارد کردن CSV", "CSV واردول");
    public string TotalStockCostLabel => Translate("Total medicine cost", "هزینه مجموع ادویه", "د درملو ټول لګښت");
    public string PotentialSalesValueLabel => Translate("Total selling value", "ارزش مجموع فروش", "د خرڅلاو ټول ارزښت");
    public string PotentialGrossProfitLabel => Translate("Potential gross profit", "سود ناخالص بالقوه", "اټکلي ناخالصه ګټه");
    public string AvailableQuantityLabel => Translate("Available quantity", "مقدار موجود", "موجود مقدار");
    public string TotalStockCostText => $"AFN {TotalStockCost:N2}";
    public string PotentialSalesValueText => $"AFN {PotentialSalesValue:N2}";
    public string PotentialGrossProfitText => $"AFN {PotentialGrossProfit:N2}";
    public string AvailableQuantityText => $"{AvailableQuantity:N4}";
    public string TotalStockCostHint => Translate(
        $"{StockBatchCount} on-hand batches at purchase cost",
        $"{StockBatchCount} بچ موجود بر اساس هزینه خرید",
        $"{StockBatchCount} موجود بېچونه د پېرود په لګښت");
    public string PotentialSalesValueHint => Translate(
        $"{SellableBatchCount} sellable batches at current sale prices",
        $"{SellableBatchCount} بچ قابل فروش با قیمت فعلی",
        $"{SellableBatchCount} د اوسني خرڅلاو په بیه د پلور وړ بېچونه");
    public string PotentialGrossProfitHint => Translate(
        "Selling value minus cost of sellable stock",
        "ارزش فروش منهای هزینه موجودی قابل فروش",
        "د خرڅلاو ارزښت منفي د پلور وړ زېرمو لګښت");
    public string AvailableQuantityHint => Translate(
        "Total units currently on hand",
        "مجموع واحدهای موجود فعلی",
        "ټول اوسني موجود واحدونه");
    public string EditorTitle => EditorSection switch
    {
        "adjustment" => AdjustmentTitle,
        "status" => StatusTitle,
        "history" => HistoryTitle,
        _ => OpeningStockTitle,
    };
    public bool IsOpeningEditor => EditorSection == "opening";
    public bool IsAdjustmentEditor => EditorSection == "adjustment";
    public bool IsStatusEditor => EditorSection == "status";
    public bool IsHistoryEditor => EditorSection == "history";
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalItems / (double)Math.Max(1, PageSize)));
    public string PageSummary
    {
        get
        {
            if (TotalItems == 0)
            {
                return Translate("Showing 0 batches", "نمایش ۰ بچ", "۰ بېچونه ښودل کېږي");
            }

            var from = ((CurrentPage - 1) * PageSize) + 1;
            var to = Math.Min(CurrentPage * PageSize, TotalItems);
            return Translate(
                $"Showing {from}-{to} of {TotalItems} batches",
                $"نمایش {from}-{to} از {TotalItems} بچ",
                $"له {TotalItems} بېچونو څخه {from}-{to} ښودل کېږي");
        }
    }

    public void SetLanguage(UiLanguage language)
    {
        _language = language;
        RaiseLocalizedProperties();
    }

    public void SetMode(bool batchesMode)
    {
        // Kept for compatibility with old navigation links. Inventory and
        // batches now share one unified workspace.
        IsBatchesMode = false;
        IsEditorOpen = false;
        OnPropertyChanged(nameof(IsInventoryMode));
        RaiseLocalizedProperties();
    }

    partial void OnIsBatchesModeChanged(bool value)
    {
        OnPropertyChanged(nameof(IsInventoryMode));
        RaiseLocalizedProperties();
    }

    public async Task LoadAsync()
    {
        await ExecuteBusyAsync(async () =>
        {
            CanManageInventory = _permissions.HasPermission("inventory.manage");
            CanAdjustInventory = _permissions.HasPermission("inventory.adjust");
            CanChangeBatchStatus = _permissions.HasPermission("inventory.status");

            await _inventory.EnsureDefaultsAsync();
            await LoadReferencesAsync();
            await LoadMedicinesAsync();
            await SearchCoreAsync();
            await LoadSummaryAsync();

            StatusMessage = Translate(
                $"{TotalItems} batch records loaded.",
                $"{TotalItems} رکورد بچ بارگذاری شد.",
                $"{TotalItems} د بېچ ریکارډونه پورته شول.");
        });
    }

    partial void OnTotalStockCostChanged(decimal value) =>
        OnPropertyChanged(nameof(TotalStockCostText));

    partial void OnPotentialSalesValueChanged(decimal value) =>
        OnPropertyChanged(nameof(PotentialSalesValueText));

    partial void OnPotentialGrossProfitChanged(decimal value) =>
        OnPropertyChanged(nameof(PotentialGrossProfitText));

    partial void OnAvailableQuantityChanged(decimal value) =>
        OnPropertyChanged(nameof(AvailableQuantityText));

    partial void OnStockBatchCountChanged(int value) =>
        OnPropertyChanged(nameof(TotalStockCostHint));

    partial void OnSellableBatchCountChanged(int value) =>
        OnPropertyChanged(nameof(PotentialSalesValueHint));

    partial void OnEditorSectionChanged(string value)
    {
        OnPropertyChanged(nameof(EditorTitle));
        OnPropertyChanged(nameof(IsOpeningEditor));
        OnPropertyChanged(nameof(IsAdjustmentEditor));
        OnPropertyChanged(nameof(IsStatusEditor));
        OnPropertyChanged(nameof(IsHistoryEditor));
    }

    partial void OnCurrentPageChanged(int value)
    {
        OnPropertyChanged(nameof(TotalPages));
        OnPropertyChanged(nameof(PageSummary));
        PreviousPageCommand.NotifyCanExecuteChanged();
        NextPageCommand.NotifyCanExecuteChanged();
        GoToPageCommand.NotifyCanExecuteChanged();
        RebuildPageNumbers();
    }

    partial void OnPageSizeChanged(int value)
    {
        CurrentPage = 1;
        RefreshPage();
    }

    partial void OnTotalItemsChanged(int value)
    {
        OnPropertyChanged(nameof(TotalPages));
        OnPropertyChanged(nameof(PageSummary));
        PreviousPageCommand.NotifyCanExecuteChanged();
        NextPageCommand.NotifyCanExecuteChanged();
        GoToPageCommand.NotifyCanExecuteChanged();
        RebuildPageNumbers();
    }

    partial void OnSelectedBatchChanged(InventoryBatchListItem? value)
    {
        LoadDetailCommand.NotifyCanExecuteChanged();
        AdjustCommand.NotifyCanExecuteChanged();
        ChangeStatusCommand.NotifyCanExecuteChanged();
        OpenBatchEditorCommand.NotifyCanExecuteChanged();
        ShowAdjustmentTabCommand.NotifyCanExecuteChanged();
        ShowStatusTabCommand.NotifyCanExecuteChanged();
        ShowHistoryTabCommand.NotifyCanExecuteChanged();

        if (value is not null)
        {
            SelectedBatchStatus = value.Status is "depleted" ? "active" : value.Status;
            _ = LoadSelectedDetailAsync();
        }
        else
        {
            Movements.Clear();
            StatusEvents.Clear();
            DetailTitle = Translate(
                "Select a batch to view its history.",
                "برای مشاهده تاریخچه یک بچ را انتخاب کنید.",
                "د تاریخچې لپاره یو بېچ وټاکئ.");
        }
    }

    private async Task SearchAsync() =>
        await ExecuteBusyAsync(SearchCoreAsync);

    private async Task SearchCoreAsync()
    {
        var status = SelectedFilterStatus == "All" ? null : SelectedFilterStatus;
        var expiry = SelectedExpiryFilter == "All" ? null : SelectedExpiryFilter;

        var result = await _inventory.SearchBatchesAsync(
            new InventoryBatchFilter(
                SearchText,
                status,
                expiry,
                SelectedFilterLocation?.Id,
                NearExpiryDays: 90,
                Take: 750));

        _matchingBatches = result.ToList();
        TotalItems = _matchingBatches.Count;
        CurrentPage = 1;
        RefreshPage();
    }

    private void OpenOpeningStock()
    {
        EditorSection = "opening";
        IsEditorOpen = true;
    }

    private void OpenBatchEditor(InventoryBatchListItem? item)
    {
        if (item is null)
        {
            return;
        }

        SelectedBatch = item;
        EditorSection = CanAdjustInventory ? "adjustment" : "history";
        IsEditorOpen = true;
    }

    private void CloseEditor() => IsEditorOpen = false;

    private void PreviousPage()
    {
        if (CurrentPage > 1)
        {
            CurrentPage--;
            RefreshPage();
        }
    }

    private void NextPage()
    {
        if (CurrentPage < TotalPages)
        {
            CurrentPage++;
            RefreshPage();
        }
    }

    private void GoToPage(int page)
    {
        if (page < 1 || page > TotalPages || page == CurrentPage)
        {
            return;
        }

        CurrentPage = page;
        RefreshPage();
    }

    private void RefreshPage()
    {
        var totalPages = TotalPages;
        if (CurrentPage > totalPages)
        {
            CurrentPage = totalPages;
        }

        var skip = Math.Max(0, (CurrentPage - 1) * Math.Max(1, PageSize));
        Batches.Clear();
        foreach (var item in _matchingBatches.Skip(skip).Take(Math.Max(1, PageSize)))
        {
            Batches.Add(item);
        }

        OnPropertyChanged(nameof(PageSummary));
        RebuildPageNumbers();
    }

    private void RebuildPageNumbers()
    {
        var total = TotalPages;
        var start = Math.Max(1, CurrentPage - 2);
        var end = Math.Min(total, start + 4);
        start = Math.Max(1, end - 4);

        PageNumbers.Clear();
        for (var page = start; page <= end; page++)
        {
            PageNumbers.Add(page);
        }
    }

    private async Task LoadSummaryAsync()
    {
        var summary = await _inventory.GetSummaryAsync();

        TotalStockCost = summary.TotalStockCost;
        PotentialSalesValue = summary.PotentialSalesValue;
        PotentialGrossProfit = summary.PotentialGrossProfit;
        AvailableQuantity = summary.AvailableQuantity;
        StockBatchCount = summary.BatchCount;
        SellableBatchCount = summary.SellableBatchCount;

        OnPropertyChanged(nameof(TotalStockCostText));
        OnPropertyChanged(nameof(PotentialSalesValueText));
        OnPropertyChanged(nameof(PotentialGrossProfitText));
        OnPropertyChanged(nameof(AvailableQuantityText));
        OnPropertyChanged(nameof(TotalStockCostHint));
        OnPropertyChanged(nameof(PotentialSalesValueHint));
        OnPropertyChanged(nameof(PotentialGrossProfitHint));
        OnPropertyChanged(nameof(AvailableQuantityHint));
    }

    private async Task LoadReferencesAsync()
    {
        var references = await _inventory.GetReferenceDataAsync();
        var selectedFilterId = SelectedFilterLocation?.Id;
        var selectedOpeningId = SelectedOpeningLocation?.Id;

        Locations.Clear();
        foreach (var location in references.Locations.Where(x => x.IsActive))
        {
            Locations.Add(location);
        }

        SelectedFilterLocation = Locations.FirstOrDefault(x => x.Id == selectedFilterId);
        SelectedOpeningLocation =
            Locations.FirstOrDefault(x => x.Id == selectedOpeningId) ??
            Locations.FirstOrDefault(x => x.IsDefault) ??
            Locations.FirstOrDefault();
    }

    private async Task LoadMedicinesAsync()
    {
        if (!_permissions.HasPermission("medicines.manage"))
        {
            Medicines.Clear();
            return;
        }

        await LoadMedicinesCoreAsync();
    }

    private async Task LoadMedicinesCoreAsync()
    {
        var result = await _medicines.SearchAsync(
            new MedicineSearchFilter(IsActive: true, Take: 1000));

        Medicines.Clear();
        foreach (var medicine in result)
        {
            Medicines.Add(medicine);
        }

        SelectedOpeningMedicine ??= Medicines.FirstOrDefault();
    }

    private async Task CreateOpeningStockAsync()
    {
        if (SelectedOpeningMedicine is null)
        {
            StatusMessage = Translate("Select a medicine.", "یک دوا را انتخاب کنید.", "یو درمل وټاکئ.");
            return;
        }

        if (SelectedOpeningLocation is null)
        {
            StatusMessage = Translate("Select a stock location.", "یک محل موجودی را انتخاب کنید.", "د زېرمتون ځای وټاکئ.");
            return;
        }

        await ExecuteBusyAsync(async () =>
        {
            var manufacturedAt = ParseOptionalDate(ManufacturedAtText, "Manufactured date");
            var expiresAt = ParseOptionalDate(ExpiresAtText, "Expiry date");

            var id = await _inventory.CreateOpeningStockAsync(
                new CreateOpeningStockRequest(
                    SelectedOpeningMedicine.Id,
                    SelectedOpeningLocation.Id,
                    OpeningBatchNumber,
                    manufacturedAt,
                    expiresAt,
                    OpeningQuantity,
                    PurchaseCost,
                    SalePrice,
                    OpeningNotes));

            await SearchCoreAsync();
            await LoadSummaryAsync();
            SelectedBatch = Batches.FirstOrDefault(x => x.Id == id);
            OpeningBatchNumber = string.Empty;
            ManufacturedAtText = string.Empty;
            ExpiresAtText = string.Empty;
            OpeningQuantity = 1m;
            PurchaseCost = 0m;
            SalePrice = null;
            OpeningNotes = string.Empty;

            IsEditorOpen = false;
            StatusMessage = Translate(
                "Opening stock posted and movement recorded.",
                "موجودی اولیه ثبت و گردش کالا ایجاد شد.",
                "پیل زېرمه ثبت او حرکت جوړ شو.");
        });
    }

    private async Task AdjustAsync()
    {
        if (SelectedBatch is null)
        {
            return;
        }

        var batchId = SelectedBatch.Id;

        await ExecuteBusyAsync(async () =>
        {
            var result = await _inventory.AdjustAsync(
                new InventoryAdjustmentRequest(
                    SelectedBatch.Id,
                    AdjustmentQuantity,
                    SelectedAdjustmentReason,
                    AdjustmentReason));

            await SearchCoreAsync();
            await LoadSummaryAsync();
            SelectedBatch = Batches.FirstOrDefault(x => x.Id == batchId);
            AdjustmentQuantity = 0m;
            AdjustmentReason = string.Empty;

            IsEditorOpen = false;
            StatusMessage = Translate(
                $"Adjustment {result.Number} posted. Balance: {result.BalanceAfter:0.####}",
                $"تعدیل {result.Number} ثبت شد. موجودی: {result.BalanceAfter:0.####}",
                $"سمون {result.Number} ثبت شو. پاتې: {result.BalanceAfter:0.####}");
        });
    }

    private async Task ChangeStatusAsync()
    {
        if (SelectedBatch is null)
        {
            return;
        }

        var batchId = SelectedBatch.Id;

        await ExecuteBusyAsync(async () =>
        {
            await _inventory.ChangeBatchStatusAsync(
                new ChangeBatchStatusRequest(
                    batchId,
                    SelectedBatchStatus,
                    BatchStatusReason));

            await SearchCoreAsync();
            await LoadSummaryAsync();
            SelectedBatch = Batches.FirstOrDefault(x => x.Id == batchId);
            BatchStatusReason = string.Empty;

            IsEditorOpen = false;
            StatusMessage = Translate(
                "Batch status updated with an audit event.",
                "وضعیت بچ با رویداد حسابرسی به‌روزرسانی شد.",
                "د بېچ حالت د پلټنې له پېښې سره تازه شو.");
        });
    }

    private async Task DownloadCsvTemplateAsync()
    {
        var dialog = new SaveFileDialog
        {
            FileName = InventoryCsvImporter.TemplateFileName,
            DefaultExt = ".csv",
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            Title = "Save inventory CSV template",
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        await Task.Yield();
        File.WriteAllText(dialog.FileName, InventoryCsvImporter.CreateTemplate(), new UTF8Encoding(true));
        StatusMessage = Translate(
            $"CSV template saved to {dialog.FileName}",
            $"قالب CSV در {dialog.FileName} ذخیره شد.",
            $"CSV کالب په {dialog.FileName} کې خوندي شو.");
    }

    private async Task ImportCsvAsync()
    {
        var location = SelectedOpeningLocation ?? SelectedFilterLocation ?? Locations.FirstOrDefault(x => x.IsDefault) ?? Locations.FirstOrDefault();
        if (location is null)
        {
            StatusMessage = Translate("Select a stock location.", "یک محل موجودی را انتخاب کنید.", "د زېرمتون ځای وټاکئ.");
            return;
        }

        var dialog = new OpenFileDialog
        {
            DefaultExt = ".csv",
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            Title = "Import inventory from CSV",
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        await ExecuteBusyAsync(async () =>
        {
            if (Medicines.Count == 0)
            {
                await LoadMedicinesCoreAsync();
            }

            var csv = await File.ReadAllTextAsync(dialog.FileName);
            var result = await InventoryCsvImporter.ImportAsync(
                csv,
                Medicines.ToList(),
                _inventory,
                location.Id);

            await SearchCoreAsync();
            await LoadSummaryAsync();

            StatusMessage = result.FailedCount == 0
                ? Translate(
                    $"Imported {result.ImportedCount} rows from CSV.",
                    $"{result.ImportedCount} سطر از CSV وارد شد.",
                    $"{result.ImportedCount} کرښې د CSV څخه وارد شوې.")
                : Translate(
                    $"Imported {result.ImportedCount} rows. {result.FailedCount} failed: " +
                    string.Join(" | ", result.Errors.Take(3)) + (result.Errors.Count > 3 ? " …" : string.Empty),
                    $"{result.ImportedCount} سطر وارد شد. {result.FailedCount} ناموفق: " +
                    string.Join(" | ", result.Errors.Take(3)) + (result.Errors.Count > 3 ? " …" : string.Empty),
                    $"{result.ImportedCount} کرښې وارد شوې. {result.FailedCount} ناکامې: " +
                    string.Join(" | ", result.Errors.Take(3)) + (result.Errors.Count > 3 ? " …" : string.Empty));
        });
    }

    private async Task LoadSelectedDetailAsync()
    {
        if (SelectedBatch is null)
        {
            return;
        }

        var detail = await _inventory.GetBatchAsync(SelectedBatch.Id);
        if (detail is null)
        {
            return;
        }

        Movements.Clear();
        foreach (var movement in detail.Movements)
        {
            Movements.Add(movement);
        }

        StatusEvents.Clear();
        foreach (var status in detail.StatusEvents)
        {
            StatusEvents.Add(status);
        }

        DetailTitle = $"{detail.Batch.BrandName} · {detail.Batch.BatchNumber ?? "No batch number"}";
    }

    private static DateOnly? ParseOptionalDate(string text, string field)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (DateOnly.TryParseExact(
                text.Trim(),
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
        {
            return parsed;
        }

        throw new ArgumentException($"{field} must use yyyy-MM-dd.");
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
        SearchCommand.NotifyCanExecuteChanged();
        CreateOpeningStockCommand.NotifyCanExecuteChanged();
        AdjustCommand.NotifyCanExecuteChanged();
        ChangeStatusCommand.NotifyCanExecuteChanged();
        LoadDetailCommand.NotifyCanExecuteChanged();
        DownloadCsvTemplateCommand.NotifyCanExecuteChanged();
        ImportCsvCommand.NotifyCanExecuteChanged();
        OpenOpeningStockCommand.NotifyCanExecuteChanged();
        OpenBatchEditorCommand.NotifyCanExecuteChanged();
        ShowAdjustmentTabCommand.NotifyCanExecuteChanged();
        ShowStatusTabCommand.NotifyCanExecuteChanged();
        ShowHistoryTabCommand.NotifyCanExecuteChanged();
        PreviousPageCommand.NotifyCanExecuteChanged();
        NextPageCommand.NotifyCanExecuteChanged();
        GoToPageCommand.NotifyCanExecuteChanged();
    }

    private string Translate(string english, string dari, string pashto) =>
        _language.Code switch
        {
            "fa" => dari,
            "ps" => pashto,
            _ => english,
        };

    private void RaiseLocalizedProperties()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Eyebrow));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(OpeningStockTitle));
        OnPropertyChanged(nameof(AdjustmentTitle));
        OnPropertyChanged(nameof(StatusTitle));
        OnPropertyChanged(nameof(HistoryTitle));
        OnPropertyChanged(nameof(OpeningStockLabel));
        OnPropertyChanged(nameof(ManageLabel));
        OnPropertyChanged(nameof(TemplateLabel));
        OnPropertyChanged(nameof(ImportLabel));
        OnPropertyChanged(nameof(TotalStockCostLabel));
        OnPropertyChanged(nameof(PotentialSalesValueLabel));
        OnPropertyChanged(nameof(PotentialGrossProfitLabel));
        OnPropertyChanged(nameof(AvailableQuantityLabel));
        OnPropertyChanged(nameof(TotalStockCostHint));
        OnPropertyChanged(nameof(PotentialSalesValueHint));
        OnPropertyChanged(nameof(PotentialGrossProfitHint));
        OnPropertyChanged(nameof(AvailableQuantityHint));
        OnPropertyChanged(nameof(EditorTitle));
        OnPropertyChanged(nameof(PageSummary));
    }
}
