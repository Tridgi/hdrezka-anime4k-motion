using System.Runtime.InteropServices;
using System.Text;

namespace HDREZKA.App.Services;

internal static class SmoothRuntime
{
    private static bool _ready;
    private static readonly List<IntPtr> Libraries = new();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr AddDllDirectory(string directory);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetModuleHandleEx(uint flags, string name, out IntPtr module);

    private static void LoadPinned(string path)
    {
        Libraries.Add(NativeLibrary.Load(path));
        // The Rust plugins own process-wide worker threads. VapourSynth releases its
        // plugin handles when a filter is removed; those threads must retain code.
        // PIN keeps the module mapped until process exit, including idle workers.
        if (!GetModuleHandleEx(1, path, out _))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    }
    public static string Prepare(int fps)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "runtime", "python");
        var vs = Path.Combine(root, "Lib", "site-packages", "vapoursynth");
        if (!_ready)
        {
            foreach (var dir in new[] { root, vs })
                if (AddDllDirectory(dir) == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            foreach (var file in new[] { Path.Combine(root, "python312.dll"), Path.Combine(root, "python3.dll"), Path.Combine(vs, "libvapoursynth.dll"), Path.Combine(vs, "vsscript.dll") })
                LoadPinned(file);
            var plugins = Path.Combine(AppContext.BaseDirectory, "runtime", "open-svpflow");
            foreach (var name in new[] { "svpflow1_vs.dll", "svpflow2_vs.dll" })
                LoadPinned(Path.Combine(plugins, name));
            Environment.SetEnvironmentVariable("VSSCRIPT_PATH", Path.Combine(vs, "vsscript.dll"));
            _ready = true;
        }
        var script = Path.Combine(AppContext.BaseDirectory, "motion", $"motion-{fps}.vpy");
        if (!File.Exists(script)) throw new FileNotFoundException("Motion interpolation script missing", script);
        return $"vapoursynth=file=%{Encoding.UTF8.GetByteCount(script)}%{script}:buffered-frames=8:concurrent-frames=4";
    }
}
