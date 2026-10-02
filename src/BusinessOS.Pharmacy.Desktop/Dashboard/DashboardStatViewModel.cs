using System.Windows;
using System.Windows.Media;

namespace BusinessOS.Pharmacy.Desktop.Dashboard;

public sealed record DashboardStatViewModel(
    string Label,
    string Value,
    string Note,
    string IconKey,
    string IconForeground,
    string IconBackground)
{
    private string AccentArtColor => IconKey switch
    {
        "reports" => "#3FC365",
        "purchases" => "#159CF4",
        "inventory" => "#FFB51B",
        "batches" => "#FA3D70",
        "expenses" => "#6035EF",
        _ => IconForeground,
    };

    /// <summary>Glossy vertical gradient for the Glass KPI icon tile.</summary>
    public Brush AccentTileBrush => CreateTile(AccentArtColor);

    /// <summary>Card artwork (tile + ribbon) used by the Glass KPI cards.</summary>
    public string AccentArtUri => IconKey switch
    {
        "reports" => AssetUri("kpi-sales.png"),
        "purchases" => AssetUri("kpi-purchases.png"),
        "inventory" => AssetUri("kpi-inventory.png"),
        "batches" => AssetUri("kpi-expiring.png"),
        "expenses" => AssetUri("kpi-cash.png"),
        _ => string.Empty,
    };

    /// <summary>Flowing translucent ribbon used at the bottom-right of the Glass KPI card.</summary>
    public Brush AccentRibbonBrush => CreateRibbon(AccentArtColor);

    /// <summary>Soft radial accent glow behind the ribbon.</summary>
    public Brush AccentGlow => CreateGlow(AccentArtColor);

    private static Brush CreateTile(string color)
    {
        var accent = Parse(color);
        var light = Blend(accent, Colors.White, 0.45);
        var dark = Blend(accent, Colors.Black, 0.12);

        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0.05, 0),
            EndPoint = new Point(0.95, 1),
        };

        brush.GradientStops.Add(new GradientStop(light, 0));
        brush.GradientStops.Add(new GradientStop(accent, 0.55));
        brush.GradientStops.Add(new GradientStop(dark, 1));
        brush.Freeze();
        return brush;
    }

    private static Brush CreateRibbon(string color)
    {
        var accent = Parse(color);

        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 0),
        };

        brush.GradientStops.Add(new GradientStop(WithAlpha(accent, 0), 0));
        brush.GradientStops.Add(new GradientStop(WithAlpha(accent, 170), 0.42));
        brush.GradientStops.Add(new GradientStop(WithAlpha(accent, 60), 1));
        brush.Freeze();
        return brush;
    }

    private static Brush CreateGlow(string color)
    {
        var accent = Parse(color);

        var brush = new RadialGradientBrush
        {
            GradientOrigin = new Point(1, 1),
            Center = new Point(1, 1),
            RadiusX = 1.25,
            RadiusY = 1.45,
        };

        brush.GradientStops.Add(new GradientStop(WithAlpha(accent, 210), 0));
        brush.GradientStops.Add(new GradientStop(WithAlpha(accent, 95), 0.55));
        brush.GradientStops.Add(new GradientStop(WithAlpha(accent, 0), 1));
        brush.Freeze();
        return brush;
    }

    private static string AssetUri(string file) =>
        $"pack://application:,,,/{typeof(DashboardStatViewModel).Assembly.GetName().Name};component/Assets/Glass/{file}";

    private static Color Parse(string color) =>
        (Color)ColorConverter.ConvertFromString(color);

    private static Color WithAlpha(Color color, byte alpha) =>
        Color.FromArgb(alpha, color.R, color.G, color.B);

    private static Color Blend(Color color, Color other, double amount) =>
        Color.FromArgb(
            255,
            (byte)(color.R + (other.R - color.R) * amount),
            (byte)(color.G + (other.G - color.G) * amount),
            (byte)(color.B + (other.B - color.B) * amount));
}
