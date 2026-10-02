using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using BusinessOS.Pharmacy.Application.Abstractions.Sales;

namespace BusinessOS.Pharmacy.Desktop.Printing;

public partial class ReceiptPreviewWindow : Window, INotifyPropertyChanged
{
    private readonly SaleDetail _sale;
    private readonly SaleReceiptPrinter _printer;
    private string? _selectedPrinter;

    public ReceiptPreviewWindow(SaleDetail sale, SaleReceiptPrinter printer)
    {
        InitializeComponent();
        _sale = sale;
        _printer = printer;
        PrinterNames = printer.GetPrinterNames();
        SelectedPrinter = printer.GetDefaultPrinterName() ?? PrinterNames.FirstOrDefault();
        DataContext = this;
        PreviewViewer.Document = printer.CreateDocument(sale, 390d);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<string> PrinterNames { get; }

    public string SaleNumberText => $"Invoice / receipt: {_sale.Sale.SaleNumber}";

    public string? SelectedPrinter
    {
        get => _selectedPrinter;
        set
        {
            if (string.Equals(_selectedPrinter, value, StringComparison.Ordinal))
            {
                return;
            }

            _selectedPrinter = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedPrinter)));
        }
    }

    public bool WasPrinted { get; private set; }

    private void OnPrintClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(SelectedPrinter))
        {
            MessageBox.Show(
                this,
                "Select a printer first.",
                "Darmaltoon",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        try
        {
            _printer.PrintToPrinter(_sale, SelectedPrinter);
            WasPrinted = true;
            DialogResult = true;
            Close();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                $"The receipt could not be printed.\n\n{exception.Message}",
                "Print error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
