using System.Runtime.InteropServices;

namespace DynamicIsland.Shell;

/// <summary>A display. <see cref="Bounds"/> is in physical pixels, in virtual-screen coordinates.</summary>
public sealed record MonitorItem(string DeviceName, string Label, int Left, int Top, int Right, int Bottom, double Scale, bool IsPrimary);

/// <summary>Display enumeration through Win32, so positions stay in physical pixels whatever each monitor's DPI is.</summary>
public static class Monitors
{
    private const uint MonitorInfoPrimary = 1;
    private const int MdtEffectiveDpi = 0;

    public static IReadOnlyList<MonitorItem> All()
    {
        var found = new List<MonitorItem>();
        EnumDisplayMonitors(nint.Zero, nint.Zero, (monitor, _, _, _) =>
        {
            var info = new MonitorInfoEx { Size = Marshal.SizeOf<MonitorInfoEx>() };
            if (!GetMonitorInfo(monitor, ref info)) return true;

            var scale = 1.0;
            if (GetDpiForMonitor(monitor, MdtEffectiveDpi, out var dpiX, out _) == 0 && dpiX > 0) scale = dpiX / 96.0;

            var m = info.Monitor;
            found.Add(new MonitorItem(info.DeviceName, "", m.Left, m.Top, m.Right, m.Bottom, scale,
                (info.Flags & MonitorInfoPrimary) != 0));
            return true;
        }, nint.Zero);

        // Primary first, then left-to-right, which matches how people think of "screen 1, 2, ...".
        var ordered = found.OrderByDescending(m => m.IsPrimary).ThenBy(m => m.Left).ThenBy(m => m.Top).ToList();
        return ordered
            .Select((m, i) => m with
            {
                Label = $"显示器 {i + 1}{(m.IsPrimary ? "(主)" : "")} · {m.Right - m.Left}×{m.Bottom - m.Top}",
            })
            .ToList();
    }

    /// <summary>The configured display, or the primary one when none is configured or it is not connected.</summary>
    public static MonitorItem? Resolve(string deviceName)
    {
        var all = All();
        return all.FirstOrDefault(m => deviceName.Length > 0 && m.DeviceName == deviceName)
               ?? all.FirstOrDefault(m => m.IsPrimary)
               ?? all.FirstOrDefault();
    }

    private delegate bool MonitorEnumProc(nint monitor, nint hdc, nint rect, nint data);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
    }

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(nint hdc, nint clip, MonitorEnumProc callback, nint data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfoEx info);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(nint monitor, int type, out uint dpiX, out uint dpiY);
}
