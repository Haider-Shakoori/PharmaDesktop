using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace BusinessOS.Pharmacy.Desktop.Pos;

public partial class PosView : UserControl
{
    private PosViewModel? _viewModel;
    private PaymentWindow? _paymentWindow;

    public PosView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        DataContextChanged += OnDataContextChanged;
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Attach(DataContext as PosViewModel);
        RequestSearchFocus();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) =>
        Attach(null);

    private void OnDataContextChanged(
        object sender,
        DependencyPropertyChangedEventArgs e) =>
        Attach(e.NewValue as PosViewModel);

    private void Attach(PosViewModel? viewModel)
    {
        if (ReferenceEquals(_viewModel, viewModel))
        {
            return;
        }

        if (_viewModel is not null)
        {
            _viewModel.SearchFocusRequested -= OnSearchFocusRequested;
            _viewModel.CustomerFocusRequested -= OnCustomerFocusRequested;
            _viewModel.PaymentRequested -= OnPaymentRequested;
        }

        _viewModel = viewModel;

        if (_viewModel is not null)
        {
            _viewModel.SearchFocusRequested += OnSearchFocusRequested;
            _viewModel.CustomerFocusRequested += OnCustomerFocusRequested;
            _viewModel.PaymentRequested += OnPaymentRequested;
        }
    }

    private void OnSearchFocusRequested(object? sender, EventArgs e) =>
        RequestSearchFocus();

    private void OnCustomerFocusRequested(object? sender, EventArgs e)
    {
        CustomerCombo.Focus();
        Keyboard.Focus(CustomerCombo);
        CustomerCombo.IsDropDownOpen = true;
    }

    private void OnPaymentRequested(object? sender, EventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        if (_paymentWindow is not null)
        {
            _paymentWindow.Activate();
            return;
        }

        var owner = Window.GetWindow(this);
        var window = new PaymentWindow(_viewModel)
        {
            Owner = owner,
        };

        _paymentWindow = window;
        window.Closed += (_, _) =>
        {
            _paymentWindow = null;
            RequestSearchFocus();
        };

        window.ShowDialog();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_viewModel is null || _paymentWindow is not null)
        {
            return;
        }

        if (e.Key == Key.F1)
        {
            RequestSearchFocus();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F2)
        {
            CustomerCombo.Focus();
            Keyboard.Focus(CustomerCombo);
            CustomerCombo.IsDropDownOpen = true;
            e.Handled = true;
            return;
        }

        var payShortcut =
            e.Key == Key.F4 ||
            (e.Key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Control));

        if (payShortcut)
        {
            if (_viewModel.OpenPaymentCommand.CanExecute(null))
            {
                _viewModel.OpenPaymentCommand.Execute(null);
            }

            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            _viewModel.IsSearchDropdownOpen = false;
            RequestSearchFocus();
            e.Handled = true;
        }
    }

    private void RequestSearchFocus()
    {
        Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            () =>
            {
                SearchBox.Focus();
                Keyboard.Focus(SearchBox);
                SearchBox.SelectAll();
            });
    }
}
