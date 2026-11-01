using System.Globalization;
using HDREZKA.App.Services;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace HDREZKA.App.Views;

public sealed partial class PlayerPage
{
    // A single UI timer reads existing mpv counters. No GPU, CPU or OS polling.
    private readonly DispatcherTimer _playbackStatsTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _playbackStatsTickAttached;
    private MpvPlayer? _sampledPlayer;
    private long _previousRenderCount;
    private double _previousDroppedCount;
    private DateTime _previousStatsTime = DateTime.MinValue;
    private DateTime _dropWarningUntil = DateTime.MinValue;

    private void StartPlaybackStats()
    {
        if (!_playbackStatsTickAttached)
        {
            _playbackStatsTimer.Tick += PlaybackStatsTimer_Tick;
            _playbackStatsTickAttached = true;
        }
        _sampledPlayer = null;
        _previousStatsTime = DateTime.MinValue;
        _dropWarningUntil = DateTime.MinValue;
        PlaybackWarningText.Visibility = Visibility.Collapsed;
        PlaybackStatsPanel.Visibility = SettingsService.Instance.ShowPlaybackStats ? Visibility.Visible : Visibility.Collapsed;
        _playbackStatsTimer.Start();
    }

    private void StopPlaybackStats()
    {
        _playbackStatsTimer.Stop();
        _sampledPlayer = null;
        _previousStatsTime = DateTime.MinValue;
        _dropWarningUntil = DateTime.MinValue;
        PlaybackWarningText.Visibility = Visibility.Collapsed;
        PlaybackStatsPanel.Visibility = Visibility.Collapsed;
    }

    private void SetPlaybackStatsVisible(bool enabled)
    {
        PlaybackStatsPanel.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        SettingsService.Instance.ShowPlaybackStats = enabled;
        SettingsService.Instance.Save();
        if (enabled) PlaybackStatsTimer_Tick(null, EventArgs.Empty);
    }

    private void PlaybackStatsTimer_Tick(object? sender, object e)
    {
        var player = _player;
        if (_tearingDown || player == null || !player.IsLoaded || !IsPlaying())
        {
            _sampledPlayer = null;
            _previousStatsTime = DateTime.MinValue;
            _dropWarningUntil = DateTime.MinValue;
            PlaybackWarningText.Visibility = Visibility.Collapsed;
            if (PlaybackStatsPanel.Visibility == Visibility.Visible)
            {
                StatsFpsText.Text = "Отрисовка: —";
                StatsDroppedText.Text = "Пропуски: —";
                StatsDetailText.Text = player?.IsLoaded == true ? "Пауза или буферизация" : "Ожидание воспроизведения";
            }
            return;
        }

        try
        {
            var now = DateTime.UtcNow;
            var frames = player.RenderedFrames;
            var dropped = player.Number("frame-drop-count");
            if (!ReferenceEquals(_sampledPlayer, player) || _previousStatsTime == DateTime.MinValue)
            {
                _sampledPlayer = player;
                _previousRenderCount = frames;
                _previousDroppedCount = dropped;
                _previousStatsTime = now;
                if (PlaybackStatsPanel.Visibility == Visibility.Visible)
                    StatsDetailText.Text = "Идёт измерение…";
                return;
            }

            var seconds = (now - _previousStatsTime).TotalSeconds;
            if (seconds < 0.5) return;
            var renderedDelta = Math.Max(0L, frames - _previousRenderCount);
            var droppedDelta = Math.Max(0d, dropped - _previousDroppedCount);
            _previousRenderCount = frames;
            _previousDroppedCount = dropped;
            _previousStatsTime = now;

            var renderFps = renderedDelta / seconds;
            if (droppedDelta > 0) _dropWarningUntil = now.AddSeconds(2);
            var hasDrops = now < _dropWarningUntil;

            // Show a warning only after actual counter growth, not based on a guessed 60 FPS target.
            if (hasDrops)
            {
                PlaybackWarningText.Text = droppedDelta > 0
                    ? $"⚠ +{droppedDelta:0} проп."
                    : PlaybackWarningText.Text;
                PlaybackWarningText.Visibility = Visibility.Visible;
            }
            else
            {
                PlaybackWarningText.Visibility = Visibility.Collapsed;
            }

            if (PlaybackStatsPanel.Visibility != Visibility.Visible) return;
            StatsFpsText.Text = $"Отрисовка: {renderFps.ToString("0.0", CultureInfo.InvariantCulture)} кадр/с";
            StatsFpsText.Foreground = new SolidColorBrush(hasDrops ? Colors.OrangeRed : Colors.LightGreen);
            StatsDroppedText.Text = $"Пропуски: +{droppedDelta:0} за {seconds:0.0} с";
            var width = (int)player.Number("video-params/w");
            var height = (int)player.Number("video-params/h");
            var resolution = width > 0 && height > 0 ? $"{width}×{height}" : "неизвестно";
            var anime = (AnimeBox.SelectedItem as Microsoft.UI.Xaml.Controls.ComboBoxItem)?.Tag as string
                ?? SettingsService.Instance.Anime4KMode;
            var smooth = player.ActiveSmoothFps is > 0 ? $"{player.ActiveSmoothFps} FPS" : "выкл.";
            StatsDetailText.Text = $"Видео: {resolution}\nAnime4K: {anime} · Плавность: {smooth}\nGPU: {player.Gpu}";
        }
        catch (Exception ex)
        {
            App.TryLog(ex);
            PlaybackWarningText.Visibility = Visibility.Collapsed;
        }
    }
}
