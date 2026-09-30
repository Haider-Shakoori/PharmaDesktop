using System.Collections.ObjectModel;
using System.Globalization;
using BusinessOS.Pharmacy.Application.Abstractions.Authentication;
using BusinessOS.Pharmacy.Application.Abstractions.Inventory;
using BusinessOS.Pharmacy.Application.Abstractions.Medicines;
using BusinessOS.Pharmacy.Desktop.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BusinessOS.Pharmacy.Desktop.Inventory;

public sealed partial class InventoryViewModel : ObservableObject
{
    private readonly IInventoryService _inventory;
    private readonly IMedicineCatalogService _medicines;
    private readonly IPermissionAuthorizer _permissions;
    private UiLanguage _language = UiLanguageCatalog.All[0];

    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string statusMessage = string.Empty;
    [ObservableProperty] private string searchText = string.Empty;
    [ObservableProperty] private string selectedFilterStatus = "All";
    [ObservableProperty] private string selectedExpiryFilter = "All";
    [ObservableProperty] private StockLocationReferenceItem? selectedFilterLocation;
    [ObservableProperty] private InventoryBatchListItem? selectedBatch;

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
    }

    public ObservableCollection<InventoryBatchListItem> Batches { get; } = new();
    public ObservableCollection<MedicineListItem> Medicines { get; } = new();
    public ObservableCollection<StockLocationReferenceItem> Locations { get; } = new();
    public ObservableCollection<StockMovementItem> Movements { get; } = new();
    public ObservableCollection<BatchStatusEventItem> StatusEvents { get; } = new();

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

    public string Title => Translate("Inventory", "موجودی", "زېرمه");
    public string Subtitle => Translate(
        "Batch-aware stock, expiry and movement control",
        "کنترل موجودی، بچ، انقضا و گردش کالا",
        "د بېچ، ختمېدو او زېرمتون حرکتونو کنټرول");
    public string OpeningStockTitle => Translate("Opening stock", "موجودی اولیه", "پیل زېرمه");
    public string AdjustmentTitle => Translate("Stock adjustment", "تعدیل موجودی", "د زېرمتون سمون");
    public string StatusTitle => Translate("Batch status", "وضعیت بچ", "د بېچ حالت");
    public string HistoryTitle => Translate("Movement & status history", "تاریخچه گردش و وضعیت", "د حرکت او حالت تاریخچه");

    public void SetLanguage(UiLanguage language)
    {
        _language = language;
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

            StatusMessage = Translate(
                $"{Batches.Count} batch records loaded.",
                $"{Batches.Count} رکورد بچ بارگذاری شد.",
                $"{Batches.Count} د بېچ ریکارډونه پورته شول.");
        });
    }

    partial void OnSelectedBatchChanged(InventoryBatchListItem? value)
    {
        LoadDetailCommand.NotifyCanExecuteChanged();
        AdjustCommand.NotifyCanExecuteChanged();
        ChangeStatusCommand.NotifyCanExecuteChanged();

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

        Batches.Clear();
        foreach (var item in result)
        {
            Batches.Add(item);
        }
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
            SelectedBatch = Batches.FirstOrDefault(x => x.Id == id);
            OpeningBatchNumber = string.Empty;
            ManufacturedAtText = string.Empty;
            ExpiresAtText = string.Empty;
            OpeningQuantity = 1m;
            PurchaseCost = 0m;
            SalePrice = null;
            OpeningNotes = string.Empty;

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

        await ExecuteBusyAsync(async () =>
        {
            var result = await _inventory.AdjustAsync(
                new InventoryAdjustmentRequest(
                    SelectedBatch.Id,
                    AdjustmentQuantity,
                    SelectedAdjustmentReason,
                    AdjustmentReason));

            await SearchCoreAsync();
            SelectedBatch = Batches.FirstOrDefault(x => x.Id == result.AdjustmentId) ??
                            Batches.FirstOrDefault(x => x.Id == SelectedBatch?.Id);
            AdjustmentQuantity = 0m;
            AdjustmentReason = string.Empty;

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
            SelectedBatch = Batches.FirstOrDefault(x => x.Id == batchId);
            BatchStatusReason = string.Empty;

            StatusMessage = Translate(
                "Batch status updated with an audit event.",
                "وضعیت بچ با رویداد حسابرسی به‌روزرسانی شد.",
                "د بېچ حالت د پلټنې له پېښې سره تازه شو.");
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
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(OpeningStockTitle));
        OnPropertyChanged(nameof(AdjustmentTitle));
        OnPropertyChanged(nameof(StatusTitle));
        OnPropertyChanged(nameof(HistoryTitle));
    }
}
