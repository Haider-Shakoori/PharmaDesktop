using System.Windows;
using System.Windows.Controls;

namespace BusinessOS.Pharmacy.Desktop.Authentication;

public partial class PasswordChangeView : UserControl
{
    public PasswordChangeView()
    {
        InitializeComponent();
    }

    private void OnCurrentPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is PasswordChangeViewModel viewModel)
        {
            viewModel.CurrentPassword = CurrentPasswordInput.Password;
        }
    }

    private void OnNewPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is PasswordChangeViewModel viewModel)
        {
            viewModel.NewPassword = NewPasswordInput.Password;
        }
    }

    private void OnConfirmPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is PasswordChangeViewModel viewModel)
        {
            viewModel.ConfirmPassword = ConfirmPasswordInput.Password;
        }
    }
}
