using System.Collections.ObjectModel;
using System.IO;
using BusinessOS.Pharmacy.Application.Abstractions.Reports;
using BusinessOS.Pharmacy.Desktop.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace BusinessOS.Pharmacy.Desktop.Reports;

public sealed partial class ReportsViewModel : ObservableObject
{
    private readonly IPharmacyReportService _reports;
    private UiLanguage _language = UiLanguageCatalog.All[0];

    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string statusMessage = string.Empty;
    [ObservableProperty] private string fromText = string.Empty;
    [ObservableProperty] private string toText = string.Empty;
    [ObservableProperty] private ReportSummary? summary;
    [ObservableProperty] private string selectedExportType = "sales";

    public ReportsViewModel(IPharmacyReportService reports)
    {
        _reports = reports;
        LoadCommand = new AsyncRelayCommand(LoadAsync, () => !IsBusy);
        ExportCommand = new AsyncRelayCommand(ExportAsync, () => !IsBusy && ExportTypes.Contains(SelectedExportType));
    }

    public IAsyncRelayCommand LoadCommand { get; }
    public IAsyncRelayCommand ExportCommand { get; }

    public ObservableCollection<ReportSaleItem> Sales { get; } = new();
    public ObservableCollection<ReportReturnItem> Returns { get; } = new();
    public ObservableCollection<ReportPurchaseItem> Purchases { get; } = new();
    public ObservableCollection<ReportMovementItem> Movements { get; } = new();
    public ObservableCollection<ReportNearExpiryItem> NearExpiry { get; } = new();
    public ObservableCollection<ReportLowStockItem> LowStock { get; } = new();

    public IReadOnlyList<string> ExportTypes { get; } = ["sales", "returns", "purchases", "movements"];

    public string Title => Translate("Reports", "گزارش‌ها", "راپورونه");
    public string SummaryTitle => Translate("Performance summary", "خلاصه عملکرد", "د فعالیت لنډیز");
    public string LoadLabel => Translate("Refresh report", "به‌روزرسانی", "راپور تازه کړئ");
    public string ExportLabel => Translate("Export CSV", "خروجی CSV", "CSV صادر کړئ");

    public void SetLanguage(UiLanguage language)
    {
        _language = language;
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(SummaryTitle));
        OnPropertyChanged(nameof(LoadLabel));
        OnPropertyChanged(nameof(ExportLabel));
    }

    public async Task LoadAsync()
    {
        await ExecuteBusyAsync(async () =>
        {
            var range = ParseRange();
            var report = await _reports.GetAsync(range);

            FromText = report.Range.From.ToString("yyyy-MM-dd");
            ToText = report.Range.To.ToString("yyyy-MM-dd");
            Summary = report.Summary;

            Replace(Sales, report.Sales);
            Replace(Returns, report.Returns);
            Replace(Purchases, report.Purchases);
            Replace(Movements, report.Movements);
            Replace(NearExpiry, report.NearExpiry);
            Replace(LowStock, report.LowStock);

            StatusMessage = Translate(
                $"Report loaded for {report.Range.From:yyyy-MM-dd} to {report.Range.To:yyyy-MM-dd}.",
                $"گزارش از {report.Range.From:yyyy-MM-dd} تا {report.Range.To:yyyy-MM-dd} بارگذاری شد.",
                $"راپور له {report.Range.From:yyyy-MM-dd} څخه تر {report.Range.To:yyyy-MM-dd} پورې پورته شو.");
        });
    }

    partial void OnSelectedExportTypeChanged(string value) =>
        ExportCommand.NotifyCanExecuteChanged();

    private async Task ExportAsync()
    {
        await ExecuteBusyAsync(async () =>
        {
            var csv = await _reports.BuildCsvAsync(SelectedExportType, ParseRange());
            var dialog = new SaveFileDialog
            {
                FileName = csv.FileName,
                DefaultExt = ".csv",
                Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                AddExtension = true,
                OverwritePrompt = true,
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            await File.WriteAllTextAsync(dialog.FileName, csv.Content);
            StatusMessage = Translate(
                $"Exported {csv.FileName}.",
                $"{csv.FileName} صادر شد.",
                $"{csv.FileName} صادر شو.");
        });
    }

    private ReportRange ParseRange()
    {
        DateOnly? from = null;
        DateOnly? to = null;

        if (!string.IsNullOrWhiteSpace(FromText))
        {
            if (!DateOnly.TryParse(FromText.Trim(), out var parsed))
            {
                throw new ArgumentException("From date must be a valid date.");
            }
            from = parsed;
        }

        if (!string.IsNullOrWhiteSpace(ToText))
        {
            if (!DateOnly.TryParse(ToText.Trim(), out var parsed))
            {
                throw new ArgumentException("To date must be a valid date.");
            }
            to = parsed;
        }

        return _reports.ResolveRange(from, to);
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
        LoadCommand.NotifyCanExecuteChanged();
        ExportCommand.NotifyCanExecuteChanged();
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> source)
    {
        target.Clear();
        foreach (var item in source)
        {
            target.Add(item);
        }
    }

    private string Translate(string english, string dari, string pashto) =>
        _language.Code switch
        {
            "fa" => dari,
            "ps" => pashto,
            _ => english,
        };
}
