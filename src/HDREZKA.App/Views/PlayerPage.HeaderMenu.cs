using HDREZKA.App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace HDREZKA.App.Views;

public sealed partial class PlayerPage
{
    // The regular ComboBoxes remain the single source of truth for selection.
    // In compact layout their existing handlers still apply changes and save settings.
    // Rebuilding the menu on each open keeps it in sync with stream/quality changes.
    private void TopBarMoreButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new MenuFlyout();

        var anime = new MenuFlyoutSubItem { Text = "Anime4K" };
        foreach (var (mode, label) in new[]
        {
            ("Off", "Выкл. / Off"), ("A", "A"), ("B", "B"),
            ("C", "C"), ("Light", "Light")
        })
        {
            var currentMode = (AnimeBox.SelectedItem as ComboBoxItem)?.Tag as string
                ?? SettingsService.Instance.Anime4KMode;
            var option = new ToggleMenuFlyoutItem
            {
                Text = label,
                IsChecked = currentMode == mode
            };
            option.Click += (_, _) =>
            {
                for (var i = 0; i < AnimeBox.Items.Count; i++)
                {
                    if (AnimeBox.Items[i] is ComboBoxItem { Tag: string tag } && tag == mode)
                    {
                        AnimeBox.SelectedIndex = i;
                        break;
                    }
                }
            };
            anime.Items.Add(option);
        }
        menu.Items.Add(anime);

        var quality = new MenuFlyoutSubItem { Text = Loc.Get("Common.Quality") };
        foreach (var comboOption in QualityBox.Items.OfType<ComboBoxItem>())
        {
            if (comboOption.Tag is not string tag) continue;
            var option = new ToggleMenuFlyoutItem
            {
                Text = comboOption.Content?.ToString() ?? tag,
                IsChecked = tag == _quality,
                IsEnabled = comboOption.IsEnabled
            };
            option.Click += (_, _) => QualityBox.SelectedItem = comboOption;
            quality.Items.Add(option);
        }
        quality.IsEnabled = quality.Items.Count > 0;
        menu.Items.Add(quality);

        menu.Items.Add(new MenuFlyoutSeparator());

        var pin = new ToggleMenuFlyoutItem
        {
            Text = Loc.Get("Player.Pin"),
            IsChecked = _isPinned
        };
        pin.Click += (_, _) =>
        {
            _isPinned = pin.IsChecked;
            ApplyPin();
        };
        menu.Items.Add(pin);

        var minimize = new MenuFlyoutItem { Text = Loc.Get("Player.Minimize") };
        minimize.Click += (_, _) => MinimizeButton_Click(MinimizeButton, new RoutedEventArgs());
        menu.Items.Add(minimize);

        menu.ShowAt(TopBarMoreButton);
    }
}
