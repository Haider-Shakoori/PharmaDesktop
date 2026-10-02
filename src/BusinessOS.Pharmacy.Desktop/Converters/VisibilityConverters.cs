using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace BusinessOS.Pharmacy.Desktop.Converters;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var flag = value is bool boolean && boolean;

        if (Invert)
        {
            flag = !flag;
        }

        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Visibility visibility && visibility == Visibility.Visible;
}

public sealed class CountToVisibilityConverter : IValueConverter
{
    public bool Empty { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var count = value switch
        {
            int number => number,
            ICollection collection => collection.Count,
            _ => 0,
        };

        var hasItems = count > 0;

        return Empty != hasItems ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}