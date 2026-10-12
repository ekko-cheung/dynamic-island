using System.Windows;
using System.Windows.Controls;

namespace DynamicIsland.Views;

public partial class NotificationView : UserControl
{
    public static readonly DependencyProperty IsExpandedProperty =
        DependencyProperty.Register(nameof(IsExpanded), typeof(bool), typeof(NotificationView), new PropertyMetadata(false));

    public bool IsExpanded
    {
        get => (bool)GetValue(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }

    public NotificationView() => InitializeComponent();
}
