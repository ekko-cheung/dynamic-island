using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace DynamicIsland.Shell;

/// <summary>
/// The blur behind the island. The island window is a transparent layered window, and acrylic cannot be limited to
/// parts of one, so the blur lives in this separate window that sits directly beneath it and is cut down (with a
/// window region) to the outlines of the pills.
/// </summary>
internal sealed class GlassBackdrop : Window
{
    private const uint Tint = 0x99101014; // ARGB: dark and mostly opaque, so the white text on the island stays readable

    private nint _hwnd;

    public GlassBackdrop()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Background = Brushes.Transparent;
        Width = Height = 1;
        Left = Top = -10000; // placed in physical pixels by Update
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        // Never take focus or input; whatever is beneath the pills' translucent fill still belongs to the island window above.
        var style = NativeMethods.GetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE,
            style | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_TRANSPARENT);
        if (HwndSource.FromHwnd(_hwnd)?.CompositionTarget is { } target)
            target.BackgroundColor = Colors.Transparent;
        NativeMethods.RoundCorners(_hwnd);
        NativeMethods.SetAcrylic(_hwnd, Tint);
    }

    /// <summary>
    /// Covers <paramref name="shape"/> (physical screen pixels) and slots in directly under <paramref name="island"/>.
    /// Null hides the backdrop.
    /// </summary>
    public void Update(nint island, GlassShape? shape)
    {
        if (_hwnd == nint.Zero) return;
        if (shape is not { } s)
        {
            NativeMethods.HideWindow(_hwnd);
            return;
        }
        // The window's own rounding hits all four corners; the overscan pushes the flat side(s) off the screen.
        NativeMethods.PlaceBelow(_hwnd, island, s.Left - s.OverLeft, s.Top - s.OverTop,
            s.Right - s.Left + 1 + s.OverLeft + s.OverRight, s.Bottom - s.Top + 1 + s.OverTop);
    }

    /// <summary>Re-seats the backdrop right under the island after something else was raised above it.</summary>
    public void KeepUnder(nint island)
    {
        if (_hwnd != nint.Zero) NativeMethods.PlaceBelow(_hwnd, island, null, null, null, null);
    }
}

/// <summary>The area of one pill in physical screen pixels. The Over* values extend it past the screen edge it is flush with, so that edge stays flat.</summary>
internal readonly record struct GlassShape(int Left, int Top, int Right, int Bottom, int OverTop, int OverLeft, int OverRight);
