using System.Collections.Concurrent;
using System.Globalization;
using System.Threading;

namespace HDREZKA.App.Services;

/// <summary>
/// A completely headless, software-only libmpv thumbnail worker. In contrast to
/// a secondary SwapChainPanel in the player visual tree it can never paint over
/// the real video or titlebar. Every native call happens on this worker, never
/// on the UI thread. Failure gracefully degrades to the time-only tooltip.
/// </summary>
internal sealed class ThumbnailFrameService : IDisposable
{
    private readonly string _source;
    private readonly string _directory;
    private readonly Action<int, string> _frameReady;
    private readonly CancellationTokenSource _stop = new();
    private readonly AutoResetEvent _requestEvent = new(false);
    private readonly ConcurrentDictionary<int, string> _frames = new();
    private readonly ConcurrentQueue<int> _frameOrder = new();
    private readonly Queue<int> _warmBuckets;
    private readonly Task _worker;
    private int _requestedBucket = -1;
    private int _disposed;
    private int _failures;
    private const int MaxFrames = 32;
    private const int SampleIntervalSeconds = 10;

    public ThumbnailFrameService(string source, double currentSeconds, double durationSeconds,
                                 Action<int, string> frameReady)
    {
        _source = source;
        _frameReady = frameReady;
        _directory = Path.Combine(Path.GetTempPath(), "HDREZKA-Thumbs", Guid.NewGuid().ToString("N"));
        var current = Math.Max(0, (int)(currentSeconds / SampleIntervalSeconds));
        var duration = Math.Max(0, (int)(durationSeconds / SampleIntervalSeconds));
        // Opportunistic pre-warming: a few representative positions, not all
        // 144 thumbnails of a 24-minute episode (which would flood the CDN).
        _warmBuckets = new Queue<int>(new[] {
            current, Math.Min(duration, current + 3),
            Math.Min(duration, current + 12),
            duration / 4, duration / 2, duration * 3 / 4
        }.Where(b => b >= 0).Distinct());
        _worker = Task.Run(Worker);
    }

    public bool TryGet(int bucket, out string? path)
    {
        if (_frames.TryGetValue(bucket, out var file) && File.Exists(file))
        {
            path = file;
            return true;
        }
        path = null;
        return false;
    }

    public void Request(int bucket)
    {
        if (Volatile.Read(ref _disposed) != 0 || bucket < 0 || _frames.ContainsKey(bucket)) return;
        Interlocked.Exchange(ref _requestedBucket, bucket);
        try { _requestEvent.Set(); }
        catch (ObjectDisposedException) { }
    }

