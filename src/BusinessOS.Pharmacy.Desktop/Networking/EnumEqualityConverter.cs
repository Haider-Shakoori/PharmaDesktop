using System.Globalization;
using System.Windows.Data;
using BusinessOS.Pharmacy.Application.Abstractions.Networking;

namespace BusinessOS.Pharmacy.Desktop.Networking;

public sealed class EnumEqualityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not DeploymentMode mode ||
            parameter is not string text ||
            !Enum.TryParse<DeploymentMode>(text, out var expected))
        {
            return false;
        }

        return mode == expected;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is true &&
            parameter is string text &&
            Enum.TryParse<DeploymentMode>(text, out var mode))
        {
            return mode;
        }

        return Binding.DoNothing;
    }
}
