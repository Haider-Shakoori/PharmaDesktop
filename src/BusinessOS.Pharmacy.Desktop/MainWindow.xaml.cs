using System.Windows;
using System.Windows.Input;

namespace BusinessOS.Pharmacy.Desktop;

public partial class MainWindow : Window
{
    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0)
        {
            return;
        }

        if (e.Key is not (Key.S or Key.F))
        {
            return;
        }

        GlobalSearchBox.Focus();
        GlobalSearchBox.SelectAll();

        if (DataContext is MainWindowViewModel viewModel &&
            !string.IsNullOrWhiteSpace(viewModel.GlobalSearchText))
        {
            viewModel.GlobalSearchCommand.Execute(null);
        }

        e.Handled = true;
    }
}
