using System.ComponentModel;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;

namespace DynamicIsland.Shell;

public partial class IslandWindow : Window
{
    private static readonly Size CompactSize = new(230, 38);
    private static readonly Size ExpandedSize = new(400, 140);
    private static readonly Duration AnimDuration = new(TimeSpan.FromMilliseconds(380));

    private readonly IslandViewModel _vm;
    private readonly DispatcherTimer _fullscreenTimer = new() { Interval = TimeSpan.FromSeconds(2) };

    public IslandWindow(IslandViewModel vm)
    {
        _vm = vm;
        DataContext = vm;
        InitializeComponent();

        Pill.SizeChanged += (_, _) => Pill.CornerRadius = new CornerRadius(Math.Min(Pill.ActualHeight / 2, 30));
        vm.PropertyChanged += OnVmPropertyChanged;
        SystemEvents.DisplaySettingsChanged += (_, _) => Dispatcher.Invoke(PositionAtTop);

        _fullscreenTimer.Tick += (_, _) => _vm.IsSuppressed = NativeMethods.IsFullscreenAppActive();
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
    }

    private void PositionAtTop()
    {
        Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
        Top = 6;
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
