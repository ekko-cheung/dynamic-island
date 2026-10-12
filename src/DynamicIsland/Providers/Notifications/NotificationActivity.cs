using System.Windows.Media;
using DynamicIsland.Core;

namespace DynamicIsland.Providers.Notifications;

/// <summary>A single Toast notification mirrored from the Windows notification center.</summary>
public sealed class NotificationActivity(string id, string appName, string title, string body, ImageSource? icon) : IslandActivity
{
    public override string Id { get; } = id;
    public override ActivityKind Kind => ActivityKind.AppNotification;

    // Above media (0), so a new notification takes over the pill while it is shown.
    public override int Priority => 10;

    public string AppName { get; } = appName;
    public string Title { get; } = title;
    public string Body { get; } = body;
    public ImageSource? Icon { get; } = icon;
}
