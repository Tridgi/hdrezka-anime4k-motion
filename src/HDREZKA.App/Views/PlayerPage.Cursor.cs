using System.Runtime.InteropServices;
using HDREZKA.App.Services;

namespace HDREZKA.App.Views;

/// <summary>
/// Window-local pointer hiding. Unlike global ShowCursor counter changes,
/// WM_SETCURSOR is intercepted only for the player's HWND while the movie
/// plays with its overlays hidden. Other Windows applications are untouched.
/// </summary>
public sealed partial class PlayerPage
{
    private delegate IntPtr SubclassCallback(
        IntPtr hwnd, uint message, IntPtr wparam, IntPtr lparam,
        UIntPtr subclassId, IntPtr referenceData);

    private const uint WmSetCursor = 0x0020;
    private const int HtClient = 1;
    private const int IdcArrow = 32512;
    private readonly UIntPtr _cursorSubclassId = (UIntPtr)0x48445231;
    private SubclassCallback? _cursorSubclassCallback;
    private IntPtr _cursorHwnd;
    private bool _cursorSubclassInstalled;

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(
        IntPtr hwnd, SubclassCallback callback, UIntPtr subclassId, IntPtr refData);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(
        IntPtr hwnd, SubclassCallback callback, UIntPtr subclassId);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(
        IntPtr hwnd, uint message, IntPtr wparam, IntPtr lparam);

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorPoint { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowRect { public int Left; public int Top; public int Right; public int Bottom; }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out CursorPoint point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out WindowRect rectangle);

    [DllImport("user32.dll")]
    private static extern IntPtr SetCursor(IntPtr cursor);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadCursor(IntPtr instance, IntPtr name);

    private void InstallCursorHandler()
    {
        // The player is hosted in its own Window; never subclass MainWindow.
        if (_cursorSubclassInstalled || HostWindow == null) return;
        try
        {
            _cursorHwnd = WinRT.Interop.WindowNative.GetWindowHandle(Host);
            if (_cursorHwnd == IntPtr.Zero) return;
            _cursorSubclassCallback = PlayerCursorSubclass;
            _cursorSubclassInstalled = SetWindowSubclass(
                _cursorHwnd, _cursorSubclassCallback, _cursorSubclassId, IntPtr.Zero);
            if (!_cursorSubclassInstalled)
                Log.Warn("[PlayerCursor] Failed to install local cursor handler");
        }
        catch (Exception ex)
        {
            Log.Warn("[PlayerCursor] Installation failed: " + ex.Message);
        }
    }

    private IntPtr PlayerCursorSubclass(
        IntPtr hwnd, uint message, IntPtr wparam, IntPtr lparam,
        UIntPtr subclassId, IntPtr referenceData)
    {
        if (message == WmSetCursor && _cursorHidden &&
            !_episodeDrawerOpen && ((long)lparam & 0xffff) == HtClient &&
            IsPointerInsidePlayerWindow())
        {
            SetCursor(IntPtr.Zero);
            return (IntPtr)1;
        }

        return DefSubclassProc(hwnd, message, wparam, lparam);
    }

    private void UninstallCursorHandler()
    {
        ShowSystemCursor();
        if (!_cursorSubclassInstalled || _cursorSubclassCallback == null) return;
        try { RemoveWindowSubclass(_cursorHwnd, _cursorSubclassCallback, _cursorSubclassId); }
        catch (Exception ex) { Log.Warn("[PlayerCursor] Removal failed: " + ex.Message); }
        _cursorSubclassInstalled = false;
        _cursorSubclassCallback = null;
        _cursorHwnd = IntPtr.Zero;
    }

    // WinUI can fail to emit PointerEntered when the window regains focus
    // with an already-stationary mouse. Use actual screen coordinates as a
    // fallback instead of permanently keeping the cursor visible.
    private bool IsPointerInsidePlayerWindow()
    {
        if (HostWindow == null) return false;
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(HostWindow);
        if (hwnd == IntPtr.Zero || !GetCursorPos(out var p) ||
            !GetWindowRect(hwnd, out var r)) return false;
        return p.X >= r.Left && p.X < r.Right &&
               p.Y >= r.Top && p.Y < r.Bottom;
    }

    internal void ResumeCursorOnActivate()
    {
        if (_tearingDown) return;
        _pointerInsidePlayer = IsPointerInsidePlayerWindow();
        if (_pointerInsidePlayer && IsPlaying() && !_episodeDrawerOpen)
            _hideTimer.Start();
    }

    private void HideSystemCursor()
    {
        if (_cursorHidden || _episodeDrawerOpen || !IsPlaying()) return;
        if (!_pointerInsidePlayer) _pointerInsidePlayer = IsPointerInsidePlayerWindow();
        if (!_pointerInsidePlayer) return;
        InstallCursorHandler();
        if (!_cursorSubclassInstalled) return;
        _cursorHidden = true;
        SetCursor(IntPtr.Zero);
        Log.Info("[PlayerCursor] Hidden after inactivity");
    }

    private void ShowSystemCursor()
    {
        if (!_cursorHidden) return;
        _cursorHidden = false;
        // Restore immediately even without another mouse-move event.
        SetCursor(LoadCursor(IntPtr.Zero, (IntPtr)IdcArrow));
    }
}
