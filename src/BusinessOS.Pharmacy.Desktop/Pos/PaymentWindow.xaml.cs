using System.Windows;
using System.Windows.Input;

namespace BusinessOS.Pharmacy.Desktop.Pos;

public partial class PaymentWindow : Window
{
    private readonly PosViewModel _viewModel;

    public PaymentWindow(PosViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        _viewModel.PaymentCloseRequested += OnPaymentCloseRequested;
        Closed += OnClosed;
    }

    private void OnPaymentCloseRequested(object? sender, EventArgs e) =>
        CloseDialog(true);

    private void OnClosed(object? sender, EventArgs e) =>
        _viewModel.PaymentCloseRequested -= OnPaymentCloseRequested;

    private void OnCancelClick(object sender, RoutedEventArgs e) =>
        CloseDialog(false);

    private void CloseDialog(bool result)
    {
        try
        {
            DialogResult = result;
        }
        catch (InvalidOperationException)
        {
            Close();
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            CloseDialog(false);
            return;
        }

        if (e.Key == Key.F5)
        {
            if (_viewModel.SetCashToTotalCommand.CanExecute(null))
            {
                _viewModel.SetCashToTotalCommand.Execute(null);
            }

            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter ||
            (e.Key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)))
        {
            if (_viewModel.CheckoutCommand.CanExecute(null))
            {
                _viewModel.CheckoutCommand.Execute(null);
                e.Handled = true;
            }
        }
    }
}
