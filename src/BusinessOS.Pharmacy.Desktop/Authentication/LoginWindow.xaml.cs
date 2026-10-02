using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BusinessOS.Pharmacy.Desktop.Authentication;

public partial class LoginWindow : Window
{
    private const string EyeOpenGeometry =
        "M2,12 C6,5.5 18,5.5 22,12 C18,18.5 6,18.5 2,12 Z M12,9.2 A2.8,2.8 0 1 0 12,14.8 A2.8,2.8 0 1 0 12,9.2 Z";

    private const string EyeClosedGeometry =
        "M4,4 L20,20 M2,12 C6,5.5 18,5.5 22,12 C18,18.5 6,18.5 2,12 Z";

    private readonly LoginViewModel _viewModel;
    private bool _passwordVisible;
    private bool _syncingPassword;

    public LoginWindow(LoginViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        _viewModel.LoginSucceeded += OnLoginSucceeded;
        Loaded += (_, _) => EmailInput.Focus();
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.LoginSucceeded -= OnLoginSucceeded;
        PasswordInput.Clear();
        PasswordText.Clear();
        base.OnClosed(e);
    }

    private string CurrentPassword => _passwordVisible ? PasswordText.Text : PasswordInput.Password;

    private async void OnSignInClick(object sender, RoutedEventArgs e)
    {
        await _viewModel.SignInAsync(CurrentPassword);
    }

    private void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_syncingPassword)
        {
            return;
        }

        _syncingPassword = true;
        PasswordText.Text = PasswordInput.Password;
        _syncingPassword = false;
    }

    private void OnPasswordTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncingPassword)
        {
            return;
        }

        _syncingPassword = true;
        PasswordInput.Password = PasswordText.Text;
        _syncingPassword = false;
    }

    private void OnTogglePasswordVisibility(object sender, RoutedEventArgs e)
    {
        _passwordVisible = !_passwordVisible;

        PasswordInput.Visibility = _passwordVisible ? Visibility.Collapsed : Visibility.Visible;
        PasswordText.Visibility = _passwordVisible ? Visibility.Visible : Visibility.Collapsed;
        EyeIcon.Data = Geometry.Parse(_passwordVisible ? EyeOpenGeometry : EyeClosedGeometry);

        if (_passwordVisible)
        {
            PasswordText.CaretIndex = PasswordText.Text.Length;
            PasswordText.Focus();
        }
        else
        {
            PasswordInput.Focus();
        }
    }

    private void OnLoginSucceeded(object? sender, EventArgs e)
    {
        PasswordInput.Clear();
        PasswordText.Clear();
        DialogResult = true;
        Close();
    }
}
