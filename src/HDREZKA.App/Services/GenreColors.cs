using Microsoft.UI.Xaml.Media;

namespace HDREZKA.App.Services;

/// <summary>
/// Colorful details and hover previews share the same deterministic per-genre
/// palette. Hue collisions within a single movie are separated so two visible
/// labels do not accidentally look like the same genre. Neutral mode stays muted.
/// </summary>
public static class GenreColors
{
    public static IReadOnlyList<(SolidColorBrush Bg, SolidColorBrush Fg)>
        Palette(IReadOnlyList<string> names, bool light, bool colorful)
    {
        var result = new List<(SolidColorBrush, SolidColorBrush)>(names.Count);
        var allocated = new List<double>();
        foreach (var name in names)
        {
            var hue = BaseHue(name);
            // A minimum 28-degree distance between labels makes neighbouring
            // genres distinguishable while preserving a familiar base hue.
            for (var tries = 0; tries < 18 &&
                 allocated.Any(old => AngularDistance(old, hue) < 28); tries++)
                hue = (hue + 137.508) % 360;
            allocated.Add(hue);

            var background = colorful
                ? Hsl(hue, light ? .58 : .38, light ? .91 : .26)
                : light ? Color(235, 238, 242) : Color(52, 55, 61);
            var foreground = colorful
                ? Hsl(hue, light ? .70 : .76, light ? .33 : .75)
                : light ? Color(64, 69, 77) : Color(207, 212, 219);
            result.Add((new SolidColorBrush(background),
                        new SolidColorBrush(foreground)));
        }
        return result;
    }

    private static double BaseHue(string value)
    {
        var name = value.Trim().ToLowerInvariant();
        // Frequently used genres keep their own stable, separated hue families.
        if (name.Contains("фантаст") || name.Contains("sci-fi") ||
            name.Contains("science fiction")) return 204;
        if (name.Contains("фэнтез") || name.Contains("fantasy")) return 266;
        if (name.Contains("драм") || name.Contains("drama")) return 349;
        if (name.Contains("мелодрам") || name.Contains("romance")) return 312;
        if (name.Contains("приключ") || name.Contains("adventure")) return 143;
        if (name.Contains("комед") || name.Contains("comedy")) return 50;
        if (name.Contains("детектив") || name.Contains("detective")) return 185;
        if (name.Contains("триллер") || name.Contains("thriller")) return 12;
        if (name.Contains("ужас") || name.Contains("horror")) return 3;
        if (name.Contains("боев") || name.Contains("action")) return 22;
        if (name.Contains("документ") || name.Contains("documentary")) return 170;
        if (name.Contains("биограф") || name.Contains("biography")) return 114;
        if (name.Contains("мистич") || name.Contains("mystery")) return 283;
        if (name.Contains("музык") || name.Contains("music")) return 326;
        if (name.Contains("истор") || name.Contains("history")) return 85;
        if (name.Contains("спорт") || name.Contains("sport")) return 99;
        if (name.Contains("кримин") || name.Contains("crime")) return 235;
        if (name.Contains("семейн") || name.Contains("family")) return 163;
        if (name.Contains("военн") || name.Contains("war")) return 76;
        if (name.Contains("вестерн") || name.Contains("western")) return 39;

        // FNV-1a, unlike string.GetHashCode(), is stable between launches.
        uint hash = 2166136261;
        foreach (var ch in name)
        {
            hash ^= ch;
            hash *= 16777619;
        }
        return hash % 360;
    }

    private static double AngularDistance(double a, double b)
    {
        var delta = Math.Abs(a - b) % 360;
        return Math.Min(delta, 360 - delta);
    }

    private static Windows.UI.Color Color(byte r, byte g, byte b)
        => Windows.UI.Color.FromArgb(255, r, g, b);

    private static Windows.UI.Color Hsl(double hue, double saturation, double lightness)
    {
        var c = (1 - Math.Abs(2 * lightness - 1)) * saturation;
        var x = c * (1 - Math.Abs((hue / 60) % 2 - 1));
        var m = lightness - c / 2;
        (double r, double g, double b) rgb = hue switch
        {
            < 60 => (c, x, 0),
            < 120 => (x, c, 0),
            < 180 => (0, c, x),
            < 240 => (0, x, c),
            < 300 => (x, 0, c),
            _ => (c, 0, x)
        };
        static byte Channel(double value) =>
            (byte)Math.Clamp(Math.Round(value * 255), 0, 255);
        return Color(Channel(rgb.r + m), Channel(rgb.g + m), Channel(rgb.b + m));
    }
}

/// <summary>Score-dependent red -> amber -> green background on a 0..10 scale.</summary>
public static class RezkaScoreColors
{
    public static SolidColorBrush Solid(float score)
    {
        var t = Math.Clamp(score, 0, 10);
        var start = t <= 5 ? (r: 207d, g: 58d, b: 69d) :
            (r: 231d, g: 147d, b: 50d);
        var end = t <= 5 ? (r: 231d, g: 147d, b: 50d) :
            (r: 33d, g: 162d, b: 100d);
        var k = t <= 5 ? t / 5 : (t - 5) / 5;
        static byte Mix(double a, double b, double amount) =>
            (byte)Math.Clamp(Math.Round(a + (b - a) * amount), 0, 255);
        return new SolidColorBrush(Windows.UI.Color.FromArgb(255,
            Mix(start.r, end.r, k), Mix(start.g, end.g, k), Mix(start.b, end.b, k)));
    }

    public static LinearGradientBrush Gradient(float score)
    {
        var baseColor = Solid(score).Color;
        static byte Adjust(byte value, double strength, bool lighten) =>
            (byte)Math.Clamp(Math.Round(lighten
                ? value + (255 - value) * strength
                : value * (1 - strength)), 0, 255);
        var start = Windows.UI.Color.FromArgb(255,
            Adjust(baseColor.R, .13, true), Adjust(baseColor.G, .13, true),
            Adjust(baseColor.B, .13, true));
        var end = Windows.UI.Color.FromArgb(255,
            Adjust(baseColor.R, .11, false), Adjust(baseColor.G, .11, false),
            Adjust(baseColor.B, .11, false));
        var result = new LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0, .5),
            EndPoint = new Windows.Foundation.Point(1, .5)
        };
        result.GradientStops.Add(new GradientStop { Offset = 0, Color = start });
        result.GradientStops.Add(new GradientStop { Offset = 1, Color = end });
        return result;
    }

    public static SolidColorBrush Text(float score) =>
        new(Windows.UI.Color.FromArgb(255, 255, 255, 255));
}
