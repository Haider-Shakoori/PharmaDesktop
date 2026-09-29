using System.Collections.ObjectModel;
using System.Globalization;
using BusinessOS.Pharmacy.Application.Abstractions.Medicines;
using BusinessOS.Pharmacy.Desktop.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BusinessOS.Pharmacy.Desktop.Medicines;

public sealed partial class MedicinesViewModel : ObservableObject
{
    private readonly IMedicineCatalogService _catalog;
    private readonly IMedicineCsvService _csv;
    private UiLanguage _language = UiLanguageCatalog.All[0];
    private string? _pendingCsvPath;

    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string statusMessage = string.Empty;
    [ObservableProperty] private string searchText = string.Empty;
    [ObservableProperty] private MedicineReferenceItem? selectedFilterCategory;
    [ObservableProperty] private string selectedStatus = "All";
    [ObservableProperty] private MedicineListItem? selectedMedicine;

    [ObservableProperty] private string? editingId;
    [ObservableProperty] private MedicineReferenceItem? selectedCategory;
    [ObservableProperty] private ManufacturerReferenceItem? selectedManufacturer;
    [ObservableProperty] private string medicineCode = string.Empty;
    [ObservableProperty] private string barcode = string.Empty;
    [ObservableProperty] private string brandName = string.Empty;
    [ObservableProperty] private string genericName = string.Empty;
    [ObservableProperty] private string strength = string.Empty;
    [ObservableProperty] private string dosageForm = string.Empty;
    [ObservableProperty] private string purchaseUnit = "pack";
    [ObservableProperty] private string saleUnit = "unit";
    [ObservableProperty] private decimal unitsPerPurchaseUnit = 1m;
    [ObservableProperty] private decimal reorderLevel;
    [ObservableProperty] private bool prescriptionRequired;
    [ObservableProperty] private bool batchTrackingRequired = true;
    [ObservableProperty] private bool expiryTrackingRequired = true;
    [ObservableProperty] private bool isActive = true;
    [ObservableProperty] private string notes = string.Empty;

    [ObservableProperty] private string newCategoryName = string.Empty;
    [ObservableProperty] private string newManufacturerName = string.Empty;
    [ObservableProperty] private string newManufacturerCountry = string.Empty;

    [ObservableProperty] private string csvPreviewSummary = string.Empty;
    [ObservableProperty] private bool hasCsvPreview;
    [ObservableProperty] private bool canImportCsv;

    public MedicinesViewModel(
        IMedicineCatalogService catalog,
        IMedicineCsvService csv)
    {
        _catalog = catalog;
        _csv = csv;

        RefreshCommand = new AsyncRelayCommand(LoadAsync, () => !IsBusy);
        SearchCommand = new AsyncRelayCommand(SearchAsync, () => !IsBusy);
        NewCommand = new RelayCommand(NewMedicine);
        EditSelectedCommand = new AsyncRelayCommand(EditSelectedAsync, () => SelectedMedicine is not null && !IsBusy);
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsBusy);
        AddCategoryCommand = new AsyncRelayCommand(AddCategoryAsync, () => !IsBusy);
        AddManufacturerCommand = new AsyncRelayCommand(AddManufacturerAsync, () => !IsBusy);
        ImportValidRowsCommand = new AsyncRelayCommand(ImportPendingCsvAsync, () => CanImportCsv && !IsBusy);

