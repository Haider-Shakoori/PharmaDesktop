using System.Collections.ObjectModel;
using System.Globalization;
using BusinessOS.Pharmacy.Application.Abstractions.Inventory;
using BusinessOS.Pharmacy.Application.Abstractions.Medicines;
using BusinessOS.Pharmacy.Desktop.Localization;
using BusinessOS.Pharmacy.Desktop.Notifications;
using BusinessOS.Pharmacy.Desktop.Printing;
using BusinessOS.Pharmacy.Licensing;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;

namespace BusinessOS.Pharmacy.Desktop.Barcode;

public sealed record BarcodeLabelSize(string Name, double WidthMillimeters, double HeightMillimeters);

public sealed partial class BarcodePrintViewModel : ObservableObject
{
    private readonly IMedicineCatalogService _medicines;
    private readonly IInventoryService? _inventory;
    private readonly IActivationStore? _activationStore;
    private readonly NotificationService? _notifications;
    private UiLanguage _language = UiLanguageCatalog.All[0];
    private bool _suppressPreview;

    [ObservableProperty] private string searchText = string.Empty;
    [ObservableProperty] private MedicineListItem? selectedMedicine;
    [ObservableProperty] private string priceText = string.Empty;
    [ObservableProperty] private int copies = 1;
    [ObservableProperty] private BarcodeLabelSize? selectedLabelSize;
    [ObservableProperty] private string? selectedPrinter;
    [ObservableProperty] private bool showPharmacyName = true;
    [ObservableProperty] private bool showMedicineName = true;
    [ObservableProperty] private bool showPrice = true;
    [ObservableProperty] private bool showCode = true;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string statusMessage = string.Empty;
    [ObservableProperty] private object? previewElement;

    public BarcodePrintViewModel(IMedicineCatalogService medicines, IServiceProvider services)
    {
        _medicines = medicines;
        _inventory = services.GetService<IInventoryService>();
        _activationStore = services.GetService<IActivationStore>();
        _notifications = services.GetService<NotificationService>();

        LabelSizes =
        [
            new("50 × 30 mm", 50, 30),
            new("40 × 25 mm", 40, 25),
            new("60 × 40 mm", 60, 40),
            new("75 × 50 mm", 75, 50),
        ];
        selectedLabelSize = LabelSizes[0];

        SearchCommand = new AsyncRelayCommand(SearchAsync, () => !IsBusy);
        PrintCommand = new AsyncRelayCommand(PrintAsync, () => !IsBusy && SelectedMedicine is not null);
        RefreshPrintersCommand = new RelayCommand(LoadPrinters);
    }

    public ObservableCollection<MedicineListItem> Medicines { get; } = new();
    public IReadOnlyList<BarcodeLabelSize> LabelSizes { get; }
    public ObservableCollection<string> Printers { get; } = new();

    public IAsyncRelayCommand SearchCommand { get; }
    public IAsyncRelayCommand PrintCommand { get; }
    public IRelayCommand RefreshPrintersCommand { get; }

    public string Title => T("Barcode Printing", "چاپ بارکد", "بارکوډ چاپ");
    public string Subtitle => T(
        "Print medicine barcode labels with name, price and pharmacy branding",
        "چاپ برچسب بارکد دوا همراه با نام، قیمت و نام دواخانه",
        "د درمل بارکوډ لیبل د نوم، بیې او درملتون نوم سره چاپ کړئ");
    public string SearchLabel => T("Search medicine", "جستجوی دوا", "درمل ولټوئ");
    public string ResultsLabel => T("Medicines", "ادویه", "درمل");
    public string ResultsSummary => T(
        $"{Medicines.Count} medicines",
        $"{Medicines.Count} دوا",
        $"{Medicines.Count} درمل");
    public string OptionsLabel => T("Label options", "تنظیمات برچسب", "د لیبل تنظیمات");
    public string PharmacyNameLabel => T("Pharmacy name", "نام دواخانه", "د درملتون نوم");
    public string MedicineNameLabel => T("Medicine name", "نام دوا", "د درمل نوم");
    public string PriceLabel => T("Price (AFN)", "قیمت (افغانی)", "بیه (افغانۍ)");
    public string CodeLabel => T("Barcode number", "شماره بارکد", "بارکوډ شمېره");
    public string CopiesLabel => T("Copies", "تعداد نسخه", "کاپي شمېر");
    public string LabelSizeLabel => T("Label size", "اندازه برچسب", "د لیبل اندازه");
    public string PrinterLabel => T("Printer", "چاپگر", "چاپګر");
    public string PrintLabel => T("Print labels", "چاپ برچسب‌ها", "لیبلونه چاپ کړئ");
    public string PreviewLabel => T("Label preview", "پیش‌نمایش برچسب", "د لیبل مخکتنه");

