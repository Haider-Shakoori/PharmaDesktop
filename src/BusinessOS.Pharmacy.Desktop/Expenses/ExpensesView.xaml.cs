using System.Windows.Controls;
using System.Windows.Input;

namespace BusinessOS.Pharmacy.Desktop.Expenses;

public partial class ExpensesView : UserControl
{
    public ExpensesView()
    {
        InitializeComponent();
    }

    private async void OnExpensesDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not ExpensesViewModel viewModel ||
            viewModel.SelectedExpense is not { Status: "posted" })
        {
            return;
        }

        await viewModel.EditSelectedExpenseAsync();
        e.Handled = true;
    }
}
