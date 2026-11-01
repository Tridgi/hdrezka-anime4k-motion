using System.Globalization;
using System.Runtime.InteropServices;
using HDREZKA.Core.Api;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Media.Playback;

namespace HDREZKA.App.Services;

internal sealed record MpvFailure(string ErrorMessage, int ExtendedErrorCode)
{
    public string Error => "mpv";
}

internal sealed class MpvSession
{
    private readonly MpvPlayer _owner;
    internal MpvSession(MpvPlayer owner) => _owner = owner;
    public TimeSpan Position { get => TimeSpan.FromSeconds(_owner.Number("time-pos")); set => _owner.Seek(value.TotalSeconds); }
    public TimeSpan NaturalDuration => TimeSpan.FromSeconds(_owner.Number("duration"));
    public MediaPlaybackState PlaybackState { get; internal set; } = MediaPlaybackState.None;
    public event Action<MpvSession, object>? PlaybackStateChanged;
    public event Action<MpvSession, object>? PositionChanged;
    public event Action<MpvSession, object>? NaturalDurationChanged;
    private double _lastDuration;
    internal void SetState(MediaPlaybackState state) { if (PlaybackState == state) return; PlaybackState = state; PlaybackStateChanged?.Invoke(this, EventArgs.Empty); }
    internal void Tick()
    {
        var duration = _owner.Number("duration");
        if (Math.Abs(duration - _lastDuration) > 0.01) { _lastDuration = duration; NaturalDurationChanged?.Invoke(this, EventArgs.Empty); }
        PositionChanged?.Invoke(this, EventArgs.Empty);
        if (!_owner.IsLoaded) return;
        SetState(_owner.Get("paused-for-cache") == "yes" ? MediaPlaybackState.Buffering : _owner.Get("pause") == "yes" ? MediaPlaybackState.Paused : MediaPlaybackState.Playing);
    }
}

internal sealed unsafe class MpvPlayer : IDisposable
{
    private IntPtr _handle, _render;
    private readonly SwapChainPanel _panel;
    private readonly DispatcherQueue _dispatcher;
    private D3DVideoSurface? _surface;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly MpvNative.UpdateCallback _update;
    private Thread? _eventThread;
    private volatile bool _disposed;
    private int _renderQueued;
    private string? _source;
    private string? _subtitle;
    public bool IsLoaded { get; private set; }
    public string Gpu => _surface?.AdapterName ?? "";
    public MpvSession PlaybackSession { get; }
    public event Action<MpvPlayer, object>? MediaOpened;
    public event Action<MpvPlayer, object>? MediaEnded;
    public event Action<MpvPlayer, MpvFailure>? MediaFailed;
    public event Action<string>? ShaderFailed;
    public long RenderedFrames { get; private set; }
    public int ShaderFailures { get; private set; }
    public int ActiveSmoothFps { get; private set; }

    public MpvPlayer(SwapChainPanel panel)
    {
        _panel = panel; _dispatcher = panel.DispatcherQueue; PlaybackSession = new(this);
        _update = _ => QueueRender();
        try
        {
            _handle = MpvNative.mpv_create();
            if (_handle == IntPtr.Zero) throw new InvalidOperationException("mpv_create failed");
            foreach (var (key, value) in new[] { ("config", "no"), ("load-scripts", "no"), ("osc", "no"), ("terminal", "no"), ("vo", "libmpv"), ("idle", "yes"), ("hwdec", "d3d11va"), ("pause", "yes"), ("osd-level", "0"), ("network-timeout", "15"), ("cache", "yes") })
                MpvNative.Check(MpvNative.mpv_set_option_string(_handle, key, value));
            MpvNative.Check(MpvNative.mpv_initialize(_handle));
            MpvNative.mpv_request_log_messages(_handle, "warn");
            _surface = new D3DVideoSurface(panel);
            var api = Marshal.StringToCoTaskMemUTF8("d3d11");
            var device = _surface.Device;
            try { MpvNative.Check(MpvNative.mpv_render_context_create(out _render, _handle, new[] { new MpvNative.Param(1, api), new MpvNative.Param(21, (IntPtr)(&device)), new MpvNative.Param(0, IntPtr.Zero) })); }
            finally { Marshal.FreeCoTaskMem(api); }
            MpvNative.mpv_render_context_set_update_callback(_render, _update, IntPtr.Zero);
            panel.SizeChanged += OnSizeChanged;
            panel.CompositionScaleChanged += OnScaleChanged;
            _timer.Tick += OnTick; _timer.Start();
            _eventThread = new Thread(EventLoop) { IsBackground = true, Name = "HDREZKA mpv events" }; _eventThread.Start();
            Resize();
        }
        catch { Dispose(); throw; }
    }

