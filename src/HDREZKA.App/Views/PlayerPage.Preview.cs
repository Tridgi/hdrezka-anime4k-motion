using HDREZKA.App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;

namespace HDREZKA.App.Views;

/// <summary>
/// Non-blocking seek previews. Only an ordinary WinUI Image is inserted into
/// the player visual tree: native thumbnail decoding happens headlessly on a
/// background thread, using the lowest accessible video quality.
/// </summary>
public sealed partial class PlayerPage
{
    private ThumbnailFrameService? _previewService;
    private string? _previewSource;
    private bool _previewHovering;
    private int _desiredPreviewBucket = -1;
    private int _previewGeneration;

    private void InitializeSeekPreview()
    {
        if (_tearingDown || _launch == null || !SettingsService.Instance.EnableSeekThumbnails)
            return;

        // Never use the 1080p/4K playback track if a lightweight 240p/360p
        // version exists. This is independent of the quality selected for
        // the main player and never changes the actual viewing experience.
        var lowTrack = _video?.Videos
            .Where(v => !v.NeedAccount && !v.NeedPremium && v.Urls.Count > 0)
            .OrderBy(v => QualityRank(v.Quality))
            .FirstOrDefault();
        var source = lowTrack?.Urls.FirstOrDefault() ?? _currentSource;
        if (string.IsNullOrWhiteSpace(source)) return;
        if (_previewSource == source && _previewService != null) return;

        ResetSeekPreview();
        _previewSource = source;
        var generation = _previewGeneration;
        var position = _player?.PlaybackSession.Position.TotalSeconds ?? 0;
        var duration = _player?.PlaybackSession.NaturalDuration.TotalSeconds ?? 0;

        _previewService = new ThumbnailFrameService(source, position, duration, (bucket, file) =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (_tearingDown || generation != _previewGeneration || !_previewHovering ||
                    bucket != _desiredPreviewBucket) return;
                SetPreviewFrame(file);
            });
        });
        Log.Info("[Thumbnail] Prewarming low-quality source: " +
            (lowTrack?.Quality ?? "local") + " (independent of main playback)");
    }

    private void SeekSlider_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_player == null || !_player.IsLoaded || _switching ||
            _lastTotalMs <= 0 || SeekSlider.ActualWidth <= 0) return;

        var pointer = e.GetCurrentPoint(SeekSlider).Position;
        var fraction = Math.Clamp(pointer.X / SeekSlider.ActualWidth, 0, 1);
        var seconds = fraction * _lastTotalMs / 1000.0;
        var bucket = (int)(seconds / 10);
        _desiredPreviewBucket = bucket;
        _previewHovering = true;
        SeekPreviewTime.Text = FormatTime((long)(seconds * 1000));
        SeekPreviewOverlay.Visibility = Visibility.Visible;

        var absolute = SeekSlider.TransformToVisual(RootGrid).TransformPoint(
            new Point(Math.Clamp(pointer.X, 0, SeekSlider.ActualWidth), 0));
        Canvas.SetLeft(SeekPreviewBubble, Math.Clamp(absolute.X - 96, 3,
            Math.Max(3, RootGrid.ActualWidth - 197)));
        Canvas.SetTop(SeekPreviewBubble, Math.Max(3, absolute.Y -
            (SeekPreviewImage.Visibility == Visibility.Visible ? 151 : 54)));

        if (!SettingsService.Instance.EnableSeekThumbnails)
        {
            SeekPreviewImage.Visibility = Visibility.Collapsed;
            return;
        }

        // The time bubble is immediate. A cached image is immediate as well;
        // uncached frames are generated asynchronously and may be unavailable
        // on some HLS/CDN streams. Never block seeking to wait for preview.
        if (_previewService?.TryGet(bucket, out var cached) == true && cached != null)
        {
            SetPreviewFrame(cached);
            return;
        }
        SeekPreviewImage.Visibility = Visibility.Collapsed;
        _previewService?.Request(bucket);
    }

    private void SeekSlider_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        _previewHovering = false;
        _desiredPreviewBucket = -1;
        SeekPreviewOverlay.Visibility = Visibility.Collapsed;
    }

    private void SetPreviewFrame(string path)
    {
        if (!_previewHovering || !File.Exists(path)) return;
        try
        {
            SeekPreviewImage.Source = new BitmapImage(new Uri(path, UriKind.Absolute));
            SeekPreviewImage.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            Log.Warn("[Thumbnail] Cannot show frame: " + ex.Message);
            SeekPreviewImage.Visibility = Visibility.Collapsed;
        }
    }

    private void ResetSeekPreview()
    {
        _previewGeneration++;
        _previewHovering = false;
        _desiredPreviewBucket = -1;
        SeekPreviewOverlay.Visibility = Visibility.Collapsed;
        SeekPreviewImage.Visibility = Visibility.Collapsed;
        SeekPreviewImage.Source = null;
        // No synchronous libmpv Dispose/Join on the UI thread. This call
        // merely cancels the worker; it owns and cleans up its native state.
        _previewService?.Dispose();
        _previewService = null;
        _previewSource = null;
    }
}
