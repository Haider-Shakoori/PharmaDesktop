using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace BusinessOS.Pharmacy.Desktop.Printing;

public sealed record BarcodeLabelModel(
    string PharmacyName,
    string MedicineName,
    string BarcodeValue,
    string PriceText,
    bool ShowPharmacyName,
    bool ShowMedicineName,
    bool ShowPrice,
    bool ShowCode);

/// <summary>
/// Builds and prints medicine barcode labels. Labels are rendered as vector
/// visuals so they remain sharp on thermal and laser printers.
/// </summary>
public static class BarcodeLabelPrinter
{
    public static FrameworkElement BuildLabel(BarcodeLabelModel model, double width, double height)
    {
        var panel = new Grid
        {
            Width = width,
            Height = height,
            Background = Brushes.White,
        };

        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var row = 0;

        var pharmacy = new TextBlock
        {
            Text = model.PharmacyName,
            FontSize = Math.Max(7, width * 0.065),
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(2, 2, 2, 0),
        };
        Grid.SetRow(pharmacy, row);
        panel.Children.Add(pharmacy);
        row++;

        var medicine = new TextBlock
        {
            Text = model.MedicineName,
            FontSize = Math.Max(6.5, width * 0.055),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(2, 1, 2, 0),
        };
        Grid.SetRow(medicine, row);
        panel.Children.Add(medicine);
        row++;

        var barcodeHeight = Math.Max(14, height * 0.42);
        var barcode = new Path
        {
            Data = Code128.BuildGeometry(model.BarcodeValue, Math.Max(20, width - 8), barcodeHeight),
            Fill = Brushes.Black,
            Stretch = Stretch.None,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetRow(barcode, row);
        panel.Children.Add(barcode);
        row++;

        var bottom = new Grid();
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var code = new TextBlock
        {
            Text = model.BarcodeValue,
            FontFamily = new FontFamily("Consolas"),
            FontSize = Math.Max(6.5, width * 0.05),
            Foreground = Brushes.Black,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(2, 1, 2, 2),
        };
        bottom.Children.Add(code);

        var price = new TextBlock
        {
            Text = model.PriceText,
            FontSize = Math.Max(7.5, width * 0.07),
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(2, 1, 2, 2),
        };
        Grid.SetColumn(price, 1);
        bottom.Children.Add(price);

        Grid.SetRow(bottom, row);
        panel.Children.Add(bottom);

        if (!model.ShowPharmacyName)
        {
            pharmacy.Visibility = Visibility.Collapsed;
        }

        if (!model.ShowMedicineName)
        {
            medicine.Visibility = Visibility.Collapsed;
        }

        if (!model.ShowCode)
        {
            code.Visibility = Visibility.Collapsed;
        }

        if (!model.ShowPrice)
        {
            price.Visibility = Visibility.Collapsed;
        }

        return panel;
    }

    public static IReadOnlyList<string> GetPrinterNames()
    {
        using var server = new LocalPrintServer();
        return server.GetPrintQueues()
            .Select(queue => queue.FullName)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string? GetDefaultPrinterName()
    {
        using var server = new LocalPrintServer();
        return server.DefaultPrintQueue?.FullName;
    }

    public static void Print(FrameworkElement label, string printerName, int copies)
    {
        ArgumentNullException.ThrowIfNull(label);
        ArgumentException.ThrowIfNullOrWhiteSpace(printerName);

        label.Measure(new Size(label.Width, label.Height));
        label.Arrange(new Rect(0, 0, label.Width, label.Height));
        label.UpdateLayout();

        using var server = new LocalPrintServer();
        using var queue = server.GetPrintQueue(printerName);
        var writer = PrintQueue.CreateXpsDocumentWriter(queue);
        var ticket = queue.DefaultPrintTicket;

        for (var copy = 0; copy < Math.Max(1, copies); copy++)
        {
            writer.Write(label, ticket);
        }
    }

    public static double MillimetersToDips(double millimeters) => millimeters * 96d / 25.4d;
}
