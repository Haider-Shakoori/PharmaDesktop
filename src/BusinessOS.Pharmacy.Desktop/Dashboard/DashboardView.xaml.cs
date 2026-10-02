using System.Windows;
using System.Windows.Controls;
using BusinessOS.Pharmacy.Desktop.Controls;

namespace BusinessOS.Pharmacy.Desktop.Dashboard;

public partial class DashboardView : UserControl
{
    public DashboardView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (Resources["ColumnsProxy"] is BindingProxy proxy)
        {
            proxy.Data = e.NewValue;
        }
    }
}
