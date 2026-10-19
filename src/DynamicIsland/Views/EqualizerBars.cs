using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace DynamicIsland.Views;

/// <summary>Small bouncing music bars. Animates only while visible, so a hidden or collapsed island costs nothing.</summary>
public sealed class EqualizerBars : Grid
{
    private static readonly (double Seconds, double Low, double High)[] Bars =
    [
        (0.55, 0.25, 1.00),
        (0.42, 0.35, 0.75),
        (0.68, 0.20, 0.95),
        (0.48, 0.30, 0.85),
    ];

    public static readonly DependencyProperty BarBrushProperty =
        DependencyProperty.Register(nameof(BarBrush), typeof(Brush), typeof(EqualizerBars),
            new PropertyMetadata(Brushes.White, (d, _) => ((EqualizerBars)d).ApplyBrush()));

    private readonly List<(Rectangle Bar, ScaleTransform Scale)> _bars = [];

    public Brush BarBrush
    {
        get => (Brush)GetValue(BarBrushProperty);
        set => SetValue(BarBrushProperty, value);
    }

    public EqualizerBars()
    {
        Width = 16;
        Height = 14;
        ColumnDefinitions.Clear();

        for (var i = 0; i < Bars.Length; i++)
        {
            ColumnDefinitions.Add(new ColumnDefinition());
            var scale = new ScaleTransform(1, Bars[i].Low);
            var bar = new Rectangle
            {
                Width = 2.5,
                Height = 14,
                RadiusX = 1.25,
                RadiusY = 1.25,
                VerticalAlignment = VerticalAlignment.Bottom,
                RenderTransform = scale,
                RenderTransformOrigin = new Point(0.5, 1),
                Fill = BarBrush,
            };
            SetColumn(bar, i);
            Children.Add(bar);
            _bars.Add((bar, scale));
        }

        IsVisibleChanged += (_, _) => Update();
        Loaded += (_, _) => Update();
    }

    private void ApplyBrush()
    {
        foreach (var (bar, _) in _bars) bar.Fill = BarBrush;
    }

    private void Update()
    {
        for (var i = 0; i < _bars.Count; i++)
        {
            var (_, scale) = _bars[i];
            if (!IsVisible)
            {
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                continue;
            }

            var (seconds, low, high) = Bars[i];
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(low, high, TimeSpan.FromSeconds(seconds))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            });
        }
    }
}
