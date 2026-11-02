using System.ComponentModel;
using System.Windows;
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

        Pill.SizeChanged += (_, _) => {
            // Notch style: flat against the top edge, only the bottom corners are rounded.
            var r = Math.Min(Pill.ActualHeight * 0.4, 28);
            Pill.CornerRadius = new CornerRadius(0, 0, r, r);
        };
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

    /// <summary>Re-reads the configured display and centers the island at the top of it.</summary>
    public void PositionAtTop()
    {
        var monitor = Monitors.Resolve(_settings.Monitor);
        if (monitor is null) return;

        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        // Size in physical pixels of the target display, so a move between displays with different scaling lands right.
        var width = (int)Math.Round(Width * monitor.Scale);
        var height = (int)Math.Round(Height * monitor.Scale);
        var x = monitor.Left + ((monitor.Right - monitor.Left) - width) / 2;
        var y = monitor.Top; // flush with the top edge of the display
        NativeMethods.Place(hwnd, x, y, width, height);
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
