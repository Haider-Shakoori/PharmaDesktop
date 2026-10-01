using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace BusinessOS.Pharmacy.Desktop.Pos;

public partial class PosView : UserControl
{
    private PosViewModel? _viewModel;

    public PosView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        DataContextChanged += OnDataContextChanged;
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
        }

        _viewModel = viewModel;

        if (_viewModel is not null)
        {
            _viewModel.SearchFocusRequested += OnSearchFocusRequested;
        }
    }

    private void OnSearchFocusRequested(object? sender, EventArgs e) =>
        RequestSearchFocus();

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
