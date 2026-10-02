using System.Windows;
using System.Windows.Controls;

namespace BusinessOS.Pharmacy.Desktop.Localization;

public partial class LanguageFlag : UserControl
{
    public static readonly DependencyProperty LanguageCodeProperty =
        DependencyProperty.Register(
            nameof(LanguageCode),
            typeof(string),
            typeof(LanguageFlag),
            new PropertyMetadata(string.Empty));

    public LanguageFlag()
    {
        InitializeComponent();
    }

    public string LanguageCode
    {
        get => (string)GetValue(LanguageCodeProperty);
        set => SetValue(LanguageCodeProperty, value);
    }
}
