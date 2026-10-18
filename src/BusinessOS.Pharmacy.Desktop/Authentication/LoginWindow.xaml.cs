using System.Windows;
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

    private void OnTogglePasswordVisibility(object sender, RoutedEventArgs e)
    {
        if (_passwordVisible)
        {
            // Synchronize only when switching controls. Avoid mirroring the
            // password on every keystroke; that caused unnecessary WPF text
            // layout and made credential entry feel sluggish.
            PasswordInput.Password = PasswordText.Text;
            _passwordVisible = false;
            PasswordText.Visibility = Visibility.Collapsed;
            PasswordInput.Visibility = Visibility.Visible;
            EyeIcon.Data = Geometry.Parse(EyeClosedGeometry);
            PasswordInput.Focus();
            return;
        }

        PasswordText.Text = PasswordInput.Password;
        _passwordVisible = true;
        PasswordInput.Visibility = Visibility.Collapsed;
        PasswordText.Visibility = Visibility.Visible;
        EyeIcon.Data = Geometry.Parse(EyeOpenGeometry);
        PasswordText.CaretIndex = PasswordText.Text.Length;
        PasswordText.Focus();
    }

    private void OnLoginSucceeded(object? sender, EventArgs e)
    {
        PasswordInput.Clear();
        PasswordText.Clear();
        DialogResult = true;
        Close();
    }
}
