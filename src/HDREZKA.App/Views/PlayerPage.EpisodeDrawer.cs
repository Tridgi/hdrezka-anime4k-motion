using HDREZKA.Core.Api;
using HDREZKA.App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace HDREZKA.App.Views;

/// <summary>Non-modal, in-player season/episode drawer. Reuses PlayerLaunch data
/// and never swaps out the SwapChainPanel or changes video dimensions.</summary>
public sealed partial class PlayerPage
{
    private bool _episodeDrawerOpen;
    private bool _updatingEpisodeDrawer;
    private bool _changingEpisode;

    private void EpisodeDrawerButton_Click(object sender, RoutedEventArgs e)
    {
        if (_episodeDrawerOpen) CloseEpisodeDrawer();
        else OpenEpisodeDrawer();
    }

    private void OpenEpisodeDrawer()
    {
        if (_launch?.Seasons is not { Count: > 0 }) return;
        _episodeDrawerOpen = true;
        EpisodeDrawerOverlay.Visibility = Visibility.Visible;
        EpisodeDrawerCard.Width = Math.Min(350, Math.Max(225, RootGrid.ActualWidth - 16));
        PopulateEpisodeSeasons();
        ShowControls();
    }

    private void CloseEpisodeDrawer()
    {
        _episodeDrawerOpen = false;
        EpisodeDrawerOverlay.Visibility = Visibility.Collapsed;
        if (IsPlaying()) _hideTimer.Start();
        ReturnFocus();
    }

    private void EpisodeDrawerBackdrop_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
        => CloseEpisodeDrawer();

    private void EpisodeDrawerClose_Click(object sender, RoutedEventArgs e)
        => CloseEpisodeDrawer();

    private void PopulateEpisodeSeasons()
    {
        if (_launch?.Seasons == null) return;
        _updatingEpisodeDrawer = true;
        try
        {
            EpisodeSeasonBox.Items.Clear();
            var selected = 0;
            var i = 0;
            foreach (var season in _launch.Seasons)
            {
                EpisodeSeasonBox.Items.Add(new ComboBoxItem
                {
                    Content = "Сезон " + season.Name,
                    Tag = season
                });
                if (season.SeasonId == _launch.Season?.SeasonId) selected = i;
                i++;
            }
            if (EpisodeSeasonBox.Items.Count > 0)
                EpisodeSeasonBox.SelectedIndex = selected;
            RenderEpisodeTiles(_launch.Seasons.ElementAtOrDefault(selected));
        }
        finally { _updatingEpisodeDrawer = false; }
    }

    private void EpisodeSeasonBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingEpisodeDrawer) return;
        if (EpisodeSeasonBox.SelectedItem is ComboBoxItem { Tag: MovieSeason season })
            RenderEpisodeTiles(season);
    }

    private void RenderEpisodeTiles(MovieSeason? season)
    {
        EpisodeDrawerGrid.Items.Clear();
        if (season == null) return;
        foreach (var episode in season.Episodes)
        {
            var current = season.SeasonId == _launch?.Season?.SeasonId
                && episode.EpisodeId == _launch?.Episode?.EpisodeId;
            var movieId = _launch == null ? "" :
                DetailsPage.ExtractNumericId(_launch.Details.Id) ?? _launch.Details.Id;
            var watched = _launch != null && PositionService.Instance.IsWatched(
                movieId, _launch.Voice.TranslatorId, season.SeasonId, episode.EpisodeId);
            var button = new Button
            {
                Content = episode.Name,
                Tag = (season, episode),
                Width = 65, Height = 43,
                MaxWidth = 65,
                MinWidth = 65,
                Margin = new Thickness(0, 0, 6, 8),
                Padding = new Thickness(4, 4, 4, 4),
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(current
                    ? Color.FromArgb(255, 71, 118, 212)
                    : watched ? Color.FromArgb(255, 49, 67, 62)
                    : Color.FromArgb(255, 57, 59, 65)),
                Foreground = new SolidColorBrush(Colors.White),
                BorderThickness = new Thickness(current ? 2 : 0),
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 164, 197, 255)),
                HorizontalContentAlignment = HorizontalAlignment.Center,
            };
            ToolTipService.SetToolTip(button, watched ? episode.Name + " · просмотрено" : episode.Name);
            button.Click += EpisodeDrawerEpisode_Click;
            EpisodeDrawerGrid.Items.Add(button);
        }
    }

    private async void EpisodeDrawerEpisode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ValueTuple<MovieSeason, MovieEpisode> chosen })
            return;
        if (_changingEpisode || _loadingEpisode) return;
        if (_launch?.Season?.SeasonId == chosen.Item1.SeasonId &&
            _launch?.Episode?.EpisodeId == chosen.Item2.EpisodeId)
        {
            CloseEpisodeDrawer();
            return;
        }

        _changingEpisode = true;
        try
        {
            SavePosition(final: true);
            _loadingEpisode = true;
            if (_launch != null)
                _launch = _launch with { Season = chosen.Item1, Episode = chosen.Item2 };
            _pendingSeekMs = 0;
            UpdateEpisodeText();
            CloseEpisodeDrawer();
            await LoadVideoAsync(seekToSaved: true);
        }
        catch (Exception ex)
        {
            Log.Error("[Player] Episode drawer selection failed", ex);
            _loadingEpisode = false;
        }
        finally { _changingEpisode = false; }
    }

    private void UpdateEpisodeDrawerSelection()
    {
        EpisodeDrawerButton.Visibility = _launch?.Seasons is { Count: > 0 }
            ? Visibility.Visible : Visibility.Collapsed;
        if (_episodeDrawerOpen) PopulateEpisodeSeasons();
    }
}
