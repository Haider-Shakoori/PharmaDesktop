using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace BusinessOS.Pharmacy.Desktop.Medicines;

public partial class MedicinesView : UserControl
{
    public MedicinesView()
    {
        InitializeComponent();
    }

    private async void OnDownloadTemplateClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MedicinesViewModel viewModel)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Save Darmaltoon Medicine CSV Template",
            Filter = "CSV files (*.csv)|*.csv",
            FileName = "darmaltoon_medicines_template.csv",
            DefaultExt = ".csv",
            AddExtension = true,
        };

        if (dialog.ShowDialog() == true)
        {
            await viewModel.WriteTemplateAsync(dialog.FileName);
        }
    }

    private async void OnImportCsvClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MedicinesViewModel viewModel)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "Import Darmaltoon Medicines CSV",
            Filter = "CSV files (*.csv)|*.csv",
            Multiselect = false,
            CheckFileExists = true,
        };

        if (dialog.ShowDialog() == true)
        {
            await viewModel.PreviewCsvAsync(dialog.FileName);
        }
    }
}
