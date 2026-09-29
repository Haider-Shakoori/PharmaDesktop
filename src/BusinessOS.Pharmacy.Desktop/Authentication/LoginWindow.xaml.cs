using System.Windows;

namespace BusinessOS.Pharmacy.Desktop.Authentication;

public partial class LoginWindow : Window
{
    private readonly LoginViewModel _viewModel;

    public LoginWindow(LoginViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        _viewModel.LoginSucceeded += OnLoginSucceeded;
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.LoginSucceeded -= OnLoginSucceeded;
        PasswordInput.Clear();
        base.OnClosed(e);
    }

    private async void OnSignInClick(object sender, RoutedEventArgs e)
    {
        await _viewModel.SignInAsync(PasswordInput.Password);
    }

    private void OnLoginSucceeded(object? sender, EventArgs e)
    {
        PasswordInput.Clear();
        DialogResult = true;
        Close();
    }
}