        NewMedicine();
    }

    public ObservableCollection<MedicineListItem> Medicines { get; } = new();
    public ObservableCollection<MedicineReferenceItem> Categories { get; } = new();
    public ObservableCollection<ManufacturerReferenceItem> Manufacturers { get; } = new();
    public ObservableCollection<MedicineCsvRowViewModel> CsvPreviewRows { get; } = new();

    public IReadOnlyList<string> StatusOptions { get; } = ["All", "Active", "Inactive"];

    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand SearchCommand { get; }
    public IRelayCommand NewCommand { get; }
    public IAsyncRelayCommand EditSelectedCommand { get; }
    public IAsyncRelayCommand SaveCommand { get; }
    public IAsyncRelayCommand AddCategoryCommand { get; }
    public IAsyncRelayCommand AddManufacturerCommand { get; }
    public IAsyncRelayCommand ImportValidRowsCommand { get; }

    public string Title => Translate("Medicines", "ادویه", "درمل");
    public string Subtitle => Translate("Medicine catalog & registration", "فهرست و ثبت ادویه", "د درملو لست او ثبت");
    public string AddMedicineLabel => Translate("New medicine", "دوای جدید", "نوی درمل");
    public string EditLabel => Translate("Edit selected", "ویرایش انتخاب‌شده", "ټاکل شوی سمول");
    public string TemplateLabel => Translate("CSV template", "قالب CSV", "CSV نمونه");
    public string ImportLabel => Translate("Import CSV", "وارد کردن CSV", "CSV واردول");
    public string SaveLabel => EditingId is null
        ? Translate("Create medicine", "ایجاد دوا", "درمل جوړول")
        : Translate("Save changes", "ذخیره تغییرات", "بدلونونه خوندي کول");
    public string EditorTitle => EditingId is null
        ? Translate("Add medicine", "افزودن دوا", "درمل زیاتول")
        : Translate("Edit medicine", "ویرایش دوا", "درمل سمول");
    public string ReferencesTitle => Translate("Categories & manufacturers", "دسته‌بندی‌ها و تولیدکنندگان", "کټګورۍ او جوړونکي");

    public void SetLanguage(UiLanguage language)
    {
        _language = language;
        RaiseLocalizedProperties();
    }

    public async Task LoadAsync()
    {
        await ExecuteBusyAsync(async () =>
        {
            await LoadReferencesAsync();
            await SearchCoreAsync();

            if (EditingId is null && string.IsNullOrWhiteSpace(BrandName))
            {
                MedicineCode = NextMedicineCode();
            }

            StatusMessage = Translate(
                $"{Medicines.Count} medicines loaded from the local database.",
                $"{Medicines.Count} دوا از پایگاه‌داده محلی بارگذاری شد.",
                $"{Medicines.Count} درمل له محلي ډیټابیس څخه پورته شول.");
        });
    }

    public async Task WriteTemplateAsync(string path)
    {
        await _csv.WriteTemplateAsync(path);
        StatusMessage = Translate(
            $"CSV template saved to {path}.",
            $"قالب CSV ذخیره شد: {path}",
            $"CSV نمونه خوندي شوه: {path}");
    }

    public async Task PreviewCsvAsync(string path)
    {
        await ExecuteBusyAsync(async () =>
        {
            var preview = await _csv.PreviewAsync(path);
            _pendingCsvPath = path;

            CsvPreviewRows.Clear();
            foreach (var row in preview.Rows)
            {
                CsvPreviewRows.Add(new MedicineCsvRowViewModel(
                    row.RowNumber,
                    row.MedicineCode ?? "—",
                    row.BrandName ?? "—",
                    row.IsValid ? Translate("Valid", "معتبر", "سم") : Translate("Rejected", "رد شد", "رد شو"),
                    string.Join(" | ", row.Errors)));
            }

            HasCsvPreview = true;
            CanImportCsv = preview.ValidRows > 0;
            ImportValidRowsCommand.NotifyCanExecuteChanged();
            CsvPreviewSummary = Translate(
                $"{preview.ValidRows} valid · {preview.InvalidRows} rejected · {preview.TotalRows} total",
                $"{preview.ValidRows} معتبر · {preview.InvalidRows} رد شده · {preview.TotalRows} مجموع",
                $"{preview.ValidRows} سم · {preview.InvalidRows} رد شوي · {preview.TotalRows} ټول");
        });
    }

    partial void OnSelectedMedicineChanged(MedicineListItem? value) =>
        EditSelectedCommand.NotifyCanExecuteChanged();

    partial void OnEditingIdChanged(string? value)
    {
        OnPropertyChanged(nameof(SaveLabel));
        OnPropertyChanged(nameof(EditorTitle));
    }

    private async Task SearchAsync() =>
        await ExecuteBusyAsync(SearchCoreAsync);

    private async Task SearchCoreAsync()
    {
        var active = SelectedStatus switch
        {
            "Active" => true,
            "Inactive" => false,
            _ => (bool?)null,
        };

        var result = await _catalog.SearchAsync(new MedicineSearchFilter(
            SearchText,
            SelectedFilterCategory?.Id,
            active,
            500));

        Medicines.Clear();
        foreach (var item in result)
        {
            Medicines.Add(item);
        }
    }

    private async Task EditSelectedAsync()
    {
        if (SelectedMedicine is null)
        {
            return;
        }

        await ExecuteBusyAsync(async () =>
        {
            var medicine = await _catalog.GetAsync(SelectedMedicine.Id)
                ?? throw new InvalidOperationException("Medicine was not found.");

            EditingId = medicine.Id;
            MedicineCode = medicine.MedicineCode;
            Barcode = medicine.Barcode ?? string.Empty;
            BrandName = medicine.BrandName;
            GenericName = medicine.GenericName ?? string.Empty;
            Strength = medicine.Strength ?? string.Empty;
            DosageForm = medicine.DosageForm ?? string.Empty;
            PurchaseUnit = medicine.PurchaseUnit;
            SaleUnit = medicine.SaleUnit;
            UnitsPerPurchaseUnit = medicine.UnitsPerPurchaseUnit;
            ReorderLevel = medicine.ReorderLevel;
            PrescriptionRequired = medicine.PrescriptionRequired;
            BatchTrackingRequired = medicine.BatchTrackingRequired;
            ExpiryTrackingRequired = medicine.ExpiryTrackingRequired;
            IsActive = medicine.IsActive;
            Notes = medicine.Notes ?? string.Empty;
            SelectedCategory = Categories.FirstOrDefault(x => x.Id == medicine.MedicineCategoryId);
            SelectedManufacturer = Manufacturers.FirstOrDefault(x => x.Id == medicine.ManufacturerId);
            StatusMessage = Translate("Medicine loaded for editing.", "دوا برای ویرایش باز شد.", "درمل د سمون لپاره پرانیستل شو.");
        });
    }

    private void NewMedicine()
    {
        EditingId = null;
        SelectedCategory = null;
        SelectedManufacturer = null;
        MedicineCode = NextMedicineCode();
        Barcode = string.Empty;
        BrandName = string.Empty;
        GenericName = string.Empty;
        Strength = string.Empty;
        DosageForm = string.Empty;
        PurchaseUnit = "pack";
        SaleUnit = "unit";
        UnitsPerPurchaseUnit = 1m;
        ReorderLevel = 0m;
        PrescriptionRequired = false;
        BatchTrackingRequired = true;
        ExpiryTrackingRequired = true;
        IsActive = true;
        Notes = string.Empty;
        StatusMessage = Translate("Ready for a new medicine.", "آماده ثبت دوای جدید.", "د نوي درمل لپاره چمتو.");
    }

    private async Task SaveAsync()
    {
        await ExecuteBusyAsync(async () =>
        {
            var request = new SaveMedicineRequest(
                SelectedCategory?.Id,
                SelectedManufacturer?.Id,
                MedicineCode,
                Barcode,
                BrandName,
                GenericName,
                Strength,
                DosageForm,
                PurchaseUnit,
                SaleUnit,
                UnitsPerPurchaseUnit,
                ReorderLevel,
                PrescriptionRequired,
                BatchTrackingRequired,
                ExpiryTrackingRequired,
                IsActive,
                Notes);

            var savedMessage = EditingId is null
                ? Translate("Medicine created.", "دوا ایجاد شد.", "درمل جوړ شو.")
                : Translate("Medicine updated.", "دوا به‌روزرسانی شد.", "درمل تازه شو.");

            if (EditingId is null)
            {
                await _catalog.CreateAsync(request);
            }
            else
            {
                await _catalog.UpdateAsync(EditingId, request);
            }

            await SearchCoreAsync();
            NewMedicine();
            StatusMessage = savedMessage;
        });
    }

    private async Task AddCategoryAsync()
    {
        if (string.IsNullOrWhiteSpace(NewCategoryName))
        {
            return;
        }

        await ExecuteBusyAsync(async () =>
        {
            var id = await _catalog.CreateCategoryAsync(NewCategoryName);
            NewCategoryName = string.Empty;
            await LoadReferencesAsync();
            SelectedCategory = Categories.FirstOrDefault(x => x.Id == id);
            StatusMessage = Translate("Category ready.", "دسته‌بندی آماده شد.", "کټګوري چمتو شوه.");
        });
    }

    private async Task AddManufacturerAsync()
    {
        if (string.IsNullOrWhiteSpace(NewManufacturerName))
        {
            return;
        }

        await ExecuteBusyAsync(async () =>
        {
            var id = await _catalog.CreateManufacturerAsync(
                NewManufacturerName,
                NewManufacturerCountry);

            NewManufacturerName = string.Empty;
            NewManufacturerCountry = string.Empty;
            await LoadReferencesAsync();
            SelectedManufacturer = Manufacturers.FirstOrDefault(x => x.Id == id);
            StatusMessage = Translate("Manufacturer ready.", "تولیدکننده آماده شد.", "جوړونکی چمتو شو.");
        });
    }

    private async Task ImportPendingCsvAsync()
    {
        if (string.IsNullOrWhiteSpace(_pendingCsvPath))
        {
            return;
        }

        await ExecuteBusyAsync(async () =>
        {
            var result = await _csv.ImportAsync(_pendingCsvPath);
            StatusMessage = Translate(
                $"{result.Imported} medicines imported; {result.Rejected} rejected.",
                $"{result.Imported} دوا وارد شد؛ {result.Rejected} رد شد.",
                $"{result.Imported} درمل وارد شول؛ {result.Rejected} رد شول.");

            _pendingCsvPath = null;
            HasCsvPreview = false;
            CanImportCsv = false;
            CsvPreviewRows.Clear();
            ImportValidRowsCommand.NotifyCanExecuteChanged();

            await LoadReferencesAsync();
            await SearchCoreAsync();
        });
    }

    private async Task LoadReferencesAsync()
    {
        var references = await _catalog.GetReferenceDataAsync();

        var selectedCategoryId = SelectedCategory?.Id;
        var selectedManufacturerId = SelectedManufacturer?.Id;
        var filterCategoryId = SelectedFilterCategory?.Id;

        Categories.Clear();
        foreach (var category in references.Categories.Where(x => x.IsActive))
        {
            Categories.Add(category);
        }

        Manufacturers.Clear();
        foreach (var manufacturer in references.Manufacturers.Where(x => x.IsActive))
        {
            Manufacturers.Add(manufacturer);
        }

        SelectedCategory = Categories.FirstOrDefault(x => x.Id == selectedCategoryId);
        SelectedManufacturer = Manufacturers.FirstOrDefault(x => x.Id == selectedManufacturerId);
        SelectedFilterCategory = Categories.FirstOrDefault(x => x.Id == filterCategoryId);
    }

    private string NextMedicineCode()
    {
        var max = Medicines
            .Select(x => x.MedicineCode)
            .Where(x => x.StartsWith("MED-", StringComparison.OrdinalIgnoreCase))
            .Select(x => int.TryParse(x.AsSpan(4), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : 0)
            .DefaultIfEmpty(0)
            .Max();

        return $"MED-{max + 1:0000}";
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
        EditSelectedCommand.NotifyCanExecuteChanged();
        SaveCommand.NotifyCanExecuteChanged();
        AddCategoryCommand.NotifyCanExecuteChanged();
        AddManufacturerCommand.NotifyCanExecuteChanged();
        ImportValidRowsCommand.NotifyCanExecuteChanged();
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
        OnPropertyChanged(nameof(AddMedicineLabel));
        OnPropertyChanged(nameof(EditLabel));
        OnPropertyChanged(nameof(TemplateLabel));
        OnPropertyChanged(nameof(ImportLabel));
        OnPropertyChanged(nameof(SaveLabel));
        OnPropertyChanged(nameof(EditorTitle));
        OnPropertyChanged(nameof(ReferencesTitle));
    }
}
