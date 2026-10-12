using System.Runtime.InteropServices;

namespace DynamicIsland.Shell;

internal static class NativeMethods
{
    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const int WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    public static extern nint GetWindowLongPtr(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    public static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint value);

    [DllImport("shell32.dll")]
    public static extern int SHQueryUserNotificationState(out int state);

    public const int QUNS_BUSY = 2;               // fullscreen app
    public const int QUNS_RUNNING_D3D_FULL_SCREEN = 3;
    public const int QUNS_PRESENTATION_MODE = 4;

    public static bool IsFullscreenAppActive() =>
        SHQueryUserNotificationState(out var s) == 0
        && s is QUNS_BUSY or QUNS_RUNNING_D3D_FULL_SCREEN or QUNS_PRESENTATION_MODE;
}
