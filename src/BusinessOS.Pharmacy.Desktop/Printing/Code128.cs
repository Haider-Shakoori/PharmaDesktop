using System.Windows;
using System.Windows.Media;

namespace BusinessOS.Pharmacy.Desktop.Printing;

/// <summary>
/// Minimal Code 128B encoder used for medicine barcode labels.
/// Produces vector geometry so labels stay crisp at any printer resolution.
/// </summary>
public static class Code128
{
    private static readonly string[] Patterns =
    [
        "212222", "222122", "222221", "121223", "121322", "131222", "122213",
        "122312", "132212", "221213", "221312", "231212", "112232", "122132",
        "122231", "113222", "123122", "123221", "223211", "221132", "221231",
        "213212", "223112", "312131", "311222", "321122", "321221", "312212",
        "322112", "322211", "212123", "212321", "232121", "111323", "131123",
        "131321", "112313", "132113", "132311", "211313", "231113", "231311",
        "112133", "112331", "132131", "113123", "113321", "133121", "313121",
        "211331", "231131", "213113", "213311", "213131", "311123", "311321",
        "331121", "312113", "312311", "332111", "314111", "221411", "431111",
        "111224", "111422", "121124", "121421", "141122", "141221", "112214",
        "112412", "122114", "122411", "142112", "142211", "241211", "221114",
        "413111", "241112", "134111", "111242", "121142", "121241", "114212",
        "124112", "124211", "411212", "421112", "421211", "212141", "214121",
        "412121", "111143", "111341", "131141", "114113", "114311", "411113",
        "411311", "113141", "114131", "311141", "411131", "211412", "211214",
        "211232", "2331112",
    ];

    public static Geometry BuildGeometry(string value, double width, double height)
    {
        var modules = BuildModules(value);
        var totalModules = modules.Sum(x => x.Width);
        if (totalModules <= 0 || width <= 0 || height <= 0)
        {
            return Geometry.Empty;
        }

        var moduleWidth = width / totalModules;
        var group = new GeometryGroup { FillRule = FillRule.Nonzero };
        var x = 0d;
        foreach (var run in modules)
        {
            var runWidth = run.Width * moduleWidth;
            if (run.IsBar)
            {
                group.Children.Add(new RectangleGeometry(new Rect(x, 0, runWidth, height)));
            }

            x += runWidth;
        }

        group.Freeze();
        return group;
    }

    private static List<(bool IsBar, int Width)> BuildModules(string value)
    {
        var codes = new List<int> { 104 };

        foreach (var character in value)
        {
            var code = character is >= ' ' and <= '~' ? character - 32 : '?' - 32;
            codes.Add(code);
        }

        var checksum = codes[0];
        for (var index = 1; index < codes.Count; index++)
        {
            checksum += codes[index] * index;
        }

        codes.Add(checksum % 103);
        codes.Add(106);

        var modules = new List<(bool IsBar, int Width)>();
        foreach (var code in codes)
        {
            var pattern = Patterns[code];
            var isBar = true;
            foreach (var symbol in pattern)
            {
                modules.Add((isBar, symbol - '0'));
                isBar = !isBar;
            }
        }

        return modules;
    }
}
