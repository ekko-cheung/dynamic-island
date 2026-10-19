using System.Windows.Controls;
using DynamicIsland.Providers.Timers;

namespace DynamicIsland.Views;

public partial class SatelliteView : UserControl
{
    public SatelliteView() => InitializeComponent();

    // Small islands are display-only, except a due reminder, which can be dismissed with a click.
    private void Satellite_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (DataContext is ReminderActivity reminder) reminder.Dismiss();
    }
}
