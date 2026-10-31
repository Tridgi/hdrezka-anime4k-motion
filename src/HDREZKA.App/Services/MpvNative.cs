using System.Runtime.InteropServices;

namespace HDREZKA.App.Services;

internal static class MpvNative
{
    private const string Dll = "libmpv-2.dll";
    [StructLayout(LayoutKind.Sequential)] internal struct Param { public int Type; public IntPtr Data; public Param(int type, IntPtr data) { Type = type; Data = data; } }
    [StructLayout(LayoutKind.Sequential)] internal struct Event { public int Id, Error; public ulong Reply; public IntPtr Data; }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void UpdateCallback(IntPtr context);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr mpv_create();
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern int mpv_initialize(IntPtr handle);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern void mpv_terminate_destroy(IntPtr handle);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern int mpv_set_option_string(IntPtr handle, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern int mpv_set_property_string(IntPtr handle, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr mpv_get_property_string(IntPtr handle, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern void mpv_free(IntPtr value);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr mpv_error_string(int error);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern int mpv_command(IntPtr handle, IntPtr args);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr mpv_wait_event(IntPtr handle, double timeout);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern void mpv_wakeup(IntPtr handle);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern int mpv_request_log_messages(IntPtr handle, [MarshalAs(UnmanagedType.LPUTF8Str)] string level);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern int mpv_render_context_create(out IntPtr context, IntPtr handle, [In] Param[] args);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern void mpv_render_context_set_update_callback(IntPtr context, UpdateCallback? callback, IntPtr data);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern ulong mpv_render_context_update(IntPtr context);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern int mpv_render_context_render(IntPtr context, [In] Param[] args);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern void mpv_render_context_report_swap(IntPtr context);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern void mpv_render_context_free(IntPtr context);
    internal static void Check(int result) { if (result < 0) throw new InvalidOperationException(Marshal.PtrToStringUTF8(mpv_error_string(result))); }
    internal static void Command(IntPtr handle, params string[] args)
    {
        var strings = args.Select(Marshal.StringToCoTaskMemUTF8).ToArray();
        var array = Marshal.AllocHGlobal((strings.Length + 1) * IntPtr.Size);
        try { for (var i = 0; i < strings.Length; i++) Marshal.WriteIntPtr(array, i * IntPtr.Size, strings[i]); Marshal.WriteIntPtr(array, strings.Length * IntPtr.Size, IntPtr.Zero); Check(mpv_command(handle, array)); }
        finally { foreach (var s in strings) Marshal.FreeCoTaskMem(s); Marshal.FreeHGlobal(array); }
    }
}
