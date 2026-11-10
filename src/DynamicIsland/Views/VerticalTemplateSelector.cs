using System.Windows;
using System.Windows.Controls;
using DynamicIsland.Providers.Claude;
using DynamicIsland.Providers.Media;
using DynamicIsland.Providers.Notifications;
using DynamicIsland.Providers.SystemLevels;
using DynamicIsland.Providers.SystemStats;
using DynamicIsland.Providers.Timers;

namespace DynamicIsland.Views;

/// <summary>
/// Chooses the stacked compact layout for an activity while the island is docked to a side edge. Types without one
/// return null, which falls back to the normal template.
/// </summary>
public sealed class VerticalTemplateSelector : DataTemplateSelector
{
    private static readonly Lazy<ResourceDictionary> Templates = new(() => new ResourceDictionary
    {
        Source = new Uri("/DynamicIsland;component/Views/VerticalTemplates.xaml", UriKind.Relative),
    });

    public override DataTemplate? SelectTemplate(object? item, DependencyObject container)
    {
        var key = item switch
        {
            MediaActivity => "Media",
            NotificationActivity => "Notification",
            ClaudeActivity => "Claude",
            TimerActivity => "Timer",
            ReminderActivity => "Reminder",
            LevelActivity => "Level",
            SystemStatsActivity => "Stats",
            _ => null,
        };
        return key is null ? null : Templates.Value[key] as DataTemplate;
    }
}
