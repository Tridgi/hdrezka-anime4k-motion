using HDREZKA.App.Services;
using Microsoft.UI.Xaml.Controls;

namespace HDREZKA.App.Views;

public sealed partial class PlayerPage
{
    internal async Task RunSmokeAsync(string report)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (_player?.IsLoaded != true && DateTime.UtcNow < deadline) await Task.Delay(200);
        if (_player?.IsLoaded != true) throw new InvalidOperationException("Test media did not load");
        var results = new List<object>();
        ToggleFullscreen();
        _player.Volume = 0;
        _player.PlaybackRate = 1;
        _player.PlaybackSession.Position = TimeSpan.Zero;
        foreach (var mode in new[] { "Off", "A", "B", "C", "Off" })
        {
            AnimeBox.SelectedIndex = mode == "A" ? 1 : mode == "B" ? 2 : mode == "C" ? 3 : 0;
            await Task.Delay(2000); // Exclude first-use shader compilation from steady playback.
            var beginFrames = _player.RenderedFrames;
            var beginDrops = _player.Number("frame-drop-count");
            var start = _player.PlaybackSession.Position.TotalSeconds;
            await Task.Delay(8000);
            results.Add(new { Mode = mode, GPU = _player.Gpu, Start = start, End = _player.PlaybackSession.Position.TotalSeconds,
                Frames = _player.RenderedFrames - beginFrames, ShaderFailures = _player.ShaderFailures,
                Output = _player.Get("video-out-params"), Dropped = _player.Number("frame-drop-count") - beginDrops,
                HardwareDecode = _player.Get("hwdec-current"), Shaders = _player.Get("glsl-shaders") });
        }
        _player.Pause();
        await Task.Delay(300);
        var paused = _player.PlaybackSession.PlaybackState.ToString();
        _player.PlaybackSession.Position = TimeSpan.FromSeconds(5);
        await Task.Delay(500);
        var seek = _player.PlaybackSession.Position.TotalSeconds;
        var captures = new Dictionary<string, string>();
        foreach (var mode in new[] { "Off", "A", "B", "C", "Off" })
        {
            AnimeBox.SelectedIndex = mode == "A" ? 1 : mode == "B" ? 2 : mode == "C" ? 3 : 0;
            await Task.Delay(500);
            captures[mode == "Off" && captures.ContainsKey("Off") ? "OffAgain" : mode] = _player.CaptureFrame(Path.Combine(Path.GetDirectoryName(report)!, "frame-" + mode + ".bmp"));
        }
        _player.PlaybackRate = 1.25;
        _player.SelectSubtitle(new Core.Api.MovieSubtitle("Test", Path.Combine(Path.GetDirectoryName(_launch!.LocalFile)!, "test.srt"), "en"));
        _player.Play();
        await Task.Delay(1200);
        var subtitle = _player.Get("sid");
        _player.SelectSubtitle(null);
        _player.SetZoom(1); _player.SetZoom(2); _player.SetZoom(0);
        ExitFullscreen();
        _player.Pause(); ShowControls();
        File.WriteAllText(report, System.Text.Json.JsonSerializer.Serialize(new { Results = results, Captures = captures, PausedState = paused, SeekPosition = seek, Speed = _player.PlaybackRate, Subtitle = subtitle, FinalShaderFailures = _player.ShaderFailures }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }

    private bool _animeMenuBuilding;
    private void BuildAnimeMenu()
    {
        _animeMenuBuilding = true;
        try
        {
            AnimeBox.Items.Clear();
            foreach (var (mode, label) in new[] { ("Off", "Anime4K: выкл."), ("A", "Anime4K: A"), ("B", "Anime4K: B мягкий"), ("C", "Anime4K: C шум"), ("Light", "Anime4K: лёгкий") })
                AnimeBox.Items.Add(new ComboBoxItem { Content = label, Tag = mode });
            var saved = SettingsService.Instance.Anime4KMode;
            AnimeBox.SelectedIndex = saved == "A" ? 1 : saved == "B" ? 2 : saved == "C" ? 3 : saved == "Light" ? 4 : 0;
            _player?.ApplyAnime4K(saved);
            ToolTipService.SetToolTip(AnimeBox, "GPU: " + _player?.Gpu + "\nA — основной, B — мягкий, C — подавление шума.\nЛёгкий — меньше нагрузка для совместной работы с повышением FPS.\nЭтот список меняет только обработку картинки.");
        }
        finally { _animeMenuBuilding = false; }
    }

    private void AnimeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_animeMenuBuilding || _player == null || AnimeBox.SelectedItem is not ComboBoxItem { Tag: string mode }) return;
        try
        {
            _player.ApplyAnime4K(mode);
            SettingsService.Instance.Anime4KMode = mode;
            SettingsService.Instance.Save();
        }
        catch (Exception ex) { App.TryLog(ex); OnShaderFailed(ex.Message); }
        ReturnFocus();
    }

    private void OnShaderFailed(string message)
    {
        try
        {
            _player?.ApplyAnime4K("Off");
            _animeMenuBuilding = true;
            AnimeBox.SelectedIndex = 0;
            SettingsService.Instance.Anime4KMode = "Off";
            SettingsService.Instance.Save();
            ToolTipService.SetToolTip(AnimeBox, "Anime4K отключён: " + message);
        }
        finally { _animeMenuBuilding = false; }
    }
}
