using HDREZKA.App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

namespace HDREZKA.App.Views;

/// <summary>
/// All visual preferences live here. Controls use the existing SettingsService
/// and ThemeHelper, so changes affect the running app immediately and persist.
/// </summary>
public sealed partial class AppearancePage : Page
{
    private bool _initialized;

    public AppearancePage()
    {
        InitializeComponent();
        ActualThemeChanged += (_, _) => DispatcherQueue.TryEnqueue(UpdatePreview);
        SizeChanged += (_, _) => UpdateAccentMatrixWidth();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
        SettingsService.Instance.DetailPaletteChanged += OnPaletteChanged;
        SettingsService.Instance.PosterSizeChanged += OnPosterSizeChanged;
        ThemeHelper.AccentChanged += OnAccentChanged;
        LoadControls();
        ApplyLocalization();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
        SettingsService.Instance.DetailPaletteChanged -= OnPaletteChanged;
        SettingsService.Instance.PosterSizeChanged -= OnPosterSizeChanged;
        ThemeHelper.AccentChanged -= OnAccentChanged;
    }

    private void OnLanguageChanged() => DispatcherQueue.TryEnqueue(ApplyLocalization);
    private void OnPaletteChanged() => DispatcherQueue.TryEnqueue(UpdatePreview);
    private void OnPosterSizeChanged() => DispatcherQueue.TryEnqueue(SyncPosterSize);
    private void OnAccentChanged() => DispatcherQueue.TryEnqueue(() =>
    {
        UpdateAccentPreview();
        UpdatePreview();
    });

    private void LoadControls()
    {
        _initialized = false;
        var settings = SettingsService.Instance;
        ThemeBox.Items.Clear();
        ThemeBox.Items.Add(new ComboBoxItem { Tag = AppTheme.System });
        ThemeBox.Items.Add(new ComboBoxItem { Tag = AppTheme.Light });
        ThemeBox.Items.Add(new ComboBoxItem { Tag = AppTheme.Dark });
        ThemeBox.Items.Add(new ComboBoxItem { Tag = AppTheme.Manual });
        ThemeBox.SelectedIndex = (int)settings.Theme;

        ManualBrightnessBox.Items.Clear();
        ManualBrightnessBox.Items.Add(new ComboBoxItem { Tag = true });
        ManualBrightnessBox.Items.Add(new ComboBoxItem { Tag = false });
        ManualBrightnessBox.SelectedIndex = settings.ManualThemeDark ? 0 : 1;
        ManualThemePanel.Visibility = settings.Theme == AppTheme.Manual
            ? Visibility.Visible : Visibility.Collapsed;

        PaletteBox.Items.Clear();
        PaletteBox.Items.Add(new ComboBoxItem { Tag = DetailsPalette.Neutral });
        PaletteBox.Items.Add(new ComboBoxItem { Tag = DetailsPalette.Colorful });
        PaletteBox.SelectedIndex = (int)settings.DetailPalette;

        PosterSizeBox.Items.Clear();
        foreach (var (label, width) in new[]
                 { ("S", 104), ("M", 132), ("L", 164), ("XL", 196) })
            PosterSizeBox.Items.Add(new ComboBoxItem
                { Content = $"{label} · {width}px", Tag = width });
        SyncPosterSize();

        AccentHexBox.Text = settings.ManualAccentHex;
        BuildAccentMatrix();
        UpdateAccentPreview();
        _initialized = true;
        UpdatePreview();
    }

