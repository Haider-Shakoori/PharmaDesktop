using System.Windows;

namespace BusinessOS.Pharmacy.Desktop.Networking;

public partial class DeploymentSetupWindow : Window
{
    private readonly DeploymentSetupViewModel _viewModel;

    public DeploymentSetupWindow(DeploymentSetupViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        _viewModel.SetupCompleted += OnSetupCompleted;
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.SetupCompleted -= OnSetupCompleted;
        base.OnClosed(e);
    }

    private void OnSetupCompleted(object? sender, EventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