    public void SetLanguage(UiLanguage language)
    {
        _language = language;
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(SearchLabel));
        OnPropertyChanged(nameof(ResultsLabel));
        OnPropertyChanged(nameof(ResultsSummary));
        OnPropertyChanged(nameof(OptionsLabel));
        OnPropertyChanged(nameof(PharmacyNameLabel));
        OnPropertyChanged(nameof(MedicineNameLabel));
        OnPropertyChanged(nameof(PriceLabel));
        OnPropertyChanged(nameof(CodeLabel));
        OnPropertyChanged(nameof(CopiesLabel));
        OnPropertyChanged(nameof(LabelSizeLabel));
        OnPropertyChanged(nameof(PrinterLabel));
        OnPropertyChanged(nameof(PrintLabel));
        OnPropertyChanged(nameof(PreviewLabel));
        RebuildPreview();
    }

    public async Task LoadAsync()
    {
        LoadPrinters();
        await SearchAsync();
    }

    private void LoadPrinters()
    {
        try
        {
            Printers.Clear();
            foreach (var printer in BarcodeLabelPrinter.GetPrinterNames())
            {
                Printers.Add(printer);
            }

            SelectedPrinter = BarcodeLabelPrinter.GetDefaultPrinterName() ?? Printers.FirstOrDefault();
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
        }
    }

    private async Task SearchAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        SearchCommand.NotifyCanExecuteChanged();

        try
        {
            var results = await _medicines.SearchAsync(new MedicineSearchFilter(
                string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim(),
                IsActive: true,
                Take: 250));

            Medicines.Clear();
            foreach (var medicine in results)
            {
                Medicines.Add(medicine);
            }

            StatusMessage = ResultsSummary;
            OnPropertyChanged(nameof(ResultsSummary));
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
        }
        finally
        {
            IsBusy = false;
            SearchCommand.NotifyCanExecuteChanged();
            PrintCommand.NotifyCanExecuteChanged();
        }
    }

    partial void OnSelectedMedicineChanged(MedicineListItem? value)
    {
        PrintCommand.NotifyCanExecuteChanged();
        RebuildPreview();

        if (value is not null)
        {
            _ = PrefillPriceAsync(value);
        }
    }

    partial void OnPriceTextChanged(string value) => RebuildPreview();
    partial void OnSelectedLabelSizeChanged(BarcodeLabelSize? value) => RebuildPreview();
    partial void OnShowPharmacyNameChanged(bool value) => RebuildPreview();
    partial void OnShowMedicineNameChanged(bool value) => RebuildPreview();
    partial void OnShowPriceChanged(bool value) => RebuildPreview();
    partial void OnShowCodeChanged(bool value) => RebuildPreview();

    private async Task PrefillPriceAsync(MedicineListItem medicine)
    {
        if (_inventory is null || !string.IsNullOrWhiteSpace(PriceText))
        {
            return;
        }

        try
        {
            var batches = await _inventory.SearchBatchesAsync(new InventoryBatchFilter(
                Search: medicine.MedicineCode,
                Take: 5));

            var price = batches
                .Where(x => x.SalePrice is > 0m)
                .Select(x => x.SalePrice!.Value)
                .DefaultIfEmpty(0m)
                .Max();

            if (price > 0m && string.IsNullOrWhiteSpace(PriceText))
            {
                PriceText = price.ToString("N2", CultureInfo.InvariantCulture);
            }
        }
        catch
        {
            // Price prefill is best-effort; the user can type it manually.
        }
    }

    private void RebuildPreview()
    {
        if (_suppressPreview || SelectedMedicine is null || SelectedLabelSize is null)
        {
            PreviewElement = null;
            return;
        }

        _suppressPreview = true;
        try
        {
            PreviewElement = BarcodeLabelPrinter.BuildLabel(
                BuildModel(),
                BarcodeLabelPrinter.MillimetersToDips(SelectedLabelSize.WidthMillimeters),
                BarcodeLabelPrinter.MillimetersToDips(SelectedLabelSize.HeightMillimeters));
        }
        finally
        {
            _suppressPreview = false;
        }
    }

    private BarcodeLabelModel BuildModel()
    {
        var medicine = SelectedMedicine!;
        var name = string.IsNullOrWhiteSpace(medicine.Strength)
            ? medicine.BrandName
            : $"{medicine.BrandName} {medicine.Strength}";
        var price = decimal.TryParse(PriceText, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? $"؋ {parsed:N2}"
            : "؋ —";

        return new BarcodeLabelModel(
            PharmacyName,
            name,
            medicine.MedicineCode,
            price,
            ShowPharmacyName,
            ShowMedicineName,
            ShowPrice,
            ShowCode);
    }

    private string PharmacyName { get; set; } = "Darmaltoon Pharmacy";

    public async Task LoadPharmacyNameAsync()
    {
        try
        {
            var activation = _activationStore is null ? null : await _activationStore.LoadAsync();
            if (!string.IsNullOrWhiteSpace(activation?.Tenant?.Name))
            {
                PharmacyName = activation!.Tenant!.Name;
                RebuildPreview();
            }
        }
        catch
        {
            // Keep the default pharmacy name.
        }
    }

    private async Task PrintAsync()
    {
        if (SelectedMedicine is null || SelectedLabelSize is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(SelectedPrinter))
        {
            StatusMessage = T("Select a printer first.", "ابتدا یک چاپگر انتخاب کنید.", "لومړی یو چاپګر وټاکئ.");
            _notifications?.ShowWarning(StatusMessage);
            return;
        }

        if (Copies < 1 || Copies > 500)
        {
            StatusMessage = T("Copies must be between 1 and 500.", "تعداد باید بین ۱ و ۵۰۰ باشد.", "کاپي باید د ۱ او ۵۰۰ ترمنځ وي.");
            _notifications?.ShowWarning(StatusMessage);
            return;
        }

        IsBusy = true;
        PrintCommand.NotifyCanExecuteChanged();

        try
        {
            var label = BarcodeLabelPrinter.BuildLabel(
                BuildModel(),
                BarcodeLabelPrinter.MillimetersToDips(SelectedLabelSize.WidthMillimeters),
                BarcodeLabelPrinter.MillimetersToDips(SelectedLabelSize.HeightMillimeters));

            await Task.Yield();
            BarcodeLabelPrinter.Print(label, SelectedPrinter, Copies);

            StatusMessage = T(
                $"Printed {Copies} label(s) for {SelectedMedicine.BrandName}.",
                $"{Copies} برچسب برای {SelectedMedicine.BrandName} چاپ شد.",
                $"{Copies} لیبل د {SelectedMedicine.BrandName} لپاره چاپ شول.");
            _notifications?.ShowSuccess(StatusMessage);
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
            _notifications?.ShowError(exception.Message);
        }
        finally
        {
            IsBusy = false;
            PrintCommand.NotifyCanExecuteChanged();
        }
    }

    private string T(string english, string dari, string pashto) =>
        _language.Code switch
        {
            "fa" => dari,
            "ps" => pashto,
            _ => english,
        };
}
