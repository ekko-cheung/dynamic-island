using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace DynamicIsland.Views;

public partial class MediaView : UserControl
{
    public static readonly DependencyProperty IsExpandedProperty =
        DependencyProperty.Register(nameof(IsExpanded), typeof(bool), typeof(MediaView), new PropertyMetadata(false));

    public bool IsExpanded
    {
        get => (bool)GetValue(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }

    public MediaView() => InitializeComponent();
}

public sealed class BoolToVisibility : IValueConverter
{
    public static readonly BoolToVisibility Instance = new();
    public object Convert(object value, Type t, object p, CultureInfo c) => value is true ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

public sealed class InvertedBoolToVisibility : IValueConverter
{
    public static readonly InvertedBoolToVisibility Instance = new();
    public object Convert(object value, Type t, object p, CultureInfo c) => value is true ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

public sealed class PlayPauseGlyph : IValueConverter
{
    public static readonly PlayPauseGlyph Instance = new();
    // Segoe MDL2 Assets: E769 = Pause, E768 = Play
    public object Convert(object value, Type t, object p, CultureInfo c) => value is true ? "" : "";
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}
