using System.Windows;

namespace BusinessOS.Pharmacy.Desktop.Appearance;

/// <summary>
/// Swaps the active theme ResourceDictionary at runtime.
/// Base styles resolve their brushes/geometry with DynamicResource,
/// so panels, controls and the shell update without restarting.
/// </summary>
public static class ThemeManager
{
    public const string GlassSource = "Themes/Glass.xaml";

    public static event Action<AppearanceTheme>? ThemeChanged;

    public static AppearanceTheme Current { get; private set; } = AppearanceTheme.Classic;

    public static void Apply(AppearanceTheme theme)
    {
        var application = System.Windows.Application.Current;
        if (application is null)
        {
            Current = theme;
            return;
        }

        var dictionaries = application.Resources.MergedDictionaries;
        var glass = dictionaries.FirstOrDefault(IsGlassDictionary);

        if (theme == AppearanceTheme.Glass)
        {
            if (glass is null)
            {
                var assemblyName = typeof(ThemeManager).Assembly.GetName().Name;
                dictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri(
                        $"pack://application:,,,/{assemblyName};component/Themes/Glass.xaml",
                        UriKind.Absolute),
                });
            }
        }
        else if (glass is not null)
        {
            dictionaries.Remove(glass);
        }

        Current = theme;
        ThemeChanged?.Invoke(theme);
    }

    private static bool IsGlassDictionary(ResourceDictionary dictionary) =>
        dictionary.Source is not null &&
        dictionary.Source.OriginalString.EndsWith("Glass.xaml", StringComparison.OrdinalIgnoreCase);
}