    public string? Source
    {
        get => _source;
        set
        {
            _source = value; IsLoaded = false; _subtitle = null;
            if (_disposed) return;
            if (value == null) { MpvNative.Command(_handle, "stop"); PlaybackSession.SetState(MediaPlaybackState.None); return; }
            PlaybackSession.SetState(MediaPlaybackState.Opening);
            Set("pause", "yes"); MpvNative.Command(_handle, "loadfile", value, "replace");
        }
    }
    public double Volume { get => Number("volume") / 100; set => Set("volume", (Math.Clamp(value, 0, 1) * 100).ToString(CultureInfo.InvariantCulture)); }
    public bool IsMuted { get => Get("mute") == "yes"; set => Set("mute", value ? "yes" : "no"); }
    public double PlaybackRate { get => Number("speed"); set => Set("speed", value.ToString(CultureInfo.InvariantCulture)); }
    public void Play() { Set("pause", "no"); if (IsLoaded) PlaybackSession.SetState(MediaPlaybackState.Playing); }
    public void Pause() { Set("pause", "yes"); if (IsLoaded) PlaybackSession.SetState(MediaPlaybackState.Paused); }
    public void Seek(double seconds) { if (IsLoaded) MpvNative.Command(_handle, "seek", Math.Max(0, seconds).ToString(CultureInfo.InvariantCulture), "absolute+exact"); }
    public void SetZoom(int mode) { Set("keepaspect", mode == 2 ? "no" : "yes"); Set("panscan", mode == 1 ? "1" : "0"); }
    public void ApplySmoothness(int fps)
    {
        if (fps is not (0 or 60 or 120 or 165)) throw new ArgumentOutOfRangeException(nameof(fps));
        Set("interpolation", "no");
        Set("video-sync", "audio");
        Set("options/display-fps-override", "0");
        Set("vf", fps == 0 ? "" : SmoothRuntime.Prepare(fps));
        ActiveSmoothFps = fps;
        Log.Info("[Smooth] OpenCL motion interpolation target=" + fps);
    }
    internal string CaptureFrame(string path) => _surface!.CaptureFrame(_render, path);
    public void SelectSubtitle(MovieSubtitle? subtitle)
    {
        if (subtitle == null) { Set("sid", "no"); return; }
        if (_subtitle == subtitle.Link) { Set("sid", "auto"); return; }
        MpvNative.Command(_handle, "sub-add", subtitle.Link, "select", subtitle.Name);
        _subtitle = subtitle.Link;
    }

    public void ApplyAnime4K(string mode)
    {
        var shaderDir = Path.Combine(AppContext.BaseDirectory, "shaders");
        var names = mode switch
        {
            "Light" => new[] { "Clamp_Highlights", "Restore_CNN_S", "Upscale_CNN_x2_S", "AutoDownscalePre_x2", "AutoDownscalePre_x4", "Upscale_CNN_x2_S" },
            "A" => new[] { "Clamp_Highlights", "Restore_CNN_M", "Upscale_CNN_x2_M", "AutoDownscalePre_x2", "AutoDownscalePre_x4", "Upscale_CNN_x2_S" },
            "B" => new[] { "Clamp_Highlights", "Restore_CNN_Soft_M", "Upscale_CNN_x2_M", "AutoDownscalePre_x2", "AutoDownscalePre_x4", "Upscale_CNN_x2_S" },
            "C" => new[] { "Clamp_Highlights", "Upscale_Denoise_CNN_x2_M", "AutoDownscalePre_x2", "AutoDownscalePre_x4", "Upscale_CNN_x2_S" },
            _ => Array.Empty<string>()
        };
        var paths = names.Select(n => Path.Combine(shaderDir, "Anime4K_" + n + ".glsl")).ToArray();
        foreach (var path in paths) if (!File.Exists(path)) throw new FileNotFoundException("Anime4K shader missing", path);
        // One command prevents transient, incomplete shader chains during switching.
        try { MpvNative.Command(_handle, "change-list", "glsl-shaders", "set", string.Join(';', paths)); }
        catch { MpvNative.Command(_handle, "change-list", "glsl-shaders", "clr", ""); throw; }
        Log.Info("[Anime4K] Mode=" + mode + " GPU=" + Gpu);
        QueueRender();
    }

