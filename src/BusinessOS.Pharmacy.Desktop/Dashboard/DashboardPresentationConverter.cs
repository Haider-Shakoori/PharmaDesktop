using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using BusinessOS.Pharmacy.Application.Abstractions.Dashboard;

namespace BusinessOS.Pharmacy.Desktop.Dashboard;

/// <summary>View-only formatting and coordinates for data already supplied to the dashboard.</summary>
public sealed class DashboardPresentationConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var mode = parameter as string;
        if (mode == "RowNumber")
            return value is int index && index >= 0 ? (index + 1).ToString(culture) : string.Empty;

        if (mode is "ExpiryBackground" or "ExpiryForeground")
        {
            if (value is not int days) return DependencyProperty.UnsetValue;
            var color = mode == "ExpiryBackground"
                ? days < 14 ? "#FFE5EB" : days < 40 ? "#FFF0D5" : "#E1EFFF"
                : days < 14 ? "#DA315F" : days < 40 ? "#C87316" : "#2879C8";
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
            brush.Freeze();
            return brush;
        }

        var points = (value as IEnumerable<DashboardSalesPoint>)?.ToArray() ?? [];
        // Match the existing sales polyline coordinate space, without changing its calculation.
        const double left = 24, right = 696, bottom = 148;
        var step = points.Length <= 1 ? 0 : (right - left) / (points.Length - 1);
        if (mode == "Invoices")
        {
            var maximum = Math.Max(1, points.Select(p => p.Invoices).DefaultIfEmpty().Max());
            return points.Select((p, index) => new InvoiceBar(
                left + index * step - 6, bottom - Math.Max(0, p.Invoices) * 32d / maximum,
                Math.Max(0, p.Invoices) * 32d / maximum,
                $"{p.Hour:00}:00 — {p.Invoices:N0} invoices")).ToArray();
        }
        if (mode == "Hours")
            return points.Select((p, index) => new AxisLabel(left + index * step - 18,
                    p.Hour == 0 ? "12AM" : p.Hour < 12 ? $"{p.Hour}AM" : p.Hour == 12 ? "12PM" : $"{p.Hour - 12}PM"))
                .Where((_, index) => points.Length <= 14 || index % 2 == 0).ToArray();

        return DependencyProperty.UnsetValue;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;

    public sealed record InvoiceBar(double X, double Y, double Height, string Tooltip);
    public sealed record AxisLabel(double X, string Label);
}
