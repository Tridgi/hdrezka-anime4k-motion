using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace HDREZKA.App.Services;

/// <summary>
/// Keeps Windows system light/dark and the independent manual accent scheme
/// separate. Brushes are shared instances; changing Color refreshes existing
/// consumers without recreating the window or resetting its navigation.
/// </summary>
public static class ThemeHelper
{
    public sealed record ColorScheme(string Id, string Name, string Primary, string Companion);

    // Restrained Fluent-inspired combinations: one clear accent and a softer
    // companion. The settings UI renders these as a 4 x 3 selection matrix.
    public static IReadOnlyList<ColorScheme> Schemes { get; } =
    [
        new("ocean", "Океан", "#377DFF", "#5AD6CF"),
        new("violet", "Ирис", "#8264ED", "#CF9AFB"),
        new("mint", "Мята", "#148E79", "#70CEB4"),
        new("coral", "Коралл", "#DA6562", "#F7B38E"),
        new("amber", "Янтарь", "#B77919", "#F2C66D"),
        new("rose", "Роза", "#C34F8C", "#E9A3C6"),
        new("slate", "Графит", "#54718E", "#8FAAC2"),
        new("cyan", "Лагуна", "#197FAD", "#6CC7DB"),
        new("lime", "Лайм", "#678E34", "#B7D475"),
        new("plum", "Слива", "#9057A8", "#D49DD8"),
        new("ruby", "Рубин", "#B84C58", "#E6999B"),
        new("sky", "Небо", "#487DBD", "#8BBAD8"),
    ];

    public static FrameworkElement? Root { get; private set; }

    public static SolidColorBrush AccentBrush { get; } = new(Windows.UI.Color.FromArgb(255, 55, 125, 255));
    public static SolidColorBrush CompanionBrush { get; } = new(Windows.UI.Color.FromArgb(255, 90, 214, 207));
    public static SolidColorBrush AccentForegroundBrush { get; } =
        new(Windows.UI.Color.FromArgb(255, 255, 255, 255));

    public static event Action? AccentChanged;

    public static bool TryParseHex(string? text, out Windows.UI.Color color)
    {
        color = Windows.UI.Color.FromArgb(255, 55, 125, 255);
        if (text is not { Length: 7 } || text[0] != '#') return false;
        if (!byte.TryParse(text.Substring(1, 2), System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out var r) ||
            !byte.TryParse(text.Substring(3, 2), System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out var g) ||
            !byte.TryParse(text.Substring(5, 2), System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out var b)) return false;
        color = Windows.UI.Color.FromArgb(255, r, g, b);
        return true;
    }

    public static Windows.UI.Color AccentColor => AccentBrush.Color;

    public static void Apply(AppTheme theme)
    {
        var settings = SettingsService.Instance;
        if (Root != null)
        {
            Root.RequestedTheme = theme switch
            {
                AppTheme.Light => ElementTheme.Light,
                AppTheme.Dark => ElementTheme.Dark,
                AppTheme.Manual => settings.ManualThemeDark ? ElementTheme.Dark : ElementTheme.Light,
                _ => ElementTheme.Default,
            };
        }

        Windows.UI.Color accent;
        if (theme == AppTheme.Manual && TryParseHex(settings.ManualAccentHex, out var manual))
            accent = manual;
        else
        {
            try
            {
                accent = new Windows.UI.ViewManagement.UISettings()
                    .GetColorValue(Windows.UI.ViewManagement.UIColorType.Accent);
            }
            catch
            {
                accent = Windows.UI.Color.FromArgb(255, 55, 125, 255);
            }
        }

        AccentBrush.Color = accent;
        CompanionBrush.Color = theme == AppTheme.Manual &&
            TryParseHex(settings.ManualCompanionHex, out var companion)
                ? companion : accent;

        // Approximate WCAG relative luminance to keep button labels legible.
        static double Channel(byte v)
        {
            var x = v / 255.0;
            return x <= 0.04045 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4);
        }
        var luminance = .2126 * Channel(accent.R) +
                        .7152 * Channel(accent.G) +
                        .0722 * Channel(accent.B);
        AccentForegroundBrush.Color = luminance > .36
            ? Windows.UI.Color.FromArgb(255, 26, 30, 36)
            : Windows.UI.Color.FromArgb(255, 255, 255, 255);

        AccentChanged?.Invoke();
    }

    public static void SetRoot(FrameworkElement root)
    {
        Root = root;
        Apply(SettingsService.Instance.Theme);
    }
}
