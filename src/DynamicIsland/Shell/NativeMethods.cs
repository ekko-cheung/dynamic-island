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

    // --- Frosted glass ---------------------------------------------------------------------------------------------

    public const int WS_EX_TRANSPARENT = 0x00000020;

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(nint hwnd, ref WindowCompositionAttributeData data);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(nint hwnd, nint region, bool redraw);

    [DllImport("gdi32.dll")]
    private static extern nint CreateRectRgn(int left, int top, int right, int bottom);

    [DllImport("gdi32.dll")]
    private static extern nint CreateRoundRectRgn(int left, int top, int right, int bottom, int ellipseWidth, int ellipseHeight);

    [DllImport("gdi32.dll")]
    private static extern int CombineRgn(nint dest, nint source1, nint source2, int mode);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(nint obj);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);

    /// <summary>Windows 11: lets DWM round the window's corners (and clip the blur to them) and drop its hairline border.</summary>
    public static void RoundCorners(nint hwnd)
    {
        var round = 2;                      // DWMWCP_ROUND
        DwmSetWindowAttribute(hwnd, 33, ref round, sizeof(int));   // DWMWA_WINDOW_CORNER_PREFERENCE
        var noShadow = 2;                   // DWMNCRP_DISABLED: no drop shadow around the backdrop
        DwmSetWindowAttribute(hwnd, 2, ref noShadow, sizeof(int)); // DWMWA_NCRENDERING_POLICY
        var none = unchecked((int)0xFFFFFFFE); // DWMWA_COLOR_NONE
        DwmSetWindowAttribute(hwnd, 34, ref none, sizeof(int));    // DWMWA_BORDER_COLOR
    }

    private const int WCA_ACCENT_POLICY = 19;
    private const int ACCENT_ENABLE_ACRYLICBLURBEHIND = 4;
    private const int RGN_OR = 2;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const uint SWP_HIDEWINDOW = 0x0080;

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy { public int AccentState, AccentFlags; public uint GradientColor; public int AnimationId; }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData { public int Attribute; public nint Data; public int SizeOfData; }

    /// <summary>Turns acrylic blur behind the window on, tinted with the ARGB color <paramref name="tint"/>.</summary>
    public static void SetAcrylic(nint hwnd, uint tint)
    {
        var policy = new AccentPolicy
        {
            AccentState = ACCENT_ENABLE_ACRYLICBLURBEHIND,
            AccentFlags = 2,
            // The API wants ABGR.
            GradientColor = ((tint >> 24) << 24) | ((tint & 0xFF) << 16) | (tint & 0xFF00) | ((tint >> 16) & 0xFF),
        };
        var size = Marshal.SizeOf<AccentPolicy>();
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(policy, buffer, false);
            var data = new WindowCompositionAttributeData { Attribute = WCA_ACCENT_POLICY, Data = buffer, SizeOfData = size };
            SetWindowCompositionAttribute(hwnd, ref data);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    /// <summary>Cuts the window down to the union of the given rounded rectangles (window pixels).</summary>
    public static void SetWindowShape(nint hwnd, IReadOnlyList<(int Left, int Top, int Right, int Bottom, int Radius)> shapes)
    {
        var region = CreateRectRgn(0, 0, 0, 0);
        foreach (var (l, t, r, b, radius) in shapes)
        {
            var part = CreateRoundRectRgn(l, t, r + 1, b + 1, radius * 2, radius * 2);
            CombineRgn(region, region, part, RGN_OR);
            DeleteObject(part);
        }
        SetWindowRgn(hwnd, region, true); // the system owns the region from here on
    }

    /// <summary>
    /// Shows the window directly beneath <paramref name="above"/> in z-order without activating it. With no position
    /// given, only the z-order is touched.
    /// </summary>
    public static void PlaceBelow(nint hwnd, nint above, int? x, int? y, int? width, int? height)
    {
        var flags = SWP_NOACTIVATE | SWP_NOOWNERZORDER | SWP_SHOWWINDOW;
        if (x is null || y is null || width is null || height is null) flags |= SWP_NOMOVE | SWP_NOSIZE;
        // Inserting after a window puts this one right behind it in z-order.
        SetWindowPos(hwnd, above, x ?? 0, y ?? 0, width ?? 0, height ?? 0, flags);
    }

    public static void HideWindow(nint hwnd) =>
        SetWindowPos(hwnd, nint.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_HIDEWINDOW);

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
