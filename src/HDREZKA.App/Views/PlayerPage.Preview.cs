using HDREZKA.App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;

namespace HDREZKA.App.Views;

/// <summary>
/// Experimental low-resolution seek previews. A separate paused libmpv
/// instance renders to an isolated 178x100 swapchain, not the playing frame.
/// It is created only on timeline hover; failures degrade to time-only UI.
/// </summary>
public sealed partial class PlayerPage
{
    private MpvPlayer? _previewPlayer;
    private string? _previewSource;
    private bool _previewBusy;
    private bool _previewFailed;
    private bool _previewHovering;
    private int _desiredPreviewBucket = -1;
    private readonly Dictionary<int, string> _previewFrames = new();
    private string? _previewDirectory;
    private int _previewGeneration;
    private CancellationTokenSource? _previewCancellation;

    private void SeekSlider_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_player == null || !_player.IsLoaded || _lastTotalMs <= 0 ||
            SeekSlider.ActualWidth <= 0) return;

        _previewHovering = true;
        var pointer = e.GetCurrentPoint(SeekSlider).Position;
        var progress = Math.Clamp(pointer.X / SeekSlider.ActualWidth, 0, 1);
        var time = progress * _lastTotalMs / 1000.0;
        var bucket = (int)(time / 10.0);
        _desiredPreviewBucket = bucket;
        SeekPreviewTime.Text = FormatTime((long)(time * 1000));
        SeekPreviewOverlay.Visibility = Visibility.Visible;

        // Move above the cursor, clamped to the root viewport.
        var absolute = SeekSlider.TransformToVisual(RootGrid).TransformPoint(
            new Point(Math.Clamp(pointer.X, 0, SeekSlider.ActualWidth), 0));
        Canvas.SetLeft(SeekPreviewBubble, Math.Clamp(
            absolute.X - 96, 3, Math.Max(3, RootGrid.ActualWidth - 197)));
        Canvas.SetTop(SeekPreviewBubble, Math.Max(4,
            absolute.Y - (SeekPreviewImage.Visibility == Visibility.Visible ? 146 : 56)));

        if (!SettingsService.Instance.EnableSeekThumbnails ||
            string.IsNullOrWhiteSpace(_currentSource) || _previewFailed)
        {
            SeekPreviewImage.Visibility = Visibility.Collapsed;
            return;
        }

        if (_previewFrames.TryGetValue(bucket, out var cached) && File.Exists(cached))
        {
            SetPreviewFrame(cached);
            return;
        }

        SeekPreviewImage.Visibility = Visibility.Collapsed;
        // At most one decode loop at a time. Fast mouse moves replace the
        // desired target without launching new network requests.
        if (!_previewBusy) _ = GenerateThumbnailAsync();
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
            Log.Warn("[Thumbnail] Cannot display captured frame: " + ex.Message);
            SeekPreviewImage.Visibility = Visibility.Collapsed;
        }
    }

    private async Task GenerateThumbnailAsync()
    {
        if (_previewBusy || _currentSource == null) return;
        _previewBusy = true;
        var generation = _previewGeneration;
        try
        {
            if (_previewSource != _currentSource)
            {
                DisposePreviewDecoder();
                _previewCancellation = new CancellationTokenSource();
                _previewSource = _currentSource;
                _previewFailed = false;
                _previewDirectory = Path.Combine(Path.GetTempPath(),
                    "HDREZKA-SeekPreview", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(_previewDirectory);
                _previewPlayer = new MpvPlayer(ThumbnailVideoSurface);
                _previewPlayer.Volume = 0;
                _previewPlayer.IsMuted = true;
                _previewPlayer.Source = _previewSource;
            }

            var player = _previewPlayer;
            if (player == null) return;
            var token = _previewCancellation?.Token ?? CancellationToken.None;

            // CDN/HLS streams may be slow or non-seekable. Never stall the main
            // player waiting for thumbnail decoding.
            var readyDeadline = DateTime.UtcNow.AddSeconds(5);
            while (!player.IsLoaded && DateTime.UtcNow < readyDeadline)
                await Task.Delay(120, token);
            if (!player.IsLoaded)
                throw new TimeoutException("Thumbnail decoder did not open within 5s");

            var captured = 0;
            while (_previewHovering && generation == _previewGeneration &&
                   _previewFrames.Count < 24 && captured < 3)
            {
                var bucket = _desiredPreviewBucket;
                if (bucket < 0) break;
                if (_previewFrames.TryGetValue(bucket, out var cached) && File.Exists(cached))
                {
                    SetPreviewFrame(cached);
                    break;
                }

                player.PlaybackSession.Position = TimeSpan.FromSeconds(bucket * 10.0);
                await Task.Delay(420, token);
                if (generation != _previewGeneration || !_previewHovering) break;
                if (bucket != _desiredPreviewBucket) continue;

                var file = Path.Combine(_previewDirectory!, $"t-{bucket:D6}.bmp");
                player.CaptureFrame(file);
                _previewFrames[bucket] = file;
                SetPreviewFrame(file);
                captured++;
                if (bucket == _desiredPreviewBucket) break;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _previewFailed = true; // Only time tooltip for this source.
            SeekPreviewImage.Visibility = Visibility.Collapsed;
            Log.Warn("[Thumbnail] Preview unavailable; using time only: " + ex.Message);
        }
        finally
        {
            _previewBusy = false;
            if (_previewHovering && !_previewFailed &&
                !_previewFrames.ContainsKey(_desiredPreviewBucket) &&
                generation == _previewGeneration)
                _ = GenerateThumbnailAsync();
        }
    }

    private void DisposePreviewDecoder()
    {
        _previewCancellation?.Cancel();
        _previewCancellation?.Dispose();
        _previewCancellation = null;
        try { _previewPlayer?.Dispose(); }
        catch (Exception ex) { Log.Warn("[Thumbnail] Decoder disposal: " + ex.Message); }
        _previewPlayer = null;
        _previewSource = null;
        _previewFrames.Clear();
        SeekPreviewImage.Source = null;
        SeekPreviewImage.Visibility = Visibility.Collapsed;
        if (_previewDirectory is { } dir)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
            catch { }
        }
        _previewDirectory = null;
    }

    private void ResetSeekPreview()
    {
        _previewGeneration++;
        _previewHovering = false;
        SeekPreviewOverlay.Visibility = Visibility.Collapsed;
        DisposePreviewDecoder();
        _previewFailed = false;
        _previewBusy = false;
    }
}
