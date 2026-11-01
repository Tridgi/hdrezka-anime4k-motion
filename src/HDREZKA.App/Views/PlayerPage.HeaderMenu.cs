using HDREZKA.App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace HDREZKA.App.Views;

public sealed partial class PlayerPage
{
    // Advanced processing menu; the existing ComboBox handlers remain the
    // source of truth even when the toolbar shortcuts are hidden.
    private void TopBarMoreButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new MenuFlyout { Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.Top };
        var anime = new MenuFlyoutSubItem { Text = "Anime4K" };
        foreach (var (mode, label) in new[]
        {
            ("Off", "Выкл. / Off"), ("A", "A"), ("B", "B"),
            ("C", "C"), ("Light", "Light")
        })
        {
            var current = (AnimeBox.SelectedItem as ComboBoxItem)?.Tag as string
                ?? SettingsService.Instance.Anime4KMode;
            var option = new ToggleMenuFlyoutItem { Text = label, IsChecked = current == mode };
            option.Click += (_, _) =>
            {
                foreach (var choice in AnimeBox.Items.OfType<ComboBoxItem>())
                    if (choice.Tag is string tag && tag == mode)
                    {
                        AnimeBox.SelectedItem = choice;
                        break;
                    }
            };
            anime.Items.Add(option);
        }
        menu.Items.Add(anime);

        var smooth = new MenuFlyoutSubItem { Text = "Плавность" };
        foreach (var choice in SmoothBox.Items.OfType<ComboBoxItem>())
        {
            var option = new ToggleMenuFlyoutItem
            {
                Text = choice.Content?.ToString() ?? "—",
                IsChecked = ReferenceEquals(SmoothBox.SelectedItem, choice),
                IsEnabled = choice.IsEnabled
            };
            option.Click += (_, _) => SmoothBox.SelectedItem = choice;
            smooth.Items.Add(option);
        }
        smooth.IsEnabled = smooth.Items.Count > 0;
        menu.Items.Add(smooth);
        menu.Items.Add(new MenuFlyoutSeparator());

        var stats = new ToggleMenuFlyoutItem
        {
            Text = "Статистика воспроизведения",
            IsChecked = SettingsService.Instance.ShowPlaybackStats
        };
        stats.Click += (_, _) => SetPlaybackStatsVisible(stats.IsChecked);
        menu.Items.Add(stats);

        var headerToggle = new ToggleMenuFlyoutItem
        {
            Text = "Показывать Anime4K и плавность в верхней панели",
            IsChecked = SettingsService.Instance.ShowHeaderEffects
        };
        headerToggle.Click += (_, _) =>
        {
            SettingsService.Instance.ShowHeaderEffects = headerToggle.IsChecked;
            SettingsService.Instance.Save();
            ApplyHeaderLayout();
        };
        menu.Items.Add(headerToggle);
        menu.ShowAt(TopBarMoreButton);
    }

    // Ordinary playback parameters on the lower-right settings button.
    private void PlaybackSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new MenuFlyout { Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.Top };
        var subtitles = new MenuFlyoutSubItem { Text = Loc.Get("Common.Subtitles") };
        foreach (var choice in SubtitlesBox.Items.OfType<ComboBoxItem>())
        {
            var option = new ToggleMenuFlyoutItem
            {
                Text = choice.Content?.ToString() ?? "—",
                IsChecked = ReferenceEquals(SubtitlesBox.SelectedItem, choice),
                IsEnabled = choice.IsEnabled
            };
            option.Click += (_, _) => SubtitlesBox.SelectedItem = choice;
            subtitles.Items.Add(option);
        }
        subtitles.IsEnabled = subtitles.Items.Count > 0;
        menu.Items.Add(subtitles);

        var quality = new MenuFlyoutSubItem { Text = Loc.Get("Common.Quality") };
        foreach (var choice in QualityBox.Items.OfType<ComboBoxItem>())
        {
            if (choice.Tag is not string tag) continue;
            var option = new ToggleMenuFlyoutItem
            {
                Text = choice.Content?.ToString() ?? tag,
                IsChecked = tag == _quality,
                IsEnabled = choice.IsEnabled
            };
            option.Click += (_, _) => QualityBox.SelectedItem = choice;
            quality.Items.Add(option);
        }
        quality.IsEnabled = quality.Items.Count > 0;
        menu.Items.Add(quality);

        var zoom = new MenuFlyoutSubItem { Text = Loc.Get("Player.Zoom") };
        for (var i = 0; i < ZoomLabels.Length; i++)
        {
            var index = i;
            var option = new ToggleMenuFlyoutItem
            {
                Text = ZoomLabels[i],
                IsChecked = _zoomIndex == i
            };
            option.Click += (_, _) => SetPlayerZoomMode(index);
            zoom.Items.Add(option);
        }
        menu.Items.Add(zoom);

        // The donor download function remains available without using a
        // permanent transport-bar button.
        if (DonorUnlock.IsUnlocked && string.IsNullOrEmpty(_launch?.LocalFile))
        {
            menu.Items.Add(new MenuFlyoutSeparator());
            var download = new MenuFlyoutItem { Text = "Скачать видео" };
            download.Click += (_, _) => DownloadButton_Click(this, new RoutedEventArgs());
            menu.Items.Add(download);
        }
        menu.ShowAt(PlaybackSettingsButton);
    }
}
