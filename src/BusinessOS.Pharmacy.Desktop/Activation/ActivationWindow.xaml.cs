using System.Windows;

namespace BusinessOS.Pharmacy.Desktop.Activation;

public partial class ActivationWindow : Window
{
    private readonly ActivationViewModel _viewModel;

    public ActivationWindow(ActivationViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        _viewModel.ActivationSucceeded += OnActivationSucceeded;
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.ActivationSucceeded -= OnActivationSucceeded;
        base.OnClosed(e);
    }

    private void OnActivationSucceeded(object? sender, EventArgs e)
    {
        DialogResult = true;
        Close();
    }
}