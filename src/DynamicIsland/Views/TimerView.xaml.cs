using System.Windows;
using System.Windows.Controls;

namespace DynamicIsland.Views;

public partial class TimerView : UserControl
{
    public static readonly DependencyProperty IsExpandedProperty =
        DependencyProperty.Register(nameof(IsExpanded), typeof(bool), typeof(TimerView), new PropertyMetadata(false));

    public bool IsExpanded
    {
        get => (bool)GetValue(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }

    public TimerView() => InitializeComponent();
}