    private void ApplyLocalization()
    {
        TitleText.Text = Loc.Get("Nav.Appearance");
        IntroText.Text = Loc.Get("Appearance.Intro");
        ThemeHeader.Text = Loc.Get("Settings.Theme");
        ManualBrightnessHeader.Text = Loc.Get("Settings.ManualBrightness");
        AccentMatrixHeader.Text = Loc.Get("Settings.AccentMatrix");
        ManualAccentLabel.Text = Loc.Get("Settings.AccentHex");
        ApplyAccentButton.Content = Loc.Get("Settings.AccentApply");
        AccentPreviewLabel.Text = Loc.Get("Settings.AccentPreview");
        PaletteHeader.Text = Loc.Get("Settings.Palette");
        PaletteHint.Text = Loc.Get("Settings.PaletteHint");
        PosterSizeHeader.Text = Loc.Get("Settings.PosterSize");
        PosterSizeHint.Text = Loc.Get("Appearance.PosterHint");
        PreviewHeading.Text = Loc.Get("Appearance.Preview");
        ResetAppearanceButton.Content = Loc.Get("Appearance.Reset");
        ResetHint.Text = Loc.Get("Appearance.ResetHint");

        if (ThemeBox.Items.Count == 4)
        {
            ((ComboBoxItem)ThemeBox.Items[0]).Content = Loc.Get("Settings.ThemeSystem");
            ((ComboBoxItem)ThemeBox.Items[1]).Content = Loc.Get("Settings.ThemeLight");
            ((ComboBoxItem)ThemeBox.Items[2]).Content = Loc.Get("Settings.ThemeDark");
            ((ComboBoxItem)ThemeBox.Items[3]).Content = Loc.Get("Settings.ThemeManual");
        }
        if (ManualBrightnessBox.Items.Count == 2)
        {
            ((ComboBoxItem)ManualBrightnessBox.Items[0]).Content = Loc.Get("Settings.ThemeDark");
            ((ComboBoxItem)ManualBrightnessBox.Items[1]).Content = Loc.Get("Settings.ThemeLight");
        }
        if (PaletteBox.Items.Count == 2)
        {
            ((ComboBoxItem)PaletteBox.Items[0]).Content = Loc.Get("Settings.PaletteNeutral");
            ((ComboBoxItem)PaletteBox.Items[1]).Content = Loc.Get("Settings.PaletteColorful");
        }
        UpdatePreview();
    }

