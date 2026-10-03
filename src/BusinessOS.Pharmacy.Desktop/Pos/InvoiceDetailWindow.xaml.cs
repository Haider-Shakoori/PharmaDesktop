using System.Windows;
using BusinessOS.Pharmacy.Application.Abstractions.Sales;

namespace BusinessOS.Pharmacy.Desktop.Pos;

public partial class InvoiceDetailWindow : Window
{
    private readonly SaleDetail _sale;
    private readonly Func<SaleDetail, bool> _print;
    private readonly Action<SaleDetail> _startReturn;

    public InvoiceDetailWindow(
        SaleDetail sale,
        Func<SaleDetail, bool> print,
        Action<SaleDetail> startReturn,
        bool canReturn)
    {
        InitializeComponent();
        _sale = sale;
        _print = print;
        _startReturn = startReturn;
        DataContext = sale;
        Title = $"Invoice {sale.Sale.SaleNumber}";
        ReturnButton.IsEnabled = canReturn;
    }

    private void OnPrintClick(object sender, RoutedEventArgs e) => _print(_sale);

    private void OnReturnClick(object sender, RoutedEventArgs e)
    {
        _startReturn(_sale);
        DialogResult = true;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