    private void Worker()
    {
        IntPtr handle = IntPtr.Zero;
        try
        {
            Directory.CreateDirectory(_directory);
            handle = MpvNative.mpv_create();
            if (handle == IntPtr.Zero) throw new InvalidOperationException("Thumbnail mpv_create failed");
            Configure(handle, "config", "no");
            Configure(handle, "load-scripts", "no");
            Configure(handle, "terminal", "no");
            Configure(handle, "vo", "null");
            Configure(handle, "audio", "no");
            Configure(handle, "hwdec", "no");
            Configure(handle, "osc", "no");
            Configure(handle, "pause", "yes");
            Configure(handle, "cache", "no");
            Configure(handle, "network-timeout", "3");
            Configure(handle, "hr-seek", "no");
            Configure(handle, "screenshot-sw", "yes");
            Configure(handle, "screenshot-jpeg-quality", "35");
            // Software conversion to a genuinely tiny 16:9 image. The input is
            // already the lowest playable quality selected by PlayerPage.
            Configure(handle, "vf", "scale=w=192:h=108");
            MpvNative.Check(MpvNative.mpv_initialize(handle));
            MpvNative.Command(handle, "loadfile", _source, "replace");

            var openDeadline = DateTime.UtcNow.AddSeconds(3);
            var loaded = false;
            while (!_stop.IsCancellationRequested && DateTime.UtcNow < openDeadline)
            {
                var ev = System.Runtime.InteropServices.Marshal.PtrToStructure<MpvNative.Event>(
                    MpvNative.mpv_wait_event(handle, .08));
                if (ev.Id == 8) { loaded = true; break; }
                if (ev.Id == 7 && ev.Error < 0) break;
            }
            if (!loaded)
            {
                Log.Warn("[Thumbnail] Headless decoder not ready, time-only preview enabled");
                return;
            }

            while (!_stop.IsCancellationRequested)
            {
                var target = Interlocked.Exchange(ref _requestedBucket, -1);
                if (target < 0 && _warmBuckets.Count > 0)
                    target = _warmBuckets.Dequeue();
                if (target < 0)
                {
                    _requestEvent.WaitOne(170);
                    continue;
                }
                if (_frames.ContainsKey(target)) continue;

                try
                {
                    var filename = Path.Combine(_directory, $"frame-{target:D6}.jpg");
                    MpvNative.Command(handle, "seek",
                        (target * SampleIntervalSeconds).ToString(CultureInfo.InvariantCulture),
                        "absolute+keyframes");
                    // Wait for a decoded frame, but prioritize subsequent mouse
                    // requests and do not hold the UI thread for this operation.
                    var deadline = DateTime.UtcNow.AddMilliseconds(450);
                    var frameReady = false;
                    while (!_stop.IsCancellationRequested && DateTime.UtcNow < deadline)
                    {
                        var ev = System.Runtime.InteropServices.Marshal.PtrToStructure<MpvNative.Event>(
                            MpvNative.mpv_wait_event(handle, .04));
                        if (ev.Id == 21) { frameReady = true; break; }
                    }
                    if (_stop.IsCancellationRequested) break;
                    // Some streams do not emit PLAYBACK_RESTART while paused.
                    // Try a software screenshot anyway; the file-existence
                    // check below determines whether a real frame was decoded.
                    if (!frameReady) Thread.Sleep(75);
                    MpvNative.Command(handle, "screenshot-to-file", filename, "video");
                    var writeDeadline = DateTime.UtcNow.AddMilliseconds(650);
                    while (!File.Exists(filename) && DateTime.UtcNow < writeDeadline &&
                           !_stop.IsCancellationRequested) Thread.Sleep(25);
                    if (!File.Exists(filename) || new FileInfo(filename).Length < 128)
                        throw new InvalidDataException("No image produced by software screenshot output");
                    _frames[target] = filename;
                    _frameOrder.Enqueue(target);
                    _frameReady(target, filename);
                    _failures = 0;
                    // Maintain a small cache even for long scrub sessions.
                    // Previously the worker terminated once 32 frames had
                    // been produced and immediately deleted all thumbnails.
                    while (_frames.Count > MaxFrames && _frameOrder.TryDequeue(out var oldest))
                    {
                        if (!_frames.TryRemove(oldest, out var obsolete)) continue;
                        try { File.Delete(obsolete); } catch { }
                    }
                }
                catch (Exception ex)
                {
                    _failures++;
                    Log.Warn("[Thumbnail] Frame unavailable (time-only fallback): " + ex.Message);
                    if (_failures >= 5) return; // avoid persistent retry storms
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn("[Thumbnail] Headless preview unavailable: " + ex.Message);
        }
        finally
        {
            if (handle != IntPtr.Zero)
            {
                try { MpvNative.mpv_terminate_destroy(handle); }
                catch (Exception ex) { Log.Warn("[Thumbnail] Decoder cleanup failed: " + ex.Message); }
            }
            try { Directory.Delete(_directory, recursive: true); } catch { }
            _requestEvent.Dispose();
            _stop.Dispose();
        }
    }

    private static void Configure(IntPtr handle, string key, string value) =>
        MpvNative.Check(MpvNative.mpv_set_option_string(handle, key, value));

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        // Worker may already have released its CTS after an unsupported
        // video source; disposal of the UI owner must remain harmless.
        try { _stop.Cancel(); } catch (ObjectDisposedException) { }
        try { _requestEvent.Set(); } catch (ObjectDisposedException) { }
        // Deliberately do NOT wait for libmpv here: network teardown can stall,
        // and it must never prevent the episode drawer or window from closing.
        // The background worker releases libmpv and its temporary files.
    }
}