    private void ThemeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized || ThemeBox.SelectedItem is not ComboBoxItem { Tag: AppTheme theme })
            return;
        var settings = SettingsService.Instance;
        settings.Theme = theme;
        settings.Save();
        ManualThemePanel.Visibility = theme == AppTheme.Manual
            ? Visibility.Visible : Visibility.Collapsed;
        ThemeHelper.Apply(theme);
        UpdatePreview();
    }

    private void ManualBrightnessBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized ||
            ManualBrightnessBox.SelectedItem is not ComboBoxItem { Tag: bool dark })
            return;
        SettingsService.Instance.ManualThemeDark = dark;
        SettingsService.Instance.Save();
        ThemeHelper.Apply(SettingsService.Instance.Theme);
        UpdatePreview();
    }

    private void PaletteBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized ||
            PaletteBox.SelectedItem is not ComboBoxItem { Tag: DetailsPalette palette })
            return;
        SettingsService.Instance.SetDetailPalette(palette);
        UpdatePreview();
    }

    private void PosterSizeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized || PosterSizeBox.SelectedItem is not ComboBoxItem { Tag: int width })
            return;
        SettingsService.Instance.SetPosterSize(width);
    }

    private void SyncPosterSize()
    {
        var selected = SettingsService.Instance.PosterSize;
        for (var i = 0; i < PosterSizeBox.Items.Count; i++)
            if (PosterSizeBox.Items[i] is ComboBoxItem { Tag: int width } && width == selected)
            {
                if (PosterSizeBox.SelectedIndex != i)
                {
                    var initialized = _initialized;
                    _initialized = false;
                    PosterSizeBox.SelectedIndex = i;
                    _initialized = initialized;
                }
                break;
            }
    }

    private void UpdateAccentMatrixWidth()
    {
        if (ActualWidth < 1) return;
        var target = Math.Min(440, Math.Max(220, ActualWidth - 95));
        if (double.IsNaN(AccentMatrixGrid.Width) ||
            Math.Abs(AccentMatrixGrid.Width - target) > 1)
            AccentMatrixGrid.Width = target;
    }

    private void BuildAccentMatrix()
    {
        AccentMatrixGrid.Children.Clear();
        AccentMatrixGrid.ColumnDefinitions.Clear();
        AccentMatrixGrid.RowDefinitions.Clear();
        for (var col = 0; col < 3; col++)
            AccentMatrixGrid.ColumnDefinitions.Add(new ColumnDefinition
                { Width = new GridLength(1, GridUnitType.Star) });
        for (var row = 0; row < 4; row++)
            AccentMatrixGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(69) });

        // Full two-tone tiles are easier to scan than tiny color swatches.
        // Hard gradient stops keep exactly the two configured scheme colors.
        var schemes = ThemeHelper.Schemes;
        for (var i = 0; i < schemes.Count; i++)
        {
            var scheme = schemes[i];
            if (!ThemeHelper.TryParseHex(scheme.Primary, out var primary) ||
                !ThemeHelper.TryParseHex(scheme.Companion, out var secondary))
                continue;
            var selected = scheme.Primary.Equals(SettingsService.Instance.ManualAccentHex,
                StringComparison.OrdinalIgnoreCase) &&
                scheme.Companion.Equals(SettingsService.Instance.ManualCompanionHex,
                    StringComparison.OrdinalIgnoreCase);

            var background = new LinearGradientBrush
            {
                StartPoint = new Windows.Foundation.Point(0, 0),
                EndPoint = new Windows.Foundation.Point(1, 0)
            };
            background.GradientStops.Add(new GradientStop { Color = primary, Offset = 0 });
            background.GradientStops.Add(new GradientStop { Color = primary, Offset = .5 });
            background.GradientStops.Add(new GradientStop { Color = secondary, Offset = .5 });
            background.GradientStops.Add(new GradientStop { Color = secondary, Offset = 1 });

            // Choose whichever foreground is more legible on the *worse* of
            // the two halves, not just against the primary accent.
            static double Lum(Windows.UI.Color c)
            {
                static double Channel(byte v)
                {
                    var x = v / 255d;
                    return x <= .04045 ? x / 12.92 : Math.Pow((x + .055) / 1.055, 2.4);
                }
                return .2126 * Channel(c.R) + .7152 * Channel(c.G) + .0722 * Channel(c.B);
            }
            static double Contrast(double a, double b) =>
                (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05);
            var luminanceA = Lum(primary);
            var luminanceB = Lum(secondary);
            var whiteScore = Math.Min(Contrast(1, luminanceA), Contrast(1, luminanceB));
            var blackScore = Math.Min(Contrast(.01, luminanceA), Contrast(.01, luminanceB));
            var foreground = new SolidColorBrush(whiteScore > blackScore
                ? Windows.UI.Colors.White
                : Windows.UI.Color.FromArgb(255, 20, 25, 31));

            var tile = new Grid { MinHeight = 62 };
            tile.Children.Add(new Border
            {
                Background = background, CornerRadius = new CornerRadius(9),
                BorderThickness = selected ? new Thickness(2) : new Thickness(0),
                BorderBrush = new SolidColorBrush(selected
                    ? (whiteScore > blackScore ? Windows.UI.Colors.White
                       : Windows.UI.Color.FromArgb(255, 25, 25, 25))
                    : Windows.UI.Colors.Transparent)
            });
            tile.Children.Add(new TextBlock
            {
                Text = scheme.Name,
                FontSize = 16,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = foreground,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(8, 0, 8, 0)
            });
            if (selected)
                tile.Children.Add(new FontIcon
                {
                    Glyph = "\uE73E", FontSize = 14, Foreground = foreground,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 6, 7, 0)
                });
            var button = new Button
            {
                Content = tile, Tag = scheme,
                Padding = new Thickness(0), BorderThickness = new Thickness(0),
                Background = new SolidColorBrush(Windows.UI.Colors.Transparent),
                MinWidth = 0, MinHeight = 0,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch
            };
            button.Click += AccentScheme_Click;
            ToolTipService.SetToolTip(button,
                scheme.Name + " · " + scheme.Primary + " / " + scheme.Companion);
            Grid.SetColumn(button, i % 3);
            Grid.SetRow(button, i / 3);
            AccentMatrixGrid.Children.Add(button);
        }
        UpdateAccentMatrixWidth();
    }

    private void AccentScheme_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ThemeHelper.ColorScheme scheme }) return;
        SettingsService.Instance.SetManualAccent(scheme.Primary, scheme.Companion);
        AccentHexBox.Text = scheme.Primary;
        AccentValidationText.Text = "";
        BuildAccentMatrix();
        UpdateAccentPreview();
        UpdatePreview();
    }

    private void ApplyAccentButton_Click(object sender, RoutedEventArgs e)
    {
        var hex = AccentHexBox.Text.Trim();
        if (!ThemeHelper.TryParseHex(hex, out _))
        {
            AccentValidationText.Text = Loc.Get("Settings.AccentInvalid");
            return;
        }
        SettingsService.Instance.SetManualAccent(
            hex, SettingsService.Instance.ManualCompanionHex);
        AccentValidationText.Text = "";
        BuildAccentMatrix();
        UpdateAccentPreview();
        UpdatePreview();
    }

    private void UpdateAccentPreview()
    {
        if (ThemeHelper.TryParseHex(SettingsService.Instance.ManualAccentHex, out var primary))
            AccentPreviewMain.Background = new SolidColorBrush(primary);
        if (ThemeHelper.TryParseHex(SettingsService.Instance.ManualCompanionHex, out var companion))
            AccentPreviewCompanion.Background = new SolidColorBrush(companion);
    }

    private void UpdatePreview()
    {
        if (PreviewRezkaBadge == null) return;
        var dark = ActualTheme != ElementTheme.Light;
        var colorful = SettingsService.Instance.DetailPalette == DetailsPalette.Colorful;
        var accent = ThemeHelper.AccentBrush.Color;
        var companion = ThemeHelper.CompanionBrush.Color;

        Windows.UI.Color Solid(byte r, byte g, byte b) =>
            Windows.UI.Color.FromArgb(255, r, g, b);
        Windows.UI.Color Muted() => dark ? Solid(54, 57, 63) : Solid(231, 235, 240);
        Windows.UI.Color Tint(Windows.UI.Color color, double amount)
        {
            var baseColor = dark ? Solid(38, 41, 47) : Solid(248, 249, 251);
            byte Mix(byte a, byte b) =>
                (byte)Math.Clamp(Math.Round(a * amount + b * (1 - amount)), 0, 255);
            return Solid(Mix(color.R, baseColor.R), Mix(color.G, baseColor.G),
                Mix(color.B, baseColor.B));
        }

        PreviewRezkaBadge.Background = new SolidColorBrush(colorful
            ? Tint(accent, dark ? .45 : .23) : Muted());
        PreviewImdbBadge.Background = new SolidColorBrush(colorful
            ? Tint(Solid(247, 197, 35), dark ? .45 : .32) : Muted());
        PreviewKpBadge.Background = new SolidColorBrush(colorful
            ? Tint(Solid(253, 121, 58), dark ? .45 : .28) : Muted());
        PreviewGenre1.Background = new SolidColorBrush(colorful
            ? Tint(Solid(78, 141, 220), dark ? .45 : .25) : Muted());
        PreviewGenre2.Background = new SolidColorBrush(colorful
            ? Tint(companion, dark ? .42 : .27) : Muted());
        PreviewGenre3.Background = new SolidColorBrush(colorful
            ? Tint(Solid(70, 176, 119), dark ? .44 : .27) : Muted());
    }

    private void ResetAppearanceButton_Click(object sender, RoutedEventArgs e)
    {
        var settings = SettingsService.Instance;
        _initialized = false;
        settings.Theme = AppTheme.System;
        settings.ManualThemeDark = true;
        settings.SetManualAccent("#377DFF", "#5AD6CF");
        settings.SetDetailPalette(DetailsPalette.Neutral);
        settings.SetPosterSize(132);
        settings.Save();
        ThemeHelper.Apply(settings.Theme);
        LoadControls();
        ApplyLocalization();
        ResetHint.Text = Loc.Get("Appearance.ResetDone");
    }
}
