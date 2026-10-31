using HDREZKA.App.Services;
using Microsoft.UI.Xaml.Controls;

namespace HDREZKA.App.Views;

public sealed partial class PlayerPage
{
    private bool _smoothMenuBuilding;
    private bool TryRecoverWithoutSmoothness()
    {
        if (_player?.ActiveSmoothFps is not > 0) return false;
        try
        {
            _player.ApplySmoothness(0);
            _smoothMenuBuilding = true; SmoothBox.SelectedIndex = 0; _smoothMenuBuilding = false;
            SettingsService.Instance.SmoothFps = 0; SettingsService.Instance.Save();
            ToolTipService.SetToolTip(SmoothBox, "Повышение FPS отключено после ошибки воспроизведения. Можно включить его снова.");
            _ = LoadVideoAsync(seekToSaved: false);
            return true;
        }
        catch (Exception ex) { _smoothMenuBuilding = false; App.TryLog(ex); return false; }
    }
    private void BuildSmoothMenu()
    {
        _smoothMenuBuilding = true;
        try
        {
            SmoothBox.Items.Clear();
            foreach (var (fps, label) in new[] { (0, "Плавность: выкл."), (60, "Плавность: 60 FPS"), (120, "Плавность: 120 FPS"), (165, "165 FPS (экспер.)") })
                SmoothBox.Items.Add(new ComboBoxItem { Content = label, Tag = fps });
            var savedFps = SettingsService.Instance.SmoothFps;
            SmoothBox.SelectedIndex = savedFps == 165 ? 3 : savedFps == 120 ? 2 : savedFps == 60 ? 1 : 0;
            _player?.ApplySmoothness(savedFps);
            ToolTipService.SetToolTip(SmoothBox, "Повышение FPS по движению (OpenCL), отдельно от Anime4K.\nНа этой системе рекомендуются 120 FPS + лёгкий Anime4K или 60 FPS + A/B/C.\n165 FPS — экспериментальный режим: возможны пропуски кадров.");
        }
        finally { _smoothMenuBuilding = false; }
    }

    private void SmoothBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_smoothMenuBuilding || _player == null || SmoothBox.SelectedItem is not ComboBoxItem { Tag: int fps }) return;
        try
        {
            _player.ApplySmoothness(fps);
            SettingsService.Instance.SmoothFps = fps;
            SettingsService.Instance.Save();
        }
        catch (Exception ex)
        {
            App.TryLog(ex);
            try { _player.ApplySmoothness(0); } catch (Exception reset) { App.TryLog(reset); }
            _smoothMenuBuilding = true; SmoothBox.SelectedIndex = 0; _smoothMenuBuilding = false;
            SettingsService.Instance.SmoothFps = 0; SettingsService.Instance.Save();
            ToolTipService.SetToolTip(SmoothBox, "Повышение FPS отключено: " + ex.Message);
        }
        ReturnFocus();
    }

    internal async Task RunSmoothSmokeAsync(string report)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (_player?.IsLoaded != true && DateTime.UtcNow < deadline) await Task.Delay(200);
        if (_player?.IsLoaded != true) throw new InvalidOperationException("Test media did not load");
        ToggleFullscreen(); _player.Volume = 0; _player.PlaybackRate = 1;
        _player.PlaybackSession.Position = TimeSpan.Zero;
        var results = new List<object>();
        foreach (var (mode, fps) in new[] { ("Off", 0), ("Off", 120), ("Off", 165), ("Light", 165), ("Light", 120), ("A", 60) })
        {
            _player.ApplyAnime4K(mode); _player.ApplySmoothness(fps);
            await Task.Delay(2000);
            var frames = _player.RenderedFrames; var dropped = _player.Number("frame-drop-count");
            var start = _player.PlaybackSession.Position.TotalSeconds;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            await Task.Delay(4000);
            results.Add(new { Mode = mode, TargetFps = fps, Seconds = clock.Elapsed.TotalSeconds,
                Frames = _player.RenderedFrames - frames, Dropped = _player.Number("frame-drop-count") - dropped,
                Start = start, End = _player.PlaybackSession.Position.TotalSeconds,
                DisplaySync = _player.Get("display-sync-active"), Filter = _player.Get("vf"), AVSync = _player.Get("avsync"), FilterFPS = _player.Get("estimated-vf-fps"),
                DisplayFps = _player.Get("display-fps"), ShaderFailures = _player.ShaderFailures });
        }
        _player.Pause(); _player.ApplySmoothness(0);
        var skipSettings = SettingsService.Instance.OpeningSkipSeconds;
        SettingsService.Instance.OpeningSkipSeconds = 10;
        _player.PlaybackSession.Position = TimeSpan.FromSeconds(5); await Task.Delay(200);
        SkipOpeningButton_Click(this, new Microsoft.UI.Xaml.RoutedEventArgs()); await Task.Delay(300);
        var afterSkip = _player.PlaybackSession.Position.TotalSeconds;
        SettingsService.Instance.OpeningSkipSeconds = skipSettings; RefreshOpeningSkipLabel();
        ExitFullscreen(); ShowControls();
        File.WriteAllText(report, System.Text.Json.JsonSerializer.Serialize(new { Results = results, SkipStart = 5, SkipInterval = 10, SkipEnd = afterSkip,
            ShortcutTooltipMode = KeyboardAcceleratorPlacementMode.ToString(), FpsOptions = SmoothBox.Items.Count }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }

    internal async Task RunSwitchStressAsync(string report)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (_player?.IsLoaded != true && DateTime.UtcNow < deadline) await Task.Delay(200);
        if (_player?.IsLoaded != true) throw new InvalidOperationException("Test media did not load");
        _player.Volume = 0;
        File.WriteAllText(report, "Started\n");
        for (var cycle = 0; cycle < 12; cycle++)
        {
            _player.PlaybackSession.Position = TimeSpan.FromSeconds(5);
            _player.Play();
            _player.ApplySmoothness(new[] {60, 120, 165}[cycle % 3]);
            _player.ApplyAnime4K(cycle % 2 == 0 ? "Light" : "B");
            await Task.Delay(1800);
            if (cycle % 2 == 0) _player.Pause();
            _player.ApplySmoothness(0);
            _player.ApplyAnime4K(cycle % 2 == 0 ? "B" : "Light");
            _player.Play();
            await Task.Delay(1500);
            File.AppendAllText(report, $"Cycle {cycle + 1}: OK\n");
        }
        _player.ApplySmoothness(0);
        _player.PlaybackSession.Position = TimeSpan.FromSeconds(5);
        await Task.Delay(25000);
        _player.ApplyAnime4K("A");
        await Task.Delay(5000);
        _player.Pause();
        ShowControls();
        File.AppendAllText(report, "Idle after FPS off: 30 seconds OK\nPASS\n");
    }
}
