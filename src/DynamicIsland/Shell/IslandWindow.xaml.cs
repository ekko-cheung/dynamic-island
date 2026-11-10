using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Interop;
using System.Windows.Threading;
using DynamicIsland.Settings;
using Microsoft.Win32;

namespace DynamicIsland.Shell;

public partial class IslandWindow : Window
{
    private static readonly Size CompactSize = new(230, 38);
    private static readonly Size ExpandedSize = new(400, 140);
    private static readonly Duration AnimDuration = new(TimeSpan.FromMilliseconds(380));

    private readonly IslandViewModel _vm;
    private readonly AppSettings _settings;
    private readonly DispatcherTimer _fullscreenTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    public IslandWindow(IslandViewModel vm, AppSettings settings)
    {
        _vm = vm;
        _settings = settings;
        DataContext = vm;
        InitializeComponent();

        Pill.SizeChanged += (_, _) => UpdateCorners();
        vm.PropertyChanged += OnVmPropertyChanged;
        SystemEvents.DisplaySettingsChanged += (_, _) => Dispatcher.Invoke(PositionAtTop);

        _fullscreenTimer.Tick += (_, _) => OnWatchTick();
        _fullscreenTimer.Start();

        PositionAtTop();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // Don't steal focus and stay out of Alt-Tab.
        var hwnd = new WindowInteropHelper(this).Handle;
        var style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE,
            style | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW);
        PositionAtTop();
    }

    private void OnWatchTick()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == nint.Zero) return;
        _vm.IsSuppressed = NativeMethods.IsFullscreenAppActive(hwnd);
        // Other windows (a browser opened later, another topmost app) can end up above the island.
        if (!_vm.IsSuppressed) NativeMethods.BringToTopmost(hwnd);
    }

    private void UpdateCorners()
    {
        // Notch style while flush with the top edge (only the bottom corners are rounded); a floating pill otherwise.
        var r = Math.Min(Pill.ActualHeight * 0.4, 28);
        var top = _settings.OffsetY > 0 ? r : 0;
        Pill.CornerRadius = new CornerRadius(top, top, r, r);
    }

    /// <summary>Re-reads the configured display and centers the island at the top of it.</summary>
    public void PositionAtTop()
    {
        var monitor = Monitors.Resolve(_settings.Monitor);
        if (monitor is null) return;

        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        // Size in physical pixels of the target display, so a move between displays with different scaling lands right.
        var width = (int)Math.Round(Width * monitor.Scale);
        var height = (int)Math.Round(Height * monitor.Scale);
        // Keep the visible pill (not the transparent window around it) on this display whatever the offsets say.
        var pillWidth = (int)Math.Round(Pill.ActualWidth * monitor.Scale);
        var pillHeight = (int)Math.Round(Pill.ActualHeight * monitor.Scale);
        var centerX = (monitor.Left + monitor.Right) / 2 + (int)Math.Round(_settings.OffsetX * monitor.Scale);
        centerX = Math.Clamp(centerX, monitor.Left + pillWidth / 2, Math.Max(monitor.Left + pillWidth / 2, monitor.Right - pillWidth / 2));
        var x = centerX - width / 2;
        var y = monitor.Top + (int)Math.Round(_settings.OffsetY * monitor.Scale); // 0 = flush with the top edge
        y = Math.Clamp(y, monitor.Top, Math.Max(monitor.Top, monitor.Bottom - pillHeight));
        UpdateCorners();
        NativeMethods.Place(hwnd, x, y, width, height);
    }

    // --- Drag to move ---------------------------------------------------------------------------------------------
    // A press only becomes a drag after the pointer has moved a few pixels, so buttons inside the card still click.

    private const double SnapThreshold = 24; // DIPs

    private bool _dragArmed, _dragging;
    private int _downX, _downY, _grabDx, _grabDy;

    private void Pill_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!NativeMethods.TryGetCursorPos(out _downX, out _downY)) return;
        _dragArmed = true;
        _dragging = false;
    }

    private void Pill_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_dragArmed) return;
        if (e.LeftButton != System.Windows.Input.MouseButtonState.Pressed) { EndDrag(save: _dragging); return; }
        if (!NativeMethods.TryGetCursorPos(out var x, out var y)) return;

        if (!_dragging)
        {
            var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
            if (Math.Abs(x - _downX) < SystemParameters.MinimumHorizontalDragDistance * scale
                && Math.Abs(y - _downY) < SystemParameters.MinimumVerticalDragDistance * scale) return;
            _dragging = true;
            Pill.CaptureMouse();
            // Remember where on the pill it was grabbed, so it does not jump to the cursor.
            var top = Pill.PointToScreen(new Point(Pill.ActualWidth / 2, 0));
            _grabDx = _downX - (int)Math.Round(top.X);
            _grabDy = _downY - (int)Math.Round(top.Y);
        }
        DragTo(x, y);
    }

    private void Pill_PreviewMouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!_dragging) { _dragArmed = false; return; }
        e.Handled = true; // the release ends a drag; it is not a click on whatever is under the pointer
        EndDrag(save: true);
    }

    private void Pill_LostMouseCapture(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_dragging) EndDrag(save: true);
    }

    private void EndDrag(bool save)
    {
        var wasDragging = _dragging;
        _dragArmed = _dragging = false;
        if (wasDragging) Pill.ReleaseMouseCapture();
        if (save) _settings.Save();
    }

    /// <summary>Turns a pointer position into offsets from the display's top-center, snapping near its edges.</summary>
    private void DragTo(int cursorX, int cursorY)
    {
        var monitor = Monitors.All().FirstOrDefault(m => cursorX >= m.Left && cursorX < m.Right && cursorY >= m.Top && cursorY < m.Bottom)
                      ?? Monitors.Resolve(_settings.Monitor);
        if (monitor is null) return;
        var scale = monitor.Scale;
        var monitorWidth = (monitor.Right - monitor.Left) / scale;
        var monitorHeight = (monitor.Bottom - monitor.Top) / scale;

        var offsetX = ((cursorX - _grabDx - monitor.Left) / scale) - monitorWidth / 2;
        var offsetY = (cursorY - _grabDy - monitor.Top) / scale;

        var maxX = Math.Max(0, (monitorWidth - Pill.ActualWidth) / 2);
        var maxY = Math.Max(0, monitorHeight - Pill.ActualHeight);
        offsetX = Math.Clamp(offsetX, -maxX, maxX);
        offsetY = Math.Clamp(offsetY, 0, maxY);

        if (_settings.SnapToEdges)
        {
            if (Math.Abs(offsetX) < SnapThreshold) offsetX = 0;
            else if (maxX - Math.Abs(offsetX) < SnapThreshold) offsetX = Math.Sign(offsetX) * maxX;
            if (offsetY < SnapThreshold) offsetY = 0;
            else if (maxY - offsetY < SnapThreshold) offsetY = maxY;
        }

        _settings.Monitor = monitor.IsPrimary ? "" : monitor.DeviceName;
        _settings.OffsetX = (int)Math.Round(offsetX);
        _settings.OffsetY = (int)Math.Round(offsetY);
        PositionAtTop();
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        PositionAtTop();
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IslandViewModel.IsExpanded))
            Animate(_vm.IsExpanded ? ExpandedSize : CompactSize);
    }

    private void Animate(Size target)
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        Pill.BeginAnimation(WidthProperty, new DoubleAnimation(target.Width, AnimDuration) { EasingFunction = ease });
        Pill.BeginAnimation(HeightProperty, new DoubleAnimation(target.Height, AnimDuration) { EasingFunction = ease });
    }

    private void Pill_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e) => _vm.PointerEntered();
    private void Pill_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e) => _vm.PointerLeft();
}
