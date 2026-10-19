using System.Windows;
using System.Windows.Controls;

namespace DynamicIsland.Views;

public partial class ReminderView : UserControl
{
    public static readonly DependencyProperty IsExpandedProperty =
        DependencyProperty.Register(nameof(IsExpanded), typeof(bool), typeof(ReminderView), new PropertyMetadata(false));

    public bool IsExpanded
    {
        get => (bool)GetValue(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }

    public ReminderView() => InitializeComponent();
}