    public string Get(string name)
    {
        if (_disposed || _handle == IntPtr.Zero) return "";
        var p = MpvNative.mpv_get_property_string(_handle, name);
        if (p == IntPtr.Zero) return "";
        try { return Marshal.PtrToStringUTF8(p) ?? ""; } finally { MpvNative.mpv_free(p); }
    }
    internal double Number(string name) => double.TryParse(Get(name), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) ? Math.Max(0, value) : 0;
    private void Set(string name, string value) { if (!_disposed) MpvNative.Check(MpvNative.mpv_set_property_string(_handle, name, value)); }
    private void OnTick(object? sender, object e) { if (!_disposed) PlaybackSession.Tick(); }
    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => Resize();
    private void OnScaleChanged(SwapChainPanel sender, object e) => Resize();
    private void Resize()
    {
        if (_disposed || _surface == null) return;
        try
        {
            var scaleX = _panel.CompositionScaleX;
            var scaleY = _panel.CompositionScaleY;
            if (!float.IsFinite(scaleX) || scaleX <= 0) scaleX = 1f;
            if (!float.IsFinite(scaleY) || scaleY <= 0) scaleY = 1f;
            var width = (int)Math.Max(16, Math.Round(_panel.ActualWidth * scaleX));
            var height = (int)Math.Max(16, Math.Round(_panel.ActualHeight * scaleY));
            _surface.Resize(width, height, scaleX, scaleY);
            QueueRender();
        }
        catch (Exception ex) { MediaFailed?.Invoke(this, new MpvFailure(ex.Message, ex.HResult)); }
    }
    private void QueueRender()
    {
        if (_disposed || Interlocked.Exchange(ref _renderQueued, 1) != 0) return;
        if (!_dispatcher.TryEnqueue(() =>
        {
            Interlocked.Exchange(ref _renderQueued, 0);
            if (_disposed || _render == IntPtr.Zero) return;
            try { MpvNative.mpv_render_context_update(_render); _surface!.Render(_render); RenderedFrames++; }
            catch (Exception ex) { Log.Info("[Anime4K] Render failed: " + ex); MediaFailed?.Invoke(this, new MpvFailure(ex.Message, ex.HResult)); }
        })) Interlocked.Exchange(ref _renderQueued, 0);
    }
    private void EventLoop()
    {
        while (!_disposed)
        {
            var ev = Marshal.PtrToStructure<MpvNative.Event>(MpvNative.mpv_wait_event(_handle, 0.1));
            if (_disposed) break;
            if (ev.Id == 8) _dispatcher.TryEnqueue(() => { if (_disposed) return; IsLoaded = true; MediaOpened?.Invoke(this, EventArgs.Empty); });
            if (ev.Id == 7 && ev.Data != IntPtr.Zero)
            {
                var reason = Marshal.ReadInt32(ev.Data); var error = Marshal.ReadInt32(ev.Data, 4);
                var message = Marshal.PtrToStringUTF8(MpvNative.mpv_error_string(error)) ?? "Playback failed";
                if (reason == 0) _dispatcher.TryEnqueue(() => { if (!_disposed) MediaEnded?.Invoke(this, EventArgs.Empty); });
                else if (reason == 4) _dispatcher.TryEnqueue(() => { if (!_disposed) { IsLoaded = false; MediaFailed?.Invoke(this, new MpvFailure(message, error)); } });
            }
            if (ev.Id == 2 && ev.Data != IntPtr.Zero)
            {
                var prefix = Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(ev.Data)) ?? "";
                var level = Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(ev.Data, IntPtr.Size)) ?? "";
                var message = Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(ev.Data, 2 * IntPtr.Size)) ?? "";
                Log.Info("[mpv/" + prefix + "/" + level + "] " + message.Trim());
                if ((level == "error" || level == "fatal") && (prefix.Contains("gpu") || message.Contains("shader", StringComparison.OrdinalIgnoreCase)))
                    _dispatcher.TryEnqueue(() => { if (!_disposed) { ShaderFailures++; ShaderFailed?.Invoke(message.Trim()); } });
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        _timer.Stop(); _timer.Tick -= OnTick; _panel.SizeChanged -= OnSizeChanged; _panel.CompositionScaleChanged -= OnScaleChanged;
        if (_handle != IntPtr.Zero) MpvNative.mpv_wakeup(_handle);
        _eventThread?.Join();
        if (_render != IntPtr.Zero) { MpvNative.mpv_render_context_set_update_callback(_render, null, IntPtr.Zero); MpvNative.mpv_render_context_free(_render); _render = IntPtr.Zero; }
        if (_handle != IntPtr.Zero) { MpvNative.mpv_terminate_destroy(_handle); _handle = IntPtr.Zero; }
        _surface?.Dispose(); _surface = null;
        GC.KeepAlive(_update);
    }
}
