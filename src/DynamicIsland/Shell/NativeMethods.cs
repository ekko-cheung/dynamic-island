using System.Runtime.InteropServices;
using System.Text;

namespace DynamicIsland.Shell;

internal static class NativeMethods
{
    public const int GWL_STYLE = -16;
    public const int GWL_EXSTYLE = -20;
    public const int WS_CAPTION = 0x00C00000;
    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const int WS_EX_NOACTIVATE = 0x08000000;

    private static readonly nint HwndTopmost = -1;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_NOOWNERZORDER = 0x0200;
    private const uint SWP_NOSENDCHANGING = 0x0400;
    private const uint MONITOR_DEFAULTTONEAREST = 2;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    public static extern nint GetWindowLongPtr(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    public static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint value);

    [DllImport("shell32.dll")]
    public static extern int SHQueryUserNotificationState(out int state);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint hWnd, out Rect rect);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out CursorPoint point);

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorPoint { public int X, Y; }

    /// <summary>Cursor position in physical screen pixels (stable while the window moves under it).</summary>
    public static bool TryGetCursorPos(out int x, out int y)
    {
        var ok = GetCursorPos(out var p);
        x = p.X;
        y = p.Y;
        return ok;
    }

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint hWnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint hWnd, StringBuilder text, int maxCount);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
    }

    public const int QUNS_RUNNING_D3D_FULL_SCREEN = 3;
    public const int QUNS_PRESENTATION_MODE = 4;

    /// <summary>
    /// Another topmost window (or a maximized browser taking over) can end up above us; re-assert the topmost band
    /// without activating or moving anything.
    /// </summary>
    public static void BringToTopmost(nint hwnd) =>
        SetWindowPos(hwnd, HwndTopmost, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER | SWP_NOSENDCHANGING);

    /// <summary>Moves and sizes the window in physical pixels without activating it.</summary>
    public static void Place(nint hwnd, int x, int y, int width, int height) =>
        SetWindowPos(hwnd, nint.Zero, x, y, width, height, SWP_NOACTIVATE | SWP_NOZORDER | SWP_NOOWNERZORDER);

    /// <summary>
    /// True when the foreground window really covers its whole monitor (video/game fullscreen), or the system reports
    /// D3D fullscreen or presentation mode. QUNS_BUSY is deliberately not used: it stays set for ordinary windows.
    /// </summary>
    public static bool IsFullscreenAppActive(nint ownWindow)
    {
        if (SHQueryUserNotificationState(out var state) == 0
            && state is QUNS_RUNNING_D3D_FULL_SCREEN or QUNS_PRESENTATION_MODE)
            return true;

        var hwnd = GetForegroundWindow();
        if (hwnd == nint.Zero || hwnd == ownWindow) return false;

        var cls = new StringBuilder(64);
        GetClassName(hwnd, cls, cls.Capacity);
        if (cls.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return false;

        if ((GetWindowLongPtr(hwnd, GWL_STYLE) & WS_CAPTION) == WS_CAPTION) return false;
        if (!GetWindowRect(hwnd, out var window)) return false;

        // A fullscreen window on another display does not cover the island.
        var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        if (monitor != MonitorFromWindow(ownWindow, MONITOR_DEFAULTTONEAREST)) return false;

        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info)) return false;

        var m = info.Monitor;
        return window.Left <= m.Left && window.Top <= m.Top && window.Right >= m.Right && window.Bottom >= m.Bottom;
    }
}
