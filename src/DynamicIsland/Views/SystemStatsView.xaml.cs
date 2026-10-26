using System.Globalization;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace DynamicIsland.Views;

public partial class SystemStatsView : UserControl
{
    public SystemStatsView() => InitializeComponent();
}

/// <summary>Ring color by load: the kind's own color normally, red when the machine is nearly maxed out.</summary>
public sealed class LoadBrush(Color normal) : IValueConverter
{
    public static readonly LoadBrush Cpu = new(Color.FromRgb(0x0A, 0x84, 0xFF));
    public static readonly LoadBrush Memory = new(Color.FromRgb(0xBF, 0x5A, 0xF2));

    private static readonly Brush Hot = Freeze(Color.FromRgb(0xFF, 0x45, 0x3A));
    private readonly Brush _normal = Freeze(normal);

    public object Convert(object value, Type t, object p, CultureInfo c) => value is double v && v >= 90 ? Hot : _normal;
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotSupportedException();

    private static Brush Freeze(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